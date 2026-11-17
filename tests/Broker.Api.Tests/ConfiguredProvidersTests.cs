using Broker.Api.Data;
using Broker.Api.Providers;
using Broker.Api.Providers.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Api.Tests;

/// <summary>Rejestracja wypożyczalni z konfiguracji (Type = Generic) i włączanie przez Enabled.</summary>
public sealed class ConfiguredProvidersTests
{
    [Fact]
    public void AddConfiguredProviders_RegistersOnlyGenericSections()
    {
        using var services = Build(new()
        {
            ["Providers:Our:BaseUrl"] = "http://our.test",
            ["Providers:Our:ApiKey"] = "our-key",
            ["Providers:Lecturer:Type"] = "Generic",
            ["Providers:Lecturer:Code"] = "lecturer",
            ["Providers:Lecturer:Enabled"] = "true",
            ["Providers:Lecturer:BaseUrl"] = "https://lecturer.test/api",
            ["Providers:Lecturer:ApiKey"] = "lecturer-key",
            ["Providers:OtherGroup:Type"] = "generic",
            ["Providers:OtherGroup:Enabled"] = "false",
            ["Providers:OtherGroup:BaseUrl"] = "",
            ["Providers:SearchTimeoutSeconds"] = "15"
        });

        var providers = services.GetServices<ICarRentalProvider>().OrderBy(p => p.Code).ToList();

        Assert.Equal(new[] { "lecturer", "other-group" }, providers.Select(p => p.Code));
        Assert.True(providers[0].IsEnabled);
        Assert.True(providers[0].IsConfigured);
        Assert.False(providers[1].IsEnabled);
        Assert.False(providers[1].IsConfigured);
    }

    [Fact]
    public void AddConfiguredProviders_OnlyBaseUrlAndKey_UsesDefaultsLikeOurApi()
    {
        // Tak jak App Settings: Providers__kolejna__Type, __Enabled, __BaseUrl, __ApiKey
        using var services = Build(new()
        {
            ["Providers:kolejna:Type"] = "Generic",
            ["Providers:kolejna:Enabled"] = "true",
            ["Providers:kolejna:BaseUrl"] = "https://kolejna.test",
            ["Providers:kolejna:ApiKey"] = "k",
            ["Providers:kolejna:Operations:Cars:ResultPath"] = "items"
        });

        var provider = Assert.Single(services.GetServices<ICarRentalProvider>());
        var settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<GenericProviderOptions>>().Get("kolejna");

        Assert.Equal("kolejna", provider.Code);
        Assert.Equal("kolejna", provider.DisplayName);
        Assert.True(provider.IsEnabled && provider.IsConfigured);
        Assert.Equal(ProviderAuthType.ApiKey, settings.Auth.Type);
        Assert.Equal("x-api-key", settings.Auth.HeaderName);
        Assert.Equal("GET", settings.Operations.Cars.Method);
        Assert.Equal("api/v1/cars", settings.Operations.Cars.Path);
        Assert.Equal("items", settings.Operations.Cars.ResultPath);
        Assert.Equal("POST", settings.Operations.Offer.Method);
        Assert.Equal("api/v1/rentals/{rentalId}/return", settings.Operations.Return.Path);
        Assert.Equal("dailyPrice", settings.Mapping.Offer.DailyPrice);
        Assert.Equal(15, settings.TimeoutSeconds);
    }

    [Fact]
    public void AddConfiguredProviders_OAuth2WithoutSecret_IsNotConfigured()
    {
        using var services = Build(new()
        {
            ["Providers:Lecturer:Type"] = "Generic",
            ["Providers:Lecturer:Enabled"] = "true",
            ["Providers:Lecturer:BaseUrl"] = "https://lecturer.test",
            ["Providers:Lecturer:Auth:Type"] = "OAuth2",
            ["Providers:Lecturer:Auth:TokenUrl"] = "https://login.test/token",
            ["Providers:Lecturer:Auth:ClientId"] = "broker"
        });

        var provider = Assert.Single(services.GetServices<ICarRentalProvider>());

        Assert.True(provider.IsEnabled);
        Assert.False(provider.IsConfigured);
    }

    [Theory]
    [InlineData("Lecturer", "lecturer")]
    [InlineData("OtherGroup", "other-group")]
    [InlineData("other_group", "other-group")]
    [InlineData("km3", "km3")]
    public void ResolveCode_WithoutCodeSetting_UsesSectionNameInKebabCase(string key, string expected)
    {
        Assert.Equal(expected, ConfigurableHttpProvider.ResolveCode(key, new GenericProviderOptions()));
    }

    [Fact]
    public async Task Registry_EnabledInConfig_IsActiveAndGetsRowInProvidersTable()
    {
        using var database = new SqliteBrokerDb();
        using var services = new ServiceCollection()
            .AddScoped(_ => database.CreateContext())
            .BuildServiceProvider();

        // lecturer jest w seedzie z IsEnabled = false, a kolejna nie ma wiersza wcale
        var lecturer = new FakeProvider("lecturer");
        var newOne = new FakeProvider("kolejna", "Kolejna wypożyczalnia");
        var disabled = new FakeProvider("other-group") { IsEnabled = false };
        var notConfigured = new FakeProvider("our") { IsConfigured = false };

        using var db = database.CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new ProviderRegistry(
            [lecturer, newOne, disabled, notConfigured], db, cache,
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProviderRegistry>.Instance);

        var active = await registry.GetActiveAsync(CancellationToken.None);
        var ids = await registry.GetEnabledIdsAsync(CancellationToken.None);

        Assert.Equal(new[] { "lecturer", "kolejna" }, active.Select(p => p.Code));
        Assert.Equal(2, ids["lecturer"]);
        Assert.Equal(4, ids["kolejna"]);

        using var check = database.CreateContext();
        var rows = await check.Providers.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(new[] { "our", "lecturer", "other-group", "kolejna" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { false, true, false, true }, rows.Select(r => r.IsEnabled));
        Assert.Equal("Kolejna wypożyczalnia", rows[3].Name);
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConfiguredProviders(configuration);
        return services.BuildServiceProvider();
    }
}
