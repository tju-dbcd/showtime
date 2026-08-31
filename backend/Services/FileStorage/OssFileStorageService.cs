using Aliyun.OSS;
using Aliyun.OSS.Common;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.Oss;

namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 基于阿里云 OSS 的文件存储实现（后端代理上传：AccessKey 只存在后端）。
/// 上传前执行服务端校验（大小/扩展名白名单/Content-Type 二次校验/目录白名单），
/// 对象键由服务端生成（GUID + 时间分层），返回可公开访问的 URL（Bucket 公共读，无需签名）。
/// </summary>
public sealed class OssFileStorageService(
    IOptions<OssOptions> options,
    TimeProvider timeProvider,
    ILogger<OssFileStorageService> logger) : IFileStorageService
{
    private readonly OssOptions _options = options.Value;
    private readonly OssClient _client = new OssClient(
        options.Value.Endpoint,
        options.Value.AccessKeyId,
        options.Value.AccessKeySecret);

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

        try
        {
            content.Position = 0;
            // SDK 为同步方法 + Begin/End APM 模式，用 FromAsync 包装避免阻塞线程
            await Task.Factory.FromAsync(
                (callback, state) => _client.BeginPutObject(
                    _options.Bucket, objectKey, content, callback, state),
                _client.EndPutObject,
                state: null);
        }
        catch (OssException ex)
        {
            logger.LogError(
                ex,
                "OSS PutObject failed. Bucket={Bucket} ObjectKey={ObjectKey} ErrorCode={OssErrorCode}",
                _options.Bucket,
                objectKey,
                ex.ErrorCode);
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

    public async Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return;
        }

        try
        {
            // SDK 删除仅提供同步版本，且为低频清理路径，直接同步调用即可
            _client.DeleteObject(_options.Bucket, objectKey);
        }
        catch (OssException ex)
        {
            logger.LogError(
                ex,
                "OSS DeleteObject failed. Bucket={Bucket} ObjectKey={ObjectKey} ErrorCode={OssErrorCode}",
                _options.Bucket,
                objectKey,
                ex.ErrorCode);
            throw new FileStorageException(
                FileStorageException.ErrorDeleteFailed,
                "The file deletion failed. Please try again later.");
        }
    }
}
