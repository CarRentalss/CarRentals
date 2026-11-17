using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CarRental.Api.Tests;

// Fałszywe klienty SDK Azure. Klasy SDK mają chronione konstruktory i metody virtual właśnie po to,
// żeby dało się je podmienić w testach. Nic tu nie łączy się z Azure.

/// <summary>Kontener Blob w pamięci: nazwa pliku, zawartość i Content-Type.</summary>
public sealed class FakeBlobContainerClient : BlobContainerClient
{
    public Dictionary<string, (byte[] Content, string ContentType)> Blobs { get; } = [];

    public override BlobClient GetBlobClient(string blobName) => new FakeBlobClient(blobName, Blobs);
}

public sealed class FakeBlobClient(string name, Dictionary<string, (byte[] Content, string ContentType)> blobs) : BlobClient
{
    public override async Task<Response<BlobContentInfo>> UploadAsync(Stream content, BlobUploadOptions options, CancellationToken cancellationToken = default)
    {
        if (blobs.ContainsKey(name) && options.Conditions?.IfNoneMatch is not null)
        {
            throw new RequestFailedException(409, "BlobAlreadyExists");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        blobs[name] = (buffer.ToArray(), options.HttpHeaders.ContentType);

        // Wynik nie jest używany przez BlobFileStorage
        return null!;
    }

    public override Task<Stream> OpenReadAsync(BlobOpenReadOptions options, CancellationToken cancellationToken = default)
    {
        if (!blobs.TryGetValue(name, out var blob))
        {
            throw new RequestFailedException(404, "BlobNotFound");
        }

        return Task.FromResult<Stream>(new MemoryStream(blob.Content));
    }
}

/// <summary>Nadawca Service Bus, który zapamiętuje wiadomości (albo udaje, że kolejka nie działa).</summary>
public sealed class FakeServiceBusSender : ServiceBusSender
{
    private readonly List<ServiceBusMessage> _sent = [];

    public bool Fail { get; init; }

    public IReadOnlyList<ServiceBusMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Kolejka niedostępna (test).");
        }

        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }
}
