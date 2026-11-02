using CarRental.Api.Auth;
using CarRental.Api.Contracts;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Api.Controllers;

/// <summary>Katalog aut dla brokerów.</summary>
[Route("cars")]
[Authorize(Policy = AuthConstants.BrokerPolicy)]
public sealed class CarsController(CarCatalogService catalog) : ApiControllerBase
{
    /// <summary>Lista aut naszej wypożyczalni. Filtry są opcjonalne.</summary>
    /// <param name="make">Marka, np. Toyota.</param>
    /// <param name="model">Model, np. Corolla.</param>
    /// <param name="year">Rok produkcji.</param>
    /// <param name="cancellationToken">Anulowanie żądania.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CarResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CarResponse>>> GetCars(
        [FromQuery] string? make, [FromQuery] string? model, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var cars = await catalog.GetCarsAsync(make, model, year, cancellationToken);
        return Ok(cars);
    }

    /// <summary>Jedno auto (strona szczegółów).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CarResponse>> GetCar(int id, CancellationToken cancellationToken)
    {
        var car = await catalog.GetCarAsync(id, cancellationToken);
        return car is null
            ? ErrorResult(ServiceError.NotFound("Nie znaleziono samochodu."))
            : Ok(car);
    }
}
