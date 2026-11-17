using System.Reflection;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CarRental.Api.Data;
using CarRental.Api.Email;
using CarRental.Api.Messaging;
using CarRental.Api.Services;
using CarRental.Worker;
using Microsoft.EntityFrameworkCore;

// CarRental.Worker: przetwarzanie wniosków o wynajem poza procesem API (Azure Service Bus).
// Na Azure działa jako osobny App Service (app-carrentals-worker) na tym samym planie co API.
// Schemat bazy robi bundle migracji w pipeline, Worker niczego nie migruje.
var builder = WebApplication.CreateBuilder(args);

var appInsightsConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

var connectionString = builder.Configuration.GetConnectionString("CarRentalDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Brak connection stringa 'CarRentalDb' (ConnectionStrings__CarRentalDb).");
}

builder.Services.AddDbContext<CarRentalDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEmailSender(builder.Configuration);
builder.Services.AddScoped<RentalConfirmationProcessor>();
builder.Services.AddHealthChecks();

builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName));

var serviceBus = builder.Configuration.GetSection(ServiceBusOptions.SectionName).Get<ServiceBusOptions>() ?? new ServiceBusOptions();
if (serviceBus.IsConfigured)
{
    builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBus.ConnectionString));
    builder.Services.AddHostedService<RentalRequestConsumer>();
}

// Zaległe wnioski zbieramy zawsze (z kolejką to zabezpieczenie, bez kolejki jedyny tryb)
builder.Services.AddHostedService<PendingRentalSweeper>();

var app = builder.Build();

app.Logger.LogInformation(serviceBus.IsConfigured
    ? "CarRental.Worker: kolejka Service Bus włączona."
    : "CarRental.Worker: brak ServiceBus:ConnectionString, działa tylko zbieranie zaległych wniosków z bazy.");

// /health dla App Service (health check i sprawdzenie po wdrożeniu w pipeline)
var appVersion = app.Configuration["APP_VERSION"]
    ?? typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "dev";
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        version = appVersion,
        queue = serviceBus.IsConfigured ? serviceBus.QueueName : null
    })
});
app.MapGet("/", () => Results.Text("CarRental.Worker"));

app.Run();
