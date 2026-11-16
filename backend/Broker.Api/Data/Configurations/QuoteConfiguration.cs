using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Broker.Api.Data.Configurations;

public sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("Quotes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.ProviderOfferId).HasMaxLength(100).IsRequired();
        builder.Property(q => q.ProviderCarId).HasMaxLength(100).IsRequired();
        builder.Property(q => q.CarLabel).HasMaxLength(200).IsRequired();
        builder.Property(q => q.DailyPrice).HasPrecision(10, 2);
        builder.Property(q => q.DailyInsurancePrice).HasPrecision(10, 2);
        builder.Property(q => q.TotalPrice).HasPrecision(10, 2);
        builder.Property(q => q.Currency).HasMaxLength(3).IsFixedLength().IsRequired();

        // Wypożyczalni z ofertami nie da się usunąć (najpierw trzeba ją wyłączyć)
        builder.HasOne(q => q.Provider)
            .WithMany(p => p.Quotes)
            .HasForeignKey(q => q.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Usunięcie profilu nie usuwa ofert, tylko odpina je od użytkownika
        builder.HasOne(q => q.UserProfile)
            .WithMany(u => u.Quotes)
            .HasForeignKey(q => q.UserProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // Oferty użytkownika (KM3: historia) i sprzątanie wygasłych ofert w tle
        builder.HasIndex(q => q.UserProfileId);
        builder.HasIndex(q => q.ExpiresAtUtc);
    }
}
