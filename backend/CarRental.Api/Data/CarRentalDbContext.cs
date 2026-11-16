using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Data;

/// <summary>
/// Baza naszej wypożyczalni (CarRental DB): auta, oferty, wynajmy.
/// Mapowanie tabel jest w Data/Configurations (jedna klasa na encję).
/// </summary>
public class CarRentalDbContext(DbContextOptions<CarRentalDbContext> options) : DbContext(options)
{
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Rental> Rentals => Set<Rental>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Wczytuje CarConfiguration, OfferConfiguration i RentalConfiguration
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarRentalDbContext).Assembly);
    }
}
