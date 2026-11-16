using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Broker.Api.Data.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.EntraObjectId).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Location).HasMaxLength(100);

        // Wyliczane w kodzie, nie w bazie
        builder.Ignore(u => u.MissingFields);
        builder.Ignore(u => u.IsComplete);

        // Profil szukamy po oid z tokena przy każdym żądaniu, jeden profil na konto
        builder.HasIndex(u => u.EntraObjectId).IsUnique();
    }
}
