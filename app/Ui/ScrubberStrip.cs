using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// Vertical layer scrubber: every slice at a glance, top layer at the top,
/// empty layers dimmed, active layer ringed purple. Click to jump.
public partial class ScrubberStrip : ScrollContainer
{
    private readonly EditorState _s;
    private readonly VBoxContainer _col = new();
    private bool _dirty = true;
    private double _sinceRefresh;

    public ScrubberStrip(EditorState state)
    {
        _s = state;
        HorizontalScrollMode = ScrollMode.Disabled;
        CustomMinimumSize = new Vector2(64, 0);
        _s.Changed += () => _dirty = true;
        _s.StructureChanged += () => _dirty = true;
    }

    public override void _Ready()
    {
        _col.AddThemeConstantOverride("separation", 4);
        _col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_col);
        Rebuild();
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (_dirty && _sinceRefresh > 0.15)
        {
            _dirty = false;
            _sinceRefresh = 0;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        foreach (var c in _col.GetChildren()) c.QueueFree();
        var m = _s.Model;
        for (int y = m.SY - 1; y >= 0; y--)
        {
            int layer = y;
            var span = m.Layer(y);
            bool empty = true;
            foreach (var b in span) if (b != 0) { empty = false; break; }

            var btn = new Button
            {
                CustomMinimumSize = new Vector2(56, 36),
                TooltipText = $"layer {layer}" + (empty ? " (empty)" : ""),
            };
            bool active = layer == _s.Layer;
            btn.AddThemeStyleboxOverride("normal",
                Ui.Flat(active ? Ui.PrimaryDim : Ui.Bg2, active ? Ui.Primary : Ui.StrokeSoft, Ui.RadiusS, 2));
            btn.AddThemeStyleboxOverride("hover", Ui.Flat(Ui.Bg3, Ui.Stroke, Ui.RadiusS, 2));
            btn.AddThemeStyleboxOverride("pressed", Ui.Flat(Ui.PrimaryDim, Ui.Primary, Ui.RadiusS, 2));

            var inner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            inner.SetAnchorsPreset(LayoutPreset.FullRect);
            if (!empty)
            {
                var tex = new TextureRect
                {
                    Texture = LayerTexture(m, layer),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(0, 22),
                    TextureFilter = TextureFilterEnum.Nearest,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Modulate = active ? Colors.White : new Color(1, 1, 1, 0.75f),
                };
                inner.AddChild(tex);
            }
            var lbl = new Label
            {
                Text = layer.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lbl.AddThemeFontSizeOverride("font_size", 10);
            lbl.AddThemeColorOverride("font_color",
                active ? Ui.Glow : empty ? Ui.TextFaint : Ui.TextDim);
            inner.AddChild(lbl);
            btn.AddChild(inner);

            btn.Pressed += () => _s.SetLayer(layer);
            _col.AddChild(btn);
        }
    }

    private static ImageTexture LayerTexture(VoxelModel m, int y)
    {
        var img = Godot.Image.CreateEmpty(m.SX, m.SZ, false, Godot.Image.Format.Rgba8);
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
            {
                byte v = m.At(x, y, z);
                if (v == 0) continue;
                var c = m.ColorAt(v);
                img.SetPixel(x, z, new Color(c.R, c.G, c.B));
            }
        return ImageTexture.CreateFromImage(img);
    }
}
