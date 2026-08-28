namespace ShowtimeBackend.Common.Oss;

/// <summary>
/// 阿里云 OSS 配置（绑定 "Oss" 配置节）。
/// 敏感项 AccessKeyId / AccessKeySecret 不写进 appsettings.json，
/// 通过环境变量 <c>Oss__AccessKeyId</c> / <c>Oss__AccessKeySecret</c>（或
/// KMS/Docker secret / dotnet user-secrets）注入，禁止提交仓库。
/// </summary>
public sealed class OssOptions
{
    public const string SectionName = "Oss";

    /// <summary>
    /// kill-switch：false 时上传接口返回"未配置"错误，
    /// 便于无 OSS 环境下先继续开发其他功能（默认 true）。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>OSS Endpoint，必须与 Bucket 同 Region，如 https://oss-cn-hangzhou.aliyuncs.com。</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>RAM 子账号 AccessKeyId（最小权限，仅环境变量/secret 注入）。</summary>
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>RAM 子账号 AccessKeySecret（最小权限，仅环境变量/secret 注入）。</summary>
    public string AccessKeySecret { get; init; } = string.Empty;

    /// <summary>Bucket 名称，如 showtime-assets（全局唯一）。</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>公开读访问基地址 = Bucket 的外网访问域名，如 https://showtime-assets.oss-cn-hangzhou.aliyuncs.com。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>单文件大小上限（字节），默认 5MB。</summary>
    public long MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>允许上传的扩展名白名单（小写，含点）。</summary>
    public IReadOnlyList<string> AllowedExtensions { get; init; } =
        [".jpg", ".jpeg", ".png", ".webp", ".gif"];
}
