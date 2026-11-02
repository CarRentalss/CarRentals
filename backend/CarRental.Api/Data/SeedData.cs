using CarRental.Api.Domain;

namespace CarRental.Api.Data;

/// <summary>
/// Flota na start. Pracownik nie dodaje aut w aplikacji (poza zakresem), więc auta są w seedzie.
/// Wartości muszą być stałe, bo HasData trafia do migracji.
/// </summary>
public static class SeedData
{
    public static readonly Car[] Cars =
    [
        new() { Id = 1, Make = "Toyota", Model = "Corolla", Year = 2022, Location = "Warszawa", BasePricePerDay = 150m, InsurancePricePerDay = 30m },
        new() { Id = 2, Make = "Toyota", Model = "Yaris", Year = 2023, Location = "Warszawa", BasePricePerDay = 120m, InsurancePricePerDay = 25m },
        new() { Id = 3, Make = "Skoda", Model = "Octavia", Year = 2021, Location = "Warszawa", BasePricePerDay = 160m, InsurancePricePerDay = 30m },
        new() { Id = 4, Make = "Volkswagen", Model = "Golf", Year = 2022, Location = "Kraków", BasePricePerDay = 155m, InsurancePricePerDay = 30m },
        new() { Id = 5, Make = "Kia", Model = "Sportage", Year = 2023, Location = "Kraków", BasePricePerDay = 220m, InsurancePricePerDay = 40m },
        new() { Id = 6, Make = "Hyundai", Model = "i30", Year = 2020, Location = "Kraków", BasePricePerDay = 130m, InsurancePricePerDay = 25m },
        new() { Id = 7, Make = "Ford", Model = "Focus", Year = 2021, Location = "Gdańsk", BasePricePerDay = 140m, InsurancePricePerDay = 28m },
        new() { Id = 8, Make = "Renault", Model = "Clio", Year = 2022, Location = "Gdańsk", BasePricePerDay = 115m, InsurancePricePerDay = 22m },
        new() { Id = 9, Make = "BMW", Model = "Seria 3", Year = 2023, Location = "Warszawa", BasePricePerDay = 320m, InsurancePricePerDay = 60m },
        new() { Id = 10, Make = "Audi", Model = "A4", Year = 2022, Location = "Wrocław", BasePricePerDay = 300m, InsurancePricePerDay = 55m },
        new() { Id = 11, Make = "Toyota", Model = "RAV4", Year = 2024, Location = "Wrocław", BasePricePerDay = 260m, InsurancePricePerDay = 45m },
        new() { Id = 12, Make = "Opel", Model = "Corsa", Year = 2021, Location = "Poznań", BasePricePerDay = 110m, InsurancePricePerDay = 20m },
    ];
}
