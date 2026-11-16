using Broker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Zamienia błąd z serwisu na odpowiedź z kodem HTTP z kontraktu (ProblemDetails).</summary>
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
            ServiceErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ServiceErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ServiceErrorType.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorType.Conflict => StatusCodes.Status409Conflict,
            ServiceErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(title: error.Message, statusCode: statusCode);
    }
}
