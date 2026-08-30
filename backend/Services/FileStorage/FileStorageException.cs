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

    public string ErrorCode { get; } = errorCode;
}
