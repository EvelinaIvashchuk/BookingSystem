using System.Net.Http.Headers;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BookingSystem.Services;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3  _s3;
    private readonly HttpClient _http;
    private readonly string     _bucket;
    private readonly string     _serviceUrl;
    private readonly ILogger<S3StorageService> _logger;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public S3StorageService(IConfiguration config, ILogger<S3StorageService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _http   = httpClientFactory.CreateClient("s3");

        var accessKey = config["AWS_ACCESS_KEY_ID"]     ?? throw new InvalidOperationException("AWS_ACCESS_KEY_ID is not set.");
        var secretKey = config["AWS_SECRET_ACCESS_KEY"] ?? throw new InvalidOperationException("AWS_SECRET_ACCESS_KEY is not set.");
        _serviceUrl   = (config["AWS_ENDPOINT_URL"]     ?? throw new InvalidOperationException("AWS_ENDPOINT_URL is not set.")).TrimEnd('/');
        _bucket       = config["AWS_S3_BUCKET_NAME"]    ?? throw new InvalidOperationException("AWS_S3_BUCKET_NAME is not set.");

        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        var s3Config = new AmazonS3Config
        {
            ServiceURL       = _serviceUrl,
            ForcePathStyle   = true,
            SignatureVersion = "4"
        };

        _s3 = new AmazonS3Client(credentials, s3Config);
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<string> UploadAsync(IFormFile file, string folder = "cars")
    {
        if (file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (file.Length > MaxFileSizeBytes)
            throw new ArgumentException("File exceeds the 5 MB limit.");

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException("Only JPG, PNG and WebP images are allowed.");

        var key = $"{folder}/{Guid.NewGuid()}{ext}";

        // Presigned PUT URL — pure local computation, no HTTP call
        var presignRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key        = key,
            Verb       = HttpVerb.PUT,
            Expires    = DateTime.UtcNow.AddMinutes(10),
            Protocol   = Protocol.HTTPS
        };
        presignRequest.Headers["Content-Type"] = file.ContentType;

        var putUrl = await _s3.GetPreSignedURLAsync(presignRequest);

        // Plain HttpClient PUT — no AWS SDK chunked encoding overhead
        await using var stream = file.OpenReadStream();
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

        using var requestMsg = new HttpRequestMessage(HttpMethod.Put, putUrl) { Content = content };
        var response = await _http.SendAsync(requestMsg);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Uploaded file to storage: {Key}", key);
        return key; // store key in DB, not full URL
    }

    // ── Stream for proxy ─────────────────────────────────────────────────────

    public async Task<(Stream Stream, string ContentType)> GetObjectAsync(string key)
    {
        var response = await _s3.GetObjectAsync(_bucket, key);
        var contentType = string.IsNullOrEmpty(response.Headers.ContentType)
            ? "application/octet-stream"
            : response.Headers.ContentType;
        return (response.ResponseStream, contentType);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task DeleteAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        // Ignore legacy full http URLs (external images, not in our bucket)
        if (key.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            await _s3.DeleteObjectAsync(_bucket, key);
            _logger.LogInformation("Deleted file from storage: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file from storage: {Key}", key);
        }
    }
}
