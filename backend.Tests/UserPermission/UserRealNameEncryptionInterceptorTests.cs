using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common.IdentityData;
using ShowtimeBackend.Data;
using ShowtimeBackend.Data.Interceptors;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class UserRealNameEncryptionInterceptorTests
{
    [Fact]
    public async Task SaveChanges_EncryptsAddedAndModifiedValuesWithoutDoubleEncryption()
    {
        using var protector = IdentityDataProtectorTests.CreateProtector();
        await using var db = CreateDbContext(protector);
        var record = new UserRealName
        {
            UserId = 7,
            RealName = "Alice",
            IdCardNo = "31010119900101123X",
            IsVerified = true,
        };

        db.Add(record);
        await db.SaveChangesAsync();
        var firstCiphertext = record.IdCardNo;

        Assert.True(protector.IsProtected(firstCiphertext));
        Assert.Equal("31010119900101123X", protector.Unprotect(firstCiphertext).Value);

        record.IsDefault = true;
        await db.SaveChangesAsync();
        Assert.Equal(firstCiphertext, record.IdCardNo);

        record.IdCardNo = "110101198801011234";
        await db.SaveChangesAsync();
        Assert.True(protector.IsProtected(record.IdCardNo));
        Assert.NotEqual(firstCiphertext, record.IdCardNo);
        Assert.Equal("110101198801011234", protector.Unprotect(record.IdCardNo).Value);
    }

    private static AppDbContext CreateDbContext(IIdentityDataProtector protector)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UserRealNameEncryptionInterceptor(protector))
            .Options;
        return new AppDbContext(options);
    }
}
