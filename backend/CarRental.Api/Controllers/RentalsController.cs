using CarRental.Api.Auth;
using CarRental.Api.Contracts;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Api.Controllers;

/// <summary>Wnioski o wynajem, status i zgłoszenie zwrotu.</summary>
[Route("api/v1/rentals")]
[Authorize(Policy = AuthConstants.BrokerPolicy)]
public sealed class RentalsController(RentalService rentals) : ApiControllerBase
{
    /// <summary>
    /// Składa wniosek o wynajem na podstawie oferty. Odpowiedź 202: wniosek przyjęty ze statusem Pending,
    /// wynik trzeba sprawdzić przez GET /api/v1/rentals/{rentalId}.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<RentalStatusChangedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<ActionResult<RentalStatusChangedResponse>> CreateRental(CreateRentalRequest request, CancellationToken cancellationToken)
    {
        var result = await rentals.CreateAsync(request, BrokerName, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error);
        }

        return AcceptedAtAction(nameof(GetRental), new { rentalId = result.Value.RentalId }, result.Value);
    }

    /// <summary>Status wynajmu. Broker widzi tylko wynajmy złożone swoim kluczem.</summary>
    [HttpGet("{rentalId:guid}")]
    [ProducesResponseType<RentalStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RentalStatusResponse>> GetRental(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await rentals.GetStatusAsync(rentalId, BrokerName, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Zgłoszenie zwrotu. Nie zamyka wynajmu, tylko zaczyna odbiór auta przez pracownika.</summary>
    [HttpPost("{rentalId:guid}/return")]
    [Consumes("application/json")]
    [ProducesResponseType<RentalStatusChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalStatusChangedResponse>> RequestReturn(
        Guid rentalId, ReturnRequest request, CancellationToken cancellationToken)
    {
        var result = await rentals.RequestReturnAsync(rentalId, request.Email!, BrokerName, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }
}
