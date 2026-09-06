using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.Jwt;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class UserSessionServiceTests
{
    [Fact]
    public async Task ConcurrentLogins_LeaveExactlyOneActiveSession()
    {
        await using var database = await SharedSessionDatabase.CreateAsync();
        var userId = await database.SeedUserAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = CreateService(firstContext);
        var second = CreateService(secondContext);

        var results = await Task.WhenAll(
            first.StartAsync(
                userId,
                new ClientRequestMetadata("192.0.2.10", "Device-A"),
                CancellationToken.None),
            second.StartAsync(
                userId,
                new ClientRequestMetadata("192.0.2.11", "Device-B"),
                CancellationToken.None));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        await using var assertionContext = database.CreateContext();
        var sessions = await assertionContext.Set<UserSession>()
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Single(
            sessions,
            session => session.Status == UserSessionStatuses.Active);
    }

    [Fact]
    public async Task ConcurrentRefresh_AllowsAtMostOneSuccessAndLocksSession()
    {
        await using var database = await SharedSessionDatabase.CreateAsync();
        var userId = await database.SeedUserAsync();
        string refreshToken;
        await using (var setupContext = database.CreateContext())
        {
            var issued = await CreateService(setupContext).StartAsync(
                userId,
                new ClientRequestMetadata("192.0.2.10", "Device-A"),
                CancellationToken.None);
            refreshToken = issued.Value!.RefreshToken;
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            CreateService(firstContext).RotateAsync(
                refreshToken,
                CancellationToken.None),
            CreateService(secondContext).RotateAsync(
                refreshToken,
                CancellationToken.None));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(
            results,
            result => result.Failure == UserSessionFailure.TokenReused);
        await using var assertionContext = database.CreateContext();
        var session = await assertionContext.Set<UserSession>()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(UserSessionStatuses.Locked, session.Status);
        Assert.True(session.RiskFlag);
    }

    private static UserSessionService CreateService(AppDbContext context)
    {
        var options = Options.Create(new JwtOptions
        {
            Key = AuthTestFactory.TestKey,
            Issuer = AuthTestFactory.TestIssuer,
            Audience = AuthTestFactory.TestAudience,
            ExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
        });
        return new UserSessionService(
            context,
            new RefreshTokenService(options),
            options,
            new NullOperationLogWriter(),
            TimeProvider.System);
    }

    private sealed class NullOperationLogWriter : IOperationLogWriter
    {
        public ValueTask WriteAsync(
            OperationLogWriteRequest request,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SharedSessionDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly string _connectionString;

        private SharedSessionDatabase(
            SqliteConnection anchor,
            string connectionString)
        {
            _anchor = anchor;
            _connectionString = connectionString;
        }

        public static async Task<SharedSessionDatabase> CreateAsync()
        {
            var connectionString =
                $"Data Source=session-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var database = new SharedSessionDatabase(anchor, connectionString);
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public SqliteAuthDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new SqliteAuthDbContext(options);
        }

        public async Task<long> SeedUserAsync()
        {
            await using var context = CreateContext();
            var now = DateTime.UtcNow;
            var role = new Role
            {
                RoleCode = "USER",
                RoleName = "User role",
                Status = true,
                CreateTime = now,
                UpdateTime = now,
                CreateBy = "tests",
                UpdateBy = "tests",
            };
            var user = new SysUser
            {
                UserName = "alice",
                PasswordHash = "unused",
                Phone = "13900000001",
                UserType = "NORMAL",
                Status = 1,
                CreateTime = now,
                UpdateTime = now,
                CreateBy = "tests",
                UpdateBy = "tests",
            };
            user.UserRoles.Add(new UserRole { Role = role });
            context.Add(user);
            await context.SaveChangesAsync();
            return user.UserId;
        }

        public async ValueTask DisposeAsync()
        {
            await _anchor.DisposeAsync();
        }
    }
}
