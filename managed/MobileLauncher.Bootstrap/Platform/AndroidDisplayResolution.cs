namespace MobileLauncher.Bootstrap.Platform;

public static class AndroidDisplayResolution
{
    public static (int Width, int Height) NormalizeLandscape(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Android display dimensions must be positive.");
        }

        return width >= height ? (width, height) : (height, width);
    }

    public static (int Width, int Height) Scale(int width, int height, int percent, int minWidth, int minHeight)
    {
        if (percent is not (60 or 75)) return (width, height);

        double minimumScale = Math.Max((double)minWidth / width, (double)minHeight / height);
        double scale = Math.Min(1, Math.Max(percent / 100.0, minimumScale));
        return ((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
    }
}
