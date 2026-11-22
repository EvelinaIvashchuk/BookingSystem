using BookingSystem.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingSystem.Tests.Services;

public sealed class LocalStorageServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("storage-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private LocalStorageService CreateSut() => new(_root, NullLogger<LocalStorageService>.Instance);

    private static IFormFile Image(string name, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "ImageFile", name) { Headers = new HeaderDictionary(), ContentType = "image/png" };

    [Fact]
    public async Task Upload_ThenGet_ReturnsSameBytesUnderS3StyleKey()
    {
        var sut = CreateSut();
        byte[] bytes = [1, 2, 3, 4];

        var key = await sut.UploadAsync(Image("car.PNG", bytes));
        var (stream, contentType) = await sut.GetObjectAsync(key);

        key.Should().MatchRegex(@"^cars/[0-9a-f-]{36}\.png$");
        contentType.Should().Be("image/png");
        using var copy = new MemoryStream();
        await using (stream) await stream.CopyToAsync(copy);
        copy.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task Delete_RemovesFile_AndIgnoresExternalUrls()
    {
        var sut = CreateSut();
        var key = await sut.UploadAsync(Image("car.jpg", [9]));

        await sut.DeleteAsync("https://example.com/car.jpg");
        await sut.DeleteAsync(key);

        var get = () => sut.GetObjectAsync(key);
        await get.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("cars/../../secret.txt")]
    public async Task Get_RejectsKeysOutsideTheRoot(string key)
    {
        var get = () => CreateSut().GetObjectAsync(key);

        await get.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Upload_RejectsNonImageFiles()
    {
        var upload = () => CreateSut().UploadAsync(Image("notes.txt", [1]));

        await upload.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void IsS3Configured_RequiresAllAwsSettings()
    {
        var all = StorageRules.S3Settings.ToDictionary(n => n, _ => (string?)"x");
        var partial = new Dictionary<string, string?>(all) { ["AWS_S3_BUCKET_NAME"] = "" };

        StorageRules.IsS3Configured(new ConfigurationBuilder().AddInMemoryCollection(all).Build()).Should().BeTrue();
        StorageRules.IsS3Configured(new ConfigurationBuilder().AddInMemoryCollection(partial).Build()).Should().BeFalse();
    }
}
