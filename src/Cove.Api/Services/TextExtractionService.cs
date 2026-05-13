using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VersOne.Epub;

namespace Cove.Api.Services;

public sealed record TextExtractionMetadata(
    string Format,
    string? Title,
    string? Author,
    int? PageCount,
    int? WordCount,
    string? ExcerptText);

public sealed record TextExtractionContent(
    string Format,
    string RenderMode,
    string Content,
    int? PageCount,
    int? WordCount,
    string? Title,
    string? Author);

public sealed class TextExtractionService
{
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

    private static readonly HashSet<string> BlockHtmlElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "article",
        "aside",
        "blockquote",
        "body",
        "details",
        "div",
        "dl",
        "fieldset",
        "figcaption",
        "figure",
        "footer",
        "form",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "header",
        "li",
        "main",
        "nav",
        "ol",
        "p",
        "pre",
        "section",
        "summary",
        "table",
        "tbody",
        "tfoot",
        "thead",
        "tr",
        "ul",
    };

    public async Task<TextExtractionMetadata> ExtractMetadataAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await ExtractContentAsync(path, cancellationToken);
        var excerptSource = string.Equals(content.RenderMode, "html", StringComparison.OrdinalIgnoreCase)
            ? StripHtml(content.Content)
            : content.Content;
        return new TextExtractionMetadata(
            content.Format,
            content.Title,
            content.Author,
            content.PageCount,
            content.WordCount,
            BuildExcerpt(excerptSource));
    }

    public async Task<TextExtractionContent> ExtractContentAsync(string path, CancellationToken cancellationToken = default)
    {
        var format = NormalizeFormat(path);
        return format switch
        {
            "md" or "markdown" => await ExtractPlainTextFileAsync(path, format, renderMode: "markdown", cancellationToken),
            "txt" => await ExtractPlainTextFileAsync(path, format, renderMode: "text", cancellationToken),
            "htm" or "html" or "xhtml" => await ExtractHtmlFileAsync(path, format, cancellationToken),
            "pdf" => ExtractPdf(path),
            "epub" => ExtractEpub(path),
            _ => await ExtractPlainTextFileAsync(path, format, renderMode: "text", cancellationToken),
        };
    }

    private static async Task<TextExtractionContent> ExtractPlainTextFileAsync(
        string path,
        string format,
        string renderMode,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return new TextExtractionContent(
            format,
            renderMode,
            content,
            null,
            CountWords(content),
            Path.GetFileNameWithoutExtension(path),
            null);
    }

    private static async Task<TextExtractionContent> ExtractHtmlFileAsync(
        string path,
        string format,
        CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(path, cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var content = ExtractHtmlDocumentHtml(document);
        var plainText = ExtractHtmlDocumentText(document);
        var title = NormalizeInlineHtmlText(HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//head/title")?.InnerText ?? string.Empty));
        if (string.IsNullOrWhiteSpace(title))
        {
            title = TryGetHtmlMetaContent(document, "og:title", "twitter:title");
        }

        return new TextExtractionContent(
            format,
            "html",
            content,
            null,
            CountWords(plainText),
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title,
            TryGetHtmlMetaContent(document, "author", "article:author", "dc.creator", "twitter:creator"));
    }

    private static TextExtractionContent ExtractPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(ContentOrderTextExtractor.GetText(page));
        }

        var content = builder.ToString().Trim();
        return new TextExtractionContent(
            "pdf",
            "text",
            content,
            document.NumberOfPages,
            CountWords(content),
            document.Information?.Title ?? Path.GetFileNameWithoutExtension(path),
            document.Information?.Author);
    }

    private static TextExtractionContent ExtractEpub(string path)
    {
        var book = EpubReader.ReadBook(path);
        var htmlSections = new List<string>();
        var plainTextBuilder = new StringBuilder();

        foreach (var textContentFile in book.ReadingOrder)
        {
            var document = new HtmlDocument();
            document.LoadHtml(textContentFile.Content);
            var plainText = ExtractHtmlDocumentText(document);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                continue;
            }

            var htmlSection = ExtractHtmlDocumentHtml(document);
            if (!string.IsNullOrWhiteSpace(htmlSection))
            {
                htmlSections.Add($"<section>{htmlSection}</section>");
            }

            if (plainTextBuilder.Length > 0)
            {
                plainTextBuilder.AppendLine();
                plainTextBuilder.AppendLine();
            }

            plainTextBuilder.Append(plainText);
        }

        var plainTextContent = plainTextBuilder.ToString().Trim();
        var content = string.Join("\n", htmlSections);
        return new TextExtractionContent(
            "epub",
            "html",
            content,
            null,
            CountWords(plainTextContent),
            TryGetStringProperty(book, "Title") ?? Path.GetFileNameWithoutExtension(path),
            TryGetJoinedStringCollection(book, "AuthorList"));
    }

    private static string StripHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return ExtractHtmlDocumentText(document);
    }

    private static string ExtractHtmlDocumentHtml(HtmlDocument document)
    {
        var root = SelectHtmlContentRoot(document);
        var clone = root.CloneNode(deep: true);
        RemoveIgnoredHtmlNodes(clone);
        RemoveEmptyHtmlNodes(clone);
        if (!HasReadableHtmlStructure(clone))
        {
            var paragraphs = ExtractParagraphsFromText(HtmlEntity.DeEntitize(clone.InnerText));
            if (paragraphs.Count > 0)
                return "<article class=\"cove-readable-text\">" + string.Concat(paragraphs.Select(paragraph => $"<p>{System.Net.WebUtility.HtmlEncode(paragraph)}</p>")) + "</article>";
        }

        var html = (string.Equals(root.Name, "body", StringComparison.OrdinalIgnoreCase)
            ? clone.InnerHtml
            : clone.OuterHtml)
            .Trim();
        return string.IsNullOrWhiteSpace(html) ? string.Empty : html;
    }

    private static string ExtractHtmlDocumentText(HtmlDocument document)
    {
        var root = SelectHtmlContentRoot(document);
        var builder = new StringBuilder();

        foreach (var child in root.ChildNodes)
        {
            AppendHtmlNodeText(child, builder, preserveWhitespace: false);
        }

        return builder
            .ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static HtmlNode SelectHtmlContentRoot(HtmlDocument document)
    {
        foreach (var xpath in PrimaryHtmlContentXPaths)
        {
            var node = document.DocumentNode.SelectSingleNode(xpath);
            if (node != null && HasMeaningfulHtmlText(node))
            {
                return node;
            }
        }

        return document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
    }

    private static bool HasMeaningfulHtmlText(HtmlNode node)
        => !string.IsNullOrWhiteSpace(NormalizeInlineHtmlText(HtmlEntity.DeEntitize(node.InnerText)));

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

    private static bool HasReadableHtmlStructure(HtmlNode node)
        => node.SelectNodes(".//p | .//br | .//li | .//blockquote | .//pre | .//h1 | .//h2 | .//h3 | .//h4 | .//h5 | .//h6")?.Count > 0;

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

    private static void AppendHtmlNodeText(HtmlNode node, StringBuilder builder, bool preserveWhitespace)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Comment:
                return;
            case HtmlNodeType.Text:
                AppendHtmlText(builder, HtmlEntity.DeEntitize(node.InnerText), preserveWhitespace);
                return;
            case HtmlNodeType.Element:
                break;
            default:
                return;
        }

        var elementName = node.Name;
        if (IgnoredHtmlElements.Contains(elementName))
        {
            return;
        }

        if (string.Equals(elementName, "br", StringComparison.OrdinalIgnoreCase))
        {
            EnsureTrailingNewlines(builder, 1);
            return;
        }

        if (string.Equals(elementName, "hr", StringComparison.OrdinalIgnoreCase))
        {
            EnsureTrailingNewlines(builder, 2);
            return;
        }

        if (string.Equals(elementName, "li", StringComparison.OrdinalIgnoreCase))
        {
            EnsureTrailingNewlines(builder, 1);
            builder.Append("- ");
        }

        var nextPreserveWhitespace = preserveWhitespace || string.Equals(elementName, "pre", StringComparison.OrdinalIgnoreCase);
        foreach (var child in node.ChildNodes)
        {
            AppendHtmlNodeText(child, builder, nextPreserveWhitespace);
        }

        if (BlockHtmlElements.Contains(elementName))
        {
            EnsureTrailingNewlines(builder, string.Equals(elementName, "li", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
        }
    }

    private static void AppendHtmlText(StringBuilder builder, string value, bool preserveWhitespace)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = preserveWhitespace
            ? NormalizePreformattedHtmlText(value)
            : NormalizeInlineHtmlText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var shouldInsertSpace = builder.Length > 0
            && !char.IsWhiteSpace(builder[^1])
            && !StartsWithClosingPunctuation(normalized)
            && !EndsWithOpeningPunctuation(builder[^1]);
        if (shouldInsertSpace)
        {
            builder.Append(' ');
        }

        builder.Append(normalized);
    }

    private static string NormalizeInlineHtmlText(string value)
        => string.Join(' ', value
            .Replace('\u00A0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizePreformattedHtmlText(string value)
        => value
            .Replace('\u00A0', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n');

    private static bool StartsWithClosingPunctuation(string value)
        => value.Length > 0 && ".,!?;:)]}".Contains(value[0]);

    private static bool EndsWithOpeningPunctuation(char value)
        => "([{\"".Contains(value);

    private static void EnsureTrailingNewlines(StringBuilder builder, int count)
    {
        if (builder.Length == 0)
        {
            return;
        }

        while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]) && builder[^1] != '\n')
        {
            builder.Length -= 1;
        }

        var trailingNewlines = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
        {
            trailingNewlines += 1;
        }

        if (trailingNewlines < count)
        {
            builder.Append('\n', count - trailingNewlines);
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

    private static string? TryGetHtmlMetaContent(HtmlDocument document, params string[] names)
    {
        var metaNodes = document.DocumentNode.SelectNodes("//meta");
        if (metaNodes == null)
        {
            return null;
        }

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
            {
                return content;
            }
        }

        return null;
    }

    private static string NormalizeFormat(string path)
        => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static string? BuildExcerpt(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var normalized = string.Join(' ', content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 320 ? normalized : normalized[..320];
    }

    private static string? TryGetStringProperty(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName)?.GetValue(instance) as string;

    private static string? TryGetJoinedStringCollection(object instance, string propertyName)
    {
        if (instance.GetType().GetProperty(propertyName)?.GetValue(instance) is not IEnumerable<string> values)
        {
            return null;
        }

        var items = values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        return items.Length == 0 ? null : string.Join(", ", items);
    }

    private static readonly string[] PrimaryHtmlContentXPaths =
    [
        "//article",
        "//main",
        "//*[@role='main']",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' entry-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' post-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' article-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' story-body ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' story-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' storytext ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' chapter-content ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' chapter-body ')]",
        "//*[contains(concat(' ', translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' usertext-body ')]",
    ];
}