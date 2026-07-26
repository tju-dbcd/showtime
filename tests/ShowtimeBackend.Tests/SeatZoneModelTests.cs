using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;
using Xunit;

namespace ShowtimeBackend.Tests;

public class SeatZoneModelTests
{
    [Theory]
    [InlineData("ShowtimeBackend.Entities.SeatZone.SeatMap")]
    [InlineData("ShowtimeBackend.Entities.SeatZone.SeatSection")]
    [InlineData("ShowtimeBackend.Entities.SeatZone.Seat")]
    [InlineData("ShowtimeBackend.Entities.SeatZone.SeatRule")]
    [InlineData("ShowtimeBackend.Entities.SeatZone.SeatRuleScope")]
    public void Static_seat_zone_entity_is_registered_in_the_model(string entityTypeName)
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(entityTypeName);

        Assert.NotNull(entity);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseOracle("User Id=test;Password=test;Data Source=localhost:1521/XEPDB1")
            .Options;

        return new AppDbContext(options);
    }

    [Theory]
    [InlineData(typeof(SeatMap), "SEAT_MAP")]
    [InlineData(typeof(SeatSection), "SEAT_SECTION")]
    [InlineData(typeof(Seat), "SEAT")]
    [InlineData(typeof(SeatRule), "SEAT_RULE")]
    [InlineData(typeof(SeatRuleScope), "SEAT_RULE_SCOPE")]
    public void Static_seat_zone_entities_map_to_the_expected_tables(Type clrType, string tableName)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(clrType)!;

        Assert.Equal("APP_OWNER", entity.GetSchema());
        Assert.Equal(tableName, entity.GetTableName());
    }

    [Fact]
    public void Required_static_relationships_use_the_database_foreign_key_contract()
    {
        using var context = CreateContext();
        var section = context.Model.FindEntityType(typeof(SeatSection))!;
        var seat = context.Model.FindEntityType(typeof(Seat))!;
        var scope = context.Model.FindEntityType(typeof(SeatRuleScope))!;

        AssertForeignKey(section, nameof(SeatSection.SeatMapId), "FK_SEAT_SECTION_MAP");
        AssertForeignKey(seat, nameof(Seat.SeatSectionId), "FK_SEAT_SECTION");
        AssertForeignKey(scope, nameof(SeatRuleScope.SeatRuleId), "FK_SEAT_RULE_SCOPE_RULE");
        AssertForeignKey(scope, nameof(SeatRuleScope.SeatMapId), "FK_SEAT_RULE_SCOPE_MAP");
        AssertForeignKey(scope, nameof(SeatRuleScope.SeatSectionId), "FK_SEAT_RULE_SCOPE_SECTION");
    }

    [Fact]
    public void Static_seat_zone_unique_indexes_match_the_database_contract()
    {
        using var context = CreateContext();

        AssertUniqueIndex<SeatMap>(context, "UK_SEAT_MAP_VENUE_CODE");
        AssertUniqueIndex<SeatSection>(context, "UK_SEAT_SECTION_MAP_CODE");
        AssertUniqueIndex<Seat>(context, "UK_SEAT_ROW_NO");
        AssertUniqueIndex<Seat>(context, "UK_SEAT_POSITION");
        AssertUniqueIndex<SeatRule>(context, "UK_SEAT_RULE_CODE");
    }

    private static void AssertForeignKey(IEntityType entity, string propertyName, string constraintName)
    {
        var foreignKey = entity.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Single().Name == propertyName);

        Assert.Equal(constraintName, foreignKey.GetConstraintName());
        Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
    }

    private static void AssertUniqueIndex<TEntity>(AppDbContext context, string indexName)
        where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == indexName && index.IsUnique);
    }
}
