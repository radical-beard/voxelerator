namespace Voxelerator.Core;

/// Palettes seeded from Neo Terrestria's Palette.cs — the game's "color
/// language in one place", split in two to respect the 16-color cap — plus a
/// neutral starter. Hex values are the game's float constants × 255.
public static class BuiltinPalettes
{
    public static Palette NeoTerrestria() => new("neo-terrestria",
    [
        new("#C7BDA8", "wall"),
        new("#948C80", "wall-dark"),
        new("#B85C47", "roof-red"),
        new("#5C6675", "roof-slate"),
        new("#FFE08C", "window", [ColorTags.Emissive]),
        new("#B3BAC7", "metal-light"),
        new("#6B7380", "metal-dark"),
        new("#8C6B4D", "pipe"),
        new("#73AD4D", "crop-green"),
        new("#CCB352", "crop-gold"),
        new("#7A5C3D", "fence-wood"),
        new("#5C8CBF", "sci-blue"),
        new("#73D9F2", "sci-glow", [ColorTags.Emissive]),
        new("#D9D6CC", "civic-white"),
        new("#4D9E94", "terra-teal"),
        new("#9ECCE0", "glass", [ColorTags.Glass]),
    ]);

    public static Palette NeoTerrestriaDecor() => new("neo-terrestria-decor",
    [
        new("#999EA8", "space-gray"),
        new("#4D4D54", "pad-dark"),
        new("#E6E6EB", "rocket-white"),
        new("#BF4D40", "rocket-fin"),
        new("#6B4270", "flesh-purple"),
        new("#B359D9", "ichor-glow", [ColorTags.Emissive]),
        new("#B39461", "scaffold"),
        new("#F2E0C7", "people-cube"),
        new("#5C422E", "tree-trunk"),
        new("#387038", "tree-leaf"),
        new("#807873", "boulder"),
        new("#B88C4D", "ore-fleck"),
        new("#CCE0F5", "ice-chunk"),
        new("#8CBFC7", "geyser-nub"),
        new("#A8997A", "ruin-slab"),
        new("#8C59BF", "gem-glint", [ColorTags.Emissive]),
    ]);

    /// Neutral starter: a value ramp plus a few useful hues.
    public static Palette Primer() => new("primer",
    [
        new("#1A1A1F", "ink"),
        new("#3D3D46", "charcoal"),
        new("#6E6E78", "slate"),
        new("#A3A3AD", "silver"),
        new("#E8E8EC", "paper"),
        new("#B85C47", "brick"),
        new("#CCA83D", "amber", [ColorTags.Emissive]),
        new("#4F8A4C", "moss"),
        new("#3E7DA6", "sea"),
        new("#7C4DBF", "violet"),
        new("#8C5C3D", "timber"),
        new("#9ECCE0", "pane", [ColorTags.Glass]),
    ]);

    public static IEnumerable<Palette> All() => [NeoTerrestria(), NeoTerrestriaDecor(), Primer()];
}
