using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.Oss;
using ShowtimeBackend.DTOs.Files;
using ShowtimeBackend.Services.FileStorage;

namespace ShowtimeBackend.Controllers.Files;

/// <summary>
/// 统一文件上传接口：前端 multipart POST 到后端，后端用 AccessKey 代理上传 OSS 后返回公开 URL。
/// 角色/场景细分鉴权（管理员发布用 show、用户头像用 avatar）由各业务方在调用层控制。
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController(
    IFileStorageService fileStorage,
    IOptions<OssOptions> options) : ControllerBase
{
    /// <summary>
    /// 中间件层请求体上限兜底（10MB &gt; 默认 5MB 文件上限 + multipart 开销）；
    /// 业务上的精细大小校验由 OssOptions.MaxFileSizeBytes 执行，超限返回 413 语义错误。
    /// </summary>
    private const long MaxRequestBodyBytes = 10 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> Upload(
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            // kill-switch：无 OSS 环境先开发其他功能，上传接口明确报"未配置"
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<FileUploadResponse>.Fail(
                    "OSS_NOT_CONFIGURED",
                    "OSS upload is not configured (Oss:Enabled=false)."));
        }

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(
                ApiResponse<FileUploadResponse>.Fail(
                    FileUploadValidator.ErrorFileRequired,
                    "The 'file' field is required."));
        }

        var folder = string.IsNullOrWhiteSpace(request.Folder)
            ? FileStorageFolders.Tmp
            : request.Folder;
        var contentType = string.IsNullOrWhiteSpace(request.ContentType)
            ? request.File.ContentType
            : request.ContentType!;

        try
        {
            using var stream = request.File.OpenReadStream();
            var result = await fileStorage.UploadFileAsync(
                stream,
                request.File.FileName,
                contentType,
                folder,
                cancellationToken);

            return Ok(
                ApiResponse<FileUploadResponse>.Ok(
                    new FileUploadResponse(result.PublicUrl, result.ObjectKey),
                    "File uploaded successfully."));
        }
        catch (FileStorageException ex)
        {
            var statusCode = ex.ErrorCode switch
            {
                FileUploadValidator.ErrorFileTooLarge
                    => StatusCodes.Status413PayloadTooLarge,
                // 服务端与 OSS 通信故障属内部错误：500 而非 400，
                // 避免客户端按"请求错误"无谓重试。
                FileStorageException.ErrorUploadFailed
                    => StatusCodes.Status500InternalServerError,
                _ => StatusCodes.Status400BadRequest,
            };
            return StatusCode(
                statusCode,
                ApiResponse<FileUploadResponse>.Fail(ex.ErrorCode, ex.Message));
        }
    }
}
