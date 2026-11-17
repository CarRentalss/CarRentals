using Broker.Api.Data;
using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Broker.Api.Providers;

/// <summary>Które wypożyczalnie są teraz aktywne (wzorzec Registry).</summary>
public interface IProviderRegistry
{
    /// <summary>Włączone w konfiguracji (Enabled) i skonfigurowane (adres + klucz).</summary>
    Task<IReadOnlyList<ICarRentalProvider>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Aktywna wypożyczalnia o danym kodzie albo null.</summary>
    Task<ICarRentalProvider?> FindAsync(string code, CancellationToken cancellationToken);

    /// <summary>Id wierszy z tabeli Providers dla aktywnych wypożyczalni (kod i Id).</summary>
    Task<IReadOnlyDictionary<string, int>> GetEnabledIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Źródło prawdy o włączeniu wypożyczalni to konfiguracja (Providers:&lt;Klucz&gt;:Enabled), a nie baza.
/// Tabela Providers jest słownikiem dla kluczy obcych (Quotes, Rentals): Registry sam dopisuje wiersz
/// dla nowej wypożyczalni z konfiguracji i przepisuje do kolumny IsEnabled stan z konfiguracji (tylko do podglądu).
/// </summary>
public sealed class ProviderRegistry(
    IEnumerable<ICarRentalProvider> providers,
    BrokerDbContext db,
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    ILogger<ProviderRegistry> logger) : IProviderRegistry
{
    private const string CacheKey = "providers:enabled";

    // Konfiguracja zmienia się tylko z restartem, a baza jest tu tylko słownikiem, więc minuta cache wystarczy
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ICarRentalProvider>> GetActiveAsync(CancellationToken cancellationToken)
    {
        var ids = await GetEnabledIdsAsync(cancellationToken);
        return Active().Where(p => ids.ContainsKey(p.Code)).ToList();
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

        foreach (var provider in providers.Where(p => p.IsEnabled && !p.IsConfigured))
        {
            logger.LogWarning("Wypożyczalnia {Provider} ma Enabled = true, ale brakuje adresu albo klucza. Pomijam ją.", provider.Code);
        }

        var active = Active().ToList();
        var activeCodes = active.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        var missing = active.Where(p => !rows.Exists(r => Same(r.Code, p.Code))).ToList();
        var outdated = rows.Where(r => r.IsEnabled != activeCodes.Contains(r.Code)).ToList();

        if (missing.Count > 0 || outdated.Count > 0)
        {
            rows = await SyncAsync(missing, activeCodes, cancellationToken);
        }

        var ids = rows
            .Where(r => activeCodes.Contains(r.Code))
            .ToDictionary(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase);

        cache.Set<IReadOnlyDictionary<string, int>>(CacheKey, ids, CacheTime);
        return ids;
    }

    private IEnumerable<ICarRentalProvider> Active() =>
        providers
            .Where(p => p.IsEnabled && p.IsConfigured)
            .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

    /// <summary>
    /// Dopisuje brakujące wiersze i ustawia IsEnabled zgodnie z konfiguracją. Osobny DbContext,
    /// żeby nie zapisać przy okazji zmian, które serwis ma jeszcze w swoim kontekście.
    /// </summary>
    private async Task<List<Provider>> SyncAsync(
        List<ICarRentalProvider> missing, HashSet<string> activeCodes, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var syncDb = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();

        try
        {
            var rows = await syncDb.Providers.ToListAsync(cancellationToken);
            var nextId = rows.Count == 0 ? 1 : rows.Max(r => r.Id) + 1;

            foreach (var provider in missing)
            {
                var name = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Code : provider.DisplayName;
                var row = new Provider { Id = nextId++, Code = provider.Code, Name = name.Length > 100 ? name[..100] : name };
                syncDb.Providers.Add(row);
                rows.Add(row);
                logger.LogInformation("Nowa wypożyczalnia {Provider} z konfiguracji, dopisuję ją do tabeli Providers.", provider.Code);
            }

            foreach (var row in rows)
            {
                row.IsEnabled = activeCodes.Contains(row.Code);
            }

            await syncDb.SaveChangesAsync(cancellationToken);
            return rows;
        }
        catch (DbUpdateException ex)
        {
            // Druga instancja albo równoległe żądanie dopisało wiersz pierwsze. Bierzemy to, co jest w bazie.
            logger.LogWarning(ex, "Nie udało się zsynchronizować tabeli Providers, czytam ją ponownie.");
            return await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
