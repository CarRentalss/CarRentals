using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Api.Contracts;
using Broker.Api.Providers.OurCarRental;

namespace Broker.Api.Providers.Generic;

/// <summary>
/// Wypożyczalnia sterowana konfiguracją (Adapter bez własnej klasy). Ścieżki, metody HTTP, uwierzytelnienie
/// i nazwy pól bierze z sekcji Providers:&lt;Klucz&gt;. Błędy obsługuje tak jak OurCarRentalClient:
/// odmowa (400, 404, 409 itd.) to ProviderRejectedException, awaria (401, 5xx, zła odpowiedź) to HttpRequestException
/// albo JsonException, przekroczenie TimeoutSeconds to TaskCanceledException. Agregator robi z tego skippedProviders.
/// </summary>
public sealed class ConfigurableHttpProvider(
    string key,
    GenericProviderOptions settings,
    HttpClient http,
    OAuthTokenCache tokens,
    TimeProvider clock,
    ILogger<ConfigurableHttpProvider> logger) : ICarRentalProvider
{
    // Kody, które są odmową z komunikatem dla użytkownika, a nie awarią (tak jak w OurCarRentalClient, plus 422)
    private static readonly int[] RejectOnOffer = [400, 404, 409, 422];
    private static readonly int[] RejectOnRental = [400, 404, 409, 410, 422];
    private static readonly int[] RejectOnReturn = [400, 403, 404, 409, 422];

    public string Key { get; } = key;

    public string Code { get; } = ResolveCode(key, settings);

    public string DisplayName => string.IsNullOrWhiteSpace(settings.DisplayName) ? Key : settings.DisplayName;

    public bool IsEnabled => settings.Enabled ?? false;

    public bool IsConfigured => settings.IsConfigured;

    private ProviderOperationsOptions Operations => settings.Operations;

    private ProviderMappingOptions Mapping => settings.Mapping;

    /// <summary>Nazwa HttpClienta w IHttpClientFactory dla danej sekcji.</summary>
    public static string HttpClientName(string key) => $"provider:{key}";

    /// <summary>Kod z ustawienia Code albo z nazwy sekcji w kebab-case (OtherGroup daje other-group).</summary>
    public static string ResolveCode(string key, GenericProviderOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(settings.Code))
        {
            return settings.Code.Trim();
        }

        var builder = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(key[i - 1]))
            {
                builder.Append('-');
            }

            builder.Append(c == '_' ? '-' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public async Task<IReadOnlyList<ProviderCar>> GetCarsAsync(CancellationToken cancellationToken)
    {
        var operation = Operations.Cars;
        return await SendAsync(operation, Placeholders(), body: null, cancellationToken, async (response, ct) =>
        {
            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            var list = JsonPath.Find(document.RootElement, operation.ResultPath);
            if (list is not { ValueKind: JsonValueKind.Array } array)
            {
                throw new JsonException($"{DisplayName}: w odpowiedzi nie ma listy aut ({Describe(operation.ResultPath)}).");
            }

            return (IReadOnlyList<ProviderCar>)array.EnumerateArray().Select(MapCar).ToList();
        });
    }

    public async Task<ProviderCar?> GetCarAsync(string carId, CancellationToken cancellationToken)
    {
        var operation = Operations.Car;
        if (string.IsNullOrWhiteSpace(operation.Path))
        {
            // Wypożyczalnia nie ma szczegółów auta, więc szukamy go na liście
            var cars = await GetCarsAsync(cancellationToken);
            return cars.FirstOrDefault(c => string.Equals(c.CarId, carId, StringComparison.Ordinal));
        }

        return await SendAsync<ProviderCar?>(operation, Placeholders(carId: carId), body: null, cancellationToken, async (response, ct) =>
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            var car = JsonPath.Find(document.RootElement, operation.ResultPath);
            return car is { ValueKind: JsonValueKind.Object } found ? MapCar(found) : null;
        });
    }

    public async Task<ProviderQuote> CreateOfferAsync(OfferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var map = Mapping.OfferRequest;
        var body = new JsonObject();
        Put(body, map.CarId, IdNode(query.CarId));
        Put(body, map.Age, query.Age);
        Put(body, map.LicenseYears, query.LicenseYears);
        Put(body, map.Location, query.Location);
        Put(body, map.StartDate, FormatDate(query.StartDate));
        Put(body, map.EndDate, FormatDate(query.EndDate));

        var placeholders = Placeholders(carId: query.CarId);
        placeholders["age"] = query.Age.ToString(CultureInfo.InvariantCulture);
        placeholders["licenseYears"] = query.LicenseYears.ToString(CultureInfo.InvariantCulture);
        placeholders["location"] = query.Location;
        placeholders["startDate"] = FormatDate(query.StartDate);
        placeholders["endDate"] = FormatDate(query.EndDate);

        var operation = Operations.Offer;
        return await SendAsync(operation, placeholders, body, cancellationToken, async (response, ct) =>
        {
            await ThrowIfRejectedAsync(response, RejectOnOffer, "Wypożyczalnia odrzuciła zapytanie.", ct);
            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            var offer = JsonPath.Find(document.RootElement, operation.ResultPath)
                ?? throw new JsonException($"{DisplayName}: pusta odpowiedź z wyceną.");
            return MapOffer(offer, query);
        });
    }

    public async Task<ProviderRental> CreateRentalAsync(RentalOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var map = Mapping.RentalRequest;
        var body = new JsonObject();
        Put(body, map.OfferId, IdNode(order.OfferId));
        Put(body, map.FirstName, order.FirstName);
        Put(body, map.LastName, order.LastName);
        Put(body, map.Email, order.Email);

        var placeholders = Placeholders(offerId: order.OfferId, email: order.Email);
        placeholders["firstName"] = order.FirstName;
        placeholders["lastName"] = order.LastName;

        var operation = Operations.Rental;
        return await SendAsync(operation, placeholders, body, cancellationToken, async (response, ct) =>
        {
            await ThrowIfRejectedAsync(response, RejectOnRental, "Wypożyczalnia odrzuciła wniosek.", ct);
            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            return MapRental(document.RootElement, operation.ResultPath, fallbackId: null, RentalStatus.Pending);
        });
    }

    public async Task<ProviderRental?> GetRentalAsync(string rentalId, CancellationToken cancellationToken)
    {
        var operation = Operations.RentalStatus;
        return await SendAsync<ProviderRental?>(operation, Placeholders(rentalId: rentalId), body: null, cancellationToken, async (response, ct) =>
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            return MapRental(document.RootElement, operation.ResultPath, rentalId, RentalStatus.Pending);
        });
    }

    public async Task<ProviderRental> RequestReturnAsync(string rentalId, string email, CancellationToken cancellationToken)
    {
        var operation = Operations.Return;
        if (string.IsNullOrWhiteSpace(operation.Path))
        {
            throw new ProviderRejectedException("Ta wypożyczalnia nie przyjmuje zgłoszeń zwrotu przez Brokera.", StatusCodes.Status409Conflict);
        }

        var body = new JsonObject();
        Put(body, Mapping.ReturnRequest.Email, email);

        return await SendAsync(operation, Placeholders(rentalId: rentalId, email: email), body, cancellationToken, async (response, ct) =>
        {
            await ThrowIfRejectedAsync(response, RejectOnReturn, "Wypożyczalnia odrzuciła zgłoszenie zwrotu.", ct);
            await EnsureSuccessAsync(response, ct);
            using var document = await ReadJsonAsync(response, ct);
            return MapRental(document.RootElement, operation.ResultPath, rentalId, RentalStatus.ReturnRequested);
        });
    }

    // ---------- HTTP ----------

    /// <summary>
    /// Wysyła żądanie z limitem TimeoutSeconds i uwierzytelnieniem. Przy OAuth2 i 401 bierze nowy token i próbuje raz jeszcze.
    /// </summary>
    private async Task<T> SendAsync<T>(
        ProviderOperation operation,
        Dictionary<string, string?> placeholders,
        JsonObject? body,
        CancellationToken cancellationToken,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = await CreateRequestAsync(operation, placeholders, body, timeout.Token);
                using var response = await http.SendAsync(request, timeout.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized && settings.Auth.Type == ProviderAuthType.OAuth2 && attempt == 1)
                {
                    tokens.Invalidate(Key, settings.Auth);
                    continue;
                }

                return await read(response, timeout.Token);
            }
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Wypożyczalnia {Provider} nie odpowiedziała w {Seconds} s.", Code, settings.TimeoutSeconds);
            throw new TaskCanceledException($"{DisplayName}: brak odpowiedzi w {settings.TimeoutSeconds} s.", new TimeoutException(ex.Message, ex));
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        ProviderOperation operation,
        Dictionary<string, string?> placeholders,
        JsonObject? body,
        CancellationToken cancellationToken)
    {
        var method = new HttpMethod(string.IsNullOrWhiteSpace(operation.Method) ? "GET" : operation.Method.Trim().ToUpperInvariant());
        var request = new HttpRequestMessage(method, BuildUri(operation.Path, placeholders));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        var auth = settings.Auth;
        switch (auth.Type)
        {
            case ProviderAuthType.ApiKey:
                request.Headers.TryAddWithoutValidation(auth.HeaderName, settings.ApiKey);
                break;
            case ProviderAuthType.Bearer:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                break;
            case ProviderAuthType.OAuth2:
                var token = await tokens.GetTokenAsync(Key, auth, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
        }

        return request;
    }

    /// <summary>BaseUrl + ścieżka z podstawionymi wartościami (zakodowanymi do URL).</summary>
    private Uri BuildUri(string path, Dictionary<string, string?> placeholders)
    {
        var resolved = path ?? string.Empty;
        foreach (var (name, value) in placeholders)
        {
            resolved = resolved.Replace("{" + name + "}", Uri.EscapeDataString(value ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        if (Uri.TryCreate(resolved, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return absolute;
        }

        var baseUri = new Uri((settings.BaseUrl ?? string.Empty).TrimEnd('/') + "/");
        return new Uri(baseUri, resolved.TrimStart('/'));
    }

    private static Dictionary<string, string?> Placeholders(
        string? carId = null, string? rentalId = null, string? offerId = null, string? email = null) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["carId"] = carId,
            ["rentalId"] = rentalId,
            ["offerId"] = offerId,
            ["email"] = email
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
    }

    private static async Task ThrowIfRejectedAsync(
        HttpResponseMessage response, int[] rejectCodes, string fallback, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        if (!rejectCodes.Contains(code))
        {
            return;
        }

        var message = await ReadMessageAsync(response, fallback, cancellationToken);
        throw new ProviderRejectedException(message, code);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadMessageAsync(response, response.ReasonPhrase ?? "Błąd", cancellationToken);
        throw new HttpRequestException($"{DisplayName} zwróciło {(int)response.StatusCode}: {message}", null, response.StatusCode);
    }

    /// <summary>Komunikat z ProblemDetails (title, errors) albo z pól message, error, detail.</summary>
    private static async Task<string> ReadMessageAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        var problem = await ProblemReader.ReadAsync(response, cancellationToken);
        if (problem is not null)
        {
            return problem.Value.Message;
        }

        try
        {
            using var document = await ReadJsonAsync(response, cancellationToken);
            foreach (var field in new[] { "message", "error", "detail" })
            {
                var text = JsonPath.AsString(JsonPath.Find(document.RootElement, field));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // Odpowiedź nie jest JSON-em, zostaje komunikat domyślny
        }

        return fallback;
    }

    // ---------- Mapowanie ----------

    private ProviderCar MapCar(JsonElement car)
    {
        var map = Mapping.Car;
        var id = Text(car, map.Id);
        var make = Text(car, map.Make);
        var model = Text(car, map.Model);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
        {
            throw new JsonException($"{DisplayName}: auto bez pola {map.Id}, {map.Make} albo {map.Model}.");
        }

        return new ProviderCar(
            id,
            make,
            model,
            JsonPath.AsInt(Field(car, map.Year)) ?? 0,
            Text(car, map.Location),
            JsonPath.AsBool(Field(car, map.Available)) ?? true);
    }

    private ProviderQuote MapOffer(JsonElement offer, OfferQuery query)
    {
        var map = Mapping.Offer;
        var offerId = Text(offer, map.OfferId);
        if (string.IsNullOrWhiteSpace(offerId))
        {
            throw new JsonException($"{DisplayName}: oferta bez pola {map.OfferId}.");
        }

        var start = JsonPath.AsDate(Field(offer, map.StartDate)) ?? query.StartDate;
        var end = JsonPath.AsDate(Field(offer, map.EndDate)) ?? query.EndDate;
        var days = JsonPath.AsInt(Field(offer, map.Days)) ?? Math.Max(1, end.DayNumber - start.DayNumber);

        var daily = JsonPath.AsDecimal(Field(offer, map.DailyPrice));
        var insurance = JsonPath.AsDecimal(Field(offer, map.DailyInsurancePrice)) ?? 0m;
        var total = JsonPath.AsDecimal(Field(offer, map.TotalPrice));
        if (daily is null && total is null)
        {
            throw new JsonException($"{DisplayName}: oferta bez ceny ({map.DailyPrice} ani {map.TotalPrice}).");
        }

        // Brakującą cenę liczymy z drugiej: całość = (cena + ubezpieczenie) * dni
        daily ??= Math.Round(total!.Value / days - insurance, 2);
        total ??= (daily.Value + insurance) * days;

        var expires = JsonPath.AsUtcDateTime(Field(offer, map.ExpiresAt))
            ?? clock.GetUtcNow().UtcDateTime.AddMinutes(Mapping.OfferValidityMinutes);

        var currency = Text(offer, map.Currency);
        return new ProviderQuote(
            offerId,
            Text(offer, map.CarId) ?? query.CarId,
            daily.Value,
            insurance,
            total.Value,
            string.IsNullOrWhiteSpace(currency) ? Mapping.DefaultCurrency : currency,
            start,
            end,
            days,
            expires);
    }

    private ProviderRental MapRental(JsonElement root, string resultPath, string? fallbackId, RentalStatus fallbackStatus)
    {
        var map = Mapping.Rental;
        var rental = JsonPath.Find(root, resultPath);
        var id = rental is null ? null : Text(rental.Value, map.RentalId);
        id ??= fallbackId;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException($"{DisplayName}: wynajem bez pola {map.RentalId}.");
        }

        var statusText = rental is null ? null : Text(rental.Value, map.Status);
        var reason = rental is null ? null : Text(rental.Value, map.Reason);
        return new ProviderRental(id, MapStatus(statusText, fallbackStatus), reason);
    }

    /// <summary>Status wypożyczalni na nasz: najpierw Mapping:Statuses, potem nasze nazwy (bez wielkości liter).</summary>
    private RentalStatus MapStatus(string? value, RentalStatus fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var mapped = Mapping.Statuses
            .FirstOrDefault(s => string.Equals(s.Key, value.Trim(), StringComparison.OrdinalIgnoreCase)).Value ?? value.Trim();

        if (Enum.TryParse<RentalStatus>(mapped, ignoreCase: true, out var status)
            && Enum.IsDefined(status)
            && !int.TryParse(mapped, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return status;
        }

        logger.LogWarning("Wypożyczalnia {Provider}: nieznany status {Status}, przyjmujemy {Fallback}. Dopisz go w Mapping:Statuses.",
            Code, value, fallback);
        return fallback;
    }

    private static JsonElement? Field(JsonElement element, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : JsonPath.Find(element, path);

    private static string? Text(JsonElement element, string? path) => JsonPath.AsString(Field(element, path));

    private static void Put(JsonObject body, string? path, JsonNode? value)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            JsonPath.Set(body, path, value);
        }
    }

    /// <summary>Id z samych cyfr jako liczba (gdy NumericIds), inaczej tekst.</summary>
    private JsonNode IdNode(string id) =>
        Mapping.NumericIds && long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? JsonValue.Create(number)
            : JsonValue.Create(id)!;

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Describe(string path) => string.IsNullOrWhiteSpace(path) ? "cała odpowiedź" : path;
}
