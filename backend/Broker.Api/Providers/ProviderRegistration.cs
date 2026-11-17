using Broker.Api.Providers.Generic;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>
    /// Każda sekcja Providers:&lt;Klucz&gt; z Type = Generic staje się wypożyczalnią (ConfigurableHttpProvider)
    /// z własnym HttpClientem "provider:&lt;Klucz&gt;". Nowa wypożyczalnia = nowa sekcja w konfiguracji, bez zmian w kodzie.
    /// Rejestrujemy też wyłączone, a ProviderRegistry bierze tylko te z Enabled = true i pełną konfiguracją.
    /// </summary>
    /// <returns>Klucze zarejestrowanych sekcji.</returns>
    public static IReadOnlyList<string> AddConfiguredProviders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OAuthTokenCache>();
        services.AddHttpClient(OAuthTokenCache.HttpClientName);

        var keys = new List<string>();
        foreach (var section in configuration.GetSection(ProvidersOptions.SectionName).GetChildren())
        {
            if (!string.Equals(section["Type"], GenericProviderOptions.GenericType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = section.Key;
            keys.Add(key);

            services.AddOptions<GenericProviderOptions>(key).Bind(section);

            var clientName = ConfigurableHttpProvider.HttpClientName(key);
            services.AddHttpClient(clientName, (sp, http) =>
            {
                var settings = sp.GetRequiredService<IOptionsMonitor<GenericProviderOptions>>().Get(key);
                ConfigureClient(http, settings);
                // Limit pilnuje sam dostawca (TimeoutSeconds), HttpClient ma zapas, żeby nie ucinał wcześniej
                http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds + 5);
            });

            services.AddTransient<ICarRentalProvider>(sp => new ConfigurableHttpProvider(
                key,
                sp.GetRequiredService<IOptionsMonitor<GenericProviderOptions>>().Get(key),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName),
                sp.GetRequiredService<OAuthTokenCache>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ConfigurableHttpProvider>>()));
        }

        return keys;
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
