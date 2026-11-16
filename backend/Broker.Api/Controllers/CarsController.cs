using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>Katalog aut ze wszystkich wypożyczalni. Wyszukiwanie działa bez logowania.</summary>
[Route("api/v1/cars")]
[AllowAnonymous]
public sealed class CarsController(CatalogService catalog) : ApiControllerBase
{
    /// <summary>Lista aut z filtrem (marka, model, rok, lokalizacja) i stronicowaniem (domyślnie po 5).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CarListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CarListItem>>> GetCars([FromQuery] CarSearchQuery query, CancellationToken cancellationToken)
    {
        var page = await catalog.SearchAsync(query, cancellationToken);
        return Ok(page);
    }

    /// <summary>Marki i modele dostępne w podłączonych wypożyczalniach (strona główna).</summary>
    [HttpGet("makes")]
    [ProducesResponseType<IReadOnlyList<MakeModels>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MakeModels>>> GetMakes(CancellationToken cancellationToken)
    {
        var makes = await catalog.GetMakesAsync(cancellationToken);
        return Ok(makes);
    }

    /// <summary>Szczegóły auta, np. GET /api/v1/cars/our/1.</summary>
    /// <param name="provider">Kod wypożyczalni (our, lecturer, other-group).</param>
    /// <param name="carId">Id auta u tej wypożyczalni.</param>
    /// <param name="cancellationToken">Anulowanie żądania.</param>
    [HttpGet("{provider}/{carId}")]
    [ProducesResponseType<CarDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CarDetails>> GetCar(string provider, string carId, CancellationToken cancellationToken)
    {
        var result = await catalog.GetCarAsync(provider, carId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }
}
