using CarRental.Api.Options;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Storage;

/// <summary>
/// Miejsce na załączniki (zdjęcia, PDF-y, później faktury).
/// Lokalnie dysk (<see cref="LocalFileStorage"/>), na Azure prywatny kontener Blob Storage (<see cref="BlobFileStorage"/>).
/// Wybór w Program.cs: Storage:BlobConnectionString albo ConnectionStrings:Blob.
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
        var key = StorageKeys.Create(_clock, extension);
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

/// <summary>Klucze plików wspólne dla dysku i Bloba.</summary>
public static class StorageKeys
{
    /// <summary>Nazwę pliku generujemy sami (rok i miesiąc/guid.rozszerzenie), nazwa od użytkownika nigdy nie trafia do ścieżki.</summary>
    public static string Create(TimeProvider clock, string extension) =>
        $"{clock.GetUtcNow():yyyyMM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

    /// <summary>Klucz w formacie, który sami tworzymy (bez "..", ukośnika na początku i znaków spoza listy).</summary>
    public static bool IsValid(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= 200
        && !key.StartsWith('/')
        && !key.Contains("..", StringComparison.Ordinal)
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '-' or '_');
}
