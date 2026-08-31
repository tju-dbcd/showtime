namespace ShowtimeBackend.Common.LocalStorage;

/// <summary>
/// 本地磁盘文件存储配置（绑定 "LocalStorage" 配置节）。
/// 作为开发/联调中间态：数据落盘、多实例共享挂载同一卷即可互通，
/// 不依赖云 OSS；生产环境仍推荐 OSS（见 OssOptions）。
/// </summary>
public sealed class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    /// <summary>
    /// kill-switch：false 时忽略本实现；若 OSS 也未启用，上传接口返回 503 未配置错误。
    /// 本地开发（Development 环境）默认 true，替代内存 fake 提供落盘体验。
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>文件根目录：绝对路径，或相对后端内容根目录（ContentRootPath）的路径。</summary>
    public string RootDirectory { get; init; } = string.Empty;

    /// <summary>公开访问基地址：默认相对路径 /files（由后端静态文件中间件托管）。</summary>
    public string BaseUrl { get; init; } = "/files";

    /// <summary>单文件大小上限（字节），默认 5MB，与 OssOptions 保持一致。</summary>
    public long MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>允许上传的扩展名白名单（小写，含点），与 OssOptions 保持一致。</summary>
    public IReadOnlyList<string> AllowedExtensions { get; init; } =
        [".jpg", ".jpeg", ".png", ".webp", ".gif"];
}