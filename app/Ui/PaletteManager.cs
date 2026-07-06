using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The guarded palette editor. Hex/name/tag edits and additions stage until
/// Apply (recoloring bound voxels is the point of an indexed format);
/// removal and reorder act immediately because they renumber voxels —
/// removal of an in-use slot forces a remap-or-cancel choice. Apply also
/// saves the palette to the shared library so other models can adopt it.
public partial class PaletteManager : Window
{
    private readonly EditorState _s;
    private readonly VBoxContainer _rows = new();
    private readonly Label _error = new();
    private readonly LineEdit _paletteName = new();

    private sealed class Row
    {
        public required ColorRect Chip;
        public required LineEdit Hex;
        public required LineEdit Name;
        public required CheckBox Emissive;
        public required CheckBox Glass;
    }
    private readonly List<Row> _staged = new();

    public static void Show(Control parent, EditorState state)
    {
        var w = new PaletteManager(state);
        parent.AddChild(w);
        w.PopupCentered();
    }

    private PaletteManager(EditorState state)
    {
        _s = state;
        Title = "Palette manager";
        Size = new Vector2I(620, 640);
        Transient = true;
        Exclusive = true;
        CloseRequested += QueueFree;

        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 18));
        AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", Ui.Gap);
        nameRow.AddChild(Ui.Dim("PALETTE NAME", 11));
        _paletteName.Text = _s.Model.Palette.Name;
        _paletteName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameRow.AddChild(_paletteName);
        col.AddChild(nameRow);
        col.AddChild(Ui.Dim("hex/name/tag edits recolor every voxel bound to the slot — that is the point. " +
                            "Removing or reordering slots renumbers voxels and applies immediately.", 11));

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        col.AddChild(scroll);

        _error.AddThemeColorOverride("font_color", Ui.Danger);
        _error.AddThemeFontSizeOverride("font_size", 12);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_error);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Gap);
        var add = Ui.GhostButton("+ add color");
        add.Pressed += () =>
        {
            if (_staged.Count >= Palette.MaxColors) { _error.Text = "16 colors is the cap — the cap is the point"; return; }
            AddRow(new PaletteColor("#8A8A96", $"color-{_staged.Count + 1}"));
        };
        actions.AddChild(add);
        actions.AddChild(Ui.Stretch());
        var cancel = Ui.GhostButton("Close");
        cancel.Pressed += QueueFree;
        var saveAs = Ui.GhostButton("Save as new…");
        saveAs.TooltipText = "duplicate this palette under the name above and bind the model to it";
        saveAs.Pressed += () => Apply(asNew: true);
        var apply = Ui.PrimaryButton("Apply");
        apply.Pressed += () => Apply(asNew: false);
        actions.AddChild(cancel);
        actions.AddChild(saveAs);
        actions.AddChild(apply);
        col.AddChild(actions);

        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        _staged.Clear();
        foreach (var color in _s.Model.Palette.Colors) AddRow(color);
    }

    private void AddRow(PaletteColor color)
    {
        int slotIndex = _staged.Count;                       // 0-based at build time
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);

        var slotLabel = Ui.Dim(TextGrid.CharFor((byte)(slotIndex + 1)).ToString(), 13);
        slotLabel.CustomMinimumSize = new Vector2(16, 0);
        h.AddChild(slotLabel);

        var chip = new ColorRect { CustomMinimumSize = new Vector2(30, 30) };
        var hex = new LineEdit { Text = color.Hex, CustomMinimumSize = new Vector2(90, 0) };
        var name = new LineEdit { Text = color.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var emissive = new CheckBox { Text = "✦", TooltipText = "emissive — glows at night in-game", ButtonPressed = color.Has(ColorTags.Emissive) };
        var glass = new CheckBox { Text = "◇", TooltipText = "glass — renders translucent", ButtonPressed = color.Has(ColorTags.Glass) };
        void SyncChip()
        {
            try
            {
                var (r, g, b) = PaletteColor.ParseHex(hex.Text.Trim());
                chip.Color = new Color(r / 255f, g / 255f, b / 255f);
            }
            catch (FormatException) { chip.Color = Colors.Magenta; }
        }
        SyncChip();
        hex.TextChanged += _ => SyncChip();

        var up = Ui.GhostButton("↑");
        up.TooltipText = "swap with the slot above (renumbers voxels, immediate)";
        up.Pressed += () => Reorder(slotIndex, -1);
        var down = Ui.GhostButton("↓");
        down.TooltipText = "swap with the slot below (renumbers voxels, immediate)";
        down.Pressed += () => Reorder(slotIndex, +1);
        var remove = Ui.GhostButton("✕");
        remove.TooltipText = "remove this slot (guarded when in use)";
        remove.Pressed += () => RemoveSlot(slotIndex);

        h.AddChild(chip);
        h.AddChild(hex);
        h.AddChild(name);
        h.AddChild(emissive);
        h.AddChild(glass);
        h.AddChild(up);
        h.AddChild(down);
        h.AddChild(remove);
        _rows.AddChild(h);
        _staged.Add(new Row { Chip = chip, Hex = hex, Name = name, Emissive = emissive, Glass = glass });
    }

    private void Reorder(int index, int dir)
    {
        int other = index + dir;
        if (other < 0 || other >= _s.Model.Palette.Colors.Count || index >= _s.Model.Palette.Colors.Count)
            return;                                          // staged-only rows can't reorder
        _s.Do(m => EditOps.SwapPaletteSlots(m, (byte)(index + 1), (byte)(other + 1)));
        Rebuild();
    }

    private void RemoveSlot(int index)
    {
        if (index >= _s.Model.Palette.Colors.Count)          // staged, never applied: just drop the row
        {
            _rows.GetChild(index).QueueFree();
            _staged.RemoveAt(index);
            return;
        }
        byte slot = (byte)(index + 1);
        var stats = Stats.Compute(_s.Model);
        int used = stats.PerColor[slot];
        if (used == 0)
        {
            _s.Do(m => EditOps.RemovePaletteSlot(m, slot, 0));
            Rebuild();
            return;
        }

        // remap-or-cancel: no silent breakage
        var dlg = new Window { Title = "Slot is in use", Size = new Vector2I(430, 200), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        col.AddChild(Ui.Dim($"'{_s.Model.Palette.Colors[index].Name}' is used by {used} voxel(s). Remap them, then remove the slot:"));
        var target = new OptionButton();
        target.AddItem("air (erase those voxels)");
        for (int i = 0; i < _s.Model.Palette.Colors.Count; i++)
            if (i != index) target.AddItem($"{i + 1} · {_s.Model.Palette.Colors[i].Name}");
        col.AddChild(target);
        col.AddChild(Ui.Stretch());
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", Ui.Gap);
        var cancel = Ui.GhostButton("Cancel");
        cancel.Pressed += dlg.QueueFree;
        var go = Ui.PrimaryButton("Remap + remove");
        go.Pressed += () =>
        {
            int sel = target.Selected;
            byte remapTo = 0;
            if (sel > 0)
            {
                int chosen = sel - 1;                        // index into "all but removed"
                remapTo = (byte)(chosen >= index ? chosen + 2 : chosen + 1);
            }
            _s.Do(m => EditOps.RemovePaletteSlot(m, slot, remapTo));
            dlg.QueueFree();
            Rebuild();
        };
        row.AddChild(cancel);
        row.AddChild(go);
        col.AddChild(row);
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void Apply(bool asNew)
    {
        var colors = new List<PaletteColor>();
        foreach (var r in _staged)
        {
            var tags = new List<string>();
            if (r.Emissive.ButtonPressed) tags.Add(ColorTags.Emissive);
            if (r.Glass.ButtonPressed) tags.Add(ColorTags.Glass);
            colors.Add(new PaletteColor(r.Hex.Text.Trim().ToUpperInvariant(), r.Name.Text.Trim(),
                tags.Count > 0 ? tags : null));
        }
        var name = _paletteName.Text.Trim();
        var candidate = new Palette(name.Length == 0 ? _s.Model.Palette.Name : name);
        candidate.Colors.AddRange(colors);
        var problems = candidate.Validate();
        int maxUsed = MaxUsedSlot();
        if (candidate.Colors.Count < maxUsed)
            problems.Add($"model uses slot {maxUsed} — the palette can't have fewer than {maxUsed} colors (remove slots with ✕ instead)");
        if (asNew && Registry.LoadPalette(candidate.Name) is not null)
            problems.Add($"a palette named '{candidate.Name}' already exists");
        if (problems.Count > 0)
        {
            _error.Text = string.Join("\n", problems);
            return;
        }

        _s.Do(m => m.Palette = candidate);
        try { Registry.SavePalette(candidate); }
        catch (ArgumentException e) { _error.Text = e.Message; return; }
        QueueFree();
    }

    private int MaxUsedSlot()
    {
        int max = 0;
        foreach (var b in _s.Model.Voxels) if (b > max) max = b;
        return max;
    }
}
