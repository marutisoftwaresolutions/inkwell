namespace Blog.Core.Services;

/// <summary>
/// The arithmetic of cropping an image in the media library: turning the rectangle a person asked for
/// into one that lies inside the picture, and the ready-made shapes the Desk offers. Pure.
/// </summary>
public static class ImageCrop
{
    /// <summary>The smallest side a crop may have, in pixels. Anything smaller is a slip of the hand, not a picture.</summary>
    public const int MinSide = 16;

    public readonly record struct Rect(int X, int Y, int Width, int Height);

    /// <summary>Shapes offered as one click: a key, the label on the button, and the ratio (width to height).</summary>
    public static readonly IReadOnlyList<(string Key, string Label, int RatioWidth, int RatioHeight)> Presets =
    [
        ("square", "Square (1:1)", 1, 1),
        ("wide", "Wide (16:9)", 16, 9),
        ("share", "Share image (1200 × 630)", 1200, 630),
        ("portrait", "Portrait (4:5)", 4, 5),
    ];

    /// <summary>
    /// The rectangle to cut, kept inside an image of the given size; null when there is nothing sensible
    /// to cut: the image size is unknown, the rectangle starts outside the picture, a side would be
    /// under <see cref="MinSide"/>, or it is the whole picture (a copy, not a crop).
    /// </summary>
    public static Rect? Clamp(int imageWidth, int imageHeight, int x, int y, int width, int height)
    {
        if (imageWidth < MinSide || imageHeight < MinSide) return null;
        if (x < 0) { width += x; x = 0; }
        if (y < 0) { height += y; y = 0; }
        if (x >= imageWidth || y >= imageHeight) return null;

        width = Math.Min(width, imageWidth - x);
        height = Math.Min(height, imageHeight - y);
        if (width < MinSide || height < MinSide) return null;
        if (x == 0 && y == 0 && width == imageWidth && height == imageHeight) return null;
        return new Rect(x, y, width, height);
    }

    /// <summary>The largest rectangle of the given shape that fits in the image, centred.</summary>
    public static Rect Centered(int imageWidth, int imageHeight, int ratioWidth, int ratioHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || ratioWidth <= 0 || ratioHeight <= 0) return new Rect(0, 0, Math.Max(imageWidth, 0), Math.Max(imageHeight, 0));

        // Compare the two shapes with integers: imageWidth / imageHeight against ratioWidth / ratioHeight.
        long byHeight = (long)imageHeight * ratioWidth / ratioHeight;      // width when the full height is used
        int width, height;
        if (byHeight <= imageWidth) { width = (int)byHeight; height = imageHeight; }
        else { width = imageWidth; height = (int)((long)imageWidth * ratioHeight / ratioWidth); }
        width = Math.Max(1, width); height = Math.Max(1, height);
        return new Rect((imageWidth - width) / 2, (imageHeight - height) / 2, width, height);
    }
}
