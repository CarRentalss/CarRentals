using System.Text.Json;
using System.Text.Json.Nodes;

namespace CarRental.Api.Middleware;

/// <summary>
/// Maskowanie danych osobowych przed zapisaniem do logów (e-mail, imię, nazwisko).
/// Używane przez AuditMiddleware dla treści JSON.
/// </summary>
public static class SensitiveDataMasker
{
    public const string Mask = "***";

    /// <summary>Pola JSON z danymi osobowymi (porównanie bez wielkości liter).</summary>
    public static readonly IReadOnlySet<string> PersonalFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "email", "firstName", "lastName" };

    /// <summary>Np. z jan.kowalski@example.com robi j***@example.com.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Mask;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0
            ? Mask
            : $"{email[0]}{Mask}{email[at..]}";
    }

    /// <summary>Np. z Kowalski robi K***.</summary>
    public static string MaskName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Mask : $"{name.Trim()[0]}{Mask}";

    /// <summary>
    /// Zwraca JSON z zamaskowanymi polami email, firstName i lastName (na każdym poziomie zagnieżdżenia).
    /// Gdy tekst nie jest poprawnym JSON-em, zwraca null, żeby nie logować go w ogóle.
    /// </summary>
    public static string? MaskJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return null;
            }

            MaskNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MaskNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Najpierw lista kluczy, bo nie można zmieniać obiektu w trakcie przechodzenia po nim
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (PersonalFields.Contains(key))
                    {
                        obj[key] = MaskValue(key, child);
                    }
                    else if (child is not null)
                    {
                        MaskNode(child);
                    }
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        MaskNode(item);
                    }
                }
                break;
        }
    }

    private static JsonValue MaskValue(string key, JsonNode? value)
    {
        var text = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var s) ? s : null;
        var masked = string.Equals(key, "email", StringComparison.OrdinalIgnoreCase)
            ? MaskEmail(text)
            : MaskName(text);
        return JsonValue.Create(masked);
    }
}
