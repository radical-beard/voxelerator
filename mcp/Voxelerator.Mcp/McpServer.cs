using System.Text.Json;
using System.Text.Json.Nodes;

namespace Voxelerator.Mcp;

/// Thrown by tool handlers for user-facing failures — becomes a tool result
/// with isError = true, never a protocol error.
public sealed class ToolError(string message) : Exception(message);

public sealed record ToolOut(string Text, byte[]? Png = null);

/// Minimal MCP server core: newline-delimited JSON-RPC 2.0. Kept free of
/// Console so tests can drive it line-by-line in process.
public sealed class McpServer
{
    public const string ServerName = "voxelerator";
    public const string ServerVersion = "0.1.0";
    public const string ProtocolVersion = "2025-06-18";

    private readonly McpTools _tools = new();

    /// One request line in, one response line out (null for notifications).
    public string? HandleLine(string line)
    {
        JsonNode? req;
        try { req = JsonNode.Parse(line); }
        catch (JsonException)
        {
            return Error(null, -32700, "parse error: not valid JSON").ToJsonString();
        }
        if (req is not JsonObject obj) return Error(null, -32600, "invalid request").ToJsonString();

        var id = obj["id"];
        string method = obj["method"]?.GetValue<string>() ?? "";
        var p = obj["params"] as JsonObject;

        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(p),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = _tools.ListJson() },
                "tools/call" => CallTool(p),
                _ when method.StartsWith("notifications/", StringComparison.Ordinal) => null,
                _ => throw new ProtocolError(-32601, $"method not found: {method}"),
            };
            if (id is null) return null;                    // notification — no response
            if (result is null) return null;
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id.DeepClone(),
                ["result"] = result,
            }.ToJsonString();
        }
        catch (ProtocolError pe)
        {
            return id is null ? null : Error(id, pe.Code, pe.Message).ToJsonString();
        }
        catch (Exception e)
        {
            return id is null ? null : Error(id, -32603, $"internal error: {e.Message}").ToJsonString();
        }
    }

    private static JsonNode Initialize(JsonObject? p) => new JsonObject
    {
        ["protocolVersion"] = p?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
    };

    private JsonNode CallTool(JsonObject? p)
    {
        string name = p?["name"]?.GetValue<string>() ?? throw new ProtocolError(-32602, "tools/call needs a name");
        var args = p["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            var outp = _tools.Call(name, args);
            var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = outp.Text } };
            if (outp.Png is not null)
                content.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["data"] = Convert.ToBase64String(outp.Png),
                    ["mimeType"] = "image/png",
                });
            return new JsonObject { ["content"] = content, ["isError"] = false };
        }
        catch (ToolError te)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = te.Message } },
                ["isError"] = true,
            };
        }
        catch (Exception e) when (e is Voxelerator.Core.ModelFormatException or FormatException or IOException
                                      or ArgumentException or InvalidOperationException)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = e.Message } },
                ["isError"] = true,
            };
        }
    }

    private sealed class ProtocolError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
