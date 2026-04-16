namespace BookingSystem.Helpers;

public static class ImageHelper
{
    /// <summary>
    /// Returns the correct src for a car image:
    ///   - null/empty          → empty string (caller shows placeholder)
    ///   - starts with "http"  → legacy external URL, use as-is
    ///   - otherwise           → object key, route through our image proxy
    /// </summary>
    public static string Src(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return string.Empty;
        if (imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return imageUrl;
        return $"/image/{imageUrl}";
    }
}
