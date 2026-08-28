namespace ShowtimeBackend.DTOs.Files;

/// <summary>上传成功响应：可公开访问的 URL（业务表直接存该值）与对象键（供删除/清理使用）。</summary>
public sealed record FileUploadResponse(string Url, string ObjectKey);
