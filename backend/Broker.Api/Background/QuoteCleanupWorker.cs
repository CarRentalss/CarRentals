using Broker.Api.Data;
using Broker.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Broker.Api.Background;

/// <summary>
/// Sprzątanie w tle: co kilka minut usuwa oferty, które wygasły dawno temu (domyślnie ponad 24 h).
/// Oferta jest ważna 10 minut, więc dłużej nie jest potrzebna (część punktu "clear data", reszta w KM3).
/// Wynajmy zostają: mają kopię danych z oferty, a klucz do oferty baza ustawia na NULL (OnDelete SetNull).
/// </summary>
public sealed class QuoteCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<QuoteCleanupOptions> options,
    TimeProvider clock,
    ILogger<QuoteCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.CleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.CleanupIntervalMinutes), clock);
        do
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Błąd bazy nie może zatrzymać procesu w tle, spróbujemy przy następnym cyklu
                logger.LogWarning(ex, "Nie udało się usunąć wygasłych ofert.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Usuwa oferty wygasłe wcześniej niż RetentionHours temu. Zwraca liczbę usuniętych.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-options.Value.RetentionHours);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();

        var deleted = await db.Quotes
            .Where(q => q.ExpiresAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Usunięto {Count} wygasłych ofert (starszych niż {Cutoff:u}).", deleted, cutoff);
        }

        return deleted;
    }
}
