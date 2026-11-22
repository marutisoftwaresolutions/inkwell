namespace Blog.Core.Services;

/// <summary>
/// Decides the extension an upload is stored under. The browser supplies both the file name and the
/// content type, and neither can be trusted alone: a file named <c>page.html</c> sent as
/// <c>application/pdf</c> passed the type check and was then stored, and served, as HTML. The stored
/// extension therefore always comes from this table: the file's own extension is kept only when it is
/// one of the extensions that belong to the declared type, otherwise the type's canonical extension
/// is used, and an unknown type gets <c>.bin</c>, which no browser renders. Pure.
/// </summary>
public static class UploadFileTypes
{
    private static readonly Dictionary<string, string[]> ExtensionsByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/gif"] = [".gif"],
        ["image/webp"] = [".webp"],
        ["image/avif"] = [".avif"],
        ["image/bmp"] = [".bmp"],
        ["image/tiff"] = [".tif", ".tiff"],
        ["image/x-icon"] = [".ico"],
        ["image/vnd.microsoft.icon"] = [".ico"],
        ["image/svg+xml"] = [".svg"],
        ["video/mp4"] = [".mp4"],
        ["video/webm"] = [".webm"],
        ["application/pdf"] = [".pdf"],
        ["application/msword"] = [".doc"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
        ["application/vnd.ms-excel"] = [".xls"],
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = [".xlsx"],
    };

    /// <summary>The extension (with the dot, lower case) to store a file of this declared type under.</summary>
    public static string SafeExtension(string? fileName, string? mime)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var type = (mime ?? "").Split(';')[0].Trim();
        if (!ExtensionsByMime.TryGetValue(type, out var allowed)) return ".bin";
        return allowed.Contains(ext) ? ext : allowed[0];
    }

    /// <summary>True when the declared type is one this table knows.</summary>
    public static bool IsKnown(string? mime) => ExtensionsByMime.ContainsKey((mime ?? "").Split(';')[0].Trim());
}
