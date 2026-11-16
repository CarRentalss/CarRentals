namespace CarRental.Api.Auth;

public static class AuthConstants
{
    /// <summary>Brokerzy (nasz i innej grupy) logują się kluczem w nagłówku.</summary>
    public const string ApiKeyScheme = "ApiKey";
    public const string ApiKeyHeader = "x-api-key";

    /// <summary>Pracownik: token z Entra ID z rolą Employee.</summary>
    public const string EntraScheme = "Entra";

    /// <summary>Tylko lokalnie i w testach, gdy nie ma jeszcze rejestracji aplikacji w Entra ID.</summary>
    public const string DevEmployeeScheme = "DevEmployee";
    public const string DevEmployeeHeader = "X-Dev-Employee";
    public const string DevRolesHeader = "X-Dev-Roles";

    /// <summary>Poza Development bez AzureAd: panel pracownika zamknięty (patrz DisabledAuthenticationHandler).</summary>
    public const string DisabledEmployeeScheme = "EmployeeDisabled";

    public const string BrokerClaim = "broker";
    public const string EmployeeRole = "Employee";

    public const string BrokerPolicy = "Broker";
    public const string EmployeePolicy = "Employee";
}

/// <summary>Lista brokerów i ich kluczy. Klucze trzymamy w User Secrets / ustawieniach App Service, nie w repo.</summary>
public sealed class ApiKeyClientsOptions
{
    public const string SectionName = "ApiKeys";

    public List<ApiKeyClient> Clients { get; set; } = [];

    public bool IsValid() =>
        Clients.Count > 0 &&
        Clients.All(c => !string.IsNullOrWhiteSpace(c.Name) && c.Key.Length >= 16) &&
        Clients.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() == Clients.Count;
}

public sealed class ApiKeyClient
{
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
