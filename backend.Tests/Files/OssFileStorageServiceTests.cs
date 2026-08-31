using System.Text.RegularExpressions;
using ShowtimeBackend.Common.Oss;
using ShowtimeBackend.Services.FileStorage;

namespace ShowtimeBackend.Tests;

/// <summary>
/// 上传校验（大小/扩展名白名单/Content-Type 二次校验/目录白名单）与
/// 对象键生成逻辑的单测。均为纯函数（internal，经 InternalsVisibleTo 访问），
/// 不依赖真实 OSS，符合"单测不依赖外部服务"的项目约定。
/// </summary>
public sealed class OssFileStorageServiceTests
{
    private static OssOptions TestOptions(long maxSizeBytes = 1024) => new()
    {
        Enabled = true,
        Endpoint = "https://oss-cn-hangzhou.aliyuncs.com",
        AccessKeyId = "test-key",
        AccessKeySecret = "test-secret",
        Bucket = "showtime-assets",
        BaseUrl = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com",
        MaxFileSizeBytes = maxSizeBytes,
        AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"],
    };

    // ---------- FileUploadValidator ----------

    [Fact]
    public void Validate_ValidImage_Passes()
    {
        FileUploadValidator.EnsureValid(
            "show", "poster.png", "image/png", 100, TestOptions());
    }

    [Fact]
    public void Validate_ExtensionIsCaseInsensitive_Passes()
    {
        FileUploadValidator.EnsureValid(
            "show", "POSTER.JPG", "image/jpeg", 100, TestOptions());
    }

    [Fact]
    public void Validate_EmptyContentType_ReliesOnExtensionOnly()
    {
        FileUploadValidator.EnsureValid(
            "show", "poster.png", "", 100, TestOptions());
    }

    [Fact]
    public void Validate_UnknownFolder_ThrowsInvalidFolder()
    {
        var ex = Assert.Throws<FileStorageException>(() =>
            FileUploadValidator.EnsureValid(
                "website", "poster.png", "image/png", 100, TestOptions()));

        Assert.Equal("INVALID_FOLDER", ex.ErrorCode);
    }

    [Theory]
    [InlineData("tmp")]
    [InlineData("marketing")]
    [InlineData("avatar")]
    public void Validate_AllWhitelistedFolders_Pass(string folder)
    {
        FileUploadValidator.EnsureValid(
            folder, "poster.png", "image/png", 100, TestOptions());
    }

    [Fact]
    public void Validate_OverMaxFileSize_ThrowsFileTooLarge()
    {
        var options = TestOptions(maxSizeBytes: 100);

        var ex = Assert.Throws<FileStorageException>(() =>
            FileUploadValidator.EnsureValid(
                "show", "poster.png", "image/png", 101, options));

        Assert.Equal("FILE_TOO_LARGE", ex.ErrorCode);
    }

    [Fact]
    public void Validate_ExactlyMaxFileSize_Passes()
    {
        var options = TestOptions(maxSizeBytes: 100);

        FileUploadValidator.EnsureValid(
            "show", "poster.png", "image/png", 100, options);
    }

    [Fact]
    public void Validate_DisallowedExtension_ThrowsUnsupportedFileType()
    {
        var ex = Assert.Throws<FileStorageException>(() =>
            FileUploadValidator.EnsureValid(
                "show", "evil.html", "text/html", 100, TestOptions()));

        Assert.Equal("UNSUPPORTED_FILE_TYPE", ex.ErrorCode);
    }

    [Fact]
    public void Validate_MissingExtension_ThrowsUnsupportedFileType()
    {
        var ex = Assert.Throws<FileStorageException>(() =>
            FileUploadValidator.EnsureValid(
                "show", "poster", "image/png", 100, TestOptions()));

        Assert.Equal("UNSUPPORTED_FILE_TYPE", ex.ErrorCode);
    }

    [Fact]
    public void Validate_ScriptLikeContentType_ThrowsUnsupportedFileType()
    {
        // 扩展名伪装成图片、类型声明为脚本：Content-Type 二次校验必须拦截
        var ex = Assert.Throws<FileStorageException>(() =>
            FileUploadValidator.EnsureValid(
                "show", "poster.png", "application/x-javascript", 100, TestOptions()));

        Assert.Equal("UNSUPPORTED_FILE_TYPE", ex.ErrorCode);
    }

    [Fact]
    public void Validate_ContentTypeIsCaseInsensitive_Passes()
    {
        FileUploadValidator.EnsureValid(
            "show", "poster.png", "IMAGE/PNG", 100, TestOptions());
    }

    // ---------- OssObjectKeyGenerator ----------

    private static readonly Regex ObjectKeyPattern = new(
        @"^showtime/(?<folder>[a-z]+)/(?<year>\d{4})/(?<month>\d{2})/"
        + @"(?<guid>[0-9a-f]{32})\.(?<ext>[a-z0-9]+)$",
        RegexOptions.Compiled);

    [Fact]
    public void Generate_ProduceDocumentedKeyFormat()
    {
        var utcNow = new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Utc);

        var key = OssObjectKeyGenerator.Generate("show", ".png", utcNow);

        var match = ObjectKeyPattern.Match(key);
        Assert.True(match.Success, $"对象键不符合约定格式: {key}");
        Assert.Equal("show", match.Groups["folder"].Value);
        Assert.Equal("2026", match.Groups["year"].Value);
        Assert.Equal("04", match.Groups["month"].Value);
        Assert.Equal("png", match.Groups["ext"].Value);
    }

    [Fact]
    public void Generate_ExtensionIsLowercased()
    {
        var key = OssObjectKeyGenerator.Generate(
            "show", ".PNG", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.EndsWith(".png", key);
    }

    [Fact]
    public void Generate_MissingExtension_FallsBackToBin()
    {
        var key = OssObjectKeyGenerator.Generate(
            "tmp", "", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.EndsWith(".bin", key);
    }

    [Fact]
    public void Generate_DifferentCalls_ProduceDistinctGuids()
    {
        var utcNow = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var first = OssObjectKeyGenerator.Generate("show", ".png", utcNow);
        var second = OssObjectKeyGenerator.Generate("show", ".png", utcNow);

        Assert.NotEqual(first, second);
    }
}
