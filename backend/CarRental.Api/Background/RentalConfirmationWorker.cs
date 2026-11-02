using CarRental.Api.Options;
using CarRental.Api.Services;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Background;

/// <summary>
/// Proces w tle po stronie naszej wypożyczalni. Wniosek o wynajem dostaje najpierw status Pending,
/// a po chwili ten proces go potwierdza (albo odrzuca). Broker w tym czasie odpytuje GET /rentals/{id}.
/// Dzięki temu nasze API zachowuje się tak, jak opisuje projekt: wynik nie jest znany od razu.
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
            var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
            var delay = TimeSpan.FromSeconds(options.CurrentValue.ConfirmationDelaySeconds);

            var processed = await rentals.ConfirmPendingAsync(delay, stoppingToken);
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
