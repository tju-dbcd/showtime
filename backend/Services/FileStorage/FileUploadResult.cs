namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 上传结果：对象键（ObjectKey）与可公开访问的 URL（PublicUrl）。
/// </summary>
public sealed record FileUploadResult(string ObjectKey, string PublicUrl);
