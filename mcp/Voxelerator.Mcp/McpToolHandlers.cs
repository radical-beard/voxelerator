using System.Text;
using System.Text.Json.Nodes;
using Voxelerator.Core;

namespace Voxelerator.Mcp;

public sealed partial class McpTools
{
    // ---- inspect --------------------------------------------------------------

    private ToolOut ListModels()
    {
        var recents = Registry.LoadRecents().Models;
        if (recents.Count == 0)
            return new ToolOut("no models registered yet — create_model starts one, or open a folder in the editor");
        var sb = new StringBuilder($"{recents.Count} model(s), most recent first:\n");
        foreach (var e in recents)
        {
            bool ok = Directory.Exists(e.Path);
            sb.Append($"- {e.Name}  [{(ok ? "ok" : "MISSING")}]  {e.Path}\n");
        }
        return new ToolOut(sb.ToString().TrimEnd());
    }

    private ToolOut ModelInfo(string name)
    {
        var (folder, m, doc) = Open(name);
        var s = Core.Stats.Compute(m);
        var sb = new StringBuilder();
        sb.Append($"{doc.Name}: {s.W}x{s.D} footprint, {s.H} layers (w x d x h)\n");
        sb.Append($"folder: {folder}\n");
        sb.Append($"palette: {m.Palette.Name} ({m.Palette.Colors.Count} colors, {s.ColorsUsed} used)\n");
        sb.Append($"filled voxels: {s.Filled}   triangles after greedy meshing: {s.Triangles}\n");
        if (s.Bounds is { } b)
            sb.Append($"content bounds: ({b.X0},{b.Y0},{b.Z0})..({b.X1},{b.Y1},{b.Z1})\n");
        else
            sb.Append("content bounds: empty model\n");
        for (int i = 1; i <= m.Palette.Colors.Count; i++)
            if (s.PerColor[i] > 0)
                sb.Append($"  {TextGrid.CharFor((byte)i)} {m.Palette.Colors[i - 1].Name}: {s.PerColor[i]} voxels\n");
        sb.Append(Legend(m.Palette));
        return new ToolOut(sb.ToString());
    }

    private ToolOut GetLayers(string name, int from, int to)
    {
        var (_, m, _) = Open(name);
        CheckLayer(m, from, "layer");
        CheckLayer(m, to, "layer");
        if (to < from) (from, to) = (to, from);
        if (to - from > 63) throw new ToolError("asking for more than 64 layers at once — narrow the range");
        var sb = new StringBuilder();
        for (int y = from; y <= to; y++)
        {
            sb.Append($"layer {y}:\n{TextGrid.EncodeLayer(m, y)}\n");
            if (y < to) sb.Append('\n');
        }
        sb.Append(Legend(m.Palette));
        return new ToolOut(sb.ToString());
    }

    private ToolOut Validate(string name)
    {
        var folder = Registry.ResolveName(name)
            ?? throw new ToolError($"no model named '{name}'");
        var problems = ModelStore.Validate(folder);
        return new ToolOut(problems.Count == 0
            ? $"valid — every format invariant holds for {folder}"
            : "problems:\n" + string.Join("\n", problems.Select(p => "- " + p)));
    }

    // ---- edit -------------------------------------------------------------------

    private ToolOut SetLayer(string name, int layer, string grid)
    {
        var (folder, m, doc) = Open(name);
        CheckLayer(m, layer, "layer");
        var (w, d, cells) = TextGrid.Decode(grid);
        if (w != m.SX || d != m.SZ)
            throw new ToolError($"grid is {w}x{d} but the model footprint is {m.SX}x{m.SZ}");
        foreach (var c in cells)
            if (c > m.Palette.Colors.Count)
                throw new ToolError($"grid uses slot {c} but palette '{m.Palette.Name}' has only {m.Palette.Colors.Count} colors");
        EditOps.PasteLayer(m, layer, cells);
        SaveBack(folder, m, doc);
        return new ToolOut($"layer {layer} replaced ({m.Filled()} voxels filled model-wide)");
    }

    private ToolOut SetVoxels(string name, JsonArray voxels)
    {
        var (folder, m, doc) = Open(name);
        int applied = 0, skipped = 0;
        foreach (var v in voxels)
        {
            if (v is not JsonObject o) { skipped++; continue; }
            int x = o["x"]?.GetValue<int>() ?? int.MinValue;
            int y = o["y"]?.GetValue<int>() ?? int.MinValue;
            int z = o["z"]?.GetValue<int>() ?? int.MinValue;
            int c = o["color"]?.GetValue<int>() ?? int.MinValue;
            if (x < 0 || y < 0 || z < 0 || x >= m.SX || y >= m.SY || z >= m.SZ ||
                c < 0 || c > m.Palette.Colors.Count) { skipped++; continue; }
            m.Set(x, y, z, (byte)c);
            applied++;
        }
        if (applied > 0) SaveBack(folder, m, doc);
        var note = skipped > 0 ? $" ({skipped} out-of-bounds/invalid entries skipped)" : "";
        return new ToolOut($"applied {applied} voxel edit(s){note}");
    }

