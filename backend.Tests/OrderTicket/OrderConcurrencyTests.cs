using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderConcurrencyTests
{
    [Fact]
    public async Task OrderStatus_RejectsASecondUpdateBasedOnStaleStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new SqliteAuthDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            setup.Add(CreateOrder());
            await setup.SaveChangesAsync();
        }

        await using var cancelContext = new SqliteAuthDbContext(options);
        await using var paymentContext = new SqliteAuthDbContext(options);
        var cancellingOrder = await cancelContext.Set<Order>().SingleAsync();
        var payingOrder = await paymentContext.Set<Order>().SingleAsync();

        payingOrder.OrderStatus = "PAID";
        await paymentContext.SaveChangesAsync();
        cancellingOrder.OrderStatus = "CANCELLED";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => cancelContext.SaveChangesAsync());
    }

    private static Order CreateOrder() => new()
    {
        OrderId = 1,
        OrderNo = "ORD000001",
        UserId = 7,
        SessionId = 10,
        TotalAmount = 188m,
        TicketCount = 1,
        OrderStatus = "PENDING_PAY",
        ExpireTime = DateTime.UtcNow.AddMinutes(15),
        Source = "WEB"
    };
}
