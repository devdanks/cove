using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Cove.Plugins;

namespace Cove.Api.Extensions;

public sealed class DirectFileDownloaderExtension : IDownloaderProvider
{
    private const string VideoDownloaderId = "builtin.direct-file/video";
    private const string ImageDownloaderId = "builtin.direct-file/image";
    private const string AudioDownloaderId = "builtin.direct-file/audio";
    private const string TextDownloaderId = "builtin.direct-file/text";
    private const string WebTextDownloaderId = "builtin.web-text-page/text";

    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".ts", ".mpeg", ".mpg"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"];
    private static readonly string[] AudioExtensions = [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus"];
    private static readonly string[] TextExtensions = [".txt", ".md", ".markdown", ".pdf", ".epub", ".rtf", ".nfo", ".log", ".srt", ".vtt", ".ass", ".ssa", ".lrc", ".html", ".htm"];

    private static readonly DownloaderDescriptor VideoDownloader = new(
        VideoDownloaderId,
        "Direct Video File",
        DownloaderEntity.Video,
        VideoExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor ImageDownloader = new(
        ImageDownloaderId,
        "Direct Image File",
        DownloaderEntity.Image,
        ImageExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor AudioDownloader = new(
        AudioDownloaderId,
        "Direct Audio File",
        DownloaderEntity.Audio,
        AudioExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor TextDownloader = new(
        TextDownloaderId,
        "Direct Text File",
        DownloaderEntity.Text,
        TextExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor WebTextDownloader = new(
        WebTextDownloaderId,
        "Web Text Page",
        DownloaderEntity.Text,
        ["http://*", "https://*"],
        DownloaderCapabilities.None);

    private static readonly IReadOnlyDictionary<string, DownloaderDescriptor> Downloaders = new Dictionary<string, DownloaderDescriptor>(StringComparer.OrdinalIgnoreCase)
    {
        [VideoDownloader.Id] = VideoDownloader,
        [ImageDownloader.Id] = ImageDownloader,
        [AudioDownloader.Id] = AudioDownloader,
        [TextDownloader.Id] = TextDownloader,
        [WebTextDownloader.Id] = WebTextDownloader,
    };

    private static readonly HashSet<string> IgnoredHtmlElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "canvas",
        "head",
        "aside",
        "audio",
        "button",
        "footer",
        "form",
        "header",
        "iframe",
        "img",
        "input",
        "link",
        "meta",
        "nav",
        "noscript",
        "option",
        "picture",
        "script",
        "select",
        "source",
        "style",
        "svg",
        "template",
        "textarea",
        "title",
        "video",
    };

    private static readonly Regex NoiseAttributePattern = new(
        @"(^|[-_\s])(ad|ads|advert|advertisement|banner|breadcrumb|comment|comments|cookie|footer|header|login|menu|modal|nav|navbar|pager|pagination|promo|recommend|related|share|sidebar|social|subscribe|toolbar|vote|rating)([-_\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] WebTextContentXPaths =
    [
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' story-body ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' story-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' storytext ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' chapter-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' chapter-body ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' entry-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' post-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' article-content ')]",
        "//article",
        "//main",
        "//*[@role='main']",
    ];

    public string Id => "builtin.direct-file";
    public string Name => "Direct File Downloader";
    public string Version => "1.0.0";
    public string? Description => "Downloads direct image, video, audio, and text file URLs without a site-specific downloader.";
    public string? Author => "Cove";
    public string? Url => null;
    public string? IconUrl => null;
    public IReadOnlyList<string> Categories => [ExtensionCategories.Downloader];

    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
    }

    public IReadOnlyList<DownloaderDescriptor> GetDownloaders() => [VideoDownloader, ImageDownloader, AudioDownloader, TextDownloader, WebTextDownloader];

    public Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
    {
        var matches = BuildMatches(url);
        return Task.FromResult(matches.FirstOrDefault());
    }

    public Task<IReadOnlyList<DownloaderUrlMatch>> MatchAllAsync(string url, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DownloaderUrlMatch>>(BuildMatches(url));

    private static IReadOnlyList<DownloaderUrlMatch> BuildMatches(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            return [];

        var matches = new List<DownloaderUrlMatch>();

        if (IsWebTextPageCandidate(uri))
        {
            matches.Add(new DownloaderUrlMatch(
                WebTextDownloader.Id,
                NormalizeUrl(uri),
                Label: BuildLabel(uri)));
        }

        if (!TryResolveDownloader(uri.AbsolutePath, out var descriptor))
            return matches;

        matches.Add(new DownloaderUrlMatch(
            descriptor.Id,
            NormalizeUrl(uri),
            Label: BuildLabel(uri)));
        return matches;
    }

    public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
    {
        if (!Downloaders.TryGetValue(request.DownloaderId, out var descriptor))
            return null;

        if (request.Entity != descriptor.SupportedEntity)
            throw new InvalidOperationException($"Downloader {request.DownloaderId} only supports {descriptor.SupportedEntity} downloads.");

        if (string.Equals(descriptor.Id, WebTextDownloader.Id, StringComparison.OrdinalIgnoreCase))
            return await DownloadWebTextPageAsync(request, host, ct);

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !TryResolveDownloader(uri.AbsolutePath, out var resolvedDescriptor))
            throw new InvalidOperationException("This downloader only supports direct media file URLs.");

        if (!string.Equals(resolvedDescriptor.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The URL does not match the expected media type for downloader {request.DownloaderId}.");

        host.ReportProgress(0.05d, "Fetching direct media file...");

        var client = host.HttpClients.CreateClient(string.Empty);
        using var response = await client.GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var fileName = ResolveFileName(response.Content.Headers.ContentDisposition, uri, descriptor.SupportedEntity, response.Content.Headers.ContentType?.MediaType);
        var destinationPath = Path.Combine(host.TempDirectory, SanitizeFileName(fileName));

        await using (var output = File.Create(destinationPath))
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            var contentLength = response.Content.Headers.ContentLength;

            while (true)
            {
                var read = await input.ReadAsync(buffer, ct);
                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                if (contentLength is long knownLength && knownLength > 0)
                {
                    var progress = Math.Clamp((double)totalRead / knownLength, 0d, 1d);
                    host.ReportProgress(0.1d + (progress * 0.85d), $"Downloading {fileName}...");
                }
            }
        }

        host.ReportProgress(0.98d, "Direct file download completed.");

        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);

        return new DownloaderResult(Path.GetFileName(destinationPath), fileName, Headers: headers);
    }

    private static async Task<DownloaderResult?> DownloadWebTextPageAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !IsWebTextPageCandidate(uri))
            throw new InvalidOperationException("This downloader only supports web text page URLs.");

        host.ReportProgress(0.05d, "Fetching web text page...");

        var client = host.HttpClients.CreateClient(string.Empty);
        using var response = await client.GetAsync(request.Url, ct);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var payload = await response.Content.ReadAsStringAsync(ct);
        var isHtml = IsHtmlPayload(mediaType, payload);
        var title = isHtml ? ExtractHtmlTitle(payload) : null;
        var fileName = ResolveWebTextFileName(response.Content.Headers.ContentDisposition, uri, mediaType, title, isHtml);
        var destinationPath = Path.Combine(host.TempDirectory, SanitizeFileName(fileName));

        if (isHtml)
        {
            payload = BuildReadableHtmlDocument(payload, title);
        }

        await File.WriteAllTextAsync(destinationPath, payload, Encoding.UTF8, ct);

        host.ReportProgress(0.98d, "Web text page download completed.");

        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);

        return new DownloaderResult(Path.GetFileName(destinationPath), fileName, Headers: headers);
    }

    private static bool TryResolveDownloader(string absolutePath, out DownloaderDescriptor descriptor)
    {
        var extension = Path.GetExtension(absolutePath)?.ToLowerInvariant();
        if (extension is null)
        {
            descriptor = default!;
            return false;
        }

        if (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = VideoDownloader;
            return true;
        }

        if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = ImageDownloader;
            return true;
        }

        if (AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = AudioDownloader;
            return true;
        }

        if (TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = TextDownloader;
            return true;
        }

        descriptor = default!;
        return false;
    }

