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
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ShowtimeBackend\ShowtimeBackend.csproj" />
  </ItemGroup>

</Project>

```
## 2. Program.cs运行入口示例
```csharp
using System;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.TestData;

namespace ShowtimeBackend.TestDataRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Test Data Generator for Showtime Backend");
            Console.WriteLine("========================================");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql("Host=localhost;Database=showtime;Username=postgres;Password=yourpassword");

            using var context = new AppDbContext(optionsBuilder.Options);

            // Ensure database is created
            context.Database.EnsureCreated();

            var generator = new TestDataGenerator(context);
            generator.GenerateAllData();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}

```
