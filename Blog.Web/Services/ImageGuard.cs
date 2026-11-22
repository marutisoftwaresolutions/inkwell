using SixLabors.ImageSharp;

namespace Blog.Web.Services;

/// <summary>A picture that is small as a file but too large once decoded. Refused before any decoding.</summary>
public sealed class ImageTooLargeException : Exception
{
    public ImageTooLargeException(string message) : base(message) { }
}

/// <summary>A file sent as a JPG, PNG, GIF or WebP picture whose bytes are not one. Refused before it is stored.</summary>
public sealed class NotAPictureException : Exception
{
    public const string Text = "That file is not a picture this site can read. Upload a JPG, PNG, GIF or WebP image, or send other files under their own type.";
    public NotAPictureException() : base(Text) { }
}

/// <summary>
/// The upload size limit bounds the file, not the picture: a one-megabyte PNG can declare tens of
/// thousands of pixels a side and need gigabytes to decode. This reads only the header
/// (<see cref="Image.Identify(Stream)"/>) and refuses a picture above <see cref="MaxPixels"/> or
/// <see cref="MaxFrames"/> before anything decodes it. Every place that decodes an uploaded picture
/// asks here first: upload, replace, crop and the responsive copies.
/// </summary>
public static class ImageGuard
{
    /// <summary>40 megapixels: above any camera phone, far below what exhausts memory (about 160 MB decoded).</summary>
    public const long MaxPixels = 40_000_000;
    /// <summary>Frames in an animated picture.</summary>
    public const int MaxFrames = 200;

    public const string Message = "That picture is too large to process. Resize it to under 40 megapixels (for example 7000 × 5000) and try again.";

    /// <summary>Whether a picture of this size may be decoded. Pure, so the limit itself is testable.</summary>
    public static bool Allows(int width, int height, int frames) =>
        width > 0 && height > 0 && (long)width * height <= MaxPixels && frames <= MaxFrames;

    /// <summary>Throws <see cref="ImageTooLargeException"/> for a picture over the limit. Bytes that are not a readable picture pass: the decoder reports those in its own way.</summary>
    public static void Check(byte[] bytes)
    {
        ImageInfo? info;
        try { info = Image.Identify(bytes); }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException) { return; }
        Require(info);
    }

    /// <summary>
    /// Whether the bytes open with the signature of a PNG, JPEG, GIF or WebP file. The signature and not the decoder's
    /// verdict, so an unusual but genuine picture the decoder cannot read is still stored as it always was, while text
    /// renamed to .png is not.
    /// </summary>
    public static bool IsPicture(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 12) return false;
        ReadOnlySpan<byte> b = bytes;
        return b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })           // PNG
            || b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })                                         // JPEG
            || b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)                                  // GIF
            || (b.StartsWith("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8));                    // WebP
    }

    public static void Check(string path)
    {
        ImageInfo? info;
        try { info = Image.Identify(path); }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException) { return; }
        Require(info);
    }

    private static void Require(ImageInfo? info)
    {
        if (info is null) return;
        if (!Allows(info.Width, info.Height, Math.Max(1, info.FrameMetadataCollection.Count)))
            throw new ImageTooLargeException(Message);
    }
}
