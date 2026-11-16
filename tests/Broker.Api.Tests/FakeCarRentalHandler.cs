using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Broker.Api.Tests;

/// <summary>Udaje Car Rental API: zwraca przygotowane odpowiedzi i zapisuje, co dostało.</summary>
public sealed class FakeCarRentalHandler : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? ApiKey, string? Body, Dictionary<string, string> Headers)> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public static object[] TwelveCars { get; } = Enumerable.Range(1, 12)
        .Select(i => (object)new { id = i, make = i <= 3 ? "Toyota" : $"Marka{i:00}", model = $"Model{i}", year = 2020 + (i % 5), location = "Warszawa", available = true })
        .ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        headers.TryGetValue("x-api-key", out var apiKey);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, apiKey, body, headers));

        if (Responder is not null)
        {
            return Responder(request);
        }

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == "/api/v1/cars")
        {
            return Json(HttpStatusCode.OK, TwelveCars);
        }

        if (request.Method == HttpMethod.Post && path == "/api/v1/offers")
        {
            using var document = JsonDocument.Parse(body!);
            var root = document.RootElement;
            var start = DateOnly.Parse(root.GetProperty("startDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var end = DateOnly.Parse(root.GetProperty("endDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var days = end.DayNumber - start.DayNumber;
            return Json(HttpStatusCode.Created, new
            {
                offerId = Guid.NewGuid(),
                carId = root.GetProperty("carId").GetInt32(),
                dailyPrice = 150m,
                dailyInsurancePrice = 30m,
                totalPrice = 180m * days,
                currency = "PLN",
                startDate = start,
                endDate = end,
                days,
                // Bez "Z", tak jak bywa z bazy: Broker ma to potraktować jako UTC
                expiresAt = DateTime.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };
}
