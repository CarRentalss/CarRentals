using CarRental.Api.Images;
using CarRental.Api.Options;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Tests;

/// <summary>Składanie adresu zdjęcia z nazwy pliku w bazie i Images:BaseUrl.</summary>
public class CarImagesTests
{
    private static CarImages Images(string? baseUrl, Dictionary<string, string>? credits = null)
    {
        var options = new ImageOptions { BaseUrl = baseUrl };
        foreach (var (file, credit) in credits ?? [])
        {
            options.Credits[file] = credit;
        }

        return new CarImages(new StaticOptionsMonitor(options));
    }

    [Theory]
    [InlineData("https://st.blob.core.windows.net/cars/", "toyota-corolla-2022.jpg", "https://st.blob.core.windows.net/cars/toyota-corolla-2022.jpg")]
    [InlineData("https://st.blob.core.windows.net/cars", "toyota-corolla-2022.jpg", "https://st.blob.core.windows.net/cars/toyota-corolla-2022.jpg")]
    [InlineData("https://st.blob.core.windows.net/cars/", "bmw seria 3.jpg", "https://st.blob.core.windows.net/cars/bmw%20seria%203.jpg")]
    [InlineData(null, "https://example.com/auto.jpg", "https://example.com/auto.jpg")]
    public void UrlFor_BuildsAddress(string? baseUrl, string stored, string expected)
    {
        Assert.Equal(expected, Images(baseUrl).UrlFor(stored));
    }

    [Theory]
    [InlineData(null, "toyota-corolla-2022.jpg")]
    [InlineData("", "toyota-corolla-2022.jpg")]
    [InlineData("https://st.blob.core.windows.net/cars/", null)]
    [InlineData("https://st.blob.core.windows.net/cars/", " ")]
    public void UrlFor_NoBaseUrlOrNoFile_ReturnsNull(string? baseUrl, string? stored)
    {
        Assert.Null(Images(baseUrl).UrlFor(stored));
    }

    [Fact]
    public void CreditFor_ByFileName()
    {
        var images = Images("https://st.blob.core.windows.net/cars/", new() { ["audi-a4-2022.jpg"] = "Autor, CC BY 2.0" });

        Assert.Equal("Autor, CC BY 2.0", images.CreditFor("audi-a4-2022.jpg"));
        Assert.Null(images.CreditFor("opel-corsa-2021.jpg"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://st.blob.core.windows.net/cars/", true)]
    [InlineData("st.blob.core.windows.net/cars", false)]
    [InlineData("ftp://st/cars", false)]
    public void Options_ValidateBaseUrl(string? baseUrl, bool valid)
    {
        Assert.Equal(valid, new ImageOptions { BaseUrl = baseUrl }.IsValid());
    }

    private sealed class StaticOptionsMonitor(ImageOptions value) : IOptionsMonitor<ImageOptions>
    {
        public ImageOptions CurrentValue => value;

        public ImageOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ImageOptions, string?> listener) => null;
    }
}
