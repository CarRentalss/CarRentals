using CarRental.Api.Storage;

namespace CarRental.Api.Tests;

/// <summary>Załączniki w Azure Blob Storage, na fałszywym kontenerze (bez Azure).</summary>
public class BlobFileStorageTests
{
    private static readonly byte[] Pdf = "%PDF-1.4\n%protokol\n"u8.ToArray();

    private static TestClock Clock() => new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Save_UploadsWithGeneratedKeyAndContentType()
    {
        var container = new FakeBlobContainerClient();
        var storage = new BlobFileStorage(container, Clock());

        var key = await storage.SaveAsync(new MemoryStream(Pdf), ".PDF", CancellationToken.None);

        // Klucz: rok i miesiąc/guid.rozszerzenie, nazwa od użytkownika nie trafia do Bloba
        Assert.Matches("^202611/[0-9a-f]{32}\\.pdf$", key);
        var blob = Assert.Single(container.Blobs);
        Assert.Equal(key, blob.Key);
        Assert.Equal("application/pdf", blob.Value.ContentType);
        Assert.Equal(Pdf, blob.Value.Content);
    }

    [Fact]
    public async Task OpenRead_ExistingBlob_ReturnsContent()
    {
        var container = new FakeBlobContainerClient();
        var storage = new BlobFileStorage(container, Clock());
        var key = await storage.SaveAsync(new MemoryStream(Pdf), ".pdf", CancellationToken.None);

        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);

        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(Pdf, copy.ToArray());
    }

    [Fact]
    public async Task OpenRead_MissingBlob_ReturnsNull()
    {
        var storage = new BlobFileStorage(new FakeBlobContainerClient(), Clock());

        var stream = await storage.OpenReadAsync("202611/00000000000000000000000000000000.pdf", CancellationToken.None);

        Assert.Null(stream);
    }

    [Theory]
    [InlineData("../tajne.pdf")]
    [InlineData("/202611/a.pdf")]
    [InlineData("202611/a b.pdf")]
    public async Task OpenRead_KeyNotInOurFormat_Throws(string key)
    {
        var storage = new BlobFileStorage(new FakeBlobContainerClient(), Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".bin", "application/octet-stream")]
    public void ContentTypeFor_KnownExtensions(string extension, string expected)
    {
        Assert.Equal(expected, BlobFileStorage.ContentTypeFor(extension));
    }
}
