namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 文件上传/存储业务错误，携带面向客户端的错误码
/// （控制器据此映射为 ApiResponse&lt;T&gt;.Fail(code, message) 与 HTTP 状态码）。
/// </summary>
public sealed class FileStorageException(string errorCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
