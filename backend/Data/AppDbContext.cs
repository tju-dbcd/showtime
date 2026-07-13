using Microsoft.EntityFrameworkCore;

namespace ShowtimeBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
