using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 演出场次表 (SHOW_SESSION)
    /// </summary>
    public DbSet<ShowSession> ShowSessions { get; set; } = null!;

    /// <summary>
    /// 票价策略表 (PRICE_STRATEGY)
    /// </summary>
    public DbSet<PriceStrategy> PriceStrategies { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("APP_OWNER");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
