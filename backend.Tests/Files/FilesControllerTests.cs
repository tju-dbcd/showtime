using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.Files;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.FileStorage;

namespace ShowtimeBackend.Tests;

/// <summary>
/// POST /api/files/upload 的 API 层测试（错误码/状态码/信封），
/// 校验类用例走真实 OssFileStorageService（校验先于网络调用，不真连 OSS），
/// 成功路径注入 FakeFileStorageService（内存实现）或 LocalDiskFileStorageService（本地磁盘临时目录），
/// 均不依赖真实 OSS。
/// </summary>
public sealed class FilesControllerTests
{
    private const string UploadPath = "/api/files/upload";

    [Fact]
    public async Task Upload_Anonymous_Returns401()
    {
        using var factory = new AuthTestFactory(ossEnabled: true);
        using var client = factory.CreateApiClient();

        using var content = BuildMultipart("poster.png", "image/png");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_NoStorageConfigured_Returns503NotConfigured()
    {
        // OSS 与本地磁盘存储均关闭（kill-switch 全关）→ 503 未配置
        using var factory = new AuthTestFactory(); // Oss:Enabled=false, LocalStorage:Enabled=false
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(envelope.Success);
        Assert.Equal(
            FileStorageException.ErrorStorageNotConfigured,
            envelope.Code);
    }

    [Fact]
    public async Task Upload_MissingFile_Returns400FileRequired()
    {
        using var factory = new AuthTestFactory(ossEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("show"), "folder");

        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FILE_REQUIRED", envelope.Code);
    }

    [Fact]
    public async Task Upload_UnknownFolder_Returns400InvalidFolder()
    {
        using var factory = new AuthTestFactory(ossEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png", folder: "website");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_FOLDER", envelope.Code);
    }

    [Fact]
    public async Task Upload_OversizedFile_Returns413FileTooLarge()
    {
        using var factory = new AuthTestFactory(ossEnabled: true); // 上限 2048 字节
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart(
            "poster.png", "image/png", payload: new string('x', 4096));
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("FILE_TOO_LARGE", envelope.Code);
    }

    [Theory]
    [InlineData("evil.html", "text/html")]
    [InlineData("script.svg", "image/svg+xml")]
    public async Task Upload_DisallowedExtension_Returns400UnsupportedFileType(
        string fileName,
        string contentType)
    {
        using var factory = new AuthTestFactory(ossEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart(fileName, contentType);
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", envelope.Code);
    }

    [Fact]
    public async Task Upload_ScriptContentTypeWithImageExtension_Returns400UnsupportedFileType()
    {
        // 扩展名伪装成图片、类型声明为脚本：Content-Type 二次校验拦截
        using var factory = new AuthTestFactory(ossEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "application/x-javascript");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", envelope.Code);
    }

    [Fact]
    public async Task Upload_WithFakeStorage_ReturnsUrlAndObjectKey()
    {
        using var factory = new AuthTestFactory(
            ossEnabled: true,
            replaceWithFakeStorage: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png", folder: "show");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        response.EnsureSuccessStatusCode();
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.StartsWith("showtime/show/", envelope.Data!.ObjectKey);
        Assert.Equal(envelope.Data.ObjectKey, envelope.Data.Url); // fake 的 PublicUrl 即对象键
    }

    [Fact]
    public async Task Upload_StorageFailure_Returns500UploadFailed()
    {
        // OSS 服务端故障（模拟存储实现抛 UPLOAD_FAILED）：应映射 500，而非 400
        using var factory = new AuthTestFactory(
            ossEnabled: true,
            customFileStorage: new ThrowingFileStorage());
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png", folder: "show");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(envelope.Success);
        Assert.Equal("UPLOAD_FAILED", envelope.Code);
    }

    [Fact]
    public async Task Upload_WithoutFolder_DefaultsToTmp()
    {
        using var factory = new AuthTestFactory(
            ossEnabled: true,
            replaceWithFakeStorage: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("showtime/tmp/", envelope.Data!.ObjectKey);
    }

    [Fact]
    public async Task Upload_LocalStorageEnabled_PersistsFileAndServesIt()
    {
        // 本地磁盘存储（开发/联调中间态）：无 OSS 时上传落盘，public URL 由静态托管可读回
        using var factory = new AuthTestFactory(localStorageEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png", folder: "show");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);

        response.EnsureSuccessStatusCode();
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.StartsWith("showtime/show/", envelope.Data!.ObjectKey);
        // PublicUrl = BaseUrl(/files) + objectKey
        Assert.Equal($"/files/{envelope.Data.ObjectKey}", envelope.Data.Url);

        // 文件确实落盘在共享目录（而非仅内存），且经静态文件中间件按公共读风格可访问
        Assert.NotNull(factory.LocalStorageRoot);
        var diskPath = Path.Combine(
            factory.LocalStorageRoot,
            envelope.Data.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(diskPath));

        var fileResponse = await client.GetAsync(envelope.Data.Url);
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal("image/png", fileResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Upload_LocalStorageEnabled_DeleteRemovesObjectAndFile()
    {
        using var factory = new AuthTestFactory(localStorageEnabled: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var content = BuildMultipart("poster.png", "image/png", folder: "avatar");
        var response = await client.PostAsync(UploadPath, content);
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);
        response.EnsureSuccessStatusCode();

        var storage = factory.Services.GetRequiredService<IFileStorageService>();
        await storage.DeleteObjectAsync(envelope.Data!.ObjectKey);

        Assert.NotNull(factory.LocalStorageRoot);
        var diskPath = Path.Combine(
            factory.LocalStorageRoot,
            envelope.Data.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(File.Exists(diskPath));
        var afterDelete = await client.GetAsync(envelope.Data.Url);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Upload_FormContentTypeOverrides_IsUsed()
    {
        using var factory = new AuthTestFactory(
            ossEnabled: true,
            replaceWithFakeStorage: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        // multipart 文件缺省无 Content-Type，但表单字段要求 image/png → 校验应通过
        using var content = BuildMultipart("poster.png", "", formContentType: "image/png");
        var response = await client.PostAsync(UploadPath, content);

        response.EnsureSuccessStatusCode();
        var envelope = await AuthTestFactory.ReadResponseAsync<FileUploadResponse>(response);
        Assert.True(envelope.Success);
    }

    // ---------- helpers ----------

    /// <summary>模拟 OSS 服务端故障的文件存储（上传即抛 UPLOAD_FAILED）。</summary>
    private sealed class ThrowingFileStorage : IFileStorageService
    {
        public Task<FileUploadResult> UploadFileAsync(
            Stream content,
            string fileName,
            string contentType,
            string folder,
            CancellationToken cancellationToken = default) =>
            throw new FileStorageException(
                FileStorageException.ErrorUploadFailed,
                "OSS unavailable (simulated).");

        public Task<FileUploadResult> UploadFromMultipartAsync(
            IFormFile file,
            string folder,
            CancellationToken cancellationToken = default) =>
            throw new FileStorageException(
                FileStorageException.ErrorUploadFailed,
                "OSS unavailable (simulated).");

        public Task DeleteObjectAsync(
            string objectKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        AuthTestFactory factory)
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        var client = factory.CreateApiClient();

        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        login.EnsureSuccessStatusCode();
        var loginEnvelope =
            await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginEnvelope.Data!.AccessToken);
        return client;
    }

    private static MultipartFormDataContent BuildMultipart(
        string fileName,
        string contentType,
        string? folder = null,
        string? formContentType = null,
        string? payload = null)
    {
        var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(
            Encoding.UTF8.GetBytes(payload ?? "fake-image-bytes"));
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            bytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        content.Add(bytes, "file", fileName);
        if (folder is not null)
        {
            content.Add(new StringContent(folder), "folder");
        }
        if (formContentType is not null)
        {
            content.Add(new StringContent(formContentType), "contentType");
        }
        return content;
    }
}
