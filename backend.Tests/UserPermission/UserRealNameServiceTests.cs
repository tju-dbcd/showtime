using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Common.IdentityData;
using ShowtimeBackend.Data;
using ShowtimeBackend.Data.Interceptors;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class UserRealNameServiceTests
{
    [Fact]
    public async Task Create_FirstRecordIsVerifiedDefaultAndEncrypted()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var service = CreateService(db, protector);

        var result = await service.CreateAsync(
            7,
            "alice",
            CreateRequest("31010119900101123x"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsVerified);
        Assert.True(result.Value.IsDefault);
        Assert.Equal("310***********123X", result.Value.MaskedIdCardNo);
        var stored = await db.Set<UserRealName>().SingleAsync();
        Assert.True(protector.IsProtected(stored.IdCardNo));
        Assert.DoesNotContain("31010119900101123X", stored.IdCardNo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RejectsDuplicateForSameUser_ButAllowsAnotherUser()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var service = CreateService(db, protector);
        var request = CreateRequest("31010119900101123X");
        Assert.True((await service.CreateAsync(7, "alice", request, CancellationToken.None)).IsSuccess);

        var duplicate = await service.CreateAsync(7, "alice", request, CancellationToken.None);
        var anotherUser = await service.CreateAsync(8, "bob", request, CancellationToken.None);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("REAL_NAME_DUPLICATE_ID_CARD", duplicate.ErrorCode);
        Assert.True(anotherUser.IsSuccess);
    }

    [Fact]
    public async Task Update_VerifiedIdentityFieldsAreImmutable()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var service = CreateService(db, protector);
        var created = await service.CreateAsync(
            7,
            "alice",
            CreateRequest("31010119900101123X"),
            CancellationToken.None);

        var changed = await service.UpdateAsync(
            7,
            "alice",
            created.Value!.RealNameId,
            new UpdateUserRealNameRequest
            {
                RealName = "Alice Changed",
                IdCardNo = "31010119900101123X",
            },
            CancellationToken.None);
        var idempotent = await service.UpdateAsync(
            7,
            "alice",
            created.Value.RealNameId,
            new UpdateUserRealNameRequest
            {
                RealName = "Alice",
                IdCardNo = "31010119900101123X",
            },
            CancellationToken.None);

        Assert.False(changed.IsSuccess);
        Assert.Equal("REAL_NAME_VERIFIED_IMMUTABLE", changed.ErrorCode);
        Assert.True(idempotent.IsSuccess);
    }

    [Fact]
    public async Task SetDefaultAndDeleteDefault_SelectsDeterministicReplacement()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var service = CreateService(db, protector);
        var first = await service.CreateAsync(
            7,
            "alice",
            CreateRequest("31010119900101123X"),
            CancellationToken.None);
        var second = await service.CreateAsync(
            7,
            "alice",
            CreateRequest("110101198801011234"),
            CancellationToken.None);

        var switched = await service.SetDefaultAsync(
            7,
            "alice",
            second.Value!.RealNameId,
            CancellationToken.None);
        var deleted = await service.DeleteAsync(
            7,
            "alice",
            second.Value.RealNameId,
            CancellationToken.None);
        var listed = await service.ListAsync(7, CancellationToken.None);

        Assert.True(switched.IsSuccess);
        Assert.True(deleted.IsSuccess);
        var remaining = Assert.Single(listed.Value!);
        Assert.Equal(first.Value!.RealNameId, remaining.RealNameId);
        Assert.True(remaining.IsDefault);
    }

    [Fact]
    public async Task Delete_RejectsRecordReferencedByOrderItem()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var service = CreateService(db, protector);
        var created = await service.CreateAsync(
            7,
            "alice",
            CreateRequest("31010119900101123X"),
            CancellationToken.None);
        db.Add(new OrderItem
        {
            OrderItemId = 50,
            OrderId = 40,
            SeatId = 30,
            PriceStrategyId = 20,
            RealNameId = created.Value!.RealNameId,
            UnitPrice = 188,
            ItemStatus = "NORMAL",
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(
            7,
            "alice",
            created.Value.RealNameId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("REAL_NAME_IN_USE", result.ErrorCode);
    }

    private static CreateUserRealNameRequest CreateRequest(string idCardNo) => new()
    {
        RealName = "Alice",
        IdCardNo = idCardNo,
    };

    private static UserRealNameService CreateService(
        AppDbContext db,
        IIdentityDataProtector protector) => new(
        db,
        protector,
        NullLogger<UserRealNameService>.Instance);

    private static AppDbContext CreateDbContext(IIdentityDataProtector protector)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UserRealNameEncryptionInterceptor(protector))
            .Options;
        return new AppDbContext(options);
    }
}
