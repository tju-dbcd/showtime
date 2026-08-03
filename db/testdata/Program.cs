using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShowtimeBackend.Data;
using ShowtimeBackend.TestData;

namespace ShowtimeBackend.TestDataRunner;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("  ShowtimeBackend Test Data Generator");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string connectionString = args.Length > 0
                ? args[0]
                : configuration.GetConnectionString("DefaultConnection")!;

            if (string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("[ERROR] Connection string not found.");
                Console.WriteLine("Usage: dotnet run [connection_string]");
                Console.WriteLine("Example: dotnet run \"User Id=your_user;Password=your_pass;Data Source=//host:1521/XEPDB1\"");
                return;
            }

            Console.WriteLine($"Database: {ExtractDbName(connectionString)}");
            Console.WriteLine();

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseOracle(connectionString);

            using var context = new AppDbContext(optionsBuilder.Options);

            Console.WriteLine("Checking database connection...");
            context.Database.EnsureCreated();

            var genConfig = configuration.GetSection("DataGeneration");
            int showCount = int.Parse(genConfig["ShowCount"] ?? "10");
            int minSessions = int.Parse(genConfig["MinSessionsPerShow"] ?? "3");
            int maxSessions = int.Parse(genConfig["MaxSessionsPerShow"] ?? "5");
            int seatsPerSession = int.Parse(genConfig["SeatsPerSession"] ?? "200");
            bool enableDetailedLog = bool.Parse(genConfig["EnableDetailedLog"] ?? "true");

            Console.WriteLine("Configuration:");
            Console.WriteLine($"  Shows:              {showCount}");
            Console.WriteLine($"  Sessions per show:  {minSessions} ~ {maxSessions}");
            Console.WriteLine($"  Seats per session:  {seatsPerSession}");
            Console.WriteLine();

            var generator = new TestDataGenerator(
                context,
                showCount,
                minSessions,
                maxSessions,
                seatsPerSession,
                enableDetailedLog
            );

            generator.GenerateAllData();

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("  Data generation completed!");
            Console.WriteLine("========================================");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static string ExtractDbName(string connectionString)
    {
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.Trim().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Split('=')[1].Trim();
            }
        }
        return "unknown";
    }
}
