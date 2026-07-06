using System.Text;
using System.Text.Json.Nodes;
using Voxelerator.Core;

namespace Voxelerator.Mcp;

/// The tool table. Models are addressed by registered name (the recents
/// registry the editor shares); no filesystem paths cross the wire except
/// explicit create/export destinations.
public sealed partial class McpTools
{
    private const string Conventions =
        "Coordinates: x runs right in the layer image, z runs down, y is the layer index (0 = ground; " +
        "file 0001.png is layer 0). Text grids: rows are z from 0; one character per voxel: '.' = air, " +
        "palette slots 1-16 as '1'-'9' then 'a'-'g'. The palette is law: colors outside it do not exist.";

    private sealed record Tool(string Name, string Desc, JsonObject Schema, Func<JsonObject, ToolOut> Run);

    private readonly List<Tool> _tools;
    private readonly HashSet<string> _snapshotted = new(StringComparer.Ordinal);

    public McpTools()
    {
        Registry.SeedBuiltinPalettes();
        _tools =
        [
            // ---- inspect ----------------------------------------------------
            new("list_models",
                "List every model Voxelerator knows about (name, folder, availability). " + Conventions,
                Schema(), _ => ListModels()),
            new("model_info",
                "Dimensions, palette (with tags), stats (filled voxels, triangle count after greedy meshing, per-color usage, content bounds) and folder of one model.",
                Schema(("name", "string", "registered model name", true)), a => ModelInfo(Str(a, "name"))),
            new("get_layer",
                "One horizontal slice as a text grid, with a color legend. " + Conventions,
                Schema(("name", "string", "model name", true), ("layer", "integer", "layer index, 0 = ground", true)),
                a => GetLayers(Str(a, "name"), Int(a, "layer"), Int(a, "layer"))),
            new("get_layers",
                "A range of slices as labelled text grids (inclusive).",
                Schema(("name", "string", "model name", true),
                       ("from", "integer", "first layer", true), ("to", "integer", "last layer", true)),
                a => GetLayers(Str(a, "name"), Int(a, "from"), Int(a, "to"))),
            new("validate",
                "Check every format invariant of a model folder; returns problems or 'valid'.",
                Schema(("name", "string", "model name", true)), a => Validate(Str(a, "name"))),

            // ---- edit --------------------------------------------------------
            new("set_layer",
                "Replace one whole slice with a text grid (must match the model footprint). " + Conventions,
                Schema(("name", "string", "model name", true), ("layer", "integer", "layer index", true),
                       ("grid", "string", "text grid, rows separated by newlines", true)),
                a => SetLayer(Str(a, "name"), Int(a, "layer"), Str(a, "grid"))),
            new("set_voxels",
                "Sparse voxel edits: an array of {x, y, z, color} where color 0 erases. Out-of-bounds entries are skipped and reported.",
                VoxelsSchema(),
                a => SetVoxels(Str(a, "name"), a["voxels"] as JsonArray ?? throw new ToolError("set_voxels needs a 'voxels' array"))),
            new("fill_box",
                "Fill a solid box, inclusive corners. color 0 = carve air.",
                BoxSchema(), a => Box(a, hollow: false)),
            new("hollow_box",
                "Fill only the shell of a box (walls, floor, ceiling), inclusive corners.",
                BoxSchema(), a => Box(a, hollow: true)),
            new("fill_cylinder",
                "Fill a vertical cylinder: center (cx, cz) in cells, layers y0..y1 inclusive, radius in cells.",
                Schema(("name", "string", "model name", true),
                       ("cx", "number", "center x in cells", true), ("cz", "number", "center z in cells", true),
                       ("y0", "integer", "bottom layer", true), ("y1", "integer", "top layer", true),
                       ("radius", "number", "radius in cells", true), ("color", "integer", "palette slot 1-16, or 0 to carve", true)),
                a => Cylinder(a)),
            new("copy_layer",
                "Copy one slice onto another (both must exist).",
                Schema(("name", "string", "model name", true),
                       ("from", "integer", "source layer", true), ("to", "integer", "destination layer", true)),
                a => CopyLayer(Str(a, "name"), Int(a, "from"), Int(a, "to"))),
            new("insert_layer",
                "Insert a layer at an index (existing layers shift up; model grows one layer taller). Optionally copy content from an existing layer.",
                Schema(("name", "string", "model name", true), ("at", "integer", "insert position 0..height", true),
                       ("copy_from", "integer", "optional source layer to copy into the new slot", false)),
                a => InsertLayer(Str(a, "name"), Int(a, "at"), IntOpt(a, "copy_from"))),
            new("delete_layer",
                "Delete a layer (model shrinks one layer; at least one layer must remain).",
                Schema(("name", "string", "model name", true), ("at", "integer", "layer to delete", true)),
                a => DeleteLayer(Str(a, "name"), Int(a, "at"))),
            new("replace_color",
                "Swap every voxel of one palette slot for another across the whole model.",
                Schema(("name", "string", "model name", true),
                       ("from", "integer", "palette slot to replace (1-16)", true),
                       ("to", "integer", "replacement slot (1-16, or 0 to erase)", true)),
                a => ReplaceColor(Str(a, "name"), (byte)Int(a, "from"), (byte)Int(a, "to"))),
            new("mirror",
                "Mirror the whole model in place along the x or z axis.",
                Schema(("name", "string", "model name", true), ("axis", "string", "\"x\" or \"z\"", true)),
                a => Mirror(Str(a, "name"), Str(a, "axis"))),

            // ---- create ------------------------------------------------------
            new("create_model",
                "Create a new model bound to a palette. Dimensions are free (non-square is fine). " +
                "Saved under the default models directory unless 'directory' is given.",
                Schema(("name", "string", "model name (also the folder name)", true),
                       ("width", "integer", "x extent in voxels", true),
                       ("depth", "integer", "z extent in voxels", true),
                       ("height", "integer", "layer count", true),
                       ("palette", "string", "palette name (see list_palettes)", true),
                       ("directory", "string", "optional parent directory for the model folder", false)),
                a => CreateModel(a)),
            new("duplicate_model",
                "Copy an existing model to a new name (same parent directory unless 'directory' is given).",
                Schema(("name", "string", "source model", true), ("new_name", "string", "duplicate's name", true),
                       ("directory", "string", "optional parent directory", false)),
                a => DuplicateModel(Str(a, "name"), Str(a, "new_name"), StrOpt(a, "directory"))),
            new("list_palettes",
                "Every palette in the shared library, with color counts.",
                Schema(), _ => ListPalettes()),
            new("get_palette",
                "Full color list of one palette: slot, hex, name, tags (emissive colors glow at night; glass renders translucent).",
                Schema(("palette", "string", "palette name", true)), a => GetPalette(Str(a, "palette"))),
            new("create_palette",
                "Create a named palette (max 16 colors). Colors: [{hex:'#RRGGBB', name:'wall', tags:['emissive'|'glass']}].",
                PaletteSchema(), a => CreatePalette(a)),
            new("add_color",
                "Append one color to an existing palette (respects the 16-color cap).",
                Schema(("palette", "string", "palette name", true), ("hex", "string", "#RRGGBB", true),
                       ("color_name", "string", "human name for the color", true),
                       ("tag", "string", "optional: 'emissive' or 'glass'", false)),
                a => AddColor(Str(a, "palette"), Str(a, "hex"), Str(a, "color_name"), StrOpt(a, "tag"))),

            // ---- output ------------------------------------------------------
            new("render",
                "Render the model to a PNG image — the see-it/fix-it loop. Views: top/front/side/iso; " +
                "orthographic unless perspective=true; night 0..1 dims matte colors while emissive-tagged colors glow; " +
                "cutaway_above_layer hides everything above that layer.",
                Schema(("name", "string", "model name", true),
                       ("view", "string", "top | front | side | iso (default iso)", false),
                       ("perspective", "boolean", "perspective projection (default false = orthographic)", false),
                       ("night", "number", "0 = day .. 1 = night (default 0)", false),
                       ("cutaway_above_layer", "integer", "hide layers above this index", false),
                       ("size", "integer", "image size in px, 64-1024 (default 512)", false)),
                a => Render(a)),
            new("export_model",
                "Write a clean copy of the model folder (PNGs + manifest) to a destination folder — e.g. a game repo's content tree.",
                Schema(("name", "string", "model name", true),
                       ("destination", "string", "target folder for the copy (created if missing)", true)),
                a => Export(Str(a, "name"), Str(a, "destination"))),
        ];
    }

