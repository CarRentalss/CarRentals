using CarRental.Api.Auth;
using CarRental.Api.Contracts;
using CarRental.Api.Domain;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Api.Controllers;

/// <summary>
/// Endpointy panelu pracownika. Wymagają tokena z rolą Employee, sam klucz brokera nie wystarczy.
/// </summary>
[Route("employee/rentals")]
[Authorize(Policy = AuthConstants.EmployeePolicy)]
public sealed class EmployeeRentalsController(EmployeeRentalService employeeRentals) : ApiControllerBase
{
    /// <summary>Lista wynajmów naszej firmy, opcjonalnie z filtrem po statusie (np. ReturnRequested = gotowe do odbioru).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmployeeRentalListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<EmployeeRentalListItem>>> List(
        [FromQuery] RentalStatus? status, CancellationToken cancellationToken)
    {
        var rentals = await employeeRentals.ListAsync(status, cancellationToken);
        return Ok(rentals);
    }

    /// <summary>Szczegóły wynajmu.</summary>
    [HttpGet("{rentalId:guid}")]
    [ProducesResponseType<EmployeeRentalDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmployeeRentalDetails>> Get(Guid rentalId, CancellationToken cancellationToken)
    {
        var details = await employeeRentals.GetDetailsAsync(rentalId, cancellationToken);
        return details is null
            ? ErrorResult(ServiceError.NotFound("Nie znaleziono wynajmu."))
            : Ok(details);
    }

    /// <summary>Potwierdzenie zwrotu z notatką o stanie auta i zdjęciem albo PDF-em.</summary>
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
        var employeeName = User.Identity?.Name ?? "nieznany pracownik";
        var result = await employeeRentals.ConfirmReturnAsync(rentalId, form.Note!, form.File!, employeeName, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Pobranie załącznika. Pliki są prywatne i wychodzą tylko przez API, po sprawdzeniu roli.</summary>
    [HttpGet("{rentalId:guid}/attachment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await employeeRentals.GetAttachmentAsync(rentalId, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error);
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
