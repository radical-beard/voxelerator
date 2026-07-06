using System.Globalization;

namespace Voxelerator.Core;

public sealed class ModelFormatException(string message) : FormatException(message);

/// Reads and writes the on-disk contract: a folder of 0001.png … NNNN.png
/// (0001 = layer 0, the ground slice) plus model.json. Export IS save — the
/// format has no second representation.
public static class ModelStore
{
    public const int FormatVersion = 1;
    public const string ManifestFile = "model.json";

    public static string LayerFile(int layerIndex) =>
        (layerIndex + 1).ToString("D4", CultureInfo.InvariantCulture) + ".png";

    // ---- save ---------------------------------------------------------------

    /// Writes the folder atomically (per file: tmp + rename), deletes stale
    /// layer files beyond the model height, preserves `created` from any
    /// existing manifest. PNG bytes are deterministic; only the manifest's
    /// `updated` field changes between identical saves.
    public static ManifestDoc Save(VoxelModel m, string name, string folder, DateTimeOffset? now = null)
    {
        Directory.CreateDirectory(folder);
        var stamp = (now ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        string created = stamp;
        var manifestPath = Path.Combine(folder, ManifestFile);
        if (File.Exists(manifestPath))
        {
            try { created = VoxJson.Read(File.ReadAllText(manifestPath), VoxJson.Default.ManifestDoc).Created; }
            catch { /* unreadable manifest: treat as new */ }
        }

        var rgb = m.Palette.Colors.Select(c => c.RgbBytes()).ToList();
        for (int y = 0; y < m.SY; y++)
        {
            var png = Png.WriteIndexed(m.SX, m.SZ, m.Layer(y), rgb);
            WriteAtomic(Path.Combine(folder, LayerFile(y)), png);
        }

        // delete stale layers from a previously-taller model
        for (int y = m.SY; ; y++)
        {
            var stale = Path.Combine(folder, LayerFile(y));
            if (!File.Exists(stale)) break;
            File.Delete(stale);
        }

        var doc = new ManifestDoc
        {
            Format = FormatVersion,
            Name = name,
            Size = new SizeDoc { W = m.SX, D = m.SZ, H = m.SY },
            Palette = m.Palette.Name,
            Created = created,
            Updated = stamp,
            EmbeddedPalette = PaletteDoc.From(m.Palette),
        };
        WriteAtomic(manifestPath, System.Text.Encoding.UTF8.GetBytes(VoxJson.Write(doc, VoxJson.Default.ManifestDoc)));
        return doc;
    }

    // ---- load ---------------------------------------------------------------

    public static (VoxelModel Model, ManifestDoc Manifest) Load(string folder)
    {
        var manifestPath = Path.Combine(folder, ManifestFile);
        if (!File.Exists(manifestPath))
            return LoadInferred(folder);

        var doc = VoxJson.Read(File.ReadAllText(manifestPath), VoxJson.Default.ManifestDoc);
        if (doc.Format != FormatVersion)
            throw new ModelFormatException($"unknown model format {doc.Format} (this build reads format {FormatVersion})");

        var palette = doc.EmbeddedPalette.Colors.Count > 0
            ? doc.EmbeddedPalette.ToPalette()
            : throw new ModelFormatException("manifest has no embedded palette");

        int w = doc.Size.W, d = doc.Size.D, h = doc.Size.H;
        var m = new VoxelModel(w, h, d, palette);
        for (int y = 0; y < h; y++)
        {
            var file = Path.Combine(folder, LayerFile(y));
            if (!File.Exists(file))
                throw new ModelFormatException($"missing layer file {LayerFile(y)} (manifest says {h} layers)");
            ReadLayerInto(m, y, file, palette);
        }
        if (File.Exists(Path.Combine(folder, LayerFile(h))))
            throw new ModelFormatException($"extra layer file {LayerFile(h)} beyond the manifest height {h}");
        return (m, doc);
    }

    /// Fallback for manifest-less folders (dropped in from other tools):
    /// infer dimensions from the files and build a palette from the distinct
    /// colors found (must be ≤ 16).
    private static (VoxelModel, ManifestDoc) LoadInferred(string folder)
    {
        var files = LayerFiles(folder);
        if (files.Count == 0)
            throw new ModelFormatException($"'{folder}' has no {ManifestFile} and no NNNN.png layer files");

        var decoded = files.Select(f => Png.ReadRgba(File.ReadAllBytes(f))).ToList();
        int w = decoded[0].W, d = decoded[0].H;

        var palette = new Palette(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) + "-imported");
        var m = new VoxelModel(w, decoded.Count, d, palette);
        for (int y = 0; y < decoded.Count; y++)
        {
            var (lw, lh, rgba) = decoded[y];
            if (lw != w || lh != d)
                throw new ModelFormatException($"layer {y} is {lw}x{lh}, expected {w}x{d}");
            for (int i = 0; i < w * d; i++)
            {
                byte a = rgba[i * 4 + 3];
                if (a == 0) continue;
                if (a != 255)
                    throw new ModelFormatException($"layer {y} has partial alpha {a} at cell {i % w},{i / w} — alpha must be 0 or 255");
                byte r = rgba[i * 4], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2];
                var idx = palette.CellIndexOf(r, g, b);
                if (idx is null)
                {
                    if (palette.Colors.Count >= Palette.MaxColors)
                        throw new ModelFormatException($"folder uses more than {Palette.MaxColors} distinct colors — too many for a palette");
                    palette.Colors.Add(new PaletteColor(PaletteColor.ToHex(r, g, b), $"c{palette.Colors.Count + 1}"));
                    idx = (byte)palette.Colors.Count;
                }
                m.Set(i % w, y, i / w, idx.Value);
            }
        }
        var doc = new ManifestDoc
        {
            Format = FormatVersion,
            Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)),
            Size = new SizeDoc { W = w, D = d, H = decoded.Count },
            Palette = palette.Name,
            EmbeddedPalette = PaletteDoc.From(palette),
        };
        return (m, doc);
    }

    private static void ReadLayerInto(VoxelModel m, int y, string file, Palette palette)
    {
        var (w, h, rgba) = Png.ReadRgba(File.ReadAllBytes(file));
        if (w != m.SX || h != m.SZ)
            throw new ModelFormatException($"{Path.GetFileName(file)} is {w}x{h}, manifest says {m.SX}x{m.SZ}");
        for (int i = 0; i < w * h; i++)
        {
            byte a = rgba[i * 4 + 3];
            if (a == 0) continue;
            if (a != 255)
                throw new ModelFormatException($"{Path.GetFileName(file)}: partial alpha {a} at ({i % w},{i / w}) — alpha must be 0 or 255");
            byte r = rgba[i * 4], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2];
            byte idx = palette.CellIndexOf(r, g, b)
                ?? throw new ModelFormatException(
                    $"{Path.GetFileName(file)}: color {PaletteColor.ToHex(r, g, b)} at ({i % w},{i / w}) is not in palette '{palette.Name}' — the palette is law");
            m.Set(i % w, y, i / w, idx);
        }
    }

    // ---- validation ----------------------------------------------------------

    /// Non-throwing check of every format invariant; empty list = valid.
    public static List<string> Validate(string folder)
    {
        var problems = new List<string>();
        try
        {
            var (m, doc) = Load(folder);
            problems.AddRange(m.Palette.Validate());
            var files = LayerFiles(folder);
            if (files.Count != doc.Size.H)
                problems.Add($"manifest says {doc.Size.H} layers but folder has {files.Count}");
        }
        catch (Exception e) when (e is ModelFormatException or FormatException or IOException)
        {
            problems.Add(e.Message);
        }
        return problems;
    }

    /// Contiguous layer files 0001.png.. present in the folder, in order.
    public static List<string> LayerFiles(string folder)
    {
        var files = new List<string>();
        for (int y = 0; ; y++)
        {
            var f = Path.Combine(folder, LayerFile(y));
            if (!File.Exists(f)) break;
            files.Add(f);
        }
        return files;
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
