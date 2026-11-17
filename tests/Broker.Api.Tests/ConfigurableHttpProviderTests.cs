using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Broker.Api.Contracts;
using Broker.Api.Providers;
using Broker.Api.Providers.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Api.Tests;

/// <summary>Dostawca sterowany konfiguracją na fałszywym HttpMessageHandler.</summary>
public sealed class ConfigurableHttpProviderTests
{
    private const string BaseUrl = "http://othergroup.test/";
    private static readonly OfferQuery Query = new("1", 30, 8, "Warszawa", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 4));

    [Fact]
    public async Task DefaultMapping_ApiShapedLikeOurs_WorksWithBaseUrlAndKeyOnly()
    {
        var handler = new FakeCarRentalHandler();
        var provider = Create(new() { ["ApiKey"] = "other-key" }, handler);

        var cars = await provider.GetCarsAsync(CancellationToken.None);
        var offer = await provider.CreateOfferAsync(Query, CancellationToken.None);
        var rental = await provider.CreateRentalAsync(new RentalOrder(offer.OfferId, "Jan", "Kowalski", "jan@example.com"), CancellationToken.None);
        var status = await provider.GetRentalAsync(rental.RentalId, CancellationToken.None);
        var returned = await provider.RequestReturnAsync(rental.RentalId, "jan@example.com", CancellationToken.None);

        Assert.Equal(12, cars.Count);
        Assert.Equal(new ProviderCar("1", "Toyota", "Model1", 2021, "Warszawa", true), cars[0]);

        Assert.Equal(150m, offer.DailyPrice);
        Assert.Equal(30m, offer.DailyInsurancePrice);
        Assert.Equal(540m, offer.TotalPrice);
        Assert.Equal("PLN", offer.Currency);
        Assert.Equal(3, offer.Days);
        Assert.Equal(DateTimeKind.Utc, offer.ExpiresAtUtc.Kind);

        Assert.Equal(handler.RentalId.ToString(), rental.RentalId);
        Assert.Equal(RentalStatus.Pending, rental.Status);
        Assert.Equal(RentalStatus.Confirmed, status!.Status);
        Assert.Equal(RentalStatus.ReturnRequested, returned.Status);

        // Ten sam kształt body co w naszym API: carId jako liczba, daty jako RRRR-MM-DD
        var offerBody = handler.Requests.Single(r => r.Path == "/api/v1/offers").Body!;
        using var json = JsonDocument.Parse(offerBody);
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("carId").ValueKind);
        Assert.Equal("2026-12-01", json.RootElement.GetProperty("startDate").GetString());
        Assert.All(handler.Requests, r => Assert.Equal("other-key", r.ApiKey));
    }

    [Fact]
    public async Task GetCarAsync_ProviderReturns404_ReturnsNull()
    {
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = Create(new() { ["ApiKey"] = "k" }, handler);

        var car = await provider.GetCarAsync("99", CancellationToken.None);

        Assert.Null(car);
        Assert.Equal("http://othergroup.test/api/v1/cars/99", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task CustomMapping_DifferentShape_MapsFieldsPathsAndStatuses()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var handler = new StubHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v2/vehicles" => StubHttpHandler.Json(HttpStatusCode.OK,
                """{"data":{"items":[{"vehicleId":"A-7","brand":{"name":"Skoda","model":"Octavia"},"productionYear":"2023","city":"Kraków"}]}}"""),
            "/v2/quotes/A-7" => StubHttpHandler.Json(HttpStatusCode.OK,
                """{"quote":{"id":555,"price":{"daily":"200.50","total":601.5}}}"""),
            "/v2/bookings" => StubHttpHandler.Json(HttpStatusCode.Created, """{"bookingId":"B-1","state":"accepted"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var settings = new Dictionary<string, string?>
        {
            ["ApiKey"] = "secret",
            ["Auth:HeaderName"] = "X-Partner-Key",
            ["Operations:Cars:Path"] = "v2/vehicles",
            ["Operations:Cars:ResultPath"] = "data.items[]",
            ["Operations:Offer:Path"] = "v2/quotes/{carId}",
            ["Operations:Offer:ResultPath"] = "quote",
            ["Operations:Rental:Path"] = "v2/bookings",
            ["Mapping:Car:Id"] = "vehicleId",
            ["Mapping:Car:Make"] = "brand.name",
            ["Mapping:Car:Model"] = "brand.model",
            ["Mapping:Car:Year"] = "productionYear",
            ["Mapping:Car:Location"] = "city",
            ["Mapping:Car:Available"] = "",
            ["Mapping:OfferRequest:CarId"] = "",
            ["Mapping:OfferRequest:StartDate"] = "period.from",
            ["Mapping:OfferRequest:EndDate"] = "period.to",
            ["Mapping:Offer:OfferId"] = "id",
            ["Mapping:Offer:DailyPrice"] = "price.daily",
            ["Mapping:Offer:TotalPrice"] = "price.total",
            ["Mapping:Offer:DailyInsurancePrice"] = "",
            ["Mapping:RentalRequest:Email"] = "customer.email",
            ["Mapping:Rental:RentalId"] = "bookingId",
            ["Mapping:Rental:Status"] = "state",
            ["Mapping:Statuses:accepted"] = "Confirmed"
        };
        var provider = Create(settings, handler, clock: clock);

        var car = Assert.Single(await provider.GetCarsAsync(CancellationToken.None));
        var offer = await provider.CreateOfferAsync(Query with { CarId = "A-7" }, CancellationToken.None);
        var rental = await provider.CreateRentalAsync(new RentalOrder(offer.OfferId, "Jan", "Kowalski", "jan@example.com"), CancellationToken.None);

        Assert.Equal(new ProviderCar("A-7", "Skoda", "Octavia", 2023, "Kraków", true), car);

        Assert.Equal("555", offer.OfferId);
        Assert.Equal("A-7", offer.CarId);
        Assert.Equal(200.50m, offer.DailyPrice);
        Assert.Equal(601.5m, offer.TotalPrice);
        Assert.Equal("PLN", offer.Currency);
        Assert.Equal(3, offer.Days);
        Assert.Equal(new DateTime(2026, 11, 20, 12, 10, 0, DateTimeKind.Utc), offer.ExpiresAtUtc);

        Assert.Equal(new ProviderRental("B-1", RentalStatus.Confirmed), rental);

        var offerRequest = handler.Requests.Single(r => r.Uri.AbsolutePath == "/v2/quotes/A-7");
        using (var body = JsonDocument.Parse(offerRequest.Body!))
        {
            Assert.False(body.RootElement.TryGetProperty("carId", out _));
            Assert.Equal("2026-12-04", body.RootElement.GetProperty("period").GetProperty("to").GetString());
        }

        var rentalRequest = handler.Requests.Single(r => r.Uri.AbsolutePath == "/v2/bookings");
        using (var body = JsonDocument.Parse(rentalRequest.Body!))
        {
            // offerId z samych cyfr idzie jako liczba, jak w naszym API carId
            Assert.Equal(555, body.RootElement.GetProperty("offerId").GetInt32());
            Assert.Equal("jan@example.com", body.RootElement.GetProperty("customer").GetProperty("email").GetString());
        }

        Assert.All(handler.Requests, r => Assert.Equal("secret", r.Headers["X-Partner-Key"]));
        Assert.All(handler.Requests, r => Assert.False(r.Headers.ContainsKey("x-api-key")));
    }

    [Fact]
    public async Task BearerAuth_SendsApiKeyAsBearerToken()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "[]"));
        var provider = Create(new() { ["ApiKey"] = "static-token", ["Auth:Type"] = "Bearer" }, handler);

        await provider.GetCarsAsync(CancellationToken.None);

        Assert.Equal("Bearer static-token", Assert.Single(handler.Requests).Headers["Authorization"]);
    }

    [Fact]
    public async Task OAuth2_TokenIsCachedUntilExpiryAndRefreshedAfter401()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var tokenNumber = 0;
        var rejectNext = false;
        var handler = new StubHttpHandler((request, body) =>
        {
            if (request.RequestUri!.Host == "login.test")
            {
                tokenNumber++;
                Assert.Contains("grant_type=client_credentials", body, StringComparison.Ordinal);
                Assert.Contains("client_id=broker", body, StringComparison.Ordinal);
                Assert.Contains("scope=cars.read", body, StringComparison.Ordinal);
                return StubHttpHandler.Json(HttpStatusCode.OK, $$"""{"access_token":"token-{{tokenNumber}}","expires_in":3600}""");
            }

            if (rejectNext)
            {
                rejectNext = false;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return StubHttpHandler.Json(HttpStatusCode.OK, "[]");
        });
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Type"] = "OAuth2",
            ["Auth:TokenUrl"] = "https://login.test/oauth2/token",
            ["Auth:ClientId"] = "broker",
            ["Auth:ClientSecret"] = "secret",
            ["Auth:Scope"] = "cars.read"
        };
        var provider = Create(settings, handler, clock: clock);
        Assert.True(provider.IsConfigured);

        await provider.GetCarsAsync(CancellationToken.None);
        await provider.GetCarsAsync(CancellationToken.None);
        Assert.Equal(1, tokenNumber);

        // Godzina później token jest już stary, więc bierzemy nowy
        clock.Advance(TimeSpan.FromMinutes(59.5));
        await provider.GetCarsAsync(CancellationToken.None);
        Assert.Equal(2, tokenNumber);

        // Wypożyczalnia odrzuca token (401): nowy token i jedna powtórka
        rejectNext = true;
        await provider.GetCarsAsync(CancellationToken.None);
        Assert.Equal(3, tokenNumber);

        var apiCalls = handler.Requests.Where(r => r.Uri.Host == "othergroup.test").ToList();
        Assert.Equal(
            new[] { "Bearer token-1", "Bearer token-1", "Bearer token-2", "Bearer token-2", "Bearer token-3" },
            apiCalls.Select(r => r.Headers["Authorization"]));
    }

    [Fact]
    public async Task Timeout_ProviderSlowerThanTimeoutSeconds_ThrowsTaskCanceled()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "[]")) { Delay = TimeSpan.FromSeconds(10) };
        var provider = Create(new() { ["ApiKey"] = "k", ["TimeoutSeconds"] = "1" }, handler);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => provider.GetCarsAsync(CancellationToken.None));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Czekaliśmy {stopwatch.Elapsed}, a limit to 1 s.");
    }

    [Fact]
    public async Task Unauthorized_WrongKey_ThrowsHttpRequestExceptionWith401()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Unauthorized, """{"title":"Zły klucz API."}"""));
        var provider = Create(new() { ["ApiKey"] = "zly" }, handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetCarsAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("Zły klucz API.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerError_ThrowsHttpRequestExceptionWith500()
    {
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = Create(new() { ["ApiKey"] = "k" }, handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CreateOfferAsync(Query, CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task Offer409_ThrowsRejectedWithProviderMessage()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Conflict, """{"message":"Auto zajęte w tym terminie."}"""));
        var provider = Create(new() { ["ApiKey"] = "k" }, handler);

        var error = await Assert.ThrowsAsync<ProviderRejectedException>(() => provider.CreateOfferAsync(Query, CancellationToken.None));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("Auto zajęte w tym terminie.", error.Message);
    }

    [Fact]
    public async Task Aggregator_GenericProviderFails_IsSkippedWithReason()
    {
        var broken = Create(new() { ["ApiKey"] = "k" },
            new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError)), key: "Broken");
        var unauthorized = Create(new() { ["ApiKey"] = "k" },
            new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)), key: "NoKey");

        var result = await TestFactory.Aggregator().RunAsync<IReadOnlyList<ProviderCar>>(
            [broken, unauthorized], (p, ct) => p.GetCarsAsync(ct), CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.Equal(new[] { "broken", "no-key" }, result.Skipped.Select(s => s.Provider));
        Assert.All(result.Skipped, s => Assert.Equal(SkipReason.Error, s.Reason));
    }

    [Fact]
    public async Task Aggregator_ThreeProvidersOneSlow_ReturnsTwoAndSkipsSlowOne()
    {
        var ours = Create(new() { ["ApiKey"] = "a" }, new FakeCarRentalHandler(), key: "Our2");
        var lecturer = Create(
            new() { ["ApiKey"] = "b", ["Operations:Cars:ResultPath"] = "cars" },
            new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
                """{"cars":[{"id":1,"make":"Fiat","model":"500","year":2020}]}""")),
            key: "Lecturer");
        var slow = Create(
            new() { ["ApiKey"] = "c" },
            new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "[]")) { Delay = TimeSpan.FromSeconds(10) },
            key: "OtherGroup");

        var stopwatch = Stopwatch.StartNew();
        var result = await TestFactory.Aggregator(timeoutSeconds: 0.5).RunAsync<IReadOnlyList<ProviderCar>>(
            [ours, lecturer, slow], (p, ct) => p.GetCarsAsync(ct), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(new[] { "our2", "lecturer" }, result.Results.Select(r => r.Provider.Code));
        Assert.Equal(12, result.Results[0].Value.Count);
        Assert.Equal("Fiat", Assert.Single(result.Results[1].Value).Make);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("other-group", skipped.Provider);
        Assert.Equal(SkipReason.Timeout, skipped.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Czekaliśmy {stopwatch.Elapsed}, a limit to 0,5 s.");
    }

    /// <summary>
    /// Dostawca z ustawień jak z appsettings / App Settings (prawdziwe wiązanie sekcji Providers:&lt;Klucz&gt;).
    /// Type, Enabled i BaseUrl są ustawione, reszta z <paramref name="values"/>.
    /// </summary>
    internal static ConfigurableHttpProvider Create(
        Dictionary<string, string?> values, HttpMessageHandler handler, string key = "OtherGroup", TimeProvider? clock = null)
    {
        var all = new Dictionary<string, string?>
        {
            [$"Providers:{key}:Type"] = "Generic",
            [$"Providers:{key}:Enabled"] = "true",
            [$"Providers:{key}:BaseUrl"] = BaseUrl
        };
        foreach (var (name, value) in values)
        {
            all[$"Providers:{key}:{name}"] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(all).Build();
        var settings = configuration.GetSection($"Providers:{key}").Get<GenericProviderOptions>()!;

        clock ??= TimeProvider.System;
        var tokens = new OAuthTokenCache(new HandlerHttpClientFactory(handler), clock, NullLogger<OAuthTokenCache>.Instance);
        return new ConfigurableHttpProvider(key, settings, new HttpClient(handler), tokens, clock,
            NullLogger<ConfigurableHttpProvider>.Instance);
    }
}
