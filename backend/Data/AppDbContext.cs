using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;

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
    public DbSet<ShowSession> ShowSessions => Set<ShowSession>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<PriceStrategy> PriceStrategy => Set<PriceStrategy>();
    public DbSet<Show> Shows => Set<Show>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("APP_OWNER");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