    public JsonArray ListJson()
    {
        var arr = new JsonArray();
        foreach (var t in _tools)
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Desc,
                ["inputSchema"] = t.Schema.DeepClone(),
            });
        return arr;
    }

    public ToolOut Call(string name, JsonObject args)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == name)
            ?? throw new ToolError($"no tool named '{name}'");
        return tool.Run(args);
    }

    // ---- shared helpers ------------------------------------------------------

    private (string Folder, VoxelModel Model, ManifestDoc Doc) Open(string name)
    {
        var folder = Registry.ResolveName(name)
            ?? throw new ToolError($"no model named '{name}' — use list_models, or create_model to start one");
        var (m, doc) = ModelStore.Load(folder);
        return (folder, m, doc);
    }

    private void SaveBack(string folder, VoxelModel m, ManifestDoc doc)
    {
        if (_snapshotted.Add(folder))
        {
            try { Registry.Snapshot(folder); } catch (IOException) { /* backups are best-effort */ }
        }
        ModelStore.Save(m, doc.Name, folder);
        Registry.Touch(folder, doc.Name);
    }

    private static string Str(JsonObject a, string key)
        => a[key]?.GetValue<string>() ?? throw new ToolError($"missing required argument '{key}'");
    private static string? StrOpt(JsonObject a, string key) => a[key]?.GetValue<string>();
    private static int Int(JsonObject a, string key)
        => a[key] is JsonNode n ? n.GetValue<int>() : throw new ToolError($"missing required argument '{key}'");
    private static int? IntOpt(JsonObject a, string key) => a[key] is JsonNode n ? n.GetValue<int>() : null;
    private static double Num(JsonObject a, string key)
        => a[key] is JsonNode n ? n.GetValue<double>() : throw new ToolError($"missing required argument '{key}'");
    private static double? NumOpt(JsonObject a, string key) => a[key] is JsonNode n ? n.GetValue<double>() : null;
    private static bool BoolOpt(JsonObject a, string key) => a[key] is JsonNode n && n.GetValue<bool>();

    private static void CheckLayer(VoxelModel m, int y, string what)
    {
        if (y < 0 || y >= m.SY)
            throw new ToolError($"{what} {y} is out of range — the model has layers 0..{m.SY - 1}");
    }

    private static byte CheckColor(VoxelModel m, int c, bool allowAir = true)
    {
        int min = allowAir ? 0 : 1;
        if (c < min || c > m.Palette.Colors.Count)
            throw new ToolError($"color {c} is not a slot in palette '{m.Palette.Name}' (valid: {min}..{m.Palette.Colors.Count})");
        return (byte)c;
    }

    private static string Legend(Palette p)
    {
        var sb = new StringBuilder("legend: .=air");
        for (int i = 0; i < p.Colors.Count; i++)
        {
            var c = p.Colors[i];
            sb.Append($"  {TextGrid.CharFor((byte)(i + 1))}={c.Hex} {c.Name}");
            if (c.Tags is { Count: > 0 }) sb.Append($" ({string.Join(",", c.Tags)})");
        }
        return sb.ToString();
    }

    // ---- schema builders -------------------------------------------------------

    private static JsonObject Schema(params (string Name, string Type, string Desc, bool Req)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, desc, req) in props)
        {
            properties[name] = new JsonObject { ["type"] = type, ["description"] = desc };
            if (req) required.Add(name);
        }
        var s = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) s["required"] = required;
        return s;
    }

    private static JsonObject BoxSchema() => Schema(
        ("name", "string", "model name", true),
        ("x0", "integer", "corner x", true), ("y0", "integer", "corner layer", true), ("z0", "integer", "corner z", true),
        ("x1", "integer", "opposite corner x", true), ("y1", "integer", "opposite corner layer", true), ("z1", "integer", "opposite corner z", true),
        ("color", "integer", "palette slot 1-16, or 0 to carve air", true));

    private static JsonObject VoxelsSchema()
    {
        var s = Schema(("name", "string", "model name", true));
        var item = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "integer" },
                ["y"] = new JsonObject { ["type"] = "integer", ["description"] = "layer index" },
                ["z"] = new JsonObject { ["type"] = "integer" },
                ["color"] = new JsonObject { ["type"] = "integer", ["description"] = "palette slot 1-16, 0 erases" },
            },
            ["required"] = new JsonArray { "x", "y", "z", "color" },
        };
        ((JsonObject)s["properties"]!)["voxels"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "voxel edits to apply",
            ["items"] = item,
        };
        ((JsonArray)s["required"]!).Add("voxels");
        return s;
    }

    private static JsonObject PaletteSchema()
    {
        var s = Schema(("name", "string", "palette name", true));
        ((JsonObject)s["properties"]!)["colors"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "up to 16 colors, ordered (slot 1 first)",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["hex"] = new JsonObject { ["type"] = "string", ["description"] = "#RRGGBB" },
                    ["name"] = new JsonObject { ["type"] = "string" },
                    ["tags"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string", ["description"] = "'emissive' or 'glass'" },
                    },
                },
                ["required"] = new JsonArray { "hex", "name" },
            },
        };
        ((JsonArray)s["required"]!).Add("colors");
        return s;
    }
}
