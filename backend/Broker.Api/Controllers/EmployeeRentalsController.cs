using Broker.Api.Auth;
using Broker.Api.Contracts;
using Broker.Api.Providers.OurCarRental;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>
/// Panel pracownika naszej wypożyczalni. Broker sprawdza rolę Employee i przekazuje wywołanie
/// do Car Rental API (front ma jeden sposób logowania, a Car Rental API nie potrzebuje CORS).
/// </summary>
[Route("api/v1/employee/rentals")]
[Authorize(Policy = AuthSetup.EmployeePolicy)]
public sealed class EmployeeRentalsController(OurEmployeeClient employeeClient) : ApiControllerBase
{
    /// <summary>Wynajmy naszej firmy, opcjonalnie po statusie (Confirmed = aktywne, ReturnRequested = do odbioru).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmployeeRentalListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<EmployeeRentalListItem>>> List(
        [FromQuery] RentalStatus? status, CancellationToken cancellationToken)
    {
        var result = await employeeClient.ListAsync(status, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Szczegóły wynajmu.</summary>
    [HttpGet("{rentalId:guid}")]
    [ProducesResponseType<EmployeeRentalDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<EmployeeRentalDetails>> Get(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await employeeClient.GetAsync(rentalId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Potwierdzenie zwrotu: notatka + zdjęcie albo PDF (multipart), przekazywane do Car Rental API. Wymaga X-CSRF: 1.</summary>
    [HttpPost("{rentalId:guid}/confirm-return")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    [ProducesResponseType<RentalStatusChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalStatusChangedResponse>> ConfirmReturn(
        Guid rentalId, [FromForm] ConfirmReturnForm form, CancellationToken cancellationToken)
    {
        var result = await employeeClient.ConfirmReturnAsync(rentalId, form.Note!, form.File!, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Pobranie załącznika z potwierdzenia zwrotu.</summary>
    [HttpGet("{rentalId:guid}/attachment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await employeeClient.GetAttachmentAsync(rentalId, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error);
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
