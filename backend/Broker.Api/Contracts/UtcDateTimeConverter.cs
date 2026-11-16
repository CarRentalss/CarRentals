using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broker.Api.Contracts;

/// <summary>
/// Wszystkie DateTime w JSON jako UTC z "Z". Z bazy i z Car Rental API czas przychodzi czasem bez strefy
/// (DateTimeKind.Unspecified), a wtedy przeglądarka wzięłaby go za czas lokalny.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(ToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
