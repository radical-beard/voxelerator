using System.Globalization;

namespace Voxelerator.Core;

/// Semantic color tags. Tags live in the palette, never in the pixels, so the
/// PNG layers stay plain images.
public static class ColorTags
{
    /// Rendered glowing at night (maps to Rgba.A = 1 in NT's renderer).
    public const string Emissive = "emissive";
    /// Rendered as translucent glass material.
    public const string Glass = "glass";
    public const float GlassAlpha = 0.30f;

    public static readonly string[] All = [Emissive, Glass];
}

public sealed record PaletteColor(string Hex, string Name, IReadOnlyList<string>? Tags = null)
{
    public bool Has(string tag) => Tags is not null && Tags.Contains(tag);

    public (byte R, byte G, byte B) RgbBytes()
    {
        var (r, g, b) = ParseHex(Hex);
        return (r, g, b);
    }

    public Rgba ToRgba()
    {
        var (r, g, b) = RgbBytes();
        float a = Has(ColorTags.Emissive) ? 1f : Has(ColorTags.Glass) ? ColorTags.GlassAlpha : 0f;
        return new Rgba(r / 255f, g / 255f, b / 255f, a);
    }

    public static (byte, byte, byte) ParseHex(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
            throw new FormatException($"palette color must be #RRGGBB, got '{hex}'");
        byte P(int i) => byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (P(1), P(3), P(5));
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}

/// A named, ordered list of at most 16 colors. Cell value i (1-based) maps to
/// Colors[i-1]; cell 0 is air. Order matters: index = hotkey slot = text-grid
/// character.
public sealed class Palette
{
    public const int MaxColors = 16;

    public string Name;
    public readonly List<PaletteColor> Colors = new();

    public Palette(string name) { Name = name; }

    public Palette(string name, IEnumerable<PaletteColor> colors) : this(name)
    {
        Colors.AddRange(colors);
        var problems = Validate();
        if (problems.Count > 0)
            throw new ArgumentException($"invalid palette '{name}': {string.Join("; ", problems)}");
    }

    public Rgba RgbaOf(byte cellIdx) =>
        cellIdx >= 1 && cellIdx <= Colors.Count ? Colors[cellIdx - 1].ToRgba() : default;

    /// Exact-RGB lookup → 1-based cell index, or null when off-palette.
    public byte? CellIndexOf(byte r, byte g, byte b)
    {
        for (int i = 0; i < Colors.Count; i++)
        {
            var (cr, cg, cb) = Colors[i].RgbBytes();
            if (cr == r && cg == g && cb == b) return (byte)(i + 1);
        }
        return null;
    }

    public List<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) problems.Add("palette needs a name");
        if (Colors.Count == 0) problems.Add("palette needs at least one color");
        if (Colors.Count > MaxColors) problems.Add($"palette has {Colors.Count} colors; the cap is {MaxColors}");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Colors)
        {
            try { PaletteColor.ParseHex(c.Hex); }
            catch (FormatException e) { problems.Add(e.Message); continue; }
            if (!seen.Add(c.Hex)) problems.Add($"duplicate color {c.Hex} (exact-match import needs unique RGB values)");
            foreach (var t in c.Tags ?? [])
                if (!ColorTags.All.Contains(t)) problems.Add($"unknown tag '{t}' on {c.Hex}");
        }
        return problems;
    }

    public Palette Clone(string? newName = null)
    {
        var p = new Palette(newName ?? Name);
        p.Colors.AddRange(Colors);
        return p;
    }
}
