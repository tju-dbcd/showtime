using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Common.OpenApi;

public sealed class TicketRedemptionSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(RedeemTicketRequest))
        {
            return Task.CompletedTask;
        }

        schema.Required ??= new HashSet<string>();
        ConfigureProperty(schema, "qrCode", 255);
        ConfigureProperty(schema, "checkDevice", 100);
        return Task.CompletedTask;
    }

    private static void ConfigureProperty(
        OpenApiSchema schema,
        string propertyName,
        int maxLength)
    {
        schema.Required!.Add(propertyName);
        if (schema.Properties?.TryGetValue(propertyName, out var property) == true &&
            property is OpenApiSchema propertySchema)
        {
            propertySchema.Type = JsonSchemaType.String;
            propertySchema.MaxLength = maxLength;
        }
    }
}
