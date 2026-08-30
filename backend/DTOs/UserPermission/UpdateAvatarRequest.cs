namespace ShowtimeBackend.DTOs.UserPermission;

/// <summary>更新当前用户头像请求：avatarUrl 须为 http/https 绝对 URL，且不超过 500 字符。</summary>
public sealed class UpdateAvatarRequest
{
    public string? AvatarUrl { get; set; }
}
