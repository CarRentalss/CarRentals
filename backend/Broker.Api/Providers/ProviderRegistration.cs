using Microsoft.Extensions.Options;

namespace Broker.Api.Providers;

/// <summary>
/// Rejestracja wypożyczalni w DI. Dostawca to typed HttpClient z adresem i limitem z jego sekcji Providers,
/// widoczny dla ProviderRegistry jako ICarRentalProvider.
/// </summary>
public static class ProviderRegistration
{
    public static IHttpClientBuilder AddCarRentalProvider<TProvider>(
        this IServiceCollection services,
        Func<ProvidersOptions, ProviderEndpointOptions> selectSettings)
        where TProvider : class, ICarRentalProvider
    {
        ArgumentNullException.ThrowIfNull(selectSettings);

        var builder = services.AddHttpClient<TProvider>((sp, http) =>
            ConfigureClient(http, selectSettings(sp.GetRequiredService<IOptions<ProvidersOptions>>().Value)));
        services.AddTransient<ICarRentalProvider>(sp => sp.GetRequiredService<TProvider>());
        return builder;
    }

    /// <summary>Adres i limit czasu jednej wypożyczalni (np. Providers:Our).</summary>
    public static void ConfigureClient(HttpClient http, ProviderEndpointOptions settings)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsConfigured)
        {
            // Ukośnik na końcu, żeby ścieżki względne (api/v1/...) doklejały się do adresu
            http.BaseAddress = new Uri(settings.BaseUrl!.TrimEnd('/') + "/");
        }

        http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    }
}
