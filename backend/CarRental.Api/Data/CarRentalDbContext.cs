using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Data;

/// <summary>Baza naszej wypożyczalni (CarRental DB): auta, oferty, wynajmy.</summary>
public class CarRentalDbContext(DbContextOptions<CarRentalDbContext> options) : DbContext(options)
{
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Rental> Rentals => Set<Rental>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Car>(car =>
        {
            car.Property(c => c.Make).HasMaxLength(50).IsRequired();
            car.Property(c => c.Model).HasMaxLength(50).IsRequired();
            car.Property(c => c.Location).HasMaxLength(100).IsRequired();
            car.Property(c => c.BasePricePerDay).HasPrecision(10, 2);
            car.Property(c => c.InsurancePricePerDay).HasPrecision(10, 2);
            car.HasIndex(c => new { c.Make, c.Model });

            // Auta na start (seed), trafiają do migracji
            car.HasData(SeedData.Cars);
        });

        modelBuilder.Entity<Offer>(offer =>
        {
            offer.Property(o => o.Location).HasMaxLength(100).IsRequired();
            offer.Property(o => o.BrokerName).HasMaxLength(100).IsRequired();
            offer.Property(o => o.Currency).HasMaxLength(3).IsRequired();
            offer.Property(o => o.DailyPrice).HasPrecision(10, 2);
            offer.Property(o => o.DailyInsurancePrice).HasPrecision(10, 2);
            offer.Property(o => o.TotalPrice).HasPrecision(12, 2);

            offer.HasOne(o => o.Car)
                .WithMany()
                .HasForeignKey(o => o.CarId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Rental>(rental =>
        {
            rental.Property(r => r.FirstName).HasMaxLength(100).IsRequired();
            rental.Property(r => r.LastName).HasMaxLength(100).IsRequired();
            rental.Property(r => r.Email).HasMaxLength(254).IsRequired();
            rental.Property(r => r.BrokerName).HasMaxLength(100).IsRequired();
            rental.Property(r => r.Reason).HasMaxLength(500);
            rental.Property(r => r.ReturnNote).HasMaxLength(1000);
            rental.Property(r => r.CompletedBy).HasMaxLength(200);
            rental.Property(r => r.AttachmentKey).HasMaxLength(300);
            rental.Property(r => r.AttachmentContentType).HasMaxLength(100);
            rental.Property(r => r.AttachmentFileName).HasMaxLength(255);
            rental.Property(r => r.TotalPrice).HasPrecision(12, 2);

            // Status zapisujemy jako tekst, żeby w bazie było widać "Pending" a nie 0
            rental.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // Jedna oferta = najwyżej jeden wynajem
            rental.HasOne(r => r.Offer)
                .WithOne()
                .HasForeignKey<Rental>(r => r.OfferId)
                .OnDelete(DeleteBehavior.Restrict);
            rental.HasIndex(r => r.OfferId).IsUnique();

            // Restrict, bo SQL Server nie pozwala na dwie ścieżki kaskadowego usuwania
            rental.HasOne(r => r.Car)
                .WithMany()
                .HasForeignKey(r => r.CarId)
                .OnDelete(DeleteBehavior.Restrict);

            rental.HasIndex(r => new { r.CarId, r.Status });
            rental.HasIndex(r => r.BrokerName);
        });
    }
}
