using System.Collections.Concurrent;

namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 内存版文件存储（测试 double / 无 OSS 环境开发占位）。
/// 不做真实存储与安全校验（校验逻辑在 M2 的 OssFileStorageService 中），
/// 仅按既定对象键格式（showtime/{folder}/{yyyy}/{MM}/{guid}.{ext}）生成键并留存内容。
/// PublicUrl 直接返回对象键本身；真实实现为 BaseUrl + "/" + objectKey。
/// </summary>
public sealed class FakeFileStorageService : IFileStorageService
{
    /// <summary>对象键 → 文件内容。internal 供测试程序集（InternalsVisibleTo）断言。</summary>
    internal ConcurrentDictionary<string, byte[]> Store { get; } = new();

    public async Task<FileUploadResult> UploadFileAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var objectKey = BuildObjectKey(folder, Path.GetExtension(fileName));
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        Store[objectKey] = memory.ToArray();
        return new FileUploadResult(objectKey, objectKey);
    }

    public async Task<FileUploadResult> UploadFromMultipartAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default)
    {
        using var stream = file.OpenReadStream();
        // 必须 await 后再离开 using 作用域：上传为异步过程，直接返回未 await 的
        // task 会在方法返回时立即释放流，导致上传期间读取已被 Dispose 的流。
        return await UploadFileAsync(
            stream, file.FileName, file.ContentType, folder, cancellationToken);
    }

    public Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        Store.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 对象键统一前缀 showtime/（与 Redis key 前缀风格一致），
    /// 顶层目录即业务类型（便于生命周期规则与 RAM 访问控制），时间分层 + GUID 防猜测。
    /// </summary>
    private static string BuildObjectKey(string folder, string extension)
    {
        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "tmp" : folder;
        var safeExtension = string.IsNullOrWhiteSpace(extension)
            ? ".bin"
            : extension.ToLowerInvariant();
        var now = DateTime.UtcNow;
        return $"showtime/{safeFolder}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{safeExtension}";
    }
}
