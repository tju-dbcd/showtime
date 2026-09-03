using System.Collections.Generic;
using ShowtimeBackend.Common.Oss;

namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 上传文件的服务端校验（纯函数，单测不依赖真实 OSS/磁盘）。
/// 安全要点：目录白名单、大小上限、扩展名白名单、Content-Type 二次校验；
/// 原文件名仅用于推断扩展名，落盘对象键一律服务端生成 GUID（杜绝路径穿越/重名覆盖）。
/// </summary>
internal static class FileUploadValidator
{
    public const string ErrorFileRequired = "FILE_REQUIRED";
    public const string ErrorInvalidFolder = "INVALID_FOLDER";
    public const string ErrorFileTooLarge = "FILE_TOO_LARGE";
    public const string ErrorUnsupportedFileType = "UNSUPPORTED_FILE_TYPE";

    /// <summary>允许的 Content-Type 主类型（与默认图片扩展名白名单对应，防脚本类伪装）。</summary>
    private const string AllowedContentTypePrefix = "image/";

    /// <summary>OssOptions 便捷重载：解包大小上限与扩展名白名单后委托主实现（供校验单测使用）。</summary>
    public static void EnsureValid(
        string folder,
        string fileName,
        string? contentType,
        long contentLength,
        OssOptions options)
        => EnsureValid(
            folder,
            fileName,
            contentType,
            contentLength,
            options.MaxFileSizeBytes,
            options.AllowedExtensions);

    public static void EnsureValid(
        string folder,
        string fileName,
        string? contentType,
        long contentLength,
        long maxFileSizeBytes,
        IReadOnlyList<string> allowedExtensions)
    {
        if (!FileStorageFolders.Allowed.Contains(folder))
        {
            throw new FileStorageException(
                ErrorInvalidFolder,
                $"Folder '{folder}' is not allowed. Allowed folders: "
                + string.Join(", ", FileStorageFolders.Allowed) + ".");
        }

        if (contentLength > maxFileSizeBytes)
        {
            throw new FileStorageException(
                ErrorFileTooLarge,
                $"File size ({contentLength} bytes) exceeds the limit of "
                + $"{maxFileSizeBytes} bytes.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !allowedExtensions.Contains(
                extension.ToLowerInvariant()))
        {
            throw new FileStorageException(
                ErrorUnsupportedFileType,
                $"File extension '{extension}' is not allowed. Allowed extensions: "
                + string.Join(", ", allowedExtensions) + ".");
        }

        // Content-Type 二次校验：显式声明的类型必须为 image/*。
        // 内容真实类型依赖浏览器端 beforeUpload 与 OSS 侧的格式嗅探兜底，
        // 这里防的是"改扩展名为图片、类型声明为脚本"的伪装上传。
        if (!string.IsNullOrWhiteSpace(contentType)
            && !contentType.StartsWith(
                AllowedContentTypePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileStorageException(
                ErrorUnsupportedFileType,
                $"Content type '{contentType}' is not allowed.");
        }
    }
}
