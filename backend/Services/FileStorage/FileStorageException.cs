namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 文件上传/存储业务错误，携带面向客户端的错误码
/// （控制器据此映射为 ApiResponse&lt;T&gt;.Fail(code, message) 与 HTTP 状态码）。
/// </summary>
public sealed class FileStorageException(string errorCode, string message)
    : Exception(message)
{
    /// <summary>OSS 上传失败（服务端与 OSS 通信故障，控制器映射 500）。</summary>
    public const string ErrorUploadFailed = "UPLOAD_FAILED";

    /// <summary>OSS 删除失败（服务端与 OSS 通信故障，控制器映射 500）。</summary>
    public const string ErrorDeleteFailed = "DELETE_FAILED";

    /// <summary>未配置任何可用存储（OSS 与本地磁盘存储均关闭，控制器映射 503）。</summary>
    public const string ErrorStorageNotConfigured = "FILE_STORAGE_NOT_CONFIGURED";

    public string ErrorCode { get; } = errorCode;
}
