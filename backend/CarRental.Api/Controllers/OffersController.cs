using CarRental.Api.Auth;
using CarRental.Api.Contracts;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Api.Controllers;

/// <summary>Oferty z wyceną.</summary>
[Route("offers")]
[Authorize(Policy = AuthConstants.BrokerPolicy)]
public sealed class OffersController(OfferService offers) : ApiControllerBase
{
    /// <summary>
    /// Tworzy ofertę dla auta, kierowcy i terminu. To POST, a nie GET, bo oferta jest zapisywana
    /// (ma numer i termin ważności), a wiek i staż kierowcy nie powinny trafiać do adresu.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<OfferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OfferResponse>> CreateOffer(CreateOfferRequest request, CancellationToken cancellationToken)
    {
        var result = await offers.CreateAsync(request, BrokerName, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }
}
