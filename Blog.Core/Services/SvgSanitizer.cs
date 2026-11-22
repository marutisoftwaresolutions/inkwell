using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Blog.Core.Services;

/// <summary>
/// Makes an uploaded SVG safe to serve from the site's own origin. An SVG is a document, not a
/// picture: opened directly it can run script with the reader's or an admin's session, which is how
/// stored cross-site scripting through "image" uploads works. This keeps the drawing and removes
/// everything that can execute or load: script and foreignObject elements, event-handler attributes,
/// javascript: and non-image data: links, animations that rewrite a link, external stylesheets and
/// processing instructions. Document type declarations are ignored, so entity tricks cannot expand.
/// Pure; returns null when the input is not a well-formed SVG, and the caller refuses the file.
/// </summary>
public static partial class SvgSanitizer
{
    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "object", "embed", "handler", "listener", "audio", "video", "meta", "link",
    };

    private static readonly HashSet<string> AnimationElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "animate", "animateMotion", "animateTransform", "animateColor",
    };

    private static readonly HashSet<string> LinkAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "action", "formaction", "from", "to", "values", "by",
    };

    private static readonly HashSet<string> EmbeddingElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "use", "image", "feImage",
    };

    /// <summary>An SVG is a drawing, not a data file: anything larger than this is refused instead of parsed.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;
    /// <summary>How deeply elements may nest. Real drawings stay in the tens; thousands of levels only serve to stall a parser.</summary>
    public const int MaxDepth = 128;

    /// <summary>A forward-only pass that stops at the first element nested too deep, before any tree is built.</summary>
    private static bool DepthWithinLimit(byte[] input)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
            using var stream = new MemoryStream(input);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.Depth > MaxDepth) return false;
            return true;
        }
        catch (XmlException) { return false; }
    }

    public static byte[]? Sanitize(byte[] input)
    {
        if (input is null || input.Length == 0 || input.Length > MaxBytes) return null;
        if (!DepthWithinLimit(input)) return null;
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,   // a DOCTYPE is tolerated; nothing in it is processed
                XmlResolver = null,
                MaxCharactersInDocument = 20_000_000,
            };
            using var stream = new MemoryStream(input);
            using var reader = XmlReader.Create(stream, settings);
            doc = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException)
        {
            return null;   // not XML, or it leans on entities a DOCTYPE would have defined
        }

        if (doc.Root is null || !string.Equals(doc.Root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            return null;

        doc.DescendantNodes().Where(n => n is XProcessingInstruction or XDocumentType).ToList().ForEach(n => n.Remove());

        foreach (var element in doc.Descendants().ToList())
        {
            if (element.Document is null) continue;   // already removed together with an ancestor
            var name = element.Name.LocalName;

            if (RemovedElements.Contains(name)) { element.Remove(); continue; }

            if (AnimationElements.Contains(name))
            {
                var target = element.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("attributeName", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                if (target.Contains("href", StringComparison.OrdinalIgnoreCase) || target.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    element.Remove();
                    continue;
                }
            }

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && IsDangerousCss(element.Value)) { element.Remove(); continue; }

            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                var attr = attribute.Name.LocalName;
                var value = attribute.Value;

                if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase)) { attribute.Remove(); continue; }
                if (LinkAttributes.Contains(attr) && IsDangerousUrl(value)) { attribute.Remove(); continue; }
                if (attr.Equals("style", StringComparison.OrdinalIgnoreCase) && IsDangerousCss(value)) { attribute.Remove(); continue; }

                // An external <use> or <image> pulls a second, unsanitised document into this one.
                if (attr.Equals("href", StringComparison.OrdinalIgnoreCase) && EmbeddingElements.Contains(name)
                    && !value.TrimStart().StartsWith("#", StringComparison.Ordinal) && !SafeDataImage().IsMatch(Compact(value)))
                {
                    attribute.Remove();
                }
            }
        }

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
            doc.Save(writer);
        return output.ToArray();
    }

    /// <summary>True when the file name or the declared type says the upload is an SVG.</summary>
    public static bool IsSvg(string? fileName, string? mime) =>
        string.Equals(mime, "image/svg+xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(fileName ?? ""), ".svg", StringComparison.OrdinalIgnoreCase);

    private static bool IsDangerousUrl(string value)
    {
        var v = Compact(value);
        if (v.Contains("javascript:", StringComparison.OrdinalIgnoreCase) || v.Contains("vbscript:", StringComparison.OrdinalIgnoreCase)) return true;
        return v.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && !SafeDataImage().IsMatch(v);
    }

    private static bool IsDangerousCss(string css)
    {
        var v = Compact(css);
        return v.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || v.Contains("expression(", StringComparison.OrdinalIgnoreCase)
            || v.Contains("@import", StringComparison.OrdinalIgnoreCase)
            || v.Contains("behavior:", StringComparison.OrdinalIgnoreCase)
            || v.Contains("-moz-binding", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whitespace and control characters removed: browsers ignore them inside a scheme name.</summary>
    private static string Compact(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (!char.IsWhiteSpace(c) && !char.IsControl(c)) sb.Append(c);
        return sb.ToString();
    }

    [GeneratedRegex(@"^data:image/(png|jpe?g|gif|webp);base64,[A-Za-z0-9+/=]*$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeDataImage();
}
