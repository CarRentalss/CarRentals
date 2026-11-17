using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>Wynajem przez Brokera i "Moje wypożyczenia". Tylko dla zalogowanych, każdy widzi tylko swoje.</summary>
[Route("api/v1/rentals")]
[Authorize]
public sealed class RentalsController(RentalService rentals) : ApiControllerBase
{
    /// <summary>
    /// Wniosek o wynajem z oferty (quoteId z POST /api/v1/offers). Odpowiedź 202: wypożyczalnia przyjęła wniosek,
    /// zwykle ze statusem Pending, wynik sprawdza się przez GET /api/v1/rentals/{rentalId}. Wymaga X-CSRF: 1.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<RentalResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RentalResponse>> Create(CreateRentalRequest request, CancellationToken cancellationToken)
    {
        var result = await rentals.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error);
        }

        return AcceptedAtAction(nameof(Get), new { rentalId = result.Value.RentalId }, result.Value);
    }

    /// <summary>Moje wypożyczenia, najnowsze na górze. Status Pending i ReturnRequested Broker odświeża u wypożyczalni.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RentalResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<RentalResponse>>> List(CancellationToken cancellationToken)
    {
        var result = await rentals.ListAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Jedno wypożyczenie ze świeżym statusem. Cudze daje 404.</summary>
    [HttpGet("{rentalId:guid}")]
    [ProducesResponseType<RentalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RentalResponse>> Get(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await rentals.GetAsync(rentalId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Zgłoszenie zwrotu (tylko dla aktywnego wypożyczenia). Potem pracownik potwierdza odbiór auta. Wymaga X-CSRF: 1.</summary>
    [HttpPost("{rentalId:guid}/return")]
    [ProducesResponseType<RentalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RentalResponse>> RequestReturn(Guid rentalId, CancellationToken cancellationToken)
    {
        var result = await rentals.RequestReturnAsync(rentalId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }
}
