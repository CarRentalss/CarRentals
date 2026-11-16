using Broker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Broker.Api.Providers;

/// <summary>Które wypożyczalnie są teraz aktywne (wzorzec Registry).</summary>
public interface IProviderRegistry
{
    /// <summary>Włączone w bazie i skonfigurowane (adres + klucz).</summary>
    Task<IReadOnlyList<ICarRentalProvider>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Aktywna wypożyczalnia o danym kodzie albo null.</summary>
    Task<ICarRentalProvider?> FindAsync(string code, CancellationToken cancellationToken);

    /// <summary>Id wierszy z tabeli Providers dla włączonych wypożyczalni (kod i Id).</summary>
    Task<IReadOnlyDictionary<string, int>> GetEnabledIdsAsync(CancellationToken cancellationToken);
}

public sealed class ProviderRegistry(
    IEnumerable<ICarRentalProvider> providers,
    BrokerDbContext db,
    IMemoryCache cache) : IProviderRegistry
{
    private const string CacheKey = "providers:enabled";

    // Włączanie dostawcy to zmiana w bazie, więc wystarczy ją odczytywać raz na minutę
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ICarRentalProvider>> GetActiveAsync(CancellationToken cancellationToken)
    {
        var enabled = await GetEnabledIdsAsync(cancellationToken);
        return providers
            .Where(p => p.IsConfigured && enabled.ContainsKey(p.Code))
            .ToList();
    }

    public async Task<ICarRentalProvider?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var active = await GetActiveAsync(cancellationToken);
        return active.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetEnabledIdsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, int>? cached) && cached is not null)
        {
            return cached;
        }

        var enabled = await db.Providers.AsNoTracking()
            .Where(p => p.IsEnabled)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        cache.Set<IReadOnlyDictionary<string, int>>(CacheKey, enabled, CacheTime);
        return enabled;
    }
}
