using CarRental.Api.Services;
using Microsoft.Extensions.Options;

namespace CarRental.Worker;

/// <summary>
/// Zabezpieczenie: co jakiś czas przetwarza wnioski, które czekają (Pending) dłużej niż Worker:SweepAfterSeconds.
/// Tak łapiemy wnioski, których wiadomość nie dotarła na kolejkę (API loguje wtedy błąd, ale i tak zwraca 202).
/// Bez Service Bus to jedyny sposób przetwarzania w Workerze.
/// </summary>
public sealed class PendingRentalSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<PendingRentalSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.SweepIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOnceAsync(TimeSpan.FromSeconds(settings.SweepAfterSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normalne zatrzymanie
        }
    }

    private async Task SweepOnceAsync(TimeSpan olderThan, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>();
            var processed = await processor.ProcessPendingAsync(olderThan, stoppingToken);
            if (processed > 0)
            {
                logger.LogWarning("Przetworzono {Count} wniosków bez wiadomości z kolejki.", processed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Błąd przy szukaniu zaległych wniosków.");
        }
    }
}
