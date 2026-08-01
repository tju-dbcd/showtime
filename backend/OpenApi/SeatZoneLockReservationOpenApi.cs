using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ShowtimeBackend.OpenApi;

/// <summary>
/// 为前端提前发布锁座契约；这里只修改 OpenAPI 文档，不映射真实 HTTP 端点。
/// </summary>
public sealed class SeatZoneLockReservationOpenApi : IOpenApiDocumentTransformer
{
    private const string AdminPathPrefix = "/api/admin/";
    private const string AdminPermission = "SeatZone.Manage";
    private const string PlannedDescription =
        "This is a planned contract only. Runtime endpoints do not exist until Redis and JWT integration is complete.";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // 认证模块尚未接入时，先把管理权限要求写入文档，供前端和后续 JWT 模块对齐。
        foreach (var path in document.Paths.Where(path => path.Key.StartsWith(AdminPathPrefix, StringComparison.Ordinal)))
        {
            if (path.Value?.Operations is null) continue;
            foreach (var operation in path.Value.Operations.Values)
            {
                if (operation is null) continue;
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                operation.Extensions["x-required-permission"] = new JsonNodeExtension(JsonValue.Create(AdminPermission)!);
            }
        }

        document.Paths.Add("/api/sessions/{sessionId}/seat-locks", CreatePlannedPath(
            "Acquire planned seat locks",
            "seatIds",
            JsonSchemaType.Integer,
            "int64",
            JsonValue.Create(1001L)!));

        document.Paths.Add("/api/sessions/{sessionId}/seat-locks/release", CreatePlannedPath(
            "Release planned seat locks",
            "lockTokens",
            JsonSchemaType.String,
            format: null,
            JsonValue.Create("lock-token-1001")!));

        return Task.CompletedTask;
    }

    private static OpenApiPathItem CreatePlannedPath(
        string summary,
        string requestProperty,
        JsonSchemaType itemType,
        string? format,
        JsonNode exampleItem)
    {
        var path = new OpenApiPathItem();
        path.AddOperation(HttpMethod.Post, new OpenApiOperation
        {
            Summary = summary,
            Description = PlannedDescription,
            Parameters = new List<IOpenApiParameter>
            {
                new OpenApiParameter
                {
                    Name = "sessionId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" }
                }
            },
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                ["x-implementation-status"] = new JsonNodeExtension(JsonValue.Create("planned")!)
            },
            RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Required = new HashSet<string> { requestProperty },
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                [requestProperty] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema { Type = itemType, Format = format }
                                }
                            }
                        },
                        Example = new JsonObject
                        {
                            [requestProperty] = new JsonArray(exampleItem)
                        }
                    }
                }
            },
            Responses = new OpenApiResponses
            {
                ["409"] = new OpenApiResponse { Description = "Seat lock conflict." }
            }
        });

        return path;
    }
}
