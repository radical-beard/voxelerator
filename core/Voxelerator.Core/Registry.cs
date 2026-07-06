using System.Security.Cryptography;
using System.Text;

namespace Voxelerator.Core;

/// The XDG metadata home. Models live wherever the user says — this directory
/// keeps only the registry: recents, settings, shared palettes, rotating
/// backups, and regenerable thumbnails.
public static class Registry
{
    /// Test hook only — production code never overrides the XDG path.
    public static string? OverrideDataDir;

    public static string DataDir => OverrideDataDir
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "voxelerator");

    public static string PalettesDir => Path.Combine(DataDir, "palettes");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
    public static string ThumbnailsDir => Path.Combine(DataDir, "thumbnails");
    public static string RecentsFile => Path.Combine(DataDir, "recents.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public const int BackupsKept = 3;
    public const int RecentsKept = 100;

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(PalettesDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(ThumbnailsDir);
    }

    // ---- recents -------------------------------------------------------------

    public static RecentsDoc LoadRecents()
    {
        if (!File.Exists(RecentsFile)) return new RecentsDoc();
        try { return VoxJson.Read(File.ReadAllText(RecentsFile), VoxJson.Default.RecentsDoc); }
        catch { return new RecentsDoc(); }
    }

    public static void Touch(string modelPath, string name, DateTimeOffset? now = null)
    {
        EnsureDirs();
        var full = Path.GetFullPath(modelPath);
        var doc = LoadRecents();
        doc.Models.RemoveAll(e => Path.GetFullPath(e.Path) == full);
        doc.Models.Insert(0, new RecentEntryDoc
        {
            Path = full,
            Name = name,
            LastOpened = (now ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        });
        if (doc.Models.Count > RecentsKept) doc.Models.RemoveRange(RecentsKept, doc.Models.Count - RecentsKept);
        ModelStore.WriteAtomic(RecentsFile, Encoding.UTF8.GetBytes(VoxJson.Write(doc, VoxJson.Default.RecentsDoc)));
    }

    public static void Forget(string modelPath)
    {
        var full = Path.GetFullPath(modelPath);
        var doc = LoadRecents();
        if (doc.Models.RemoveAll(e => Path.GetFullPath(e.Path) == full) > 0)
            ModelStore.WriteAtomic(RecentsFile, Encoding.UTF8.GetBytes(VoxJson.Write(doc, VoxJson.Default.RecentsDoc)));
    }

    /// Resolve a registered model name to its folder (most recently opened
    /// wins on duplicates) — the MCP server's name→path mapping.
    public static string? ResolveName(string name)
    {
        foreach (var e in LoadRecents().Models)
            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(e.Path))
                return e.Path;
        return null;
    }

    // ---- settings ------------------------------------------------------------

    public static SettingsDoc LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return new SettingsDoc();
        try { return VoxJson.Read(File.ReadAllText(SettingsFile), VoxJson.Default.SettingsDoc); }
        catch { return new SettingsDoc(); }
    }

    public static void SaveSettings(SettingsDoc doc)
    {
        EnsureDirs();
        ModelStore.WriteAtomic(SettingsFile, Encoding.UTF8.GetBytes(VoxJson.Write(doc, VoxJson.Default.SettingsDoc)));
    }

    /// Where "New model" saves by default when the user hasn't picked yet.
    public static string DefaultNewModelDir()
    {
        var s = LoadSettings();
        if (!string.IsNullOrEmpty(s.DefaultNewModelDir)) return s.DefaultNewModelDir!;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "voxelerator");
    }

    // ---- palettes ------------------------------------------------------------

    public static void SeedBuiltinPalettes()
    {
        EnsureDirs();
        foreach (var p in BuiltinPalettes.All())
        {
            var path = PalettePath(p.Name);
            if (!File.Exists(path)) SavePalette(p);
        }
    }

    public static string PalettePath(string name) => Path.Combine(PalettesDir, name + ".json");

    public static List<Palette> LoadPalettes()
    {
        EnsureDirs();
        var list = new List<Palette>();
        foreach (var f in Directory.GetFiles(PalettesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try { list.Add(VoxJson.Read(File.ReadAllText(f), VoxJson.Default.PaletteDoc).ToPalette()); }
            catch { /* skip unreadable palette files; they stay on disk for the user to fix */ }
        }
        return list;
    }

    public static Palette? LoadPalette(string name)
    {
        var path = PalettePath(name);
        if (!File.Exists(path)) return null;
        return VoxJson.Read(File.ReadAllText(path), VoxJson.Default.PaletteDoc).ToPalette();
    }

    public static void SavePalette(Palette p)
    {
        EnsureDirs();
        var problems = p.Validate();
        if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
        ModelStore.WriteAtomic(PalettePath(p.Name),
            Encoding.UTF8.GetBytes(VoxJson.Write(PaletteDoc.From(p), VoxJson.Default.PaletteDoc)));
    }

    // ---- backups & thumbnails --------------------------------------------------

    /// Stable 8-hex id for a model folder path.
    public static string IdFor(string modelPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(modelPath)));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// Copies the model folder into backups/<id>/<stamp>/, pruning to the
    /// newest BackupsKept snapshots.
    public static string Snapshot(string modelPath, DateTimeOffset? now = null)
    {
        EnsureDirs();
        var dir = Path.Combine(BackupsDir, IdFor(modelPath));
        Directory.CreateDirectory(dir);
        var stamp = (now ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(dir, stamp);
        for (int suffix = 1; Directory.Exists(dest); suffix++) dest = Path.Combine(dir, $"{stamp}-{suffix}");
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(modelPath))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));

        var snaps = Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal).ToList();
        while (snaps.Count > BackupsKept)
        {
            Directory.Delete(snaps[0], recursive: true);
            snaps.RemoveAt(0);
        }
        return dest;
    }

    public static string ThumbnailPath(string modelPath) => Path.Combine(ThumbnailsDir, IdFor(modelPath) + ".png");
}
