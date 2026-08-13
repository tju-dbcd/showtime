using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ShowtimeBackend.Common.OpenApi;

/// <summary>
/// 将 C# 枚举 schema 组件渲染为字符串枚举（enum 列表 + type=string），
/// 与 JsonStringEnumConverter 的序列化行为保持一致，使枚举值进入 OpenAPI schema，
/// 前端 openapi-typescript 可据此生成 union 类型。
/// </summary>
public sealed class EnumStringSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type is { IsEnum: true } && schema.Type == JsonSchemaType.Integer)
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Enum = Enum.GetNames(type)
                .Select(name => (JsonNode)JsonValue.Create(name)!)
                .ToList();
        }

        return Task.CompletedTask;
    }
}
