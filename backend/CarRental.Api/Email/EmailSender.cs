using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Email;

// Plik wspólny dla Car Rental API i CarRental.Worker (Worker linkuje go w swoim csproj).
// Dlatego tylko Microsoft.Extensions.*, bez typów z ASP.NET Core.

/// <summary>Jeden mail do klienta: wersja HTML i zwykły tekst.</summary>
public sealed record EmailMessage(string To, string Subject, string Html, string PlainText);

/// <summary>Wysyłka maili. Na Azure przez Azure Communication Services, lokalnie tylko wpis w logu.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Ustawienia maili (sekcja Email). Bez connection stringa maile idą tylko do logu.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Connection string zasobu Azure Communication Services (endpoint=...;accesskey=...).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Adres nadawcy z połączonej domeny, np. DoNotReply@xxxx.azurecomm.net.</summary>
    public string? Sender { get; set; }

    [MemberNotNullWhen(true, nameof(ConnectionString), nameof(Sender))]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(Sender);
}

/// <summary>Lokalnie i w testach: zamiast wysyłać, zapisujemy w logu do kogo i z jakim tematem.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mail (bez wysyłki, brak Email:ConnectionString) do {To}: {Subject}", MaskEmail(message.To), message.Subject);
        return Task.CompletedTask;
    }

    // W logach nie trzymamy pełnych adresów klientów
    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" : $"{email[0]}***{email[at..]}";
    }
}

/// <summary>Wysyłka przez Azure Communication Services Email.</summary>
public sealed class AcsEmailSender(EmailClient client, IOptions<EmailOptions> options, ILogger<AcsEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        // WaitUntil.Started: ACS przyjmuje mail do wysyłki, nie czekamy na doręczenie
        var operation = await client.SendAsync(
            WaitUntil.Started,
            options.Value.Sender!,
            message.To,
            message.Subject,
            message.Html,
            message.PlainText,
            cancellationToken);

        logger.LogInformation("Mail przyjęty przez ACS (operacja {OperationId}) do {To}: {Subject}",
            operation.Id, LoggingEmailSender.MaskEmail(message.To), message.Subject);
    }
}

public static class EmailServiceCollectionExtensions
{
    /// <summary>Rejestruje IEmailSender: ACS, gdy jest Email:ConnectionString i Email:Sender, inaczej LoggingEmailSender.</summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.AddOptions<EmailOptions>().Bind(section);

        var settings = section.Get<EmailOptions>() ?? new EmailOptions();
        if (settings.IsConfigured)
        {
            services.AddSingleton(_ => new EmailClient(settings.ConnectionString));
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
