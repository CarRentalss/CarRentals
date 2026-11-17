using Azure.Messaging.ServiceBus;

namespace CarRental.Api.Messaging;

/// <summary>Daje znać, że jest nowy wniosek o wynajem do sprawdzenia.</summary>
public interface IRentalRequestPublisher
{
    Task PublishAsync(Guid rentalId, CancellationToken cancellationToken);
}

/// <summary>
/// Na Azure: wiadomość na kolejkę rental-requests, odbiera ją CarRental.Worker.
/// MessageId = numer wynajmu, więc ten sam wniosek nie zostanie przetworzony dwa razy (Worker i tak sprawdza status).
/// </summary>
public sealed class ServiceBusRentalRequestPublisher(ServiceBusSender sender) : IRentalRequestPublisher
{
    public Task PublishAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(new RentalRequestMessage(rentalId).ToJson())
        {
            MessageId = rentalId.ToString(),
            ContentType = "application/json",
            Subject = "rental-requested"
        };

        return sender.SendMessageAsync(message, cancellationToken);
    }
}

/// <summary>
/// Lokalnie i w testach (bez ServiceBus:ConnectionString): nic nie wysyłamy,
/// wniosek odbierze RentalConfirmationWorker w procesie API.
/// </summary>
public sealed class InProcessRentalRequestPublisher : IRentalRequestPublisher
{
    public Task PublishAsync(Guid rentalId, CancellationToken cancellationToken) => Task.CompletedTask;
}
