using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using ShowtimeBackend.Controllers.OrderTicket;

namespace ShowtimeBackend.Common.OpenApi;

public sealed class OrderIdempotencyOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.HttpMethod != HttpMethods.Post ||
            context.Description.ActionDescriptor is not ControllerActionDescriptor action ||
            action.ControllerTypeInfo.AsType() != typeof(OrdersController) ||
            action.MethodInfo.Name != nameof(OrdersController.Create))
        {
            return Task.CompletedTask;
        }

        var parameter = operation.Parameters?
            .OfType<OpenApiParameter>()
            .SingleOrDefault(candidate =>
                candidate.In == ParameterLocation.Header &&
                string.Equals(
                    candidate.Name,
                    "Idempotency-Key",
                    StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
        {
            throw new InvalidOperationException(
                "POST /api/orders is missing its Idempotency-Key OpenAPI parameter.");
        }

        parameter.Required = true;
        if (parameter.Schema is not OpenApiSchema schema)
        {
            throw new InvalidOperationException(
                "Idempotency-Key is missing its OpenAPI schema.");
        }

        schema.Type = JsonSchemaType.String;
        schema.MaxLength = 64;
        return Task.CompletedTask;
    }
}
