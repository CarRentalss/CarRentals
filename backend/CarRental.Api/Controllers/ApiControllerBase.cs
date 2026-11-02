using CarRental.Api.Auth;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Nazwa brokera z klucza API (ustawiana przy uwierzytelnianiu).</summary>
    protected string BrokerName =>
        User.FindFirst(AuthConstants.BrokerClaim)?.Value
        ?? throw new InvalidOperationException("Brak informacji o brokerze w żądaniu.");

    /// <summary>Zamienia błąd z serwisu na odpowiedź z kodem HTTP z kontraktu.</summary>
    protected ActionResult ErrorResult(ServiceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ServiceErrorType.Validation)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message);
            return ValidationProblem(ModelState);
        }

        var statusCode = error.Type switch
        {
            ServiceErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ServiceErrorType.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorType.Conflict => StatusCodes.Status409Conflict,
            ServiceErrorType.Gone => StatusCodes.Status410Gone,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(title: error.Message, statusCode: statusCode);
    }
}
