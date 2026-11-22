using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BookingSystem.Services;

/// <summary>Upload rules shared by the S3 and local storage, and the S3 configuration check.</summary>
public static class StorageRules
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp"
    };

    public static readonly string[] S3Settings =
        ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_ENDPOINT_URL", "AWS_S3_BUCKET_NAME"];

    public static void Validate(IFormFile file)
    {
        if (file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (file.Length > MaxFileSizeBytes)
            throw new ArgumentException("File exceeds the 5 MB limit.");

        if (!ContentTypes.ContainsKey(Path.GetExtension(file.FileName)))
            throw new ArgumentException("Only JPG, PNG and WebP images are allowed.");
    }

    public static string ContentType(string fileName) =>
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    /// <summary>True when all S3 settings are present (Railway); otherwise local storage is used.</summary>
    public static bool IsS3Configured(IConfiguration config) =>
        S3Settings.All(name => !string.IsNullOrWhiteSpace(config[name]));
}
