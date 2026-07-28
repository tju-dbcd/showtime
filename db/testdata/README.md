# 测试数据生成工具

## 1. 测试项目配置 TestDataGenerator.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Bogus" Version="35.6.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Oracle.EntityFrameworkCore" Version="10.23.26200" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\backend\ShowtimeBackend\ShowtimeBackend.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>

```
## 2. Program.cs运行入口示例
```csharp
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShowtimeBackend.Data;
using ShowtimeBackend.TestData;

namespace ShowtimeBackend.TestDataRunner
{
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
                    : configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    Console.WriteLine("[ERROR] Connection string not found.");
                    Console.WriteLine("Usage: dotnet run [connection_string]");
                    Console.WriteLine("Example: dotnet run \"User Id=liborui;Password=liborui;Data Source=//120.27.157.163:1521/XEPDB1\"");
                    return;
                }

                Console.WriteLine($"Database: {ExtractDbName(connectionString)}");

                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseOracle(connectionString);

                using var context = new AppDbContext(optionsBuilder.Options);

                Console.WriteLine("Checking database...");
                context.Database.EnsureCreated();

                var genConfig = configuration.GetSection("DataGeneration");
                int showCount = int.Parse(genConfig["ShowCount"] ?? "10");
                int minSessions = int.Parse(genConfig["MinSessionsPerShow"] ?? "3");
                int maxSessions = int.Parse(genConfig["MaxSessionsPerShow"] ?? "5");
                int seatsPerSession = int.Parse(genConfig["SeatsPerSession"] ?? "200");
                bool enableDetailedLog = bool.Parse(genConfig["EnableDetailedLog"] ?? "true");

                Console.WriteLine($"Generating test data...");
                Console.WriteLine($"  - Shows: {showCount}");
                Console.WriteLine($"  - Sessions per show: {minSessions} ~ {maxSessions}");
                Console.WriteLine($"  - Seats per session: {seatsPerSession}");
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
                Console.WriteLine($"[ERROR] {ex.Message}");
                Console.WriteLine(ex.StackTrace);
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
}

```
## 3. appsettings.json 配置文件
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "User Id=liborui;Password=liborui;Data Source=//120.27.157.163:1521/XEPDB1"
  },
  "DataGeneration": {
    "ShowCount": 10,
    "MinSessionsPerShow": 3,
    "MaxSessionsPerShow": 5,
    "SeatsPerSession": 200,
    "EnableDetailedLog": true
  }
}