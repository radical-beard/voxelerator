using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxelerator.Core;

// On-disk JSON documents. Source-generated (de)serialization keeps the core
// AOT/trim clean; camelCase + indented writes; property order is declaration
// order, so re-saves are byte-stable.

public sealed class PaletteColorDoc
{
    public string Hex { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string>? Tags { get; set; }
}

public sealed class PaletteDoc
{
    public string Name { get; set; } = "";
    public List<PaletteColorDoc> Colors { get; set; } = new();

    public static PaletteDoc From(Palette p) => new()
    {
        Name = p.Name,
        Colors = p.Colors.Select(c => new PaletteColorDoc
        {
            Hex = c.Hex,
            Name = c.Name,
            Tags = c.Tags is { Count: > 0 } t ? t.ToList() : null,
        }).ToList(),
    };

    public Palette ToPalette() => new(Name,
        Colors.Select(c => new PaletteColor(c.Hex, c.Name, c.Tags is { Count: > 0 } t ? t : null)));
}

public sealed class SizeDoc
{
    public int W { get; set; }
    public int D { get; set; }
    public int H { get; set; }
}

/// model.json — makes an exported folder self-describing. `format` is the
/// contract version; readers must reject formats they don't know.
public sealed class ManifestDoc
{
    public int Format { get; set; } = 1;
    public string Name { get; set; } = "";
    public SizeDoc Size { get; set; } = new();
    public string Palette { get; set; } = "";
    public string Created { get; set; } = "";
    public string Updated { get; set; } = "";
    public PaletteDoc EmbeddedPalette { get; set; } = new();
}

public sealed class RecentEntryDoc
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string LastOpened { get; set; } = "";
}

public sealed class RecentsDoc
{
    public List<RecentEntryDoc> Models { get; set; } = new();
}

public sealed class SettingsDoc
{
    public string? DefaultNewModelDir { get; set; }
    public string? LastPalette { get; set; }
    public float OnionOpacity { get; set; } = 0.25f;
    public bool ShowGrid { get; set; } = true;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PaletteDoc))]
[JsonSerializable(typeof(PaletteColorDoc))]
[JsonSerializable(typeof(List<PaletteColorDoc>))]
[JsonSerializable(typeof(ManifestDoc))]
[JsonSerializable(typeof(SizeDoc))]
[JsonSerializable(typeof(RecentsDoc))]
[JsonSerializable(typeof(RecentEntryDoc))]
[JsonSerializable(typeof(List<RecentEntryDoc>))]
[JsonSerializable(typeof(SettingsDoc))]
[JsonSerializable(typeof(List<string>))]
public partial class VoxJsonContext : JsonSerializerContext;

/// Read/write helpers over the source-generated context.
public static class VoxJson
{
    public static VoxJsonContext Default => VoxJsonContext.Default;

    public static string Write<T>(T doc, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
        => JsonSerializer.Serialize(doc, ti) + "\n";

    public static T Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
        => JsonSerializer.Deserialize(json, ti) ?? throw new FormatException("empty JSON document");
}
