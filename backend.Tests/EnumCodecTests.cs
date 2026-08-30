using ShowtimeBackend.Common;

namespace ShowtimeBackend.Tests;

/// <summary>
/// 验证 EnumCodec 的字符串↔枚举转换边界行为。
/// 重点：Enum.TryParse 会接受数字字符串并产生未定义的枚举值，
/// 必须配合 Enum.IsDefined 拒绝，避免脏数据在序列化阶段抛 JsonException。
/// </summary>
public sealed class EnumCodecTests
{
    [Fact]
    public void ToEnum_NamedValue_ReturnsEnum()
    {
        Assert.Equal(SessionStatus.ONSALE, "ONSALE".ToEnum<SessionStatus>());
    }

    [Fact]
    public void ToEnum_EmptyOrNull_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => "".ToEnum<SessionStatus>());
        Assert.Throws<InvalidOperationException>(() => "  ".ToEnum<SessionStatus>());
        Assert.Throws<InvalidOperationException>(() => ((string?)null)!.ToEnum<SessionStatus>());
    }

    [Fact]
    public void ToEnum_UnknownName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => "UNKNOWN_STATUS".ToEnum<SessionStatus>());
    }

    [Fact]
    public void ToEnum_NumericString_Throws()
    {
        // Enum.TryParse("123") 会成功返回未定义值 (SessionStatus)123，
        // 若不加 Enum.IsDefined 校验，allowIntegerValues:false 序列化时会抛 JsonException
        Assert.Throws<InvalidOperationException>(() => "123".ToEnum<SessionStatus>());
    }

    [Fact]
    public void ToEnum_CaseSensitive()
    {
        // 枚举成员名与数据库 CHECK 约束取值一致（全大写），小写不匹配
        Assert.Throws<InvalidOperationException>(() => "onsale".ToEnum<SessionStatus>());
    }

    [Fact]
    public void ToEnumOrNull_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(((string?)null).ToEnumOrNull<SessionStatus>());
        Assert.Null("".ToEnumOrNull<SessionStatus>());
        Assert.Equal(SessionStatus.PRESALE, "PRESALE".ToEnumOrNull<SessionStatus>());
    }

    [Fact]
    public void ToDbString_ReturnsMemberName()
    {
        Assert.Equal("PENDING_PAY", OrderStatus.PENDING_PAY.ToDbString());
    }
}
