namespace CarRental.Api.Services;

public readonly record struct ValidAttachment(string Extension, string ContentType);

/// <summary>
/// Sprawdza plik od pracownika: tylko jpg, png i pdf do 10 MB (#19).
/// Patrzymy nie tylko na rozszerzenie, ale też na pierwsze bajty pliku,
/// żeby ktoś nie wrzucił np. pliku .exe przemianowanego na .pdf.
/// </summary>
public static class AttachmentValidator
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private sealed record AllowedType(string ContentType, byte[] Signature);

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    private static readonly Dictionary<string, AllowedType> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("image/jpeg", JpegSignature),
        [".jpeg"] = new("image/jpeg", JpegSignature),
        [".png"] = new("image/png", PngSignature),
        [".pdf"] = new("application/pdf", PdfSignature),
    };

    public static async Task<ServiceResult<ValidAttachment>> ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length == 0)
        {
            return ServiceError.Validation("file", "Plik jest pusty.");
        }

        if (file.Length > MaxBytes)
        {
            return ServiceError.Validation("file", "Plik może mieć najwyżej 10 MB.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !Allowed.TryGetValue(extension, out var allowed))
        {
            return ServiceError.Validation("file", "Dozwolone są tylko pliki jpg, png i pdf.");
        }

        var header = new byte[allowed.Signature.Length];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        if (read < header.Length || !header.AsSpan().SequenceEqual(allowed.Signature))
        {
            return ServiceError.Validation("file", "Zawartość pliku nie zgadza się z jego rozszerzeniem.");
        }

        return new ValidAttachment(extension.ToLowerInvariant(), allowed.ContentType);
    }
}
