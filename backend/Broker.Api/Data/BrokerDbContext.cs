using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Broker.Api.Data;

/// <summary>Baza Brokera: wypożyczalnie, profile użytkowników, zapisane oferty i wynajmy.</summary>
public class BrokerDbContext(DbContextOptions<BrokerDbContext> options) : DbContext(options)
{
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Rental> Rentals => Set<Rental>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Konfiguracja tylko fluent, każda encja w osobnej klasie w Data/Configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BrokerDbContext).Assembly);
    }
}
