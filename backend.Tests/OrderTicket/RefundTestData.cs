using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Data;

namespace ShowtimeBackend.Tests.OrderTicket;

internal sealed class RefundTestData : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private RefundTestData(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public AppDbContext Db { get; }

    public static async Task<RefundTestData> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        return new RefundTestData(connection, db);
    }

    public static string CreateToken(long userId, string userName, string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestFactory.TestKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            AuthTestFactory.TestIssuer,
            AuthTestFactory.TestAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
