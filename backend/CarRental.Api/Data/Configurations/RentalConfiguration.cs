using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarRental.Api.Data.Configurations;

/// <summary>Tabela Rentals.</summary>
public sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.Property(r => r.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.LastName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Email).HasMaxLength(254).IsRequired();
        builder.Property(r => r.BrokerName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.ReturnNote).HasMaxLength(1000);
        builder.Property(r => r.CompletedBy).HasMaxLength(200);
        builder.Property(r => r.AttachmentKey).HasMaxLength(300);
        builder.Property(r => r.AttachmentContentType).HasMaxLength(100);
        builder.Property(r => r.AttachmentFileName).HasMaxLength(255);
        builder.Property(r => r.TotalPrice).HasPrecision(12, 2);

        // Status zapisujemy jako tekst, żeby w bazie było widać "Pending" a nie 0
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // Jedna oferta = najwyżej jeden wynajem
        builder.HasOne(r => r.Offer)
            .WithOne()
            .HasForeignKey<Rental>(r => r.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.OfferId).IsUnique();

        // Restrict, bo SQL Server nie pozwala na dwie ścieżki kaskadowego usuwania
        builder.HasOne(r => r.Car)
            .WithMany()
            .HasForeignKey(r => r.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        // Dostępność auta (BlockingCar)
        builder.HasIndex(r => new { r.CarId, r.Status });
        // Izolacja danych brokerów (WHERE BrokerName = @broker)
        builder.HasIndex(r => r.BrokerName);
        // Proces w tle (Status = Pending AND CreatedAtUtc <= @cutoff) i filtr panelu pracownika
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });
    }
}
