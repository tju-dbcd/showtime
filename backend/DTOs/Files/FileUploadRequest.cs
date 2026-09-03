namespace ShowtimeBackend.DTOs.Files;

/// <summary>
/// 上传接口 multipart 表单模型。
/// file 必填；folder 可选（白名单见 FileStorageFolders，默认 tmp）；
/// contentType 可选，缺省用 multipart 文件自带的 Content-Type。
/// </summary>
public sealed class FileUploadRequest
{
    public IFormFile? File { get; set; }

    public string? Folder { get; set; }

    public string? ContentType { get; set; }
}
