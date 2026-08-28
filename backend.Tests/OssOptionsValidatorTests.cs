using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.Oss;

namespace ShowtimeBackend.Tests;

/// <summary>
/// 验证 OssOptions 启动期校验：kill-switch 关闭时不做任何必填校验，
/// 开启时要求 Endpoint/Bucket/BaseUrl 已配置（AccessKey 属运行时敏感项，不在此校验）。
/// </summary>
public sealed class OssOptionsValidatorTests
{
    private readonly OssOptionsValidator _validator = new();

    [Fact]
    public void Validate_Disabled_SucceedsEvenWithoutRequiredFields()
    {
        var options = new OssOptions
        {
            Enabled = false,
            Endpoint = string.Empty,
            Bucket = string.Empty,
            BaseUrl = string.Empty,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EnabledAndComplete_Succeeds()
    {
        var options = new OssOptions
        {
            Enabled = true,
            Endpoint = "https://oss-cn-hangzhou.aliyuncs.com",
            Bucket = "showtime-assets",
            BaseUrl = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com",
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Endpoint", "")]
    [InlineData("Bucket", "")]
    [InlineData("BaseUrl", " ")]
    public void Validate_EnabledMissingField_Fails(
        string missingField,
        string missingValue)
    {
        var options = new OssOptions
        {
            Enabled = true,
            Endpoint = "https://oss-cn-hangzhou.aliyuncs.com",
            Bucket = "showtime-assets",
            BaseUrl = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com",
        };

        switch (missingField)
        {
            case "Endpoint":
                options = new OssOptions
                {
                    Enabled = true,
                    Endpoint = missingValue,
                    Bucket = options.Bucket,
                    BaseUrl = options.BaseUrl,
                };
                break;
            case "Bucket":
                options = new OssOptions
                {
                    Enabled = true,
                    Endpoint = options.Endpoint,
                    Bucket = missingValue,
                    BaseUrl = options.BaseUrl,
                };
                break;
            case "BaseUrl":
                options = new OssOptions
                {
                    Enabled = true,
                    Endpoint = options.Endpoint,
                    Bucket = options.Bucket,
                    BaseUrl = missingValue,
                };
                break;
        }

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"Oss:{missingField}", result.Failures.First());
    }

    [Fact]
    public void Defaults_MatchPlan()
    {
        var options = new OssOptions();

        Assert.True(options.Enabled);
        Assert.Equal(5 * 1024 * 1024, options.MaxFileSizeBytes);
        Assert.Equal(
            new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" },
            options.AllowedExtensions);
    }

    [Fact]
    public void BoundFromConfiguration_SectionNameIsOss()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oss:Enabled"] = "true",
                ["Oss:Endpoint"] = "https://oss-cn-hangzhou.aliyuncs.com",
                ["Oss:Bucket"] = "showtime-assets",
                ["Oss:BaseUrl"] = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com",
                ["Oss:MaxFileSizeBytes"] = "1048576",
            })
            .Build();

        services
            .AddOptions<OssOptions>()
            .Bind(configuration.GetSection(OssOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OssOptions>, OssOptionsValidator>();

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<OssOptions>>()
            .Value;

        Assert.True(options.Enabled);
        Assert.Equal("https://oss-cn-hangzhou.aliyuncs.com", options.Endpoint);
        Assert.Equal("showtime-assets", options.Bucket);
        Assert.Equal(1048576, options.MaxFileSizeBytes);
    }
}
