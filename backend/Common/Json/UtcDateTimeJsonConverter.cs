using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowtimeBackend.Common.Json;

/// <summary>
/// 后端时间统一规范：所有 DateTime 一律按 UTC 序列化（输出带 Z），
/// 前端用 new Date(...).toLocaleString 即可正确换算到本地时区。
/// 反序列化时若输入不带偏移，也按 UTC 处理（业务入参由前端 .toISOString() 产生，本身即 UTC）。
/// </summary>
public sealed class UtcDateTimeJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(DateTime) || typeToConvert == typeof(DateTime?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(DateTime)
            ? new UtcDateTimeJsonConverter()
            : new UtcNullableDateTimeJsonConverter();
}

public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        return UtcDateTimeFormatting.NormalizeToUtc(
            DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(UtcDateTimeFormatting.FormatUtc(value));
    }
}

public sealed class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return UtcDateTimeFormatting.NormalizeToUtc(
            DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTimeFormatting.FormatUtc(value.Value));
    }
}

internal static class UtcDateTimeFormatting
{
    public static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Oracle TIMESTAMP 不带时区，回读为 Unspecified；规范约定库中一律存 UTC
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static string FormatUtc(DateTime value) =>
        NormalizeToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