    private static bool IsWebTextPageCandidate(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            return false;

        var extension = Path.GetExtension(uri.AbsolutePath)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            return true;

        if (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return extension is ".html" or ".htm" or ".xhtml";
    }

    private static bool IsHtmlPayload(string? mediaType, string payload)
    {
        if (mediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var trimmed = payload.TrimStart();
        return trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<p", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReadableHtmlDocument(string html, string? title)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        title = CleanWebTextTitle(title ?? ExtractHtmlTitle(document));
        var author = ReadMetaContent(document, "author", "article:author", "dc.creator", "twitter:creator");
        var root = SelectWebTextContentRoot(document);
        var clone = root.CloneNode(deep: true);
        RemoveIgnoredHtmlNodes(clone);

        var contentHtml = BuildReadableContentHtml(clone, root.Name);
        if (string.IsNullOrWhiteSpace(contentHtml))
            contentHtml = WebUtility.HtmlEncode(HtmlEntity.DeEntitize(root.InnerText));

        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html>");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        if (!string.IsNullOrWhiteSpace(title))
            builder.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        if (!string.IsNullOrWhiteSpace(author))
            builder.AppendLine($"<meta name=\"author\" content=\"{WebUtility.HtmlEncode(author)}\">");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine(contentHtml);
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static HtmlNode SelectWebTextContentRoot(HtmlDocument document)
    {
        var candidates = new List<HtmlNode>();
        foreach (var xpath in WebTextContentXPaths)
        {
            var nodes = document.DocumentNode.SelectNodes(xpath);
            if (nodes != null)
                candidates.AddRange(nodes);
        }

        var paragraphContainers = document.DocumentNode.SelectNodes("//article | //main | //section[count(.//p) >= 2] | //div[count(.//p) >= 2]");
        if (paragraphContainers != null)
            candidates.AddRange(paragraphContainers);

        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        candidates.Add(body);

        return candidates
            .Where(HasMeaningfulHtmlText)
            .DistinctBy(node => node.XPath)
            .Select(node => new { Node = node, Score = ScoreWebTextContentCandidate(node) })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault()?.Node ?? body;
    }

    private static double ScoreWebTextContentCandidate(HtmlNode node)
    {
        var clone = node.CloneNode(deep: true);
        RemoveIgnoredHtmlNodes(clone);

        var text = NormalizeInlineHtmlText(HtmlEntity.DeEntitize(clone.InnerText));
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var paragraphCount = clone.SelectNodes(".//p | .//li | .//br")?.Count ?? 0;
        var linkTextLength = clone.SelectNodes(".//a")?.Sum(link => NormalizeInlineHtmlText(HtmlEntity.DeEntitize(link.InnerText)).Length) ?? 0;
        var linkDensity = text.Length == 0 ? 0 : Math.Min(1d, (double)linkTextLength / text.Length);
        var classAndId = $"{node.GetAttributeValue("class", string.Empty)} {node.GetAttributeValue("id", string.Empty)}";
        var contentBonus = Regex.IsMatch(classAndId, "story|article|chapter|content|entry|post|text|body|read", RegexOptions.IgnoreCase) ? 80 : 0;
        var noisePenalty = NoiseAttributePattern.IsMatch(classAndId) ? 120 : 0;
        var bodyPenalty = string.Equals(node.Name, "body", StringComparison.OrdinalIgnoreCase) ? 60 : 0;

        return wordCount + paragraphCount * 18 + contentBonus - noisePenalty - bodyPenalty - linkDensity * wordCount * 1.5d;
    }

    private static bool HasMeaningfulHtmlText(HtmlNode node)
        => !string.IsNullOrWhiteSpace(NormalizeInlineHtmlText(HtmlEntity.DeEntitize(node.InnerText)));

    private static string BuildReadableContentHtml(HtmlNode clone, string rootName)
    {
        RemoveEmptyHtmlNodes(clone);

        if (!HasReadableHtmlStructure(clone))
        {
            var paragraphs = ExtractParagraphsFromText(HtmlEntity.DeEntitize(clone.InnerText));
            if (paragraphs.Count > 0)
                return "<article class=\"cove-readable-text\">" + string.Concat(paragraphs.Select(paragraph => $"<p>{WebUtility.HtmlEncode(paragraph)}</p>")) + "</article>";
        }

        return (string.Equals(rootName, "body", StringComparison.OrdinalIgnoreCase)
            ? clone.InnerHtml
            : clone.OuterHtml).Trim();
    }

    private static bool HasReadableHtmlStructure(HtmlNode node)
        => node.SelectNodes(".//p | .//br | .//li | .//blockquote | .//pre | .//h1 | .//h2 | .//h3 | .//h4 | .//h5 | .//h6")?.Count > 0;

    private static void RemoveIgnoredHtmlNodes(HtmlNode node)
    {
        for (var index = node.ChildNodes.Count - 1; index >= 0; index--)
        {
            var child = node.ChildNodes[index];
            if (child.NodeType == HtmlNodeType.Comment)
            {
                child.Remove();
                continue;
            }

            if (child.NodeType == HtmlNodeType.Element && (IgnoredHtmlElements.Contains(child.Name) || IsNoisyHtmlNode(child)))
            {
                child.Remove();
                continue;
            }

            RemoveIgnoredHtmlNodes(child);
        }
    }

    private static bool IsNoisyHtmlNode(HtmlNode node)
    {
        var role = node.GetAttributeValue("role", string.Empty);
        if (role.Equals("navigation", StringComparison.OrdinalIgnoreCase)
            || role.Equals("banner", StringComparison.OrdinalIgnoreCase)
            || role.Equals("contentinfo", StringComparison.OrdinalIgnoreCase)
            || role.Equals("complementary", StringComparison.OrdinalIgnoreCase)
            || role.Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var attributes = $"{node.GetAttributeValue("class", string.Empty)} {node.GetAttributeValue("id", string.Empty)}";
        return NoiseAttributePattern.IsMatch(attributes);
    }

    private static void RemoveEmptyHtmlNodes(HtmlNode node)
    {
        for (var index = node.ChildNodes.Count - 1; index >= 0; index--)
        {
            var child = node.ChildNodes[index];
            RemoveEmptyHtmlNodes(child);

            if (child.NodeType == HtmlNodeType.Element
                && child.ChildNodes.Count == 0
                && string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(child.InnerText))
                && !string.Equals(child.Name, "br", StringComparison.OrdinalIgnoreCase))
            {
                child.Remove();
            }
        }
    }

    private static List<string> ExtractParagraphsFromText(string text)
    {
        var normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var paragraphs = Regex.Split(normalizedText, @"\n\s*\n")
            .Select(NormalizeInlineHtmlText)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .ToList();

        if (paragraphs.Count > 1)
            return paragraphs;

        var lineParagraphs = normalizedText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeInlineHtmlText)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .ToList();

        return lineParagraphs.Count > 1 ? lineParagraphs : paragraphs;
    }

    private static string? ExtractHtmlTitle(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return ExtractHtmlTitle(document);
    }

    private static string? ExtractHtmlTitle(HtmlDocument document)
    {
        var title = NormalizeInlineHtmlText(HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//head/title")?.InnerText ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        return ReadMetaContent(document, "og:title", "twitter:title", "dc.title");
    }

    private static string? ReadMetaContent(HtmlDocument document, params string[] names)
    {
        var metaNodes = document.DocumentNode.SelectNodes("//meta");
        if (metaNodes == null)
            return null;

        foreach (var metaNode in metaNodes)
        {
            var metaName = metaNode.GetAttributeValue("name", string.Empty);
            var metaProperty = metaNode.GetAttributeValue("property", string.Empty);
            if (!names.Any(candidate => string.Equals(candidate, metaName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, metaProperty, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = NormalizeInlineHtmlText(HtmlEntity.DeEntitize(metaNode.GetAttributeValue("content", string.Empty)));
            if (!string.IsNullOrWhiteSpace(content))
                return content;
        }

        return null;
    }

    private static string? CleanWebTextTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string NormalizeInlineHtmlText(string value)
        => string.Join(' ', value
            .Replace('\u00A0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    private static string BuildLabel(Uri uri)
    {
        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
            return Uri.UnescapeDataString(fileName);

        return uri.Host;
    }

    private static string ResolveFileName(ContentDispositionHeaderValue? contentDisposition, Uri uri, DownloaderEntity entity, string? mediaType)
    {
        var candidate = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate.Trim('"').Trim();

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
            return Uri.UnescapeDataString(fileName);

        var inferredExtension = InferExtension(mediaType) ?? GetDefaultExtension(entity);
        return $"download{inferredExtension}";
    }

    private static string ResolveWebTextFileName(ContentDispositionHeaderValue? contentDisposition, Uri uri, string? mediaType, string? title, bool isHtml)
    {
        var defaultExtension = isHtml ? ".html" : InferExtension(mediaType) ?? ".txt";
        var candidate = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var trimmed = candidate.Trim('"').Trim();
            var extension = Path.GetExtension(trimmed);
            if (string.IsNullOrWhiteSpace(extension))
                return $"{trimmed}{defaultExtension}";

            return trimmed;
        }

        var titleCandidate = CleanWebTextTitle(title);
        if (!string.IsNullOrWhiteSpace(titleCandidate))
            return $"{titleCandidate}{defaultExtension}";

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var decoded = Uri.UnescapeDataString(fileName);
            return string.IsNullOrWhiteSpace(Path.GetExtension(decoded)) ? $"{decoded}{defaultExtension}" : decoded;
        }

        return $"download{defaultExtension}";
    }

    private static string? InferExtension(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/flac" => ".flac",
            "audio/wav" => ".wav",
            "text/plain" => ".txt",
            "text/markdown" => ".md",
            "text/html" => ".html",
            "application/pdf" => ".pdf",
            "application/epub+zip" => ".epub",
            _ => null,
        };
    }

    private static string GetDefaultExtension(DownloaderEntity entity)
    {
        return entity switch
        {
            DownloaderEntity.Video => ".mp4",
            DownloaderEntity.Image => ".jpg",
            DownloaderEntity.Audio => ".mp3",
            DownloaderEntity.Text => ".txt",
            _ => ".bin",
        };
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? "download.bin" : value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName;
    }
}
