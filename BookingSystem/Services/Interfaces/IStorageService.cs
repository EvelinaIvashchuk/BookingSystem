using Microsoft.AspNetCore.Http;

namespace BookingSystem.Services.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file to the configured bucket and returns the public URL.
    /// </summary>
    Task<string> UploadAsync(IFormFile file, string folder = "cars");

    /// <summary>
    /// Deletes a file by its public URL. No-op if URL is null/empty or not from this bucket.
    /// </summary>
    Task DeleteAsync(string? publicUrl);
}
