namespace ShowtimeBackend.Common;

/// <summary>
/// 字符串枚举（数据库存储值）与 C# 枚举之间的转换辅助。
/// 实体字段仍以字符串存储以贴合数据库 CHECK 约束，
/// DTO 边界处通过本类完成双向转换。
/// </summary>
public static class EnumCodec
{
    /// <summary>
    /// 将数据库字符串值转换为枚举；未知值抛出异常以尽早暴露脏数据。
    /// </summary>
    public static T ToEnum<T>(this string value)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"无法将空字符串转换为枚举 {typeof(T).Name}。");
        }

        if (Enum.TryParse<T>(value, ignoreCase: false, out var result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"值 \"{value}\" 不是合法的 {typeof(T).Name} 枚举值。");
    }

    /// <summary>
    /// 将可空字符串转换为可空枚举。
    /// </summary>
    public static T? ToEnumOrNull<T>(this string? value)
        where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : value.ToEnum<T>();

    /// <summary>
    /// 将枚举转换为数据库存储字符串（与成员名一致）。
    /// </summary>
    public static string ToDbString<T>(this T value)
        where T : struct, Enum => value.ToString();
}
