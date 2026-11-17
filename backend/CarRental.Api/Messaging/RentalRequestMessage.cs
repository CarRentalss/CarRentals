using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CarRental.Api.Messaging;

// Plik wspólny dla Car Rental API (wysyła) i CarRental.Worker (odbiera).

/// <summary>Wiadomość na kolejce rental-requests: "jest nowy wniosek o wynajem, sprawdź go".</summary>
public sealed record RentalRequestMessage(Guid RentalId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Czyta wiadomość z kolejki. Zwraca null, gdy treść jest nieprawidłowa (taka idzie do dead-letter).</summary>
    public static RentalRequestMessage? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<RentalRequestMessage>(json, Json);
            return message is null || message.RentalId == Guid.Empty ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Ustawienia Azure Service Bus (sekcja ServiceBus). Bez connection stringa kolejki nie używamy.</summary>
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string? ConnectionString { get; set; }

    public string QueueName { get; set; } = "rental-requests";

    [MemberNotNullWhen(true, nameof(ConnectionString))]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
