using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Broker.Api.Data.Configurations;

public sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("Providers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(p => p.Code).IsUnique();

        // Kody muszą się zgadzać z konfiguracją (Providers:Lecturer ma kod lecturer, Providers:OtherGroup ma other-group).
        // IsEnabled to tylko stan początkowy, potem ProviderRegistry przepisuje go z Providers:<Klucz>:Enabled.
        // Nowe wypożyczalnie z konfiguracji Registry dopisuje sam, bez migracji.
        builder.HasData(
            new Provider { Id = 1, Code = Provider.OurCode, Name = "Car Rentals", IsEnabled = true },
            new Provider { Id = 2, Code = Provider.LecturerCode, Name = "Wypożyczalnia prowadzącego", IsEnabled = false },
            new Provider { Id = 3, Code = Provider.OtherGroupCode, Name = "Wypożyczalnia innej grupy", IsEnabled = false });
    }
}