    private ToolOut Box(JsonObject a, bool hollow)
    {
        var (folder, m, doc) = Open(Str(a, "name"));
        byte color = CheckColor(m, Int(a, "color"));
        if (hollow)
            EditOps.HollowBox(m, Int(a, "x0"), Int(a, "y0"), Int(a, "z0"), Int(a, "x1"), Int(a, "y1"), Int(a, "z1"), color);
        else
            EditOps.FillBox(m, Int(a, "x0"), Int(a, "y0"), Int(a, "z0"), Int(a, "x1"), Int(a, "y1"), Int(a, "z1"), color);
        SaveBack(folder, m, doc);
        return new ToolOut($"{(hollow ? "hollow box" : "box")} applied ({m.Filled()} voxels filled model-wide)");
    }

    private ToolOut Cylinder(JsonObject a)
    {
        var (folder, m, doc) = Open(Str(a, "name"));
        byte color = CheckColor(m, Int(a, "color"));
        EditOps.FillCylinder(m, Num(a, "cx"), Num(a, "cz"), Int(a, "y0"), Int(a, "y1"), Num(a, "radius"), color);
        SaveBack(folder, m, doc);
        return new ToolOut($"cylinder applied ({m.Filled()} voxels filled model-wide)");
    }

    private ToolOut CopyLayer(string name, int from, int to)
    {
        var (folder, m, doc) = Open(name);
        CheckLayer(m, from, "source layer");
        CheckLayer(m, to, "destination layer");
        EditOps.PasteLayer(m, to, EditOps.CopyLayer(m, from));
        SaveBack(folder, m, doc);
        return new ToolOut($"layer {from} copied onto layer {to}");
    }

    private ToolOut InsertLayer(string name, int at, int? copyFrom)
    {
        var (folder, m, doc) = Open(name);
        if (at < 0 || at > m.SY) throw new ToolError($"insert position {at} out of range 0..{m.SY}");
        byte[]? content = null;
        if (copyFrom is int cf)
        {
            CheckLayer(m, cf, "copy_from layer");
            content = EditOps.CopyLayer(m, cf);
        }
        var grown = EditOps.InsertLayer(m, at, content);
        SaveBack(folder, grown, doc);
        return new ToolOut($"layer inserted at {at}; model is now {grown.SY} layers tall");
    }

    private ToolOut DeleteLayer(string name, int at)
    {
        var (folder, m, doc) = Open(name);
        CheckLayer(m, at, "layer");
        var shrunk = EditOps.DeleteLayer(m, at);
        SaveBack(folder, shrunk, doc);
        return new ToolOut($"layer {at} deleted; model is now {shrunk.SY} layers tall");
    }

    private ToolOut ReplaceColor(string name, byte from, byte to)
    {
        var (folder, m, doc) = Open(name);
        CheckColor(m, from, allowAir: false);
        CheckColor(m, to);
        EditOps.ReplaceColor(m, from, to);
        SaveBack(folder, m, doc);
        return new ToolOut($"every slot-{from} voxel is now slot {to}");
    }

    private ToolOut Mirror(string name, string axis)
    {
        var (folder, m, doc) = Open(name);
        bool alongX = axis.ToLowerInvariant() switch
        {
            "x" => true,
            "z" => false,
            _ => throw new ToolError("axis must be \"x\" or \"z\""),
        };
        EditOps.MirrorModel(m, alongX);
        SaveBack(folder, m, doc);
        return new ToolOut($"model mirrored along {axis.ToLowerInvariant()}");
    }

    // ---- create -------------------------------------------------------------------

