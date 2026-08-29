namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 文件存储服务抽象（上行/下行）。
/// 默认实现为 OssFileStorageService（阿里云 OSS）；
/// 单测与未启用 OSS（Oss:Enabled=false）的环境使用 FakeFileStorageService（内存实现）。
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// 上传文件流，服务端生成对象键（不可预测、无路径穿越），返回 ObjectKey + PublicUrl。
    /// </summary>
    /// <param name="content">文件内容流。</param>
    /// <param name="fileName">原始文件名，仅用于推断扩展名，不作为落盘名称。</param>
    /// <param name="contentType">文件 Content-Type。</param>
    /// <param name="folder">业务目录，取值白名单（show/marketing/avatar/tmp），默认 tmp。</param>
    Task<FileUploadResult> UploadFileAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>封装 multipart 表单文件的上传解析与校验。</summary>
    Task<FileUploadResult> UploadFromMultipartAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>按对象键删除对象（对象不存在时静默成功）。</summary>
    Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
