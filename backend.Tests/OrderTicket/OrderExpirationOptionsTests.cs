using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderExpirationOptionsTests
{
    [Fact]
    public void Defaults_AreDocumentedValues()
    {
        var options = Bind(new Dictionary<string, string?>());

        Assert.Equal(15, options.PendingPaymentExpireMinutes);
        Assert.Equal(30, options.ExpirationScanIntervalSeconds);
        Assert.Equal(50, options.ExpirationBatchSize);
    }

    [Theory]
    [InlineData("OrderExpiration:PendingPaymentExpireMinutes", "0")]
    [InlineData("OrderExpiration:ExpirationScanIntervalSeconds", "3601")]
    [InlineData("OrderExpiration:ExpirationBatchSize", "501")]
    public void ValueOutsideRange_FailsValidation(string key, string value)
    {
        Assert.Throws<OptionsValidationException>(() => Bind(
            new Dictionary<string, string?> { [key] = value }));
    }

    private static OrderExpirationOptions Bind(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<OrderExpirationOptions>()
            .Bind(
                configuration.GetSection(OrderExpirationOptions.SectionName),
                binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OrderExpirationOptions>>().Value;
    }
}