    private ToolOut CreateModel(JsonObject a)
    {
        string name = Str(a, "name");
        int w = Int(a, "width"), d = Int(a, "depth"), h = Int(a, "height");
        if (w < 1 || d < 1 || h < 1) throw new ToolError("dimensions must be at least 1");
        string paletteName = Str(a, "palette");
        var palette = Registry.LoadPalette(paletteName)
            ?? throw new ToolError($"no palette named '{paletteName}' — see list_palettes");

        string parent = StrOpt(a, "directory") ?? Registry.DefaultNewModelDir();
        string folder = Path.Combine(parent, name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new ToolError($"'{folder}' already exists and is not empty — pick another name or directory");

        var m = new VoxelModel(w, h, d, palette);
        ModelStore.Save(m, name, folder);
        Registry.Touch(folder, name);
        return new ToolOut($"created '{name}' ({w}x{d}, {h} layers, palette {paletteName}) at {folder}\n{Legend(palette)}");
    }

    private ToolOut DuplicateModel(string name, string newName, string? directory)
    {
        var (srcFolder, m, _) = Open(name);
        string parent = directory ?? Path.GetDirectoryName(srcFolder)!;
        string folder = Path.Combine(parent, newName);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new ToolError($"'{folder}' already exists and is not empty");
        ModelStore.Save(m, newName, folder);
        Registry.Touch(folder, newName);
        return new ToolOut($"duplicated '{name}' → '{newName}' at {folder}");
    }

    private ToolOut ListPalettes()
    {
        var all = Registry.LoadPalettes();
        var sb = new StringBuilder($"{all.Count} palette(s):\n");
        foreach (var p in all)
        {
            var tagged = p.Colors.Count(c => c.Tags is { Count: > 0 });
            sb.Append($"- {p.Name}: {p.Colors.Count} colors{(tagged > 0 ? $" ({tagged} tagged)" : "")}\n");
        }
        return new ToolOut(sb.ToString().TrimEnd());
    }

    private ToolOut GetPalette(string name)
    {
        var p = Registry.LoadPalette(name) ?? throw new ToolError($"no palette named '{name}'");
        var sb = new StringBuilder($"palette {p.Name} ({p.Colors.Count}/{Palette.MaxColors} colors):\n");
        for (int i = 0; i < p.Colors.Count; i++)
        {
            var c = p.Colors[i];
            sb.Append($"  slot {i + 1} ('{TextGrid.CharFor((byte)(i + 1))}'): {c.Hex} {c.Name}");
            if (c.Tags is { Count: > 0 }) sb.Append($" [{string.Join(",", c.Tags)}]");
            sb.Append('\n');
        }
        return new ToolOut(sb.ToString().TrimEnd());
    }

    private ToolOut CreatePalette(JsonObject a)
    {
        string name = Str(a, "name");
        if (Registry.LoadPalette(name) is not null)
            throw new ToolError($"palette '{name}' already exists — palettes are shared, edit via add_color or pick a new name");
        var colorsArr = a["colors"] as JsonArray ?? throw new ToolError("create_palette needs a 'colors' array");
        var colors = new List<PaletteColor>();
        foreach (var c in colorsArr)
        {
            if (c is not JsonObject o) throw new ToolError("each color must be an object with hex + name");
            var tags = (o["tags"] as JsonArray)?.Select(t => t!.GetValue<string>()).ToList();
            colors.Add(new PaletteColor(
                o["hex"]?.GetValue<string>() ?? throw new ToolError("color missing 'hex'"),
                o["name"]?.GetValue<string>() ?? throw new ToolError("color missing 'name'"),
                tags is { Count: > 0 } ? tags : null));
        }
        var p = new Palette(name, colors);
        Registry.SavePalette(p);
        return new ToolOut($"palette '{name}' created with {p.Colors.Count} colors\n{Legend(p)}");
    }

    private ToolOut AddColor(string paletteName, string hex, string colorName, string? tag)
    {
        var p = Registry.LoadPalette(paletteName) ?? throw new ToolError($"no palette named '{paletteName}'");
        if (p.Colors.Count >= Palette.MaxColors)
            throw new ToolError($"palette '{paletteName}' already has {Palette.MaxColors} colors — the cap is the point");
        p.Colors.Add(new PaletteColor(hex, colorName, tag is null ? null : [tag]));
        var problems = p.Validate();
        if (problems.Count > 0) throw new ToolError(string.Join("; ", problems));
        Registry.SavePalette(p);
        return new ToolOut($"added {hex} '{colorName}' as slot {p.Colors.Count} of '{paletteName}'");
    }

    // ---- output -------------------------------------------------------------------

    private ToolOut Render(JsonObject a)
    {
        var (_, m, doc) = Open(Str(a, "name"));
        var view = (StrOpt(a, "view") ?? "iso").ToLowerInvariant() switch
        {
            "top" => RenderView.Top,
            "front" => RenderView.Front,
            "side" => RenderView.Side,
            "iso" => RenderView.Iso,
            var v => throw new ToolError($"unknown view '{v}' (top | front | side | iso)"),
        };
        int size = Math.Clamp(IntOpt(a, "size") ?? 512, 64, 1024);
        int? cutaway = IntOpt(a, "cutaway_above_layer");
        if (cutaway is int cw) CheckLayer(m, cw, "cutaway_above_layer");
        var opts = new RenderOptions
        {
            Size = size,
            View = view,
            Perspective = BoolOpt(a, "perspective"),
            Night = (float)Math.Clamp(NumOpt(a, "night") ?? 0, 0, 1),
            CutawayAboveLayer = cutaway,
        };
        var png = SoftwareRenderer.RenderPng(m, opts);
        var desc = $"{doc.Name}: {view} view, {(opts.Perspective ? "perspective" : "orthographic")}" +
                   (opts.Night > 0 ? $", night {opts.Night:0.##}" : "") +
                   (cutaway is int c2 ? $", cutaway above layer {c2}" : "");
        return new ToolOut(desc, png);
    }

    private ToolOut Export(string name, string destination)
    {
        var (_, m, doc) = Open(name);
        ModelStore.Save(m, doc.Name, destination);
        return new ToolOut($"exported '{doc.Name}' to {destination} ({m.SY} layer PNGs + model.json)");
    }
}
