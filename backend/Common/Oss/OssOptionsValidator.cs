using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.Oss;

/// <summary>
/// OssOptions 启动期校验：仅在 Oss:Enabled=true 时要求
/// Endpoint / Bucket / BaseUrl 已配置；AccessKey 属运行时敏感项，
/// 不在启动期强制（缺失时由上传服务给出明确错误）。
/// </summary>
public sealed class OssOptionsValidator : IValidateOptions<OssOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        OssOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return ValidateOptionsResult.Fail(
                "Oss:Endpoint is required when Oss:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            return ValidateOptionsResult.Fail(
                "Oss:Bucket is required when Oss:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail(
                "Oss:BaseUrl is required when Oss:Enabled is true.");
        }

        return ValidateOptionsResult.Success;
    }
}
