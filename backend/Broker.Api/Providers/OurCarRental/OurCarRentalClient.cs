using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broker.Api.Contracts;
using Broker.Api.Domain;
using Microsoft.Extensions.Options;

namespace Broker.Api.Providers.OurCarRental;

/// <summary>
/// Klient naszego Car Rental API (typed HttpClient). Adapter: tłumaczy odpowiedzi API na wspólny model Brokera.
/// Każde wywołanie idzie z kluczem brokera w nagłówku x-api-key.
/// </summary>
public sealed class OurCarRentalClient(HttpClient http, IOptions<ProvidersOptions> options) : ICarRentalProvider
{
    public const string ApiKeyHeader = "x-api-key";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };

    private ProviderEndpointOptions Settings => options.Value.Our;

    public string Code => Provider.OurCode;

    public string DisplayName => string.IsNullOrWhiteSpace(Settings.DisplayName) ? "Car Rentals" : Settings.DisplayName;

    public bool IsConfigured => Settings.IsConfigured;

    public async Task<IReadOnlyList<ProviderCar>> GetCarsAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/v1/cars");
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var cars = await response.Content.ReadFromJsonAsync<List<OurCarDto>>(Json, cancellationToken) ?? [];
        return cars.Select(Map).ToList();
    }

    public async Task<ProviderCar?> GetCarAsync(string carId, CancellationToken cancellationToken)
    {
        // U nas id auta to liczba, inne id na pewno nie istnieje
        if (!int.TryParse(carId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return null;
        }

        using var request = CreateRequest(HttpMethod.Get, $"api/v1/cars/{id}");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var car = await response.Content.ReadFromJsonAsync<OurCarDto>(Json, cancellationToken);
        return car is null ? null : Map(car);
    }

    public async Task<ProviderQuote> CreateOfferAsync(OfferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!int.TryParse(query.CarId, NumberStyles.None, CultureInfo.InvariantCulture, out var carId) || carId <= 0)
        {
            throw new ProviderRejectedException("Nie znaleziono samochodu.", StatusCodes.Status404NotFound);
        }

        using var request = CreateRequest(HttpMethod.Post, "api/v1/offers");
        request.Content = JsonContent.Create(
            new OurCreateOfferDto(carId, query.Age, query.LicenseYears, query.Location, query.StartDate, query.EndDate),
            options: Json);

        using var response = await http.SendAsync(request, cancellationToken);

        // 400 (walidacja), 404 (brak auta), 409 (zajęte) to odmowa z komunikatem dla użytkownika, a nie awaria
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            var message = await ProblemReader.ReadMessageAsync(response, "Wypożyczalnia odrzuciła zapytanie.", cancellationToken);
            throw new ProviderRejectedException(message, (int)response.StatusCode);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var offer = await response.Content.ReadFromJsonAsync<OurOfferDto>(Json, cancellationToken)
            ?? throw new InvalidOperationException("Pusta odpowiedź z Car Rental API.");

        return new ProviderQuote(
            offer.OfferId.ToString(),
            offer.CarId.ToString(CultureInfo.InvariantCulture),
            offer.DailyPrice,
            offer.DailyInsurancePrice,
            offer.TotalPrice,
            offer.Currency,
            offer.StartDate,
            offer.EndDate,
            offer.Days,
            UtcDateTimeConverter.ToUtc(offer.ExpiresAt));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(ApiKeyHeader, Settings.ApiKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ProblemReader.ReadMessageAsync(response, response.ReasonPhrase ?? "Błąd", cancellationToken);
        throw new HttpRequestException($"Car Rental API zwróciło {(int)response.StatusCode}: {message}", null, response.StatusCode);
    }

    private static ProviderCar Map(OurCarDto car) =>
        new(car.Id.ToString(CultureInfo.InvariantCulture), car.Make, car.Model, car.Year, car.Location, car.Available);

}

/// <summary>Czyta komunikat z ProblemDetails (title albo pierwszy błąd walidacji).</summary>
internal static class ProblemReader
{
    public static async Task<string> ReadMessageAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        var problem = await ReadAsync(response, cancellationToken);
        return problem?.Message ?? fallback;
    }

    public static async Task<(string? Field, string Message)?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                    {
                        var message = field.Value[0].GetString();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            return (ToCamelCase(field.Name), message);
                        }
                    }
                }
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return (null, title.GetString()!);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
}
