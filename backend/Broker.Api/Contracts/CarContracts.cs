using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Contracts;

// Kontrakty dla frontu (docs/broker-api-contract.md). Atrybuty walidacji są tylko w DTO, encje są bez atrybutów.

/// <summary>Auto na liście (GET /api/v1/cars). ImageUrl i ImageCredit: zdjęcie i jego autor, null gdy wypożyczalnia nie podaje.</summary>
public sealed record CarListItem(
    string Provider,
    string ProviderName,
    string CarId,
    string Make,
    string Model,
    int Year,
    string? Location,
    bool Available,
    string? ImageUrl = null,
    string? ImageCredit = null);

/// <summary>Szczegóły auta (GET /api/v1/cars/{provider}/{carId}).</summary>
public sealed record CarDetails(
    string Provider,
    string ProviderName,
    string CarId,
    string Make,
    string Model,
    int Year,
    string? Location,
    bool Available,
    string? ImageUrl = null,
    string? ImageCredit = null);

/// <summary>Marka z listą modeli (landing page).</summary>
public sealed record MakeModels(string Make, IReadOnlyList<string> Models);

/// <summary>Jedna strona wyników.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)
{
    /// <summary>Wypożyczalnie, których katalogu nie udało się pobrać.</summary>
    public IReadOnlyList<SkippedProvider> SkippedProviders { get; init; } = [];
}

/// <summary>Filtry i stronicowanie listy aut. Nazwy z FromQuery, żeby klucze błędów walidacji były w camelCase.</summary>
public sealed class CarSearchQuery
{
    /// <summary>Marka, np. Toyota (bez wielkości liter).</summary>
    [FromQuery(Name = "make"), StringLength(100)]
    public string? Make { get; init; }

    /// <summary>Model, np. Corolla.</summary>
    [FromQuery(Name = "model"), StringLength(100)]
    public string? Model { get; init; }

    /// <summary>Rok produkcji.</summary>
    [FromQuery(Name = "year"), Range(1900, 2100)]
    public int? Year { get; init; }

    /// <summary>Miasto.</summary>
    [FromQuery(Name = "location"), StringLength(100)]
    public string? Location { get; init; }

    /// <summary>Numer strony od 1.</summary>
    [FromQuery(Name = "page"), Range(1, 100_000)]
    public int Page { get; init; } = 1;

    /// <summary>Wyników na stronę (domyślnie 5).</summary>
    [FromQuery(Name = "pageSize"), Range(1, 50)]
    public int PageSize { get; init; } = 5;
}

/// <summary>Dlaczego wypożyczalnia została pominięta.</summary>
public enum SkipReason
{
    Timeout,
    Error,
    Rejected
}

/// <summary>Wypożyczalnia, która nie odpowiedziała w czasie albo zwróciła błąd.</summary>
public sealed record SkippedProvider(string Provider, string ProviderName, SkipReason Reason, string Message);
