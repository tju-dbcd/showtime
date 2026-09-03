using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeOptionsTests
{
    [Fact]
    public void PlannedConfigurationKeys_BindAtDocumentedBoundaries()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Exchange:ReviewExpireMinutes"] = "10080",
            ["Exchange:PaymentExpireMinutes"] = "1",
            ["Exchange:ExpirationScanIntervalSeconds"] = "3600",
            ["Exchange:ExpirationBatchSize"] = "500",
        });

        Assert.Equal(10_080, options.ReviewExpireMinutes);
        Assert.Equal(1, options.PaymentExpireMinutes);
        Assert.Equal(3_600, options.ExpirationScanIntervalSeconds);
        Assert.Equal(500, options.ExpirationBatchSize);
    }

    [Fact]
    public void TtlOutsideDocumentedRange_FailsValidation()
    {
        Assert.Throws<OptionsValidationException>(() => Bind(
            new Dictionary<string, string?>
            {
                ["Exchange:ReviewExpireMinutes"] = "10081",
            }));
    }

    [Fact]
    public void RetiredConfigurationKey_IsRejectedInsteadOfSilentlyIgnored()
    {
        Assert.Throws<InvalidOperationException>(() => Bind(
            new Dictionary<string, string?>
            {
                ["Exchange:ReviewTtlMinutes"] = "60",
            }));
    }

    private static ExchangeOptions Bind(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<ExchangeOptions>()
            .Bind(
                configuration.GetSection(ExchangeOptions.SectionName),
                binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ExchangeOptions>>().Value;
    }
}
