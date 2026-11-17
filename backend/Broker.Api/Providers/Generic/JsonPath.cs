using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broker.Api.Providers.Generic;

/// <summary>
/// Proste ścieżki JSON do mapowania pól: "price.daily", "data.items[]", "items[0].id".
/// "[]" na końcu znaczy tylko "tu jest tablica". Nazwy pól bez względu na wielkość liter.
/// </summary>
internal static class JsonPath
{
    /// <summary>Element pod ścieżką albo null, jak go nie ma (albo jest JSON null).</summary>
    public static JsonElement? Find(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return root.ValueKind == JsonValueKind.Null ? null : root;
        }

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (name, indexes) = Parse(segment);

            if (name.Length > 0)
            {
                if (current.ValueKind != JsonValueKind.Object || !TryGetProperty(current, name, out current))
                {
                    return null;
                }
            }

            foreach (var index in indexes)
            {
                if (index is null)
                {
                    // "[]": tylko znacznik tablicy
                    continue;
                }

                if (current.ValueKind != JsonValueKind.Array || index.Value >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index.Value];
            }
        }

        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    /// <summary>Wpisuje wartość pod ścieżkę, zakładając po drodze obiekty ("driver.email").</summary>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(root);

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject next)
            {
                next = [];
                current[segments[i]] = next;
            }

            current = next;
        }

        current[segments[^1]] = value;
    }

    public static string? AsString(JsonElement? element) => element?.ValueKind switch
    {
        null => null,
        JsonValueKind.String => element.Value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.Value.GetRawText()
    };

    public static decimal? AsDecimal(JsonElement? element)
    {
        if (element is { ValueKind: JsonValueKind.Number } number && number.TryGetDecimal(out var value))
        {
            return value;
        }

        return decimal.TryParse(AsString(element), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static int? AsInt(JsonElement? element)
    {
        var value = AsDecimal(element);
        return value is null ? null : (int)value.Value;
    }

    public static bool? AsBool(JsonElement? element)
    {
        switch (element?.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return AsDecimal(element) != 0;
            default:
                var text = AsString(element)?.Trim().ToUpperInvariant();
                return text switch
                {
                    "TRUE" or "1" or "YES" or "TAK" => true,
                    "FALSE" or "0" or "NO" or "NIE" => false,
                    _ => null
                };
        }
    }

    public static DateOnly? AsDate(JsonElement? element)
    {
        var text = AsString(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : null;
    }

    /// <summary>Czas jako UTC. Bez strefy w tekście traktujemy go jako UTC (tak samo jak przy naszym API).</summary>
    public static DateTime? AsUtcDateTime(JsonElement? element)
    {
        var text = AsString(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value.UtcDateTime
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>"items[0][]" daje nazwę "items" i indeksy [0, null] (null = "[]").</summary>
    private static (string Name, List<int?> Indexes) Parse(string segment)
    {
        var bracket = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracket < 0)
        {
            return (segment, []);
        }

        var indexes = new List<int?>();
        var rest = segment[bracket..];
        while (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                throw new FormatException($"Brak ']' w ścieżce JSON: {segment}");
            }

            var inside = rest[1..close].Trim();
            indexes.Add(inside.Length == 0 ? null : int.Parse(inside, NumberStyles.None, CultureInfo.InvariantCulture));
            rest = rest[(close + 1)..];
        }

        return (segment[..bracket], indexes);
    }
}
