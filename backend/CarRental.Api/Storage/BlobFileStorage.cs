using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CarRental.Api.Storage;

/// <summary>
/// Załączniki w prywatnym kontenerze Azure Blob Storage (domyślnie "attachments").
/// Kontener nie ma publicznego dostępu: plik wychodzi tylko przez GET /api/v1/employee/rentals/{id}/attachment
/// po sprawdzeniu roli Employee (API czyta blob i oddaje strumień).
/// </summary>
public sealed class BlobFileStorage(BlobContainerClient container, TimeProvider clock) : IFileStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        var key = StorageKeys.Create(clock, extension);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = ContentTypeFor(extension) },
            // Nie nadpisujemy istniejącego pliku (klucz jest losowy, ale na wszelki wypadek)
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        await container.GetBlobClient(key).UploadAsync(content, options, cancellationToken);
        return key;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!StorageKeys.IsValid(key))
        {
            throw new InvalidOperationException("Nieprawidłowy klucz pliku.");
        }

        try
        {
            return await container.GetBlobClient(key)
                .OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Typ pliku w Blobie (Content-Type przy podglądzie w portalu). Do API i tak idzie typ zapisany w bazie.</summary>
    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
