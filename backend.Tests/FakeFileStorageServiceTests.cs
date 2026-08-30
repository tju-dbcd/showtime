using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using ShowtimeBackend.Services.FileStorage;

namespace ShowtimeBackend.Tests;

/// <summary>
/// 验证 FakeFileStorageService（M1 骨架的内存实现）：
/// 对象键遵循 showtime/{folder}/{yyyy}/{MM}/{guid}.{ext} 格式、内容可回读、删除生效。
/// 真实 OSS 实现的校验逻辑（大小/类型/文件名清洗）在 M2 的 OssFileStorageService 单测中覆盖。
/// </summary>
public sealed class FakeFileStorageServiceTests
{
    private static readonly Regex ObjectKeyPattern =
        new(
            @"^showtime/(?<folder>[a-z]+)/(?<year>\d{4})/(?<month>\d{2})/"
            + @"(?<guid>[0-9a-f]{32})\.(?<ext>[a-z0-9]+)$",
            RegexOptions.Compiled);

    [Fact]
    public async Task UploadFileAsync_ReturnsObjectKeyInDocumentedFormat()
    {
        var service = new FakeFileStorageService();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("poster-bytes"));

        var result = await service.UploadFileAsync(
            content, "my-poster.JPG", "image/jpeg", "show");

        Assert.NotEmpty(result.ObjectKey);
        var match = ObjectKeyPattern.Match(result.ObjectKey);
        Assert.True(match.Success, $"ObjectKey 不符合约定格式: {result.ObjectKey}");
        Assert.Equal("show", match.Groups["folder"].Value);
        Assert.Equal(".jpg", Path.GetExtension(result.ObjectKey));
        Assert.Equal(result.ObjectKey, result.PublicUrl);
    }

    [Fact]
    public async Task UploadFileAsync_StoresExactContentReadableByObjectKey()
    {
        var service = new FakeFileStorageService();
        var bytes = Encoding.UTF8.GetBytes("image-content-123");
        using var content = new MemoryStream(bytes);

        var result = await service.UploadFileAsync(
            content, "poster.png", "image/png", "show");

        Assert.True(service.Store.TryGetValue(result.ObjectKey, out var stored));
        Assert.Equal(bytes, stored);
    }

    [Fact]
    public async Task UploadFileAsync_DifferentCallsProduceDifferentKeys()
    {
        var service = new FakeFileStorageService();
        using var first = new MemoryStream(Encoding.UTF8.GetBytes("a"));
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("b"));

        var r1 = await service.UploadFileAsync(first, "a.png", "image/png", "show");
        var r2 = await service.UploadFileAsync(second, "b.png", "image/png", "show");

        Assert.NotEqual(r1.ObjectKey, r2.ObjectKey);
    }

    [Fact]
    public async Task UploadFileAsync_EmptyFolderFallsBackToTmp()
    {
        var service = new FakeFileStorageService();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));

        var result = await service.UploadFileAsync(
            content, "temp.txt", "text/plain", "  ");

        Assert.StartsWith("showtime/tmp/", result.ObjectKey);
    }

    [Fact]
    public async Task UploadFromMultipartAsync_ReadsFormFileAndDelegates()
    {
        var service = new FakeFileStorageService();
        var formFile = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes("avatar-bytes")),
            0,
            Encoding.UTF8.GetByteCount("avatar-bytes"),
            "avatar.png",
            "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };

        var result = await service.UploadFromMultipartAsync(formFile, "avatar");

        Assert.StartsWith("showtime/avatar/", result.ObjectKey);
        Assert.True(service.Store.TryGetValue(result.ObjectKey, out var stored));
        Assert.Equal("avatar-bytes", Encoding.UTF8.GetString(stored));
    }

    [Fact]
    public async Task DeleteObjectAsync_RemovesStoredObject()
    {
        var service = new FakeFileStorageService();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        var result = await service.UploadFileAsync(
            content, "t.png", "image/png", "tmp");

        await service.DeleteObjectAsync(result.ObjectKey);

        Assert.False(service.Store.TryGetValue(result.ObjectKey, out _));
    }

    [Fact]
    public async Task DeleteObjectAsync_MissingObject_SucceedsSilently()
    {
        var service = new FakeFileStorageService();

        await service.DeleteObjectAsync("showtime/tmp/nonexistent.png");
    }
}
