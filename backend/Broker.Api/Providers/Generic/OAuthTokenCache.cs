using System.Collections.Concurrent;
using System.Text.Json;

namespace Broker.Api.Providers.Generic;

/// <summary>
/// Tokeny OAuth2 (client credentials) dla wypożyczalni, które ich wymagają. Token trzymamy w pamięci
/// i bierzemy nowy minutę przed wygaśnięciem, żeby nie prosić o niego przy każdym wywołaniu.
/// </summary>
public sealed class OAuthTokenCache(IHttpClientFactory httpFactory, TimeProvider clock, ILogger<OAuthTokenCache> logger)
{
    public const string HttpClientName = "provider-oauth";

    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<string> GetTokenAsync(string providerKey, ProviderAuthOptions auth, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auth);

        var key = CacheKey(providerKey, auth);
        if (TryGetValid(key, out var token))
        {
            return token;
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Ktoś mógł pobrać token, kiedy czekaliśmy
            if (TryGetValid(key, out token))
            {
                return token;
            }

            var fresh = await RequestTokenAsync(providerKey, auth, cancellationToken);
            _tokens[key] = fresh;
            return fresh.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Wypożyczalnia odrzuciła token (401), następne wywołanie pobierze nowy.</summary>
    public void Invalidate(string providerKey, ProviderAuthOptions auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        _tokens.TryRemove(CacheKey(providerKey, auth), out _);
    }

    private bool TryGetValid(string key, out string token)
    {
        if (_tokens.TryGetValue(key, out var cached) && cached.RefreshAtUtc > clock.GetUtcNow())
        {
            token = cached.AccessToken;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> RequestTokenAsync(string providerKey, ProviderAuthOptions auth, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.ClientId ?? string.Empty,
            ["client_secret"] = auth.ClientSecret ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(auth.Scope))
        {
            form["scope"] = auth.Scope;
        }

        using var http = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, auth.TokenUrl) { Content = new FormUrlEncodedContent(form) };
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Wypożyczalnia {Provider}: serwer tokenów zwrócił {Status}.", providerKey, (int)response.StatusCode);
            throw new HttpRequestException(
                $"Nie udało się pobrać tokena OAuth2 ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        var accessToken = JsonPath.AsString(JsonPath.Find(document.RootElement, "access_token"));
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new JsonException("Odpowiedź serwera tokenów nie ma access_token.");
        }

        var seconds = JsonPath.AsInt(JsonPath.Find(document.RootElement, "expires_in"));
        var lifetime = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : DefaultLifetime;

        // Krótki token odświeżamy w połowie ważności, dłuższy minutę przed końcem
        var refreshAfter = lifetime > RefreshBefore * 2 ? lifetime - RefreshBefore : lifetime / 2;
        return new CachedToken(accessToken, clock.GetUtcNow() + refreshAfter);
    }

    private static string CacheKey(string providerKey, ProviderAuthOptions auth) =>
        $"{providerKey}|{auth.TokenUrl}|{auth.ClientId}|{auth.Scope}";

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAtUtc);
}
