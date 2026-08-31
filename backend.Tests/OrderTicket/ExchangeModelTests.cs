using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeModelTests
{
    [Fact]
    public void ETicketStatus_ContainsExchangingAndRoundTripsAsString()
    {
        Assert.Contains(ETicketStatus.EXCHANGING, Enum.GetValues<ETicketStatus>());
        Assert.Equal("EXCHANGING", ETicketStatus.EXCHANGING.ToString());

        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        Assert.Equal(
            "\"EXCHANGING\"",
            JsonSerializer.Serialize(ETicketStatus.EXCHANGING, options));
        Assert.Equal(
            ETicketStatus.EXCHANGING,
            JsonSerializer.Deserialize<ETicketStatus>("\"EXCHANGING\"", options));
    }

    [Fact]
    public void ETicketConfiguration_CheckConstraintContainsSixStatuses()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ETicket))!;
        var check = entityType.GetCheckConstraints()
            .Single(constraint => constraint.Name == "CHK_ETICKET_STATUS");

        Assert.Equal(
            "TICKET_STATUS IN ('UNUSED', 'REFUNDING', 'USED', 'REFUNDED', 'EXCHANGING', 'EXCHANGED')",
            check.Sql);
    }

    [Fact]
    public void ExchangeRequest_MapsAppliedPolicyAndConcurrencyTokens()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ExchangeRequest))!;

        var appliedPolicy = entityType.FindProperty(
            nameof(ExchangeRequest.AppliedPolicyId))!;
        Assert.True(appliedPolicy.IsNullable);
        Assert.Equal("APPLIED_POLICY_ID", appliedPolicy.GetColumnName());
        Assert.Equal(
            "NUMBER(19)",
            appliedPolicy.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);

        Assert.True(entityType.FindProperty(nameof(ExchangeRequest.ApproveStatus))!
            .IsConcurrencyToken);
        Assert.True(entityType.FindProperty(nameof(ExchangeRequest.ExchangeStatus))!
            .IsConcurrencyToken);

        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey => foreignKey.GetConstraintName() == "FK_EXCHANGE_APPLIED_POLICY");
    }

    [Fact]
    public void ExchangeRequest_AppliedPolicyIndexIsNonUnique()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ExchangeRequest))!;

        var index = entityType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IDX_EXCHANGE_APPLIED_POLICY");
        Assert.False(index.IsUnique);
        var property = Assert.Single(index.Properties);
        Assert.Equal(nameof(ExchangeRequest.AppliedPolicyId), property.Name);
    }

    [Fact]
    public void ExchangeRequest_StateComboCheckExpressesFiveCombos()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ExchangeRequest))!;
        var check = entityType.GetCheckConstraints()
            .Single(constraint => constraint.Name == "CHK_EXCHANGE_STATE_COMBO");

        Assert.Equal(
            "(APPROVE_STATUS = 'PENDING' AND EXCHANGE_STATUS = 'PENDING') OR " +
            "(APPROVE_STATUS = 'APPROVED' AND EXCHANGE_STATUS IN ('PROCESSING', 'COMPLETED', 'FAILED')) OR " +
            "(APPROVE_STATUS = 'REJECTED' AND EXCHANGE_STATUS = 'FAILED')",
            check.Sql);
    }

    [Fact]
    public void OrderItem_ExposesOriginalExchangeItemsAsCollection()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(OrderItem))!;

        var navigation = entityType.FindNavigation(
            nameof(OrderItem.OriginalExchangeItems));
        Assert.NotNull(navigation);
        Assert.True(navigation!.IsCollection);
        Assert.Null(entityType.FindNavigation("OriginalExchangeItem"));
    }

    [Fact]
    public void ExchangeItem_OrderItemIdIndexIsNonUniqueWithLegacyName()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ExchangeItem))!;

        var orderItemIndex = entityType.GetIndexes()
            .Single(candidate => candidate.Properties.Any(
                property => property.Name == nameof(ExchangeItem.OrderItemId)));
        Assert.Equal("IDX_EXCHANGE_ITEM_ORDER", orderItemIndex.GetDatabaseName());
        Assert.False(orderItemIndex.IsUnique);

        var newItemIndex = entityType.GetIndexes()
            .Single(candidate => candidate.Properties.Any(
                property => property.Name == nameof(ExchangeItem.NewOrderItemId)));
        Assert.Equal("IDX_EXCHANGE_ITEM_NEW_ITEM", newItemIndex.GetDatabaseName());
        Assert.False(newItemIndex.IsUnique);
    }

    [Fact]
    public void ExchangePolicy_ByteFlagsMapToNumber3()
    {
        using var db = CreateDbContext();
        var entityType = GetDesignTimeModel(db).FindEntityType(typeof(ExchangePolicy))!;

        var allowCrossSession = entityType.FindProperty(
            nameof(ExchangePolicy.AllowCrossSession))!;
        Assert.Equal(typeof(byte), allowCrossSession.ClrType);
        Assert.Equal(
            "NUMBER(3)",
            allowCrossSession.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);

        var status = entityType.FindProperty(nameof(ExchangePolicy.Status))!;
        Assert.Equal(typeof(byte), status.ClrType);
        Assert.Equal(
            "NUMBER(3)",
            status.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static IModel GetDesignTimeModel(AppDbContext db) =>
        db.GetService<IDesignTimeModel>().Model;
}
