using Broker.Api.Contracts;
using Microsoft.Extensions.Options;

namespace Broker.Api.Providers;

/// <summary>Udane wywołanie jednej wypożyczalni.</summary>
public sealed record ProviderSuccess<T>(ICarRentalProvider Provider, T Value);

/// <summary>Wynik odpytania wszystkich wypożyczalni: udane i pominięte.</summary>
public sealed record AggregateResult<T>(IReadOnlyList<ProviderSuccess<T>> Results, IReadOnlyList<SkippedProvider> Skipped);

/// <summary>
/// Woła wszystkie wypożyczalnie równolegle (Task.WhenAll) ze wspólnym limitem czasu (domyślnie 15 s).
/// Kto nie zdąży albo zwróci błąd, trafia do listy pominiętych, a reszta wyników idzie dalej.
/// </summary>
public sealed class ProviderAggregator(IOptions<ProvidersOptions> options, ILogger<ProviderAggregator> logger)
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(options.Value.SearchTimeoutSeconds);

    public async Task<AggregateResult<T>> RunAsync<T>(
        IEnumerable<ICarRentalProvider> providers,
        Func<ICarRentalProvider, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(call);

        var list = providers.ToList();
        if (list.Count == 0)
        {
            return new AggregateResult<T>([], []);
        }

        var timeout = Timeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var outcomes = await Task.WhenAll(
            list.Select(p => CallOneAsync(p, call, timeout, timeoutSource.Token, cancellationToken)));

        // Użytkownik przerwał żądanie: nie ma sensu zwracać częściowych wyników
        cancellationToken.ThrowIfCancellationRequested();

        return new AggregateResult<T>(
            outcomes.Where(o => o.Success is not null).Select(o => o.Success!).ToList(),
            outcomes.Where(o => o.Skipped is not null).Select(o => o.Skipped!).ToList());
    }

    private async Task<Outcome<T>> CallOneAsync<T>(
        ICarRentalProvider provider,
        Func<ICarRentalProvider, CancellationToken, Task<T>> call,
        TimeSpan timeout,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            // WaitAsync: nawet jak dostawca zignoruje token, nie czekamy na niego dłużej niż limit
            var value = await call(provider, timeoutToken).WaitAsync(timeoutToken);
            return new Outcome<T>(new ProviderSuccess<T>(provider, value), null);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            logger.LogWarning("Wypożyczalnia {Provider} nie odpowiedziała w {Seconds} s, pomijamy ją.", provider.Code, timeout.TotalSeconds);
            return Skip<T>(provider, SkipReason.Timeout, $"Brak odpowiedzi w ciągu {timeout.TotalSeconds:0.#} s.");
        }
        catch (ProviderRejectedException ex)
        {
            logger.LogInformation("Wypożyczalnia {Provider} odmówiła: {Message}", provider.Code, ex.Message);
            return Skip<T>(provider, SkipReason.Rejected, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Błąd wywołania wypożyczalni {Provider}, pomijamy ją.", provider.Code);
            return Skip<T>(provider, SkipReason.Error, "Wypożyczalnia chwilowo nie działa.");
        }
    }

    private static Outcome<T> Skip<T>(ICarRentalProvider provider, SkipReason reason, string message) =>
        new(null, new SkippedProvider(provider.Code, provider.DisplayName, reason, message));

    private sealed record Outcome<T>(ProviderSuccess<T>? Success, SkippedProvider? Skipped);
}
