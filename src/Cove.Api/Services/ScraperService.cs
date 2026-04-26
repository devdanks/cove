using HtmlAgilityPack;
using Cove.Core.DTOs;
using Cove.Core.Interfaces;
using Cove.Plugins;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using System.Text;

namespace Cove.Api.Services;

public class ScraperService
{
    private static readonly string[] SupportedExtensions = [".yml", ".yaml"];

    private readonly CoveConfiguration _config;
    private readonly ILogger<ScraperService> _logger;
    private readonly IDeserializer _deserializer;
    private readonly HttpClient _httpClient;
    private readonly ExtensionManager _extensionManager;
    private readonly Lock _sync = new();
    private IReadOnlyList<ScraperSummaryDto> _cached = [];
    private readonly Dictionary<string, ScraperManifest> _manifestCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExtensionScraperRegistration> _extensionScraperCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions ExtensionScrapeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ScraperService(CoveConfiguration config, ILogger<ScraperService> logger, IHttpClientFactory httpClientFactory, ExtensionManager extensionManager)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("scraper");
        _extensionManager = extensionManager;
        _deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public IReadOnlyList<ScraperSummaryDto> GetScrapers()
    {
        lock (_sync)
        {
            if (_cached.Count == 0)
                _cached = LoadScrapers();

            return _cached;
        }
    }

    public IReadOnlyList<ScraperSummaryDto> ReloadScrapers()
    {
        lock (_sync)
        {
            _cached = LoadScrapers();
            return _cached;
        }
    }

    /// <summary>
    /// Find loaded scrapers whose URL patterns match the given URL.
    /// Built-in extension scrapers are preferred and listed first.
    /// </summary>
    public IReadOnlyList<ScraperSummaryDto> FindScrapersForUrl(string url, string? entityType = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return [];

        var normalized = url.Trim();
        var loweredUrl = normalized.ToLowerInvariant();

        return GetScrapers()
            .Where(s => string.IsNullOrWhiteSpace(entityType) ||
                        string.Equals(s.EntityType, entityType, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.SupportedScrapes.Any(k => string.Equals(k, "URL", StringComparison.OrdinalIgnoreCase)))
            .Where(s => s.Urls.Any(pattern => UrlMatchesPattern(loweredUrl, pattern)))
            .OrderByDescending(s => s.SourcePath.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => BestPatternStrength(loweredUrl, s.Urls))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Pick the best loaded scraper for the URL/entity and run a URL scrape.
    /// Returns null if no scraper matched or all matching scrapers failed.
    /// </summary>
    public async Task<(string ScraperId, Dictionary<string, object> Result)?> ScrapeUrlAutoAsync(string url, string entityType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var candidates = FindScrapersForUrl(url, entityType);
        foreach (var candidate in candidates)
        {
            try
            {
                var result = await ScrapeUrlAsync(candidate.Id, entityType, url, ct);
                if (result is { Count: > 0 })
                    return (candidate.Id, result);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Auto scraper {ScraperId} failed for URL {Url}", candidate.Id, url);
            }
        }

        return null;
    }

    private static bool UrlMatchesPattern(string loweredUrl, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var loweredPattern = pattern.Trim().ToLowerInvariant();
        if (!loweredPattern.Contains('*'))
            return loweredUrl.Contains(loweredPattern, StringComparison.Ordinal);

        var fragments = loweredPattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
        if (fragments.Length == 0)
            return true;

        var index = 0;
        foreach (var fragment in fragments)
        {
            var found = loweredUrl.IndexOf(fragment, index, StringComparison.Ordinal);
            if (found < 0)
                return false;
            index = found + fragment.Length;
        }
        return true;
    }

    private static int BestPatternStrength(string loweredUrl, IEnumerable<string> patterns)
    {
        var best = 0;
        foreach (var pattern in patterns)
        {
            if (UrlMatchesPattern(loweredUrl, pattern))
                best = Math.Max(best, pattern.Trim().Length);
        }
        return best;
    }

    private IReadOnlyList<ScraperSummaryDto> LoadScrapers()
    {
        var summaries = new List<ScraperSummaryDto>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _manifestCache.Clear();
        _extensionScraperCache.Clear();

        foreach (var directory in _config.Scraping.ScraperDirectories.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!Directory.Exists(directory))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate scraper directory {Directory}", directory);
                continue;
            }

            foreach (var file in files)
            {
                if (!seenFiles.Add(file))
                    continue;

                try
                {
                    summaries.AddRange(ParseScraperFile(file));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load scraper definition from {File}", file);
                }
            }
        }

        foreach (var provider in _extensionManager.GetScraperProviders())
        {
            foreach (var descriptor in provider.GetScrapers())
            {
                _extensionScraperCache[descriptor.Id] = new ExtensionScraperRegistration(provider, descriptor);
                summaries.Add(new ScraperSummaryDto(
                    descriptor.Id,
                    descriptor.Name,
                    descriptor.Entity.ToString().ToLowerInvariant(),
                    GetSupportedScrapeNames(descriptor.Capabilities),
                    descriptor.SupportedUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim()).ToList(),
                    $"builtin:{provider.Id}"));
            }
        }

        return summaries
            .OrderBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ScraperSummaryDto> ParseScraperFile(string file)
    {
        using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream);
        var definition = _deserializer.Deserialize<ScraperManifest>(reader);

        var scraperId = Path.GetFileNameWithoutExtension(file);
        var scraperName = string.IsNullOrWhiteSpace(definition.Name)
            ? scraperId
            : definition.Name.Trim();

        // Cache manifest for execution
        definition.FilePath = file;
        _manifestCache[scraperId] = definition;

        var summaries = new List<ScraperSummaryDto>();

        AddSummary(
            summaries,
            scraperId,
            scraperName,
            "scene",
            file,
            byName: definition.SceneByName,
            byFragments: [definition.SceneByFragment, definition.SceneByQueryFragment],
            byUrls: definition.SceneByUrl
        );
        AddSummary(
            summaries,
            scraperId,
            scraperName,
            "performer",
            file,
            byName: definition.PerformerByName,
            byFragments: [definition.PerformerByFragment],
            byUrls: definition.PerformerByUrl
        );
        AddSummary(
            summaries,
            scraperId,
            scraperName,
            "gallery",
            file,
            byFragments: [definition.GalleryByFragment],
            byUrls: definition.GalleryByUrl
        );
        AddSummary(
            summaries,
            scraperId,
            scraperName,
            "image",
            file,
            byFragments: [definition.ImageByFragment],
            byUrls: definition.ImageByUrl
        );
        AddSummary(
            summaries,
            scraperId,
            scraperName,
            "group",
            file,
            byUrls: [.. definition.GroupByUrl, .. definition.MovieByUrl]
        );

