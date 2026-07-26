using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<SeatMap> SeatMaps => Set<SeatMap>();
    public DbSet<SeatSection> SeatSections => Set<SeatSection>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<SeatRule> SeatRules => Set<SeatRule>();
    public DbSet<SeatRuleScope> SeatRuleScopes => Set<SeatRuleScope>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("APP_OWNER");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
