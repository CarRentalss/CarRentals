using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarRental.Api.Data.Configurations;

/// <summary>Tabela Cars. Konfiguracja tylko fluent, encja nie ma atrybutów EF.</summary>
public sealed class CarConfiguration : IEntityTypeConfiguration<Car>
{
    public void Configure(EntityTypeBuilder<Car> builder)
    {
        builder.Property(c => c.Make).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Location).HasMaxLength(100).IsRequired();
        builder.Property(c => c.BasePricePerDay).HasPrecision(10, 2);
        builder.Property(c => c.InsurancePricePerDay).HasPrecision(10, 2);
        builder.Property(c => c.ImageUrl).HasMaxLength(300);

        // Filtr katalogu po marce i modelu
        builder.HasIndex(c => new { c.Make, c.Model });

        // Auta na start (seed), trafiają do migracji
        builder.HasData(SeedData.Cars);
    }
}
