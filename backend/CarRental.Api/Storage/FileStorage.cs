using CarRental.Api.Options;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Storage;

/// <summary>
/// Miejsce na załączniki (zdjęcia, PDF-y, później faktury).
/// Teraz zapis na dysk, w KM3 dojdzie implementacja dla prywatnego kontenera Azure Blob Storage.
/// </summary>
public interface IFileStorage
{
    /// <summary>Zapisuje plik i zwraca jego klucz (ścieżkę w magazynie).</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);

    /// <summary>Otwiera plik do odczytu albo zwraca null, gdy go nie ma.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly TimeProvider _clock;

    public LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment, TimeProvider clock)
    {
        var path = options.Value.LocalPath;
        _root = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
        _clock = clock;
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        // Nazwę pliku generujemy sami, nazwa od użytkownika nigdy nie trafia do ścieżki
        var key = $"{_clock.GetUtcNow():yyyyMM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = ToFullPath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(key);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    private string ToFullPath(string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, key));

        // Ochrona przed wyjściem poza katalog (np. klucz z "..")
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nieprawidłowy klucz pliku.");
        }

        return fullPath;
    }
}
