using System.Reflection;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CarRental.Api.Auth;
using CarRental.Api.Background;
using CarRental.Api.Data;
using CarRental.Api.Middleware;
using CarRental.Api.Options;
using CarRental.Api.Pricing;
using CarRental.Api.Services;
using CarRental.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var isLocal = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

// ---------- Kontrolery, JSON, błędy ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// ---------- Application Insights (OpenTelemetry) ----------
// Tylko gdy jest connection string (App Setting w Azure). Lokalnie i w testach nic nie wysyłamy.
var appInsightsConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

// ---------- Ustawienia (sekcje z appsettings.json) ----------
builder.Services.AddOptions<PricingOptions>()
    .Bind(builder.Configuration.GetSection(PricingOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<OfferOptions>()
    .Bind(builder.Configuration.GetSection(OfferOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RentalProcessingOptions>()
    .Bind(builder.Configuration.GetSection(RentalProcessingOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ApiKeyClientsOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyClientsOptions.SectionName))
    .Validate(o => o.IsValid(), "Skonfiguruj ApiKeys:Clients (nazwa + unikalny klucz min. 16 znaków dla każdego brokera).")
    .ValidateOnStart();

// ---------- Baza (CarRental DB) ----------
// W testach baza jest podmieniana na SQLite w pamięci, dlatego tu jej nie rejestrujemy
if (!builder.Environment.IsEnvironment("Testing"))
{
    var connectionString = builder.Configuration.GetConnectionString("CarRentalDb")
        ?? throw new InvalidOperationException("Brak connection stringa 'CarRentalDb'.");

    builder.Services.AddDbContext<CarRentalDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}

// ---------- Serwisy ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PriceCalculator>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<CarCatalogService>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddScoped<RentalService>();
builder.Services.AddScoped<EmployeeRentalService>();
builder.Services.AddHostedService<RentalConfirmationWorker>();

// ---------- Uwierzytelnianie i autoryzacja ----------
var authentication = builder.Services
    .AddAuthentication(AuthConstants.ApiKeyScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthConstants.ApiKeyScheme, _ => { });

string employeeScheme;
var employeeLoginDisabled = false;
var entraClientId = builder.Configuration["AzureAd:ClientId"];
var tenantId = builder.Configuration["AzureAd:TenantId"];

if (!string.IsNullOrWhiteSpace(entraClientId) && !string.IsNullOrWhiteSpace(tenantId))
{
    // Docelowo: token z Entra ID, rola Employee nadana w rejestracji aplikacji
    var instance = builder.Configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";

    authentication.AddJwtBearer(AuthConstants.EntraScheme, options =>
    {
        options.Authority = $"{instance.TrimEnd('/')}/{tenantId}/v2.0";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidAudiences = [entraClientId, $"api://{entraClientId}"];
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.NameClaimType = "name";
    });
    employeeScheme = AuthConstants.EntraScheme;
}
else if (isLocal)
{
    // Lokalnie, dopóki nie ma rejestracji w Entra ID: pracownik z nagłówków X-Dev-Employee / X-Dev-Roles
    authentication.AddScheme<AuthenticationSchemeOptions, DevEmployeeAuthenticationHandler>(AuthConstants.DevEmployeeScheme, _ => { });
    employeeScheme = AuthConstants.DevEmployeeScheme;
}
else
{
    // Poza Development bez Entra: API wstaje (katalog i wynajmy dla brokerów działają),
    // ale panel pracownika jest zamknięty. Fail-safe: nikt nie dostaje roli Employee.
    authentication.AddScheme<AuthenticationSchemeOptions, DisabledAuthenticationHandler>(AuthConstants.DisabledEmployeeScheme, _ => { });
    employeeScheme = AuthConstants.DisabledEmployeeScheme;
    employeeLoginDisabled = true;
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthConstants.BrokerPolicy, policy => policy
        .AddAuthenticationSchemes(AuthConstants.ApiKeyScheme)
        .RequireAuthenticatedUser()
        .RequireClaim(AuthConstants.BrokerClaim))
    .AddPolicy(AuthConstants.EmployeePolicy, policy =>
    {
        policy.AddAuthenticationSchemes(employeeScheme)
            .RequireAuthenticatedUser()
            .RequireRole(AuthConstants.EmployeeRole);

        if (employeeLoginDisabled)
        {
            // Drugi bezpiecznik: nawet gdyby ktoś był zalogowany, polityka odmawia
            policy.RequireAssertion(_ => false);
        }
    })
    // Każdy endpoint bez [Authorize] i tak wymaga klucza, żeby nic nie było otwarte przez przypadek
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(AuthConstants.ApiKeyScheme)
        .RequireAuthenticatedUser()
        .Build());

// ---------- Swagger ----------
// Domyślnie tylko w Development. W Azure włączamy App Setting Swagger__Enabled=true (np. na demo).
// Swagger nie otwiera API: każde wywołanie i tak wymaga klucza.
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Car Rental API",
        Version = "v1",
        Description = "API naszej wypożyczalni (Car Rentals). Brokerzy łączą się kluczem w nagłówku x-api-key, endpointy /api/v1/employee wymagają roli Employee. Trasy: /api/v1/..., /health bez prefiksu."
    });

    AddHeaderScheme(options, "ApiKey", AuthConstants.ApiKeyHeader, "Klucz brokera");
    if (isLocal && employeeScheme == AuthConstants.DevEmployeeScheme)
    {
        AddHeaderScheme(options, "DevEmployee", AuthConstants.DevEmployeeHeader, "Tylko lokalnie: nazwa pracownika, np. anna");
        AddHeaderScheme(options, "DevRoles", AuthConstants.DevRolesHeader, "Tylko lokalnie: role, np. Employee");
    }

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

if (employeeLoginDisabled)
{
    app.Logger.LogWarning(
        "Brak AzureAd:ClientId lub AzureAd:TenantId w środowisku {Environment}. Endpointy /api/v1/employee są zablokowane (401) do czasu konfiguracji Entra ID.",
        app.Environment.EnvironmentName);
}

await InitializeDatabaseAsync(app);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!isLocal)
{
    // HSTS: przeglądarka łączy się dalej tylko po HTTPS (App Service ma też włączone HTTPS Only)
    app.UseHsts();
}

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Car Rental API");
}

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

app.Run();

// Tworzy bazę przy starcie (lokalnie). Gdy są migracje, używa migracji, a jak jeszcze ich nie ma, tworzy bazę z modelu.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        return;
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CarRentalDbContext>();

    if (db.Database.GetMigrations().Any())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

static void AddHeaderScheme(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options, string id, string header, string description)
{
    options.AddSecurityDefinition(id, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = header,
        Description = description
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id } }] = []
    });
}

/// <summary>Potrzebne, żeby testy integracyjne (WebApplicationFactory) widziały klasę Program.</summary>
public partial class Program;
