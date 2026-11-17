using CarRental.Api.Options;
using CarRental.Api.Services;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Background;

/// <summary>
/// Potwierdzanie wniosków w procesie API, gdy nie ma Azure Service Bus (lokalnie, docker compose, testy).
/// Na Azure z ServiceBus:ConnectionString ten proces nie startuje, wnioski odbiera CarRental.Worker z kolejki.
/// Logika jest wspólna dla obu trybów: <see cref="RentalConfirmationProcessor"/>.
/// Wniosek dostaje najpierw status Pending, a po chwili ten proces go potwierdza (albo odrzuca).
/// Broker w tym czasie odpytuje GET /api/v1/rentals/{id}.
/// </summary>
public sealed class RentalConfirmationWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RentalProcessingOptions> options,
    ILogger<RentalConfirmationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
        {
            logger.LogInformation("Potwierdzanie wynajmów w tle jest wyłączone.");
            return;
        }

        logger.LogInformation("Potwierdzanie wynajmów w procesie API (bez Service Bus), co {Seconds} s.", settings.PollIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds));

        try
        {
            do
            {
                await ProcessOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normalne zatrzymanie aplikacji
        }
    }

    private async Task ProcessOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>();
            var delay = TimeSpan.FromSeconds(options.CurrentValue.ConfirmationDelaySeconds);

            var processed = await processor.ProcessPendingAsync(delay, stoppingToken);
            if (processed > 0)
            {
                logger.LogInformation("Przetworzono {Count} wniosków o wynajem.", processed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Błąd jednej rundy nie może zatrzymać całego procesu, spróbujemy przy następnym tyknięciu
            logger.LogError(ex, "Błąd podczas potwierdzania wynajmów.");
        }
    }
}
