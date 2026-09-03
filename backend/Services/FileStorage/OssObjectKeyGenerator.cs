namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 对象键生成：showtime/{folder}/{yyyy}/{MM}/{guid}.{ext}。
/// GUID 服务端生成不可预测；顶层目录为业务类型，时间分层便于生命周期规则清理。
/// </summary>
internal static class OssObjectKeyGenerator
{
    public static string Generate(string folder, string extension, DateTime utcNow)
    {
        var safeExtension = string.IsNullOrWhiteSpace(extension)
            ? ".bin"
            : extension.ToLowerInvariant();
        return $"showtime/{folder}/{utcNow:yyyy}/{utcNow:MM}/{Guid.NewGuid():N}{safeExtension}";
    }
}
