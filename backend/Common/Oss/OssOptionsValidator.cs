using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.Oss;

/// <summary>
/// OssOptions 启动期校验：仅在 Oss:Enabled=true 时要求
/// Endpoint / Bucket / BaseUrl / AccessKeyId / AccessKeySecret 均已配置。
/// AccessKey 属敏感项不落仓库（环境变量/secret 注入），但启用后缺失会让
/// OssClient 构造即抛异常，故一并纳入 fail-fast 校验。
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

        if (string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            return ValidateOptionsResult.Fail(
                "Oss:AccessKeyId is required when Oss:Enabled is true. "
                + "Inject via the Oss__AccessKeyId environment variable or user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeySecret))
        {
            return ValidateOptionsResult.Fail(
                "Oss:AccessKeySecret is required when Oss:Enabled is true. "
                + "Inject via the Oss__AccessKeySecret environment variable or user-secrets.");
        }

        return ValidateOptionsResult.Success;
    }
}
