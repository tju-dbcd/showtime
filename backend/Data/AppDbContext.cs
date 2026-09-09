using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected AppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<SeatMap> SeatMaps => Set<SeatMap>();
    public DbSet<SeatSection> SeatSections => Set<SeatSection>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<SeatRule> SeatRules => Set<SeatRule>();
    public DbSet<SeatRuleScope> SeatRuleScopes => Set<SeatRuleScope>();
    public DbSet<SeatLock> SeatLocks => Set<SeatLock>();
    public DbSet<SeatReservation> SeatReservations => Set<SeatReservation>();
    public DbSet<OrderEventOutbox> OrderEventOutbox => Set<OrderEventOutbox>();
    public DbSet<ShowSession> ShowSessions => Set<ShowSession>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<PriceStrategy> PriceStrategy => Set<PriceStrategy>();
    public DbSet<Show> Shows => Set<Show>();
    public DbSet<DynamicPricingRule> DynamicPricingRules => Set<DynamicPricingRule>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<MarketingContent> MarketingContents => Set<MarketingContent>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("APP_OWNER");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.IsPrimaryKey() && property.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)
                    {
                        property.SetColumnType(null);
                    }
                }
            }
        }
    }
}
