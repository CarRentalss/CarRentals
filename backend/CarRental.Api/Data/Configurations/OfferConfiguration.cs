using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarRental.Api.Data.Configurations;

/// <summary>Tabela Offers.</summary>
public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.Property(o => o.Location).HasMaxLength(100).IsRequired();
        builder.Property(o => o.BrokerName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        builder.Property(o => o.DailyPrice).HasPrecision(10, 2);
        builder.Property(o => o.DailyInsurancePrice).HasPrecision(10, 2);
        builder.Property(o => o.TotalPrice).HasPrecision(12, 2);

        // Restrict: auta z ofertami nie da się usunąć (auta wycofujemy flagą IsActive)
        builder.HasOne(o => o.Car)
            .WithMany()
            .HasForeignKey(o => o.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pod sprzątanie wygasłych ofert (WHERE ExpiresAtUtc < @now)
        builder.HasIndex(o => o.ExpiresAtUtc);
    }
}