        return summaries;
    }

    private static void AddSummary(
        ICollection<ScraperSummaryDto> summaries,
        string scraperId,
        string scraperName,
        string entityType,
        string file,
        ByNameDefinition? byName = null,
        IEnumerable<ByFragmentDefinition?>? byFragments = null,
        IEnumerable<ByUrlDefinition>? byUrls = null)
    {
        var supportedScrapes = new List<string>();
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (byName != null && IsSupportedAction(byName))
            supportedScrapes.Add("Name");

        if (byFragments?.Any(definition => definition != null && IsSupportedAction(definition)) == true)
            supportedScrapes.Add("Fragment");

        if (byUrls?.Any(IsSupportedAction) == true)
        {
            supportedScrapes.Add("URL");
            foreach (var url in byUrls.Where(IsSupportedAction).SelectMany(definition => definition.Url ?? []))
            {
                if (!string.IsNullOrWhiteSpace(url))
                    urls.Add(url.Trim());
            }
        }

        if (supportedScrapes.Count == 0)
            return;

        summaries.Add(new ScraperSummaryDto(
            Id: $"{scraperId}:{entityType}",
            Name: scraperName,
            EntityType: entityType,
            SupportedScrapes: supportedScrapes,
            Urls: urls.OrderBy(url => url, StringComparer.OrdinalIgnoreCase).ToList(),
            SourcePath: file
        ));
    }

    private sealed class ScraperManifest
    {
        [YamlIgnore]
        public string FilePath { get; set; } = string.Empty;

        [YamlMember(Alias = "name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "xPathScrapers")]
        public Dictionary<string, MappedScraperDef> XPathScrapers { get; init; } = new();

        [YamlMember(Alias = "jsonScrapers")]
        public Dictionary<string, MappedScraperDef> JsonScrapers { get; init; } = new();

        [YamlMember(Alias = "performerByName")]
        public ByNameDefinition? PerformerByName { get; init; }

        [YamlMember(Alias = "performerByFragment")]
        public ByFragmentDefinition? PerformerByFragment { get; init; }

        [YamlMember(Alias = "performerByURL")]
        public List<ByUrlDefinition> PerformerByUrl { get; init; } = [];

        [YamlMember(Alias = "sceneByName")]
        public ByNameDefinition? SceneByName { get; init; }

        [YamlMember(Alias = "sceneByFragment")]
        public ByFragmentDefinition? SceneByFragment { get; init; }

        [YamlMember(Alias = "sceneByQueryFragment")]
        public ByFragmentDefinition? SceneByQueryFragment { get; init; }

        [YamlMember(Alias = "sceneByURL")]
        public List<ByUrlDefinition> SceneByUrl { get; init; } = [];

        [YamlMember(Alias = "galleryByFragment")]
        public ByFragmentDefinition? GalleryByFragment { get; init; }

        [YamlMember(Alias = "galleryByURL")]
        public List<ByUrlDefinition> GalleryByUrl { get; init; } = [];

        [YamlMember(Alias = "imageByFragment")]
        public ByFragmentDefinition? ImageByFragment { get; init; }

        [YamlMember(Alias = "imageByURL")]
        public List<ByUrlDefinition> ImageByUrl { get; init; } = [];

        [YamlMember(Alias = "groupByURL")]
        public List<ByUrlDefinition> GroupByUrl { get; init; } = [];

        [YamlMember(Alias = "movieByURL")]
        public List<ByUrlDefinition> MovieByUrl { get; init; } = [];

        [YamlMember(Alias = "driver")]
        public DriverDefinition? Driver { get; init; }
    }

    private sealed class DriverDefinition
    {
        [YamlMember(Alias = "headers")]
        public List<DriverHeaderDefinition> Headers { get; init; } = [];

        [YamlMember(Alias = "cookies")]
        public List<DriverCookieScopeDefinition> Cookies { get; init; } = [];
    }

    private sealed class DriverHeaderDefinition
    {
        [YamlMember(Alias = "Key")]
        public string? Key { get; init; }

        [YamlMember(Alias = "Value")]
        public string? Value { get; init; }
    }

    private sealed class DriverCookieScopeDefinition
    {
        [YamlMember(Alias = "CookieURL")]
        public string? CookieUrl { get; init; }

        [YamlMember(Alias = "Cookies")]
        public List<DriverCookieDefinition> Cookies { get; init; } = [];
    }

    private sealed class DriverCookieDefinition
    {
        [YamlMember(Alias = "Name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "Value")]
        public string? Value { get; init; }
    }

    private sealed class ByNameDefinition : ActionDefinitionBase
    {
    }

    private sealed class ByFragmentDefinition : ActionDefinitionBase
    {
    }

    private sealed class RegexReplaceDefinition
    {
        [YamlMember(Alias = "regex")]
        public string? Regex { get; init; }

        [YamlMember(Alias = "with")]
        public string? With { get; init; }
    }

    private sealed class ByUrlDefinition
    {
        [YamlMember(Alias = "url")]
        public List<string> Url { get; init; } = [];

        [YamlMember(Alias = "queryURL")]
        public string? QueryUrl { get; init; }

        [YamlMember(Alias = "action")]
        public string? Action { get; init; }

        [YamlMember(Alias = "scraper")]
        public string? Scraper { get; init; }

        [YamlMember(Alias = "script")]
        public List<string>? Script { get; init; }
    }

    // ===== Execution Engine =====

    /// <summary>
    /// Scrape a URL using the specified scraper and entity type.
    /// </summary>
    public async Task<Dictionary<string, object>?> ScrapeUrlAsync(string scraperId, string entityType, string url, CancellationToken ct = default)
    {
        // Ensure scrapers are loaded
        GetScrapers();

        if (TryGetExtensionScraperRegistration(scraperId, entityType, out var extensionRegistration))
            return await ScrapeUrlWithExtensionAsync(extensionRegistration, url, ct);

        // Parse the base scraper id (format: "scraperId:entityType")
        var baseId = scraperId.Contains(':') ? scraperId.Split(':')[0] : scraperId;

        if (!_manifestCache.TryGetValue(baseId, out var manifest))
        {
            _logger.LogWarning("Scraper {Id} not found", baseId);
            return null;
        }

        // Find matching URL definition
        var urlDefs = entityType switch
        {
            "scene" => manifest.SceneByUrl,
            "performer" => manifest.PerformerByUrl,
            "gallery" => manifest.GalleryByUrl,
            "image" => manifest.ImageByUrl,
            "group" or "movie" => [.. manifest.GroupByUrl, .. manifest.MovieByUrl],
            _ => []
        };

        var matchingDef = urlDefs.FirstOrDefault(d => d.Url.Any(u => url.Contains(u, StringComparison.OrdinalIgnoreCase)));
        if (matchingDef == null)
        {
            _logger.LogWarning("No URL match for {Url} in scraper {Id}", url, baseId);
            return null;
        }

        var targetUrl = matchingDef.QueryUrl?.Replace("{url}", Uri.EscapeDataString(url)) ?? url;
        var action = matchingDef.Action ?? "scrapeXPath";
        var scraperName = matchingDef.Scraper;

        if (IsScriptAction(action))
        {
            LogScriptScraperUnsupported(baseId, entityType, action);
            return null;
        }

        return action switch
        {
            "scrapeXPath" => await ScrapeXPathAsync(manifest, scraperName, entityType, targetUrl, ct),
            "scrapeJson" => await ScrapeJsonAsync(manifest, scraperName, entityType, targetUrl, ct),
            _ => null
        };
    }

    /// <summary>
    /// Scrape by name (search) using the specified scraper and entity type.
    /// </summary>
    public async Task<List<Dictionary<string, object>>?> ScrapeNameAsync(string scraperId, string entityType, string name, CancellationToken ct = default)
    {
        GetScrapers();

        if (TryGetExtensionScraperRegistration(scraperId, entityType, out var extensionRegistration))
            return await ScrapeNameWithExtensionAsync(extensionRegistration, name, ct);

        var baseId = scraperId.Contains(':') ? scraperId.Split(':')[0] : scraperId;

        if (!_manifestCache.TryGetValue(baseId, out var manifest))
            return null;

        var nameDef = entityType switch
        {
            "scene" => manifest.SceneByName,
            "performer" => manifest.PerformerByName,
            _ => null
        };

        if (nameDef == null || string.IsNullOrEmpty(nameDef.QueryUrl))
            return null;
        var action = nameDef.Action ?? "scrapeXPath";
        var scraperName = nameDef.Scraper;

        if (IsScriptAction(action))
        {
            LogScriptScraperUnsupported(baseId, entityType, action);
            return null;
        }

        foreach (var searchTerm in BuildNameSearchTerms(name))
        {
            var targetUrl = BuildNameTargetUrl(nameDef.QueryUrl, searchTerm);
            var result = action switch
            {
                "scrapeXPath" => await ScrapeXPathAsync(manifest, scraperName, entityType, targetUrl, ct, preserveCollections: true),
                "scrapeJson" => await ScrapeJsonAsync(manifest, scraperName, entityType, targetUrl, ct, preserveCollections: true),
                _ => null
            };

            var candidates = ExpandNameSearchResults(result);
            if (candidates is { Count: > 0 })
                return await EnrichNameSearchCandidatesAsync(scraperId, entityType, candidates, targetUrl, ct);
        }

        return null;
    }

    /// <summary>
    /// Scrape by fragment (entity data) using the specified scraper and entity type.
    /// </summary>
    public async Task<Dictionary<string, object>?> ScrapeFragmentAsync(string scraperId, string entityType, Dictionary<string, object> fragment, CancellationToken ct = default)
    {
        GetScrapers();

        if (TryGetExtensionScraperRegistration(scraperId, entityType, out var extensionRegistration))
            return await ScrapeFragmentWithExtensionAsync(extensionRegistration, fragment, ct);

        var baseId = scraperId.Contains(':') ? scraperId.Split(':')[0] : scraperId;

        if (!_manifestCache.TryGetValue(baseId, out var manifest))
            return null;

        var fragDefs = entityType switch
        {
            "scene" => GetSceneFragmentDefinitions(manifest, fragment),
            "performer" => manifest.PerformerByFragment is null ? [] : [manifest.PerformerByFragment],
            "gallery" => manifest.GalleryByFragment is null ? [] : [manifest.GalleryByFragment],
            "image" => manifest.ImageByFragment is null ? [] : [manifest.ImageByFragment],
            _ => []
        };

        if (fragDefs.Count == 0)
            return null;

        foreach (var fragDef in fragDefs)
        {
            var targetUrl = BuildFragmentTargetUrl(fragDef, fragment);
            var action = fragDef.Action ?? "scrapeXPath";
            var scraperName = fragDef.Scraper;

            if (IsScriptAction(action))
            {
                LogScriptScraperUnsupported(baseId, entityType, action);
                return null;
            }

            if (string.IsNullOrEmpty(targetUrl))
                continue;

            var result = action switch
            {
                "scrapeXPath" => await ScrapeXPathAsync(manifest, scraperName, entityType, targetUrl, ct),
                "scrapeJson" => await ScrapeJsonAsync(manifest, scraperName, entityType, targetUrl, ct),
                _ => null
            };

            if (result is { Count: > 0 })
                return result;
        }

        return null;
    }

    private static List<ActionDefinitionBase> GetSceneFragmentDefinitions(ScraperManifest manifest, IReadOnlyDictionary<string, object> fragment)
    {
        var definitions = new List<ActionDefinitionBase>();
        var hasUrl = !string.IsNullOrWhiteSpace(GetFragmentString(fragment, "url"))
            || GetFragmentStringList(fragment, "urls").Count > 0;

        if (hasUrl && manifest.SceneByQueryFragment != null)
            definitions.Add(manifest.SceneByQueryFragment);

        if (manifest.SceneByFragment != null)
            definitions.Add(manifest.SceneByFragment);

        if (!hasUrl && manifest.SceneByQueryFragment != null)
            definitions.Add(manifest.SceneByQueryFragment);

        return definitions;
    }

    private static string? BuildFragmentTargetUrl(ActionDefinitionBase definition, IReadOnlyDictionary<string, object> fragment)
    {
        var targetUrl = definition.QueryUrl;
        if (string.IsNullOrWhiteSpace(targetUrl))
            return null;

        foreach (var kv in fragment)
        {
            var placeholder = $"{{{kv.Key}}}";
            var rawValue = ConvertFragmentString(kv.Value) ?? string.Empty;
            var resolvedValue = ApplyQueryUrlReplacements(rawValue, kv.Key, definition.QueryUrlReplace);
            targetUrl = targetUrl.Replace(placeholder, ResolveQueryUrlPlaceholderValue(targetUrl, placeholder, resolvedValue));
        }

        return targetUrl;
    }

    private static string BuildNameTargetUrl(string queryUrl, string name)
    {
        var encodedName = Uri.EscapeDataString(name);
        return queryUrl
            .Replace("{}", encodedName, StringComparison.Ordinal)
            .Replace("{name}", encodedName, StringComparison.Ordinal)
            .Replace("{query}", encodedName, StringComparison.Ordinal);
    }

    private static List<string> BuildNameSearchTerms(string name)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
                terms.Add(trimmed);
        }

        Add(name);
        Add(SanitizeNameSearchTerm(name));

        if (!string.IsNullOrWhiteSpace(name) && name.Contains(':', StringComparison.Ordinal))
            Add(name[(name.LastIndexOf(':') + 1)..]);

        return terms;
    }

    private static string SanitizeNameSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
                continue;
            }

            if (!char.IsWhiteSpace(character) && character is not ':' and not '-' and not '_' and not '/' and not '\\')
                continue;

            if (lastWasSpace)
                continue;

            builder.Append(' ');
            lastWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    private async Task<List<Dictionary<string, object>>> EnrichNameSearchCandidatesAsync(
        string scraperId,
        string entityType,
        List<Dictionary<string, object>> candidates,
        string searchUrl,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return candidates;

        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Select(async (candidate, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (Index: index, Candidate: await EnrichNameSearchCandidateAsync(scraperId, entityType, candidate, searchUrl, ct));
            }
            finally
            {
                gate.Release();
            }
        });

        var enriched = await Task.WhenAll(tasks);
        return enriched.OrderBy(item => item.Index).Select(item => item.Candidate).ToList();
    }

    private async Task<Dictionary<string, object>> EnrichNameSearchCandidateAsync(
        string scraperId,
        string entityType,
        Dictionary<string, object> candidate,
        string searchUrl,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, object>(candidate, StringComparer.OrdinalIgnoreCase);
        var candidateUrl = ExtractCandidateUrl(candidate);
        if (string.IsNullOrWhiteSpace(candidateUrl))
            return merged;

        var absoluteUrl = ResolveCandidateUrl(searchUrl, candidateUrl);
        if (!string.IsNullOrWhiteSpace(absoluteUrl))
            merged["URL"] = absoluteUrl;

        try
        {
            var scraped = await ScrapeUrlAsync(scraperId, entityType, absoluteUrl ?? candidateUrl, ct);
            if (scraped == null || scraped.Count == 0)
                return merged;

            foreach (var (field, value) in scraped)
                merged[field] = value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Candidate enrichment failed for {EntityType} URL {Url}", entityType, absoluteUrl ?? candidateUrl);
        }

        return merged;
    }

    private static string? ExtractCandidateUrl(IReadOnlyDictionary<string, object> candidate)
    {
        foreach (var field in new[] { "URL", "Url" })
        {
            if (candidate.TryGetValue(field, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        if (candidate.TryGetValue("URLs", out var urlsValue) && urlsValue is List<string> urls && urls.Count > 0)
            return urls[0];

        return null;
    }

    private static string? ResolveCandidateUrl(string searchUrl, string candidateUrl)
    {
        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (!Uri.TryCreate(searchUrl, UriKind.Absolute, out var baseUri))
            return candidateUrl;

        return Uri.TryCreate(baseUri, candidateUrl, out var resolved)
            ? resolved.ToString()
            : candidateUrl;
    }

    private static string ResolveQueryUrlPlaceholderValue(string targetUrlTemplate, string placeholder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Equals(targetUrlTemplate.Trim(), placeholder, StringComparison.Ordinal)
            ? value
            : Uri.EscapeDataString(value);
    }

    private static string NormalizeRequestUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        var decodedUrl = Uri.UnescapeDataString(url);
        return Uri.TryCreate(decodedUrl, UriKind.Absolute, out _) ? decodedUrl : url;
    }

    private static string ApplyQueryUrlReplacements(string value, string fieldName, IReadOnlyDictionary<string, List<RegexReplaceDefinition>>? replacements)
    {
        if (string.IsNullOrWhiteSpace(value)
            || replacements == null
            || !replacements.TryGetValue(fieldName, out var fieldReplacements)
            || fieldReplacements.Count == 0)
        {
            return value;
        }

        var current = value;
        foreach (var replacement in fieldReplacements)
        {
            if (string.IsNullOrWhiteSpace(replacement.Regex))
                continue;

            current = Regex.Replace(
                current,
                replacement.Regex,
                replacement.With ?? string.Empty,
                RegexOptions.Singleline);
        }

        return current;
    }

    private async Task<Dictionary<string, object>?> ScrapeXPathAsync(
        ScraperManifest manifest,
        string? scraperName,
        string entityType,
        string url,
        CancellationToken ct,
        bool preserveCollections = false)
    {
        if (string.IsNullOrEmpty(scraperName) || !manifest.XPathScrapers.TryGetValue(scraperName, out var scraperDef))
        {
            _logger.LogWarning("XPath scraper definition '{Name}' not found", scraperName);
            return null;
        }

        var entitySelectors = GetEntitySelectors(scraperDef, entityType);
        if (entitySelectors == null || entitySelectors.Count == 0) return null;

        // Apply common substitutions
        var common = scraperDef.Common ?? new Dictionary<string, string>();

        try
        {
            _logger.LogDebug("Fetching URL for XPath scrape: {Url}", url);
            var html = await FetchContentAsync(manifest, url, ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var (field, selectorObj) in entitySelectors)
            {
                try
                {
                    if (IsRelationshipField(field))
                    {
                        var items = ExtractXPathRelationshipItems(doc.DocumentNode, selectorObj, common);
                        if (items.Count > 0)
                            result[field] = items;
                    }
                    else
                    {
                        var values = ExtractXPathValues(doc.DocumentNode, selectorObj, common, treatPlainStringsAsFixed: false);
                        var value = ConvertSelectorValues(values, preserveCollections);

                        if (value is string textValue && !string.IsNullOrWhiteSpace(textValue))
                            result[field] = textValue;
                        else if (value is List<string> listValue && listValue.Count > 0)
                            result[field] = listValue;
                        else if (value is not null)
                            result[field] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("XPath selector error for field {Field}: {Error}", field, ex.Message);
                }
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrape URL {Url}", url);
            return null;
        }
    }

    private async Task<Dictionary<string, object>?> ScrapeJsonAsync(
        ScraperManifest manifest,
        string? scraperName,
        string entityType,
        string url,
        CancellationToken ct,
        bool preserveCollections = false)
    {
        if (string.IsNullOrEmpty(scraperName) || !manifest.JsonScrapers.TryGetValue(scraperName, out var scraperDef))
        {
            _logger.LogWarning("JSON scraper definition '{Name}' not found", scraperName);
            return null;
        }

        var entitySelectors = GetEntitySelectors(scraperDef, entityType);
        if (entitySelectors == null || entitySelectors.Count == 0) return null;

        var common = scraperDef.Common ?? new Dictionary<string, string>();

        try
        {
            _logger.LogDebug("Fetching URL for JSON scrape: {Url}", url);
            var jsonStr = await FetchContentAsync(manifest, url, ct);
            var jsonDoc = JsonDocument.Parse(jsonStr);

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var (field, selectorObj) in entitySelectors)
            {
                try
                {
                    if (IsRelationshipField(field))
                    {
                        var items = ExtractJsonRelationshipItems(jsonDoc.RootElement, selectorObj, common);
                        if (items.Count > 0)
                            result[field] = items;
                    }
                    else
                    {
                        var values = ExtractJsonValues(jsonDoc.RootElement, selectorObj, common, treatPlainStringsAsFixed: false);
                        var value = ConvertSelectorValues(values, preserveCollections);

                        if (value is string textValue && !string.IsNullOrWhiteSpace(textValue))
                            result[field] = textValue;
                        else if (value is List<string> listValue && listValue.Count > 0)
                            result[field] = listValue;
                        else if (value is not null)
                            result[field] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("JSON selector error for field {Field}: {Error}", field, ex.Message);
                }
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrape JSON URL {Url}", url);
            return null;
        }
    }

    private async Task<Dictionary<string, object>?> ScrapeScriptAsync(ScraperManifest manifest, List<string>? scriptCmd, object input, CancellationToken ct)
    {
        var scriptTarget = scriptCmd == null || scriptCmd.Count == 0 ? "<missing>" : string.Join(' ', scriptCmd);
        _logger.LogWarning("Blocked unsupported script scraper execution for {ScriptTarget} from {SourcePath}", scriptTarget, manifest.FilePath);
        await Task.CompletedTask;
        return null;
    }

    private bool TryGetExtensionScraperRegistration(string scraperId, string entityType, [NotNullWhen(true)] out ExtensionScraperRegistration? registration)
    {
        if (_extensionScraperCache.TryGetValue(scraperId, out registration)
            && string.Equals(registration.Descriptor.Entity.ToString(), entityType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        registration = null;
        return false;
    }

    private async Task<Dictionary<string, object>?> ScrapeUrlWithExtensionAsync(ExtensionScraperRegistration registration, string url, CancellationToken ct)
    {
        if (registration.Descriptor.Entity != ScraperEntity.Scene || !registration.Descriptor.Capabilities.HasFlag(ScraperCapabilities.ByUrl))
            return null;

        var result = await registration.Provider.ScrapeSceneAsync(
            new ScraperRequest<SceneScrapeInput>(
                registration.Descriptor.Id,
                new SceneScrapeInput { Url = url, Urls = string.IsNullOrWhiteSpace(url) ? [] : [url] },
                BuildScraperPermissions(url)),
            ct);

        return ToResultDictionary(result);
    }

    private async Task<List<Dictionary<string, object>>?> ScrapeNameWithExtensionAsync(ExtensionScraperRegistration registration, string name, CancellationToken ct)
    {
        if (registration.Descriptor.Entity != ScraperEntity.Scene || !registration.Descriptor.Capabilities.HasFlag(ScraperCapabilities.ByName))
            return null;

        var results = await registration.Provider.SearchScenesAsync(
            new ScraperRequest<string>(registration.Descriptor.Id, name, new ScraperPermissions()),
            ct);

        return results.Select(ToResultDictionary).OfType<Dictionary<string, object>>().ToList();
    }

    private async Task<Dictionary<string, object>?> ScrapeFragmentWithExtensionAsync(ExtensionScraperRegistration registration, Dictionary<string, object> fragment, CancellationToken ct)
    {
        if (registration.Descriptor.Entity != ScraperEntity.Scene || !registration.Descriptor.Capabilities.HasFlag(ScraperCapabilities.ByFragment))
            return null;

        var input = BuildSceneInput(fragment);
        var result = await registration.Provider.ScrapeSceneAsync(
            new ScraperRequest<SceneScrapeInput>(registration.Descriptor.Id, input, BuildScraperPermissions(input.Url)),
            ct);

        return ToResultDictionary(result);
    }

    private static SceneScrapeInput BuildSceneInput(IReadOnlyDictionary<string, object> fragment)
    {
        var urls = GetFragmentStringList(fragment, "urls", "url");
        var primaryUrl = GetFragmentString(fragment, "url") ?? urls.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(primaryUrl) && !urls.Any(url => string.Equals(url, primaryUrl, StringComparison.OrdinalIgnoreCase)))
            urls.Insert(0, primaryUrl);

        return new SceneScrapeInput
        {
            Url = primaryUrl,
            Urls = urls,
            Title = GetFragmentString(fragment, "title", "name"),
            Code = GetFragmentString(fragment, "code", "id", "viewkey"),
            Date = GetFragmentString(fragment, "date"),
            Details = GetFragmentString(fragment, "details", "description"),
            Director = GetFragmentString(fragment, "director"),
        };
    }

    private static string? GetFragmentString(IReadOnlyDictionary<string, object> fragment, params string[] names)
    {
        foreach (var name in names)
        {
            var value = fragment.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            var converted = ConvertFragmentString(value);
            if (!string.IsNullOrWhiteSpace(converted))
                return converted;
        }

        return null;
    }

    private static List<string> GetFragmentStringList(IReadOnlyDictionary<string, object> fragment, params string[] names)
    {
        foreach (var name in names)
        {
            var entry = fragment.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            var values = ConvertFragmentStringList(entry.Value);
            if (values.Count > 0)
                return values;
        }

        return [];
    }

    private static string? ConvertFragmentString(object? value)
    {
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString()!.Trim(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
                _ => null,
            },
            _ => value.ToString(),
        };
    }

    private static List<string> ConvertFragmentStringList(object? value)
    {
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } element => element
                .EnumerateArray()
                .Select(item => ConvertFragmentString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => ConvertFragmentString(value) is { } singleValue
                ? [singleValue]
                : [],
        };
    }

    private static object? ConvertSelectorValues(List<string> values, bool preserveCollections)
    {
        if (values.Count == 0)
            return null;

        if (!preserveCollections)
            return values.Count == 1 ? values[0] : string.Join(", ", values);

        return values.Count == 1 ? values[0] : values;
    }

    private static List<Dictionary<string, object>>? ExpandNameSearchResults(Dictionary<string, object>? result)
    {
        if (result == null || result.Count == 0)
            return null;

        var candidateCount = result.Values.Select(GetCandidateValueCount).DefaultIfEmpty(0).Max();
        if (candidateCount <= 1)
            return [new Dictionary<string, object>(result, StringComparer.OrdinalIgnoreCase)];

        var candidates = new List<Dictionary<string, object>>();
        for (var index = 0; index < candidateCount; index++)
        {
            var candidate = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var (field, value) in result)
            {
                var extractedValue = ExtractCandidateValue(value, index, candidateCount);
                if (extractedValue != null)
                    candidate[field] = extractedValue;
            }

            if (candidate.Count > 0 && HasMeaningfulCandidateValue(candidate))
                candidates.Add(candidate);
        }

        return candidates.Count > 0 ? candidates : [new Dictionary<string, object>(result, StringComparer.OrdinalIgnoreCase)];
    }

    private static int GetCandidateValueCount(object value)
    {
        return value switch
        {
            List<string> values => values.Count,
            List<Dictionary<string, string>> values => values.Count,
            _ => 1,
        };
    }

    private static object? ExtractCandidateValue(object value, int index, int candidateCount)
    {
        return value switch
        {
            List<string> values => ExtractCandidateString(values, index, candidateCount),
            List<Dictionary<string, string>> values => ExtractCandidateRelationship(values, index, candidateCount),
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => value,
        };
    }

    private static object? ExtractCandidateString(List<string> values, int index, int candidateCount)
    {
        if (values.Count == 0)
            return null;

        if (values.Count == 1 || candidateCount == 1)
            return values[0];

        return index < values.Count && !string.IsNullOrWhiteSpace(values[index]) ? values[index] : null;
    }

    private static object? ExtractCandidateRelationship(List<Dictionary<string, string>> values, int index, int candidateCount)
    {
        if (values.Count == 0)
            return null;

        if (values.Count == 1 || candidateCount == 1)
            return new List<Dictionary<string, string>> { values[0] };

        return index < values.Count ? new List<Dictionary<string, string>> { values[index] } : null;
    }

    private static bool HasMeaningfulCandidateValue(Dictionary<string, object> candidate)
    {
        foreach (var (key, value) in candidate)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return true;

            if (value is List<Dictionary<string, string>> relationshipItems && relationshipItems.Count > 0)
                return true;

            if (string.Equals(key, "Title", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "URL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Url", StringComparison.OrdinalIgnoreCase))
            {
                return value != null;
            }
        }

        return false;
    }

    private static ScraperPermissions BuildScraperPermissions(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return new ScraperPermissions([uri.Host]);

        return new ScraperPermissions();
    }

    private static Dictionary<string, object>? ToResultDictionary<T>(T? value)
    {
        if (value == null)
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, object>>(
            JsonSerializer.Serialize(value, ExtensionScrapeJsonOptions),
            ExtensionScrapeJsonOptions);
    }

    private static List<string> GetSupportedScrapeNames(ScraperCapabilities capabilities)
    {
        var names = new List<string>();
        if (capabilities.HasFlag(ScraperCapabilities.ByUrl))
            names.Add("URL");
        if (capabilities.HasFlag(ScraperCapabilities.ByName))
            names.Add("Name");
        if (capabilities.HasFlag(ScraperCapabilities.ByFragment) || capabilities.HasFlag(ScraperCapabilities.ByQueryFragment))
            names.Add("Fragment");
        return names;
    }

    private sealed record ExtensionScraperRegistration(IScraperProvider Provider, ScraperDescriptor Descriptor);

    // Helper methods

    private static bool IsSupportedAction(ActionDefinitionBase definition) => !IsScriptAction(definition.Action ?? "scrapeXPath");

    private static bool IsSupportedAction(ByUrlDefinition definition) => !IsScriptAction(definition.Action ?? "scrapeXPath");

    private static bool IsScriptAction(string? action) => string.Equals(action, "script", StringComparison.OrdinalIgnoreCase);

    private void LogScriptScraperUnsupported(string scraperId, string entityType, string? action)
    {
        _logger.LogWarning("Blocked unsupported scraper action {Action} for {ScraperId}:{EntityType}", action, scraperId, entityType);
    }

    private static Dictionary<string, object>? GetEntitySelectors(MappedScraperDef scraperDef, string entityType)
    {
        return entityType switch
        {
            "scene" => scraperDef.Scene,
            "performer" => scraperDef.Performer,
            "gallery" => scraperDef.Gallery,
            "image" => scraperDef.Image,
            "group" or "movie" => scraperDef.Group,
            _ => null
        };
    }

    private static string? ResolveSelector(object selectorObj, Dictionary<string, string> common)
    {
        var selector = selectorObj switch
        {
            string s => s,
            Dictionary<object, object> dict when dict.TryGetValue("selector", out var s) => s?.ToString(),
            _ => null
        };

        if (selector == null) return null;

        foreach (var (key, value) in common)
            selector = selector.Replace(key, value);

        return selector;
    }

    private static Dictionary<string, string>? ResolveSubSelectors(object selectorObj, Dictionary<string, string> common)
    {
        if (selectorObj is not Dictionary<object, object> dict) return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in dict)
        {
            var k = key.ToString();
            if (k is "selector" or "fixed" or "concat" or "split" or "postProcess") continue;

            var selector = value switch
            {
                string s => s,
                Dictionary<object, object> subDict when subDict.TryGetValue("selector", out var s) => s?.ToString(),
                _ => null
            };

            if (selector != null)
            {
                foreach (var (ck, cv) in common)
                    selector = selector.Replace(ck, cv);
                result[k!] = selector;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private async Task<string> FetchContentAsync(ScraperManifest manifest, string url, CancellationToken ct)
    {
        var requestUrl = NormalizeRequestUrl(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        foreach (var header in manifest.Driver?.Headers ?? [])
        {
            if (!string.IsNullOrWhiteSpace(header.Key) && header.Value != null)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var cookieHeader = BuildCookieHeader(manifest, requestUrl);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                _logger.LogDebug(
                    "Scrape fetch for {Url} returned {StatusCode}; continuing with response body.",
                    requestUrl,
                    (int)response.StatusCode);
                return content;
            }

            response.EnsureSuccessStatusCode();
        }

        return content;
    }

    private static string? BuildCookieHeader(ScraperManifest manifest, string requestUrl)
    {
        var cookies = new List<string>();

        foreach (var scope in manifest.Driver?.Cookies ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scope.CookieUrl) &&
                !requestUrl.StartsWith(scope.CookieUrl, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var cookie in scope.Cookies)
            {
                if (!string.IsNullOrWhiteSpace(cookie.Name))
                    cookies.Add($"{cookie.Name}={cookie.Value ?? string.Empty}");
            }
        }

        return cookies.Count > 0 ? string.Join("; ", cookies) : null;
    }

    private static List<Dictionary<string, string>> ExtractXPathRelationshipItems(HtmlNode scope, object selectorObj, Dictionary<string, string> common)
    {
        var subSelectors = ResolveSubSelectorDefinitions(selectorObj);
        if (subSelectors.Count == 0)
            return [];

        var containerSelector = ResolveSelector(selectorObj, common);
        if (!string.IsNullOrWhiteSpace(containerSelector))
        {
            var containers = scope.SelectNodes(containerSelector);
            if (containers is { Count: > 0 })
            {
                var items = new List<Dictionary<string, string>>();
                foreach (var container in containers)
                {
                    var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (subField, subSelector) in subSelectors)
                    {
                        var entries = ExtractXPathValueEntries(container, subSelector, common, treatPlainStringsAsFixed: true);
                        if (entries.Count == 0)
                            continue;

                        item[subField] = entries[0].Value;
                        if (!item.ContainsKey("URL") && ShouldCaptureRelationshipUrl(subField) && !string.IsNullOrWhiteSpace(entries[0].Href))
                            item["URL"] = entries[0].Href!;
                    }

                    if (item.Count > 0)
                        items.Add(item);
                }

                if (items.Count > 0)
                    return items;
            }
        }

        var valuesByField = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (subField, subSelector) in subSelectors)
        {
            var entries = ExtractXPathValueEntries(scope, subSelector, common, treatPlainStringsAsFixed: true);
            valuesByField[subField] = entries.Select(entry => entry.Value).ToList();

            if (valuesByField.ContainsKey("URL") || !ShouldCaptureRelationshipUrl(subField))
                continue;

            var urls = entries
                .Select(entry => entry.Href)
                .Where(href => !string.IsNullOrWhiteSpace(href))
                .Select(href => href!)
                .ToList();

            if (urls.Count > 0)
                valuesByField["URL"] = urls;
        }

        return ZipRelationshipValues(valuesByField);
    }

    private static List<XPathValueEntry> ExtractXPathValueEntries(HtmlNode scope, object selectorObj, Dictionary<string, string> common, bool treatPlainStringsAsFixed)
    {
        if (TryGetFixedValue(selectorObj, treatPlainStringsAsFixed, out var fixedValue))
            return [new XPathValueEntry(fixedValue, null)];

        var selector = ResolveSelector(selectorObj, common);
        if (string.IsNullOrWhiteSpace(selector))
            return [];

        var navigator = scope.CreateNavigator();
        var iterator = navigator.Select(selector);
        var values = new List<XPathValueEntry>();

        while (iterator.MoveNext())
        {
            var current = iterator.Current;
            var rawValue = current?.Value;
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            var value = ApplyPostProcesses(HtmlEntity.DeEntitize(rawValue.Trim()), selectorObj);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var href = current?.Name == "href"
                ? current.Value
                : current?.GetAttribute("href", string.Empty);

            values.Add(new XPathValueEntry(value, string.IsNullOrWhiteSpace(href) ? null : href.Trim()));
        }

        return values;
    }

    private static List<Dictionary<string, string>> ExtractJsonRelationshipItems(JsonElement scope, object selectorObj, Dictionary<string, string> common)
    {
        var subSelectors = ResolveSubSelectorDefinitions(selectorObj);
        if (subSelectors.Count == 0)
            return [];

        return ZipRelationshipValues(subSelectors.ToDictionary(
            selector => selector.Key,
            selector => ExtractJsonValues(scope, selector.Value, common, treatPlainStringsAsFixed: true),
            StringComparer.OrdinalIgnoreCase));
    }

    private static List<Dictionary<string, string>> ZipRelationshipValues(Dictionary<string, List<string>> valuesByField)
    {
        var count = valuesByField.Count == 0 ? 0 : valuesByField.Values.Max(values => values.Count);
        var items = new List<Dictionary<string, string>>();

        for (var index = 0; index < count; index++)
        {
            var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (field, values) in valuesByField)
            {
                if (index < values.Count && !string.IsNullOrWhiteSpace(values[index]))
                    item[field] = values[index];
            }

            if (item.Count > 0)
                items.Add(item);
        }

        return items;
    }

    private static List<string> ExtractXPathValues(HtmlNode scope, object selectorObj, Dictionary<string, string> common, bool treatPlainStringsAsFixed)
    {
        if (TryGetFixedValue(selectorObj, treatPlainStringsAsFixed, out var fixedValue))
            return [fixedValue];

        var selector = ResolveSelector(selectorObj, common);
        if (string.IsNullOrWhiteSpace(selector))
            return [];

        var navigator = scope.CreateNavigator();
        var iterator = navigator.Select(selector);
        var values = new List<string>();

        while (iterator.MoveNext())
        {
            var current = iterator.Current?.Value;
            if (!string.IsNullOrWhiteSpace(current))
                values.Add(current.Trim());
        }

        return values
            .Select(value => ApplyPostProcesses(HtmlEntity.DeEntitize(value), selectorObj))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static List<string> ExtractJsonValues(JsonElement scope, object selectorObj, Dictionary<string, string> common, bool treatPlainStringsAsFixed)
    {
        if (TryGetFixedValue(selectorObj, treatPlainStringsAsFixed, out var fixedValue))
            return [fixedValue];

        var selector = ResolveSelector(selectorObj, common);
        if (string.IsNullOrWhiteSpace(selector))
            return [];

        return GetJsonValues(scope, selector)
            .Select(value => ApplyPostProcesses(value, selectorObj))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static Dictionary<string, object> ResolveSubSelectorDefinitions(object selectorObj)
    {
        if (selectorObj is not Dictionary<object, object> dict)
            return [];

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in dict)
        {
            var name = key.ToString();
            if (name is null || name is "selector" or "fixed" or "concat" or "split" or "postProcess")
                continue;

            result[name] = value;
        }

        return result;
    }

    private static bool TryGetFixedValue(object selectorObj, bool treatPlainStringsAsFixed, out string value)
    {
        switch (selectorObj)
        {
            case Dictionary<object, object> dict when dict.TryGetValue("fixed", out var fixedValue) && fixedValue != null:
                value = fixedValue.ToString()!.Trim();
                return !string.IsNullOrWhiteSpace(value);
            case string text when treatPlainStringsAsFixed && !LooksLikeSelector(text):
                value = text.Trim();
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool LooksLikeSelector(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;

        return trimmed.StartsWith("/")
            || trimmed.StartsWith(".")
            || trimmed.StartsWith("$")
            || trimmed.Contains("//", StringComparison.Ordinal)
            || trimmed.Contains('@')
            || trimmed.Contains('[', StringComparison.Ordinal)
            || trimmed.Contains('(', StringComparison.Ordinal)
            || trimmed.Contains('|', StringComparison.Ordinal)
            || trimmed.Contains("::", StringComparison.Ordinal);
    }

    private static string ApplyPostProcesses(string value, object selectorObj)
    {
        if (selectorObj is not Dictionary<object, object> dict || !dict.TryGetValue("postProcess", out var postProcess) || postProcess is not IEnumerable<object> steps)
            return value;

        var current = value;
        foreach (var step in steps)
        {
            if (step is not Dictionary<object, object> stepDict)
                continue;

            foreach (var (key, stepValue) in stepDict)
            {
                var operation = key.ToString();
                switch (operation)
                {
                    case "replace":
                        current = ApplyReplace(current, stepValue);
                        break;
                    case "parseDate":
                        current = ApplyParseDate(current, stepValue?.ToString());
                        break;
                    case "map":
                        current = ApplyMap(current, stepValue);
                        break;
                }
            }
        }

        return current.Trim();
    }

    private static string ApplyReplace(string value, object? stepValue)
    {
        if (stepValue is not IEnumerable<object> replacements)
            return value;

        var current = value;
        foreach (var replacement in replacements)
        {
            if (replacement is not Dictionary<object, object> replacementDict)
                continue;

            var pattern = replacementDict.TryGetValue("regex", out var regexValue) ? regexValue?.ToString() : null;
            var replaceWith = replacementDict.TryGetValue("with", out var withValue) ? withValue?.ToString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            current = Regex.Replace(current, pattern, replaceWith, RegexOptions.Singleline);
        }

        return current;
    }

    private static string ApplyParseDate(string value, string? format)
    {
        if (!string.IsNullOrWhiteSpace(format) && DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exactDate))
            return exactDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;
    }

    private static string ApplyMap(string value, object? stepValue)
    {
        if (stepValue is not Dictionary<object, object> map)
            return value;

        foreach (var (mapKey, mapValue) in map)
        {
            if (string.Equals(mapKey?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                return mapValue?.ToString() ?? value;
        }

        return value;
    }

    private static bool IsRelationshipField(string field) =>
        field is "Tags" or "Performers" or "Studio" or "Movies" or "Groups";

    private static bool ShouldCaptureRelationshipUrl(string field)
        => field is "Name" or "Title";

    private sealed record XPathValueEntry(string Value, string? Href);

    private static List<string> GetJsonValues(JsonElement element, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = new List<JsonElement> { element };

        foreach (var part in parts)
        {
            var next = new List<JsonElement>();
            foreach (var candidate in current)
            {
                if (part == "#")
                {
                    if (candidate.ValueKind == JsonValueKind.Array)
                        next.AddRange(candidate.EnumerateArray());
                    continue;
                }

                if (int.TryParse(part, out var index))
                {
                    if (candidate.ValueKind == JsonValueKind.Array && index >= 0 && index < candidate.GetArrayLength())
                        next.Add(candidate[index]);
                    continue;
                }

                if (candidate.ValueKind != JsonValueKind.Object)
                    continue;

                if (TryGetJsonProperty(candidate, part, out var value))
                    next.Add(value);
            }

            current = next;
            if (current.Count == 0)
                return [];
        }

        return current
            .Select(value => value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
                _ => null,
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // ===== Enhanced YAML Model for Execution =====

    private sealed class MappedScraperDef
    {
        [YamlMember(Alias = "common")]
        public Dictionary<string, string>? Common { get; init; }

        [YamlMember(Alias = "scene")]
        public Dictionary<string, object>? Scene { get; init; }

        [YamlMember(Alias = "performer")]
        public Dictionary<string, object>? Performer { get; init; }

        [YamlMember(Alias = "gallery")]
        public Dictionary<string, object>? Gallery { get; init; }

        [YamlMember(Alias = "image")]
        public Dictionary<string, object>? Image { get; init; }

        [YamlMember(Alias = "group")]
        public Dictionary<string, object>? Group { get; init; }
    }

    private abstract class ActionDefinitionBase
    {
        [YamlMember(Alias = "action")]
        public string? Action { get; init; }

        [YamlMember(Alias = "queryURL")]
        public string? QueryUrl { get; init; }

        [YamlMember(Alias = "queryURLReplace")]
        public Dictionary<string, List<RegexReplaceDefinition>>? QueryUrlReplace { get; init; }

        [YamlMember(Alias = "scraper")]
        public string? Scraper { get; init; }

        [YamlMember(Alias = "script")]
        public List<string>? Script { get; init; }
    }
}