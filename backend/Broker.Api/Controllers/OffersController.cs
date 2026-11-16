using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>Wycena u wypożyczalni. Działa bez logowania, zalogowanemu brakujące dane bierzemy z profilu.</summary>
[Route("api/v1/offers")]
[AllowAnonymous]
public sealed class OffersController(QuoteService quotes) : ApiControllerBase
{
    /// <summary>
    /// Oferty dla auta, kierowcy i terminu (limit 15 s na wszystkie wypożyczalnie, pominięte są w skippedProviders).
    /// Wymaga nagłówka X-CSRF: 1.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<QuoteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteResult>> CreateQuote(QuoteRequest request, CancellationToken cancellationToken)
    {
        var result = await quotes.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }
}
