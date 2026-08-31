using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.LocalStorage;

namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 本地磁盘文件存储（开发/联调中间态）：数据落盘到共享目录，
/// 多实例挂载同一卷即可互通，比内存 fake 更贴近生产，又不依赖云 OSS。
/// 与 OssFileStorageService 共用同一套安全校验（目录/大小/扩展名/Content-Type），
/// 对象键服务端生成 GUID，PublicUrl 由静态文件中间件按 BaseUrl 托管（默认 /files）。
/// </summary>
public sealed class LocalDiskFileStorageService(
    IOptions<LocalStorageOptions> options,
    IWebHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<LocalDiskFileStorageService> logger) : IFileStorageService
{
    private readonly LocalStorageOptions _options = options.Value;
    private readonly string _rootDirectory = LocalStoragePaths.ResolveRootDirectory(
        options.Value.RootDirectory, environment.ContentRootPath);

    public async Task<FileUploadResult> UploadFileAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
        {
            throw new InvalidOperationException(
                "The upload stream must be seekable so its length can be checked.");
        }

        FileUploadValidator.EnsureValid(
            folder,
            fileName,
            contentType,
            content.Length,
            _options.MaxFileSizeBytes,
            _options.AllowedExtensions);

        var objectKey = OssObjectKeyGenerator.Generate(
            folder,
            Path.GetExtension(fileName),
            timeProvider.GetUtcNow().UtcDateTime);
        var fullPath = BuildFullPath(objectKey);

        try
        {
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            content.Position = 0;
            await using var fileStream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await content.CopyToAsync(fileStream, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Local disk upload failed. ObjectKey={ObjectKey} Root={Root}",
                objectKey,
                _rootDirectory);
            throw new FileStorageException(
                FileStorageException.ErrorUploadFailed,
                "The file upload failed. Please try again later.");
        }

        return new FileUploadResult(
            objectKey,
            $"{_options.BaseUrl.TrimEnd('/')}/{objectKey}");
    }

    public async Task<FileUploadResult> UploadFromMultipartAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var stream = file.OpenReadStream();
        // 必须 await 后再离开 using 作用域：上传为异步过程，直接返回未 await 的
        // task 会在方法返回时立即释放流，导致上传期间读取已被 Dispose 的流。
        return await UploadFileAsync(
            stream,
            file.FileName,
            file.ContentType,
            folder,
            cancellationToken);
    }

    public Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Task.CompletedTask;
        }

        try
        {
            var fullPath = BuildFullPath(objectKey);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Local disk delete failed. ObjectKey={ObjectKey} Root={Root}",
                objectKey,
                _rootDirectory);
            throw new FileStorageException(
                FileStorageException.ErrorDeleteFailed,
                "The file deletion failed. Please try again later.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 对象键 → 落盘绝对路径。对象键由服务端生成（showtime/{folder}/{yyyy}/{MM}/{guid}.{ext}），
    /// 这里仍做一次防御性规整：拒绝绝对路径与 .. 逃逸，防路径穿越。
    /// </summary>
    private string BuildFullPath(string objectKey)
    {
        var normalized = objectKey.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(".."))
        {
            throw new FileStorageException(
                FileUploadValidator.ErrorInvalidFolder,
                $"Object key '{objectKey}' is not a valid storage path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, normalized));
        var rootWithSeparator = _rootDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new FileStorageException(
                FileUploadValidator.ErrorInvalidFolder,
                $"Object key '{objectKey}' escapes the storage root.");
        }

        return fullPath;
    }
}

/// <summary>本地存储根目录解析：相对路径基于内容根目录展开，绝对路径原样使用。</summary>
public static class LocalStoragePaths
{
    public static string ResolveRootDirectory(
        string? configuredRoot,
        string contentRootPath)
    {
        var root = configuredRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "LocalStorage:RootDirectory is not set.");
        }

        var fullRoot = Path.IsPathRooted(root)
            ? root
            : Path.Combine(contentRootPath, root);
        return Path.GetFullPath(fullRoot);
    }
}