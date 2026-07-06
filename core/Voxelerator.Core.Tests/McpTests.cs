using System.Text.Json.Nodes;
using Voxelerator.Core;
using Voxelerator.Mcp;
using Xunit;

namespace Voxelerator.Core.Tests;

/// Drives the MCP server exactly as a client would: JSON-RPC lines in,
/// JSON-RPC lines out. Shares the "registry" collection because the server
/// reads and writes the (overridden) data dir.
[Collection("registry")]
public class McpTests : IDisposable
{
    private readonly string _dir;
    private readonly McpServer _server;
    private int _id;

    public McpTests()
    {
        _dir = Fixtures.TempDir();
        Registry.OverrideDataDir = Path.Combine(_dir, "data");
        var settings = Registry.LoadSettings();
        settings.DefaultNewModelDir = Path.Combine(_dir, "models");
        Registry.SaveSettings(settings);
        _server = new McpServer();
    }

    public void Dispose()
    {
        Registry.OverrideDataDir = null;
        Directory.Delete(_dir, recursive: true);
    }

    private JsonObject Rpc(string method, JsonObject? @params = null)
    {
        var req = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method };
        if (@params is not null) req["params"] = @params;
        var resp = _server.HandleLine(req.ToJsonString());
        Assert.NotNull(resp);
        return (JsonObject)JsonNode.Parse(resp!)!;
    }

    private JsonObject Call(string tool, JsonObject args)
        => Rpc("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args });

    private static string TextOf(JsonObject resp)
        => resp["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonObject resp)
        => resp["result"]!["isError"]!.GetValue<bool>();

    [Fact]
    public void InitializeHandshakeWorks()
    {
        var resp = Rpc("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
        });
        Assert.Equal("voxelerator", resp["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Null(_server.HandleLine("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public void ToolsListCoversTheSpecSurface()
    {
        var resp = Rpc("tools/list");
        var names = ((JsonArray)resp["result"]!["tools"]!).Select(t => t!["name"]!.GetValue<string>()).ToList();
        string[] expected =
        [
            "list_models", "model_info", "get_layer", "get_layers", "validate",
            "set_layer", "set_voxels", "fill_box", "hollow_box", "fill_cylinder",
            "copy_layer", "insert_layer", "delete_layer", "replace_color", "mirror",
            "create_model", "duplicate_model", "list_palettes", "get_palette",
            "create_palette", "add_color", "render", "export_model",
        ];
        foreach (var e in expected) Assert.Contains(e, names);
        Assert.DoesNotContain("delete_model", names);       // only humans destroy
    }

    [Fact]
    public void FullAuthoringLoopWorks()
    {
        var created = Call("create_model", new JsonObject
        {
            ["name"] = "hut",
            ["width"] = 6,
            ["depth"] = 6,
            ["height"] = 5,
            ["palette"] = "neo-terrestria",
        });
        Assert.False(IsError(created));
        Assert.Contains("created 'hut'", TextOf(created));

        var box = Call("fill_box", new JsonObject
        {
            ["name"] = "hut",
            ["x0"] = 0, ["y0"] = 0, ["z0"] = 0, ["x1"] = 5, ["y1"] = 0, ["z1"] = 5,
            ["color"] = 1,
        });
        Assert.False(IsError(box));

        var grid = "111111\n1....1\n1....1\n1....1\n1....1\n111111";
        Assert.False(IsError(Call("set_layer", new JsonObject { ["name"] = "hut", ["layer"] = 1, ["grid"] = grid })));

        var got = Call("get_layer", new JsonObject { ["name"] = "hut", ["layer"] = 1 });
        Assert.Contains(grid, TextOf(got));
        Assert.Contains("legend:", TextOf(got));

        Assert.False(IsError(Call("set_voxels", new JsonObject
        {
            ["name"] = "hut",
            ["voxels"] = new JsonArray(
                new JsonObject { ["x"] = 2, ["y"] = 2, ["z"] = 0, ["color"] = 5 },
                new JsonObject { ["x"] = 99, ["y"] = 0, ["z"] = 0, ["color"] = 1 }),
        })));

        var info = Call("model_info", new JsonObject { ["name"] = "hut" });
        Assert.Contains("6x6 footprint, 5 layers", TextOf(info));
        Assert.Contains("triangles", TextOf(info));

        var render = Call("render", new JsonObject { ["name"] = "hut", ["view"] = "iso", ["night"] = 0.8 });
        Assert.False(IsError(render));
        var content = (JsonArray)render["result"]!["content"]!;
        Assert.Equal(2, content.Count);
        Assert.Equal("image", content[1]!["type"]!.GetValue<string>());
        var png = Convert.FromBase64String(content[1]!["data"]!.GetValue<string>());
        var (w, h, _) = Png.ReadRgba(png);
        Assert.Equal((512, 512), (w, h));

        Assert.Contains("valid", TextOf(Call("validate", new JsonObject { ["name"] = "hut" })));

        var dest = Path.Combine(_dir, "exported", "hut");
        Assert.False(IsError(Call("export_model", new JsonObject { ["name"] = "hut", ["destination"] = dest })));
        Assert.Empty(ModelStore.Validate(dest));

        var listed = Call("list_models", new JsonObject());
        Assert.Contains("hut", TextOf(listed));
    }

    [Fact]
    public void LayerReshapeToolsWork()
    {
        Call("create_model", new JsonObject
        {
            ["name"] = "tower", ["width"] = 4, ["depth"] = 4, ["height"] = 3, ["palette"] = "primer",
        });
        Call("set_layer", new JsonObject { ["name"] = "tower", ["layer"] = 0, ["grid"] = "1111\n1111\n1111\n1111" });

        Assert.Contains("4 layers", TextOf(Call("insert_layer",
            new JsonObject { ["name"] = "tower", ["at"] = 1, ["copy_from"] = 0 })));
        Assert.Contains("3 layers", TextOf(Call("delete_layer",
            new JsonObject { ["name"] = "tower", ["at"] = 3 })));
        Assert.False(IsError(Call("copy_layer", new JsonObject { ["name"] = "tower", ["from"] = 0, ["to"] = 2 })));
        Assert.False(IsError(Call("mirror", new JsonObject { ["name"] = "tower", ["axis"] = "x" })));
        Assert.False(IsError(Call("replace_color", new JsonObject { ["name"] = "tower", ["from"] = 1, ["to"] = 3 })));

        var layer = TextOf(Call("get_layer", new JsonObject { ["name"] = "tower", ["layer"] = 0 }));
        Assert.Contains("3333", layer);
    }

    [Fact]
    public void PaletteToolsEnforceTheLaw()
    {
        var made = Call("create_palette", new JsonObject
        {
            ["name"] = "duo",
            ["colors"] = new JsonArray(
                new JsonObject { ["hex"] = "#112233", ["name"] = "dark" },
                new JsonObject { ["hex"] = "#DDEEFF", ["name"] = "light", ["tags"] = new JsonArray("emissive") }),
        });
        Assert.False(IsError(made));

        Assert.Contains("duo", TextOf(Call("list_palettes", new JsonObject())));
        Assert.Contains("emissive", TextOf(Call("get_palette", new JsonObject { ["palette"] = "duo" })));

        Assert.False(IsError(Call("add_color", new JsonObject
        {
            ["palette"] = "duo", ["hex"] = "#445566", ["color_name"] = "mid",
        })));

        // duplicate palette name is refused
        Assert.True(IsError(Call("create_palette", new JsonObject
        {
            ["name"] = "duo",
            ["colors"] = new JsonArray(new JsonObject { ["hex"] = "#000000", ["name"] = "x" }),
        })));

        // model bound to 'duo' refuses slots beyond its size
        Call("create_model", new JsonObject
        {
            ["name"] = "duo-model", ["width"] = 2, ["depth"] = 2, ["height"] = 1, ["palette"] = "duo",
        });
        Assert.True(IsError(Call("set_layer", new JsonObject
        {
            ["name"] = "duo-model", ["layer"] = 0, ["grid"] = "99\n99",
        })));
    }

    [Fact]
    public void ErrorsAreToolResultsNotProtocolErrors()
    {
        var unknownTool = Call("no_such_tool", new JsonObject());
        Assert.True(IsError(unknownTool));

        var unknownModel = Call("model_info", new JsonObject { ["name"] = "ghost" });
        Assert.True(IsError(unknownModel));
        Assert.Contains("list_models", TextOf(unknownModel));

        var badJson = _server.HandleLine("this is not json");
        Assert.Contains("-32700", badJson);

        var badMethod = Rpc("bogus/method");
        Assert.Contains("-32601", badMethod["error"]!["code"]!.ToJsonString());
    }

    [Fact]
    public void MutationsCreateOneBackupPerSession()
    {
        Call("create_model", new JsonObject
        {
            ["name"] = "b", ["width"] = 2, ["depth"] = 2, ["height"] = 1, ["palette"] = "primer",
        });
        Call("set_layer", new JsonObject { ["name"] = "b", ["layer"] = 0, ["grid"] = "11\n11" });
        Call("set_layer", new JsonObject { ["name"] = "b", ["layer"] = 0, ["grid"] = "22\n22" });

        var folder = Registry.ResolveName("b")!;
        var backupDir = Path.Combine(Registry.BackupsDir, Registry.IdFor(folder));
        Assert.True(Directory.Exists(backupDir));
        Assert.Single(Directory.GetDirectories(backupDir));   // snapshot-once per session
    }
}
