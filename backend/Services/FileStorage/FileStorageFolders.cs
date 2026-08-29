namespace ShowtimeBackend.Services.FileStorage;

/// <summary>
/// 上传目录白名单：顶层目录即业务类型（对象键第一段），
/// 便于 OSS 生命周期规则与 RAM 访问控制（策略已限定 showtime/*）。
/// </summary>
public static class FileStorageFolders
{
    public const string Show = "show";
    public const string Marketing = "marketing";
    public const string Avatar = "avatar";
    public const string Tmp = "tmp";

    public static readonly IReadOnlyList<string> Allowed =
        [Show, Marketing, Avatar, Tmp];
}
