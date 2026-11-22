using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Services;

/// <summary>
/// File-system storage used when S3 is not configured (local development, demos): car images are saved
/// under <c>rootPath</c> with the same keys as in S3 (<c>cars/{guid}.jpg</c>) and served by ImageController.
/// </summary>
public sealed class LocalStorageService : IStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(string rootPath, ILogger<LocalStorageService> logger)
    {
        _root   = Path.GetFullPath(rootPath);
        _logger = logger;
        Directory.CreateDirectory(_root);
        logger.LogWarning("S3 storage is not configured (AWS_* variables); images are stored locally in {Root}", _root);
    }

    public async Task<string> UploadAsync(IFormFile file, string folder = "cars")
    {
        StorageRules.Validate(file);

        var key  = $"{folder}/{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var target = File.Create(path))
            await file.CopyToAsync(target);

        _logger.LogInformation("Saved file to local storage: {Key}", key);
        return key;
    }

    public Task<(Stream Stream, string ContentType)> GetObjectAsync(string key)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
            throw new FileNotFoundException("Object not found in local storage.", key);

        Stream stream = File.OpenRead(path);
        return Task.FromResult((stream, StorageRules.ContentType(path)));
    }

    public Task DeleteAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        try
        {
            var path = Resolve(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file from local storage: {Key}", key);
        }
        return Task.CompletedTask;
    }

    /// <summary>Maps a key to a file under the root; keys that escape the root ("../") are rejected.</summary>
    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('\\', '/').TrimStart('/')));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return path;
    }
}
