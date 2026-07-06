using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The palette dock: the only colors that exist. Swatches select the active
/// color (1-9 hotkeys mirror the first nine slots); emissive/glass tags show
/// as markers; Manage… opens the palette manager, Recolor… swaps slots
/// model-wide.
public partial class PaletteDock : VBoxContainer
{
    private readonly EditorState _s;
    private readonly GridContainer _grid = new() { Columns = 2 };
    private readonly Label _header = new();

    public event Action? ManageRequested;

    public PaletteDock(EditorState state)
    {
        _s = state;
        AddThemeConstantOverride("separation", 6);
        _s.Changed += RefreshSelection;
        _s.StructureChanged += Rebuild;
    }

    public override void _Ready()
    {
        _header.AddThemeFontSizeOverride("font_size", 11);
        _header.AddThemeColorOverride("font_color", Ui.TextDim);
        AddChild(_header);

        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        AddChild(_grid);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var manage = Ui.GhostButton("Manage…");
        manage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        manage.Pressed += () => ManageRequested?.Invoke();
        var recolor = Ui.GhostButton("Recolor…");
        recolor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        recolor.TooltipText = "swap every voxel of one slot for another";
        recolor.Pressed += ShowRecolor;
        row.AddChild(manage);
        row.AddChild(recolor);
        AddChild(row);

        Rebuild();
    }

    public void Rebuild()
    {
        _header.Text = $"PALETTE — {_s.Model.Palette.Name}  ({_s.Model.Palette.Colors.Count}/16)";
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        var pal = _s.Model.Palette;
        for (int i = 0; i < pal.Colors.Count; i++)
        {
            byte slot = (byte)(i + 1);
            var color = pal.Colors[i];
            var (r, g, b) = color.RgbBytes();
            var btn = new Button
            {
                CustomMinimumSize = new Vector2(92, 34),
                TooltipText = $"{color.Name} · {color.Hex}" +
                    (color.Tags is { Count: > 0 } t ? $" · {string.Join(",", t)}" : "") +
                    (slot <= 9 ? $"   [{slot}]" : ""),
            };
            StyleSwatch(btn, slot);

            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            h.SetAnchorsPreset(LayoutPreset.FullRect);
            h.AddThemeConstantOverride("separation", 6);
            var chip = new ColorRect
            {
                Color = new Color(r / 255f, g / 255f, b / 255f),
                CustomMinimumSize = new Vector2(20, 20),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            h.AddChild(Ui.HSpace(4));
            h.AddChild(chip);
            var tag = color.Has(ColorTags.Emissive) ? "✦" : color.Has(ColorTags.Glass) ? "◇" : "";
            var lbl = new Label
            {
                Text = $"{TextGrid.CharFor(slot)} {tag}",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lbl.AddThemeFontSizeOverride("font_size", 12);
            lbl.AddThemeColorOverride("font_color", Ui.TextDim);
            h.AddChild(lbl);
            btn.AddChild(h);

            btn.Pressed += () => { _s.Color = slot; RefreshSelection(); };
            _grid.AddChild(btn);
        }
    }

    private void StyleSwatch(Button btn, byte slot)
    {
        bool selected = _s.Color == slot;
        btn.AddThemeStyleboxOverride("normal",
            Ui.Flat(selected ? Ui.PrimaryDim : Ui.Bg2, selected ? Ui.Primary : Ui.StrokeSoft, Ui.RadiusS, 4));
        btn.AddThemeStyleboxOverride("hover", Ui.Flat(Ui.Bg3, selected ? Ui.Primary : Ui.Stroke, Ui.RadiusS, 4));
        btn.AddThemeStyleboxOverride("pressed", Ui.Flat(Ui.PrimaryDim, Ui.Primary, Ui.RadiusS, 4));
    }

    private void RefreshSelection()
    {
        var kids = _grid.GetChildren();
        for (int i = 0; i < kids.Count; i++)
            if (kids[i] is Button b) StyleSwatch(b, (byte)(i + 1));
    }

    private void ShowRecolor()
    {
        var pal = _s.Model.Palette;
        var dlg = new Window { Title = "Recolor model", Size = new Vector2I(380, 210), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        col.AddChild(Ui.Dim("swap every voxel of one slot for another (one undo step)"));
        OptionButton Pick(bool withAir)
        {
            var o = new OptionButton();
            if (withAir) o.AddItem("air (erase)");
            for (int i = 0; i < pal.Colors.Count; i++) o.AddItem($"{i + 1} · {pal.Colors[i].Name}");
            return o;
        }
        var from = Pick(false);
        var to = Pick(true);
        var r1 = new HBoxContainer(); r1.AddChild(Ui.Dim("from ", 13)); r1.AddChild(from);
        var r2 = new HBoxContainer(); r2.AddChild(Ui.Dim("to     ", 13)); r2.AddChild(to);
        col.AddChild(r1);
        col.AddChild(r2);
        col.AddChild(Ui.Stretch());
        var apply = Ui.PrimaryButton("Recolor");
        apply.Pressed += () =>
        {
            byte f = (byte)(from.Selected + 1);
            byte t = (byte)to.Selected;                      // 0 = air
            _s.Do(m => EditOps.ReplaceColor(m, f, t));
            dlg.QueueFree();
        };
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(apply);
        col.AddChild(row);
        AddChild(dlg);
        dlg.PopupCentered();
    }
}
