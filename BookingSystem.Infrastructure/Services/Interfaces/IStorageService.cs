using Microsoft.AspNetCore.Http;

namespace BookingSystem.Services.Interfaces;

public interface IStorageService
{
    /// <summary>Uploads a file and returns the object key (e.g. "cars/uuid.jpg").</summary>
    Task<string> UploadAsync(IFormFile file, string folder = "cars");

    /// <summary>Streams the object for proxying. Caller must dispose the stream.</summary>
    Task<(Stream Stream, string ContentType)> GetObjectAsync(string key);

    /// <summary>Deletes an object by key. No-op if key is null/empty.</summary>
    Task DeleteAsync(string? key);
}
