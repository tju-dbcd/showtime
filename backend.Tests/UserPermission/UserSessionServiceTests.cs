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
    public async Task ConcurrentRefresh_AllowsBothWithinGrace_SessionStaysActive()
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

        // 并发双发同一旧 token：第一个正常轮换，第二个命中宽限期再轮换一次，
        // 两者都应成功，且会话保持 Active（不再因单次重放锁死会话）。
        Assert.All(results, result => Assert.True(result.IsSuccess));
        await using var assertionContext = database.CreateContext();
        var session = await assertionContext.Set<UserSession>()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(UserSessionStatuses.Active, session.Status);
        Assert.False(session.RiskFlag);
    }

    [Fact]
    public async Task OldTokenReuseBeyondGrace_IsRejectedWithoutLockingSession()
    {
        await using var database = await SharedSessionDatabase.CreateAsync();
        var userId = await database.SeedUserAsync();
        string refreshToken;
        string rotatedToken;
        await using (var setupContext = database.CreateContext())
        {
            var issued = await CreateService(setupContext).StartAsync(
                userId,
                new ClientRequestMetadata("192.0.2.10", "Device-A"),
                CancellationToken.None);
            refreshToken = issued.Value!.RefreshToken;
        }

        await using (var rotateContext = database.CreateContext())
        {
            var rotated = await CreateService(rotateContext).RotateAsync(
                refreshToken,
                CancellationToken.None);
            Assert.True(rotated.IsSuccess);
            rotatedToken = rotated.Value!.RefreshToken;
        }

        await using (var replayContext = database.CreateContext())
        {
            var replay = await CreateService(replayContext, graceSeconds: 0).RotateAsync(
                refreshToken,
                CancellationToken.None);
            Assert.False(replay.IsSuccess);
            Assert.Equal(UserSessionFailure.TokenReused, replay.Failure);
        }

        // 会话仍在：最新 token 继续可用。
        await using (var latestContext = database.CreateContext())
        {
            var latest = await CreateService(latestContext).RotateAsync(
                rotatedToken,
                CancellationToken.None);
            Assert.True(latest.IsSuccess);
        }

        await using var assertionContext = database.CreateContext();
        var session = await assertionContext.Set<UserSession>()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(UserSessionStatuses.Active, session.Status);
        Assert.False(session.RiskFlag);
    }

    private static UserSessionService CreateService(
        AppDbContext context,
        int graceSeconds = 30)
    {
        var options = Options.Create(new JwtOptions
        {
            Key = AuthTestFactory.TestKey,
            Issuer = AuthTestFactory.TestIssuer,
            Audience = AuthTestFactory.TestAudience,
            ExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
            RefreshTokenReuseGraceSeconds = graceSeconds,
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
