using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.IdentityData;
using Microsoft.Extensions.Configuration;
using ShowtimeBackend.Data;
using ShowtimeBackend.Data.Interceptors;
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

            var encryptionKey = Environment.GetEnvironmentVariable(
                "IdentityData__EncryptionKey") ?? string.Empty;
            var identityOptions = new IdentityDataOptions
            {
                EncryptionKey = encryptionKey,
            };
            var validation = new IdentityDataOptionsValidator().Validate(
                Options.DefaultName,
                identityOptions);
            if (validation.Failed)
            {
                throw new InvalidOperationException(validation.FailureMessage);
            }

            using var identityProtector = new AesGcmIdentityDataProtector(
                Options.Create(identityOptions));

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder
                .UseOracle(connectionString)
                .AddInterceptors(
                    new UserRealNameEncryptionInterceptor(identityProtector));

            using var context = new AppDbContext(optionsBuilder.Options);

            Console.WriteLine("Checking database connection...");
            // 注意：数据库 Schema 已由 APP_OWNER 持有（db/baseline 脚本），此处不做 EnsureCreated：
            // 1) Oracle 提供器 HasTables 只检查当前用户 USER_TABLES，个人账号空 schema 会被误判为空库触发建表；
            // 2) 个人账号无 DDL 权限，也不应重建 Schema。
            context.Database.ExecuteSqlRaw("SELECT 1 FROM DUAL");

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
            Console.WriteLine($"测试账号 -> 管理员:   {TestDataGenerator.AdminUserName} / {TestDataGenerator.AdminPassword}");
            Console.WriteLine($"测试账号 -> 普通用户: testuser1~3 / {TestDataGenerator.TestUserPassword}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }

        // 非交互环境（CI / 管道重定向）下不阻塞等待按键
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
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
