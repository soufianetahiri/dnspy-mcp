using System.Text.Json;
using DnSpyMcpServer.Services;
using DnSpyMcpServer.Tools;
using DnSpyMcpServer.Transport;

namespace DnSpyMcpServer.Core;

internal sealed class McpServer(StdioJsonRpc rpc, ToolRegistry tools, ToolContext toolContext)
{
    private const string SupportedProtocolVersion = "2024-11-05";
    private readonly ResourceRegistry _resources = new(toolContext.Analysis);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var raw = await rpc.ReadMessageAsync(cancellationToken);
            if (raw is null)
                break;

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in root.EnumerateArray())
                        await ProcessRequestAsync(entry, cancellationToken);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    await ProcessRequestAsync(root, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                var id = TryGetId(raw);
                if (id is JsonElement requestId)
                    await rpc.WriteErrorAsync(requestId, -32000, ex.Message, cancellationToken);
            }
            finally
            {
                doc?.Dispose();
            }
        }
    }

    private async Task ProcessRequestAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var method = request.GetProperty("method").GetString();
        var hasId = request.TryGetProperty("id", out var id);
        var @params = request.TryGetProperty("params", out var value) ? value : default;

        object? result = method switch
        {
            "initialize" => BuildInitializeResult(@params),
            "notifications/initialized" => null,
            "ping" => new { },
            "tools/list" => new { tools = tools.GetDefinitions() },
            "tools/call" => HandleToolCall(@params),
            "resources/list" => new { resources = _resources.ListResources() },
            "resources/read" => HandleResourceRead(@params),
            _ => throw new InvalidOperationException($"Method not found: {method}")
        };

        if (!hasId)
            return;

        await rpc.WriteResultAsync(id, result ?? new { }, cancellationToken);
    }

    private object BuildInitializeResult(JsonElement @params)
    {
        var protocolVersion = SupportedProtocolVersion;
        if (@params.ValueKind == JsonValueKind.Object &&
            @params.TryGetProperty("protocolVersion", out var clientVersion) &&
            clientVersion.ValueKind == JsonValueKind.String)
        {
            protocolVersion = clientVersion.GetString() ?? protocolVersion;
        }

        return new
        {
            protocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                },
                resources = new
                {
                    listChanged = true,
                    subscribe = false
                }
            },
            serverInfo = new
            {
                name = "dnspy-mcp-server",
                version = "1.2.1"
            }
        };
    }

    private object HandleToolCall(JsonElement @params)
    {
        var toolName = @params.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Missing tool name.");
        var arguments = @params.TryGetProperty("arguments", out var args) ? args : default;

        try
        {
            var output = tools.Invoke(toolName, arguments, toolContext);
            return new
            {
                content = new[]
                {
                    new { type = "text", text = output.Text }
                },
                structuredContent = output.StructuredContent
            };
        }
        catch (Exception ex)
        {
            return new
            {
                content = new[]
                {
                    new { type = "text", text = ex.Message }
                },
                isError = true
            };
        }
    }

    private object HandleResourceRead(JsonElement @params)
    {
        var uri = @params.GetProperty("uri").GetString()
            ?? throw new InvalidOperationException("Missing resource uri.");

        var text = _resources.ReadResource(uri);
        return new
        {
            contents = new[]
            {
                new
                {
                    uri,
                    mimeType = "text/plain",
                    text
                }
            }
        };
    }

    private static JsonElement? TryGetId(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
                return root.TryGetProperty("id", out var id) ? id.Clone() : null;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var id))
                        return id.Clone();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
