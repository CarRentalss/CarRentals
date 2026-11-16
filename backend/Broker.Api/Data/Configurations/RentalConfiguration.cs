using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Broker.Api.Data.Configurations;

public sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.ToTable("Rentals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ProviderRentalId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ProviderCarId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.CarLabel).HasMaxLength(200).IsRequired();
        builder.Property(r => r.TotalPrice).HasPrecision(10, 2);
        builder.Property(r => r.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(r => r.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.LastName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Email).HasMaxLength(254).IsRequired();
        // Status jako tekst, łatwiej czytać w bazie
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500);

        // Wynajmy zostają w historii, więc profilu z wynajmami nie da się usunąć
        builder.HasOne(r => r.UserProfile)
            .WithMany()
            .HasForeignKey(r => r.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sprzątanie starych ofert nie może usunąć wynajmu, tylko odpina go od oferty
        builder.HasOne(r => r.Quote)
            .WithMany()
            .HasForeignKey(r => r.QuoteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Provider)
            .WithMany()
            .HasForeignKey(r => r.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Moje wypożyczenia" szukamy po użytkowniku, a jedna oferta to najwyżej jeden wynajem
        builder.HasIndex(r => r.UserProfileId);
        builder.HasIndex(r => r.QuoteId).IsUnique();
    }
}
