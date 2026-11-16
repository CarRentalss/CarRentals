using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Broker.Api.Auth;
using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Broker.Api.Providers.OurCarRental;

/// <summary>
/// Proxy panelu pracownika do naszego Car Rental API (/api/v1/employee/rentals).
/// Z Entra: token pracownika pobrany przez Microsoft.Identity.Web (rola Employee jest w tokenie).
/// Lokalnie z dev-login: nagłówki X-Dev-Employee / X-Dev-Roles (Car Rental API przyjmuje je tylko w Development).
/// </summary>
public sealed class OurEmployeeClient(
    HttpClient http,
    IOptions<ProvidersOptions> options,
    AuthModeInfo authMode,
    ICurrentUser currentUser,
    IServiceProvider services,
    ILogger<OurEmployeeClient> logger)
{
    public const string DevEmployeeHeader = "X-Dev-Employee";
    public const string DevRolesHeader = "X-Dev-Roles";

    private const string BasePath = "api/v1/employee/rentals";

    public Task<ServiceResult<IReadOnlyList<EmployeeRentalListItem>>> ListAsync(RentalStatus? status, CancellationToken cancellationToken)
    {
        var path = status is null ? BasePath : $"{BasePath}?status={status}";
        return SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            async (response, ct) => (IReadOnlyList<EmployeeRentalListItem>)(
                await response.Content.ReadFromJsonAsync<List<EmployeeRentalListItem>>(OurCarRentalClient.Json, ct) ?? []),
            cancellationToken);
    }

    public Task<ServiceResult<EmployeeRentalDetails>> GetAsync(Guid rentalId, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{rentalId}"),
            async (response, ct) => (await response.Content.ReadFromJsonAsync<EmployeeRentalDetails>(OurCarRentalClient.Json, ct))!,
            cancellationToken);

    public Task<ServiceResult<RentalStatusChangedResponse>> ConfirmReturnAsync(
        Guid rentalId, string note, IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        return SendAsync(
            () =>
            {
                // Przekazujemy multipart dalej: notatka + plik z oryginalną nazwą i typem
                var fileContent = new StreamContent(file.OpenReadStream());
                if (MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
                {
                    fileContent.Headers.ContentType = contentType;
                }

                var form = new MultipartFormDataContent
                {
                    { new StringContent(note), "note" },
                    { fileContent, "file", Path.GetFileName(file.FileName) }
                };

                return new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{rentalId}/confirm-return") { Content = form };
            },
            async (response, ct) => (await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(OurCarRentalClient.Json, ct))!,
            cancellationToken);
    }

    public Task<ServiceResult<AttachmentFile>> GetAttachmentAsync(Guid rentalId, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{rentalId}/attachment"),
            async (response, ct) =>
            {
                var content = await response.Content.ReadAsByteArrayAsync(ct);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? $"zalacznik-{rentalId}";
                return new AttachmentFile(content, contentType, fileName);
            },
            cancellationToken);

    private async Task<ServiceResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var settings = options.Value.Our;
        if (!settings.IsConfigured)
        {
            return ServiceError.Unavailable("Car Rental API nie jest skonfigurowane (Providers:Our).");
        }

        try
        {
            using var request = createRequest();
            request.Headers.Add(OurCarRentalClient.ApiKeyHeader, settings.ApiKey);

            var authError = await AddEmployeeIdentityAsync(request);
            if (authError is not null)
            {
                return authError;
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await read(response, cancellationToken);
            }

            return await MapErrorAsync(response, cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Car Rental API (panel pracownika) nie odpowiada.");
            return ServiceError.Unavailable("Car Rental API nie odpowiada. Spróbuj za chwilę.");
        }
    }

    private async Task<ServiceError?> AddEmployeeIdentityAsync(HttpRequestMessage request)
    {
        switch (authMode.Mode)
        {
            case AuthMode.Entra:
            {
                var tokenAcquisition = services.GetRequiredService<ITokenAcquisition>();
                try
                {
                    var token = await tokenAcquisition.GetAccessTokenForUserAsync(new[] { authMode.EmployeeApiScope! });
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    return null;
                }
                catch (MicrosoftIdentityWebChallengeUserException ex)
                {
                    // Np. po restarcie aplikacji cache tokenów w pamięci jest pusty, trzeba zalogować się ponownie
                    logger.LogInformation(ex, "Brak tokena do Car Rental API w cache, użytkownik musi zalogować się ponownie.");
                    return ServiceError.Unauthorized("Sesja wygasła. Zaloguj się ponownie.");
                }
            }

            case AuthMode.DevLogin:
                request.Headers.Add(DevEmployeeHeader, currentUser.UserName ?? "dev");
                request.Headers.Add(DevRolesHeader, string.Join(',', currentUser.Roles));
                return null;

            default:
                return ServiceError.Unauthorized("Logowanie nie jest skonfigurowane.");
        }
    }

    private static async Task<ServiceError> MapErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await ProblemReader.ReadAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => ServiceError.Validation(problem?.Field ?? string.Empty, problem?.Message ?? "Niepoprawne dane."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ServiceError.Forbidden("Car Rental API odrzuciło uprawnienia pracownika."),
            HttpStatusCode.NotFound => ServiceError.NotFound(problem?.Message ?? "Nie znaleziono wynajmu."),
            HttpStatusCode.Conflict => ServiceError.Conflict(problem?.Message ?? "Konflikt stanu wynajmu."),
            _ => ServiceError.Unavailable("Car Rental API zwróciło błąd. Spróbuj za chwilę.")
        };
    }
}
