using System.Reflection;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Broker.Api.Auth;
using Broker.Api.Background;
using Broker.Api.Contracts;
using Broker.Api.Data;
using Broker.Api.Middleware;
using Broker.Api.Options;
using Broker.Api.Providers;
using Broker.Api.Providers.OurCarRental;
using Broker.Api.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------- Kontrolery, JSON, błędy ----------
builder.Services
    .AddControllers(options =>
    {
        // POST/PUT/DELETE z ciasteczkiem wymagają nagłówka X-CSRF: 1
        options.Filters.Add<CsrfHeaderFilter>();
        // Klucze błędów walidacji w camelCase, tak jak pola JSON (np. "endDate")
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();

// ---------- Application Insights (OpenTelemetry) ----------
// Tylko gdy jest connection string (App Setting w Azure). Lokalnie i w testach nic nie wysyłamy.
var appInsightsConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

// ---------- Ustawienia ----------
builder.Services.AddOptions<ProvidersOptions>()
    .Bind(builder.Configuration.GetSection(ProvidersOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<CatalogOptions>()
    .Bind(builder.Configuration.GetSection(CatalogOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<QuoteCleanupOptions>()
    .Bind(builder.Configuration.GetSection(QuoteCleanupOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();

// ---------- Baza (Broker DB) ----------
// W testach baza jest podmieniana na SQLite w pamięci, dlatego tu jej nie rejestrujemy
if (!builder.Environment.IsEnvironment("Testing"))
{
    var connectionString = builder.Configuration.GetConnectionString("BrokerDb");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Brak connection stringa 'BrokerDb' (ConnectionStrings__BrokerDb).");
    }

    builder.Services.AddDbContext<BrokerDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}

// ---------- Wypożyczalnie (Adapter + Registry + Aggregator) ----------
builder.Services.AddCarRentalProvider<OurCarRentalClient>(providers => providers.Our);
// KM3: nowy dostawca to nowa klasa ICarRentalProvider i jedna linia tutaj, np.
// builder.Services.AddCarRentalProvider<LecturerCarRentalClient>(providers => providers.Lecturer);
// plus adres i klucz w Providers:Lecturer i IsEnabled = 1 w tabeli Providers.
builder.Services.AddHttpClient<OurEmployeeClient>((services, http) =>
    ProviderRegistration.ConfigureClient(http, services.GetRequiredService<IOptions<ProvidersOptions>>().Value.Our));
builder.Services.AddScoped<IProviderRegistry, ProviderRegistry>();
builder.Services.AddSingleton<ProviderAggregator>();

// ---------- Serwisy ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<QuoteService>();
builder.Services.AddScoped<RentalService>();
builder.Services.AddHostedService<QuoteCleanupWorker>();

// ---------- Logowanie i autoryzacja ----------
var authMode = builder.AddBrokerAuthentication();

// ---------- Swagger ----------
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", false);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Broker API",
        Version = "v1",
        Description = "Broker (BFF) dla frontu Car Rentals: katalog i wycena ze wszystkich wypożyczalni, wynajem, logowanie, profil, panel pracownika. " +
            "Logowanie przez ciasteczko (/api/v1/auth/login, lokalnie /api/v1/auth/dev-login). POST/PUT/DELETE wymagają nagłówka X-CSRF: 1."
    });

    options.AddSecurityDefinition("Csrf", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = CsrfHeaderFilter.HeaderName,
        Description = "Wpisz 1 (wymagane dla POST/PUT/DELETE)"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Csrf" } }] = []
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

if (authMode == AuthMode.None)
{
    app.Logger.LogWarning("Logowanie wyłączone: brak AzureAd:ClientId (dev-login działa tylko w Development z Auth:UseDevLogin=true).");
}

await InitializeDatabaseAsync(app);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Broker API");
}

// React (zbudowany frontend/dist skopiowany do wwwroot) z tej samej domeny co /api
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseMiddleware<AuditMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// /health: status i wersja aplikacji (APP_VERSION z App Service, inaczej wersja assembly z pipeline)
var appVersion = app.Configuration["APP_VERSION"]
    ?? typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "dev";
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
        context.Response.WriteAsJsonAsync(new { status = report.Status.ToString(), version = appVersion })
}).AllowAnonymous();

// Nieznane /api/... to 404 z ProblemDetails, a nie index.html
app.MapFallback("/api/{**path}", context =>
        Results.Problem(title: "Nie ma takiego endpointu.", statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context))
    .AllowAnonymous();
// Pozostałe trasy (np. /cars/our/1 po odświeżeniu strony) obsługuje router Reacta
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

// Tworzy bazę przy starcie (lokalnie). Gdy są migracje, używa migracji, a jak jeszcze ich nie ma, tworzy bazę z modelu.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        return;
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();

    if (db.Database.GetMigrations().Any())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

/// <summary>Potrzebne, żeby testy integracyjne (WebApplicationFactory) widziały klasę Program.</summary>
public partial class Program;
