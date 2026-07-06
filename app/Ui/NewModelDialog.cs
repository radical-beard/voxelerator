using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// New-model modal: name, save location (user-chosen — decided 2026-07-05),
/// dimensions (presets or fully custom, non-square fine, no hard cap), and
/// the palette the model will be bound to.
public partial class NewModelDialog : Window
{
    private static readonly (string Label, int W, int D, int H)[] Presets =
    [
        ("Standard building — 8×8×26 (one Neo Terrestria cell)", 8, 8, 26),
        ("Hero / 2×2 slots — 16×16×26", 16, 16, 26),
        ("Dome prop — 16×16×10", 16, 16, 10),
        ("Cube — 16×16×16", 16, 16, 16),
        ("Custom…", 0, 0, 0),
    ];

    private readonly LineEdit _name = new() { PlaceholderText = "model name (also the folder name)" };
    private readonly LineEdit _dir = new();
    private readonly OptionButton _preset = new();
    private readonly SpinBox _w = Spin(), _d = Spin(), _h = Spin();
    private readonly OptionButton _palette = new();
    private readonly Label _warn = new();

    public static void Show(Control parent)
    {
        var dlg = new NewModelDialog();
        parent.AddChild(dlg);
        dlg.PopupCentered();
    }

    private static SpinBox Spin()
    {
        var s = new SpinBox { MinValue = 1, MaxValue = 4096, Value = 8, UpdateOnTextChanged = true };
        s.CustomMinimumSize = new Vector2(90, 0);
        return s;
    }

    public NewModelDialog()
    {
        Title = "New model";
        Size = new Vector2I(560, 470);
        Transient = true;
        Exclusive = true;
        CloseRequested += QueueFree;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 20));
        panel.Theme = Ui.Build();
        AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        col.AddChild(Ui.Dim("NAME", 11));
        col.AddChild(_name);

        col.AddChild(Ui.VSpace(2));
        col.AddChild(Ui.Dim("SAVE LOCATION", 11));
        var dirRow = new HBoxContainer();
        dirRow.AddThemeConstantOverride("separation", Ui.Gap);
        _dir.Text = Registry.DefaultNewModelDir();
        _dir.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var browse = Ui.GhostButton("Browse…");
        browse.Pressed += () =>
        {
            var fd = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
            };
            AddChild(fd);
            fd.DirSelected += dir => _dir.Text = dir;
            fd.PopupCentered(new Vector2I(900, 600));
        };
        dirRow.AddChild(_dir);
        dirRow.AddChild(browse);
        col.AddChild(dirRow);

        col.AddChild(Ui.VSpace(2));
        col.AddChild(Ui.Dim("DIMENSIONS (W × D × H — footprint × layers)", 11));
        foreach (var (label, _, _, _) in Presets) _preset.AddItem(label);
        _preset.ItemSelected += idx =>
        {
            var p = Presets[idx];
            bool custom = p.W == 0;
            _w.Editable = _d.Editable = _h.Editable = custom;
            if (!custom) { _w.Value = p.W; _d.Value = p.D; _h.Value = p.H; }
            UpdateWarning();
        };
        col.AddChild(_preset);
        var dims = new HBoxContainer();
        dims.AddThemeConstantOverride("separation", Ui.Gap);
        dims.AddChild(_w); dims.AddChild(Ui.Dim("×"));
        dims.AddChild(_d); dims.AddChild(Ui.Dim("×"));
        dims.AddChild(_h);
        dims.AddChild(Ui.Dim("  (0.5 m per voxel — 8 = one 4 m cell)", 11));
        col.AddChild(dims);
        _w.ValueChanged += _ => UpdateWarning();
        _d.ValueChanged += _ => UpdateWarning();
        _h.ValueChanged += _ => UpdateWarning();
        _w.Editable = _d.Editable = _h.Editable = false;
        _w.Value = 8; _d.Value = 8; _h.Value = 26;

        col.AddChild(Ui.VSpace(2));
        col.AddChild(Ui.Dim("PALETTE (≤16 colors — the palette is law)", 11));
        foreach (var p in Registry.LoadPalettes()) _palette.AddItem(p.Name);
        var last = Registry.LoadSettings().LastPalette;
        for (int i = 0; i < _palette.ItemCount; i++)
            if (_palette.GetItemText(i) == (last ?? "neo-terrestria")) _palette.Selected = i;
        col.AddChild(_palette);

        _warn.AddThemeColorOverride("font_color", Ui.Danger);
        _warn.AddThemeFontSizeOverride("font_size", 12);
        col.AddChild(_warn);
        col.AddChild(Ui.Stretch());

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", Ui.Gap);
        var cancel = Ui.GhostButton("Cancel");
        cancel.Pressed += QueueFree;
        var create = Ui.PrimaryButton("Create model");
        create.Pressed += Create;
        buttons.AddChild(cancel);
        buttons.AddChild(create);
        col.AddChild(buttons);
    }

    private void UpdateWarning()
    {
        long cells = (long)_w.Value * (long)_d.Value * (long)_h.Value;
        _warn.Text = cells > 2_000_000
            ? $"heads-up: {cells:N0} cells is a lot — editing stays fine, meshing may take a moment"
            : "";
        if (_warn.Text.Length > 0) _warn.AddThemeColorOverride("font_color", Ui.TextDim);
    }

    private void Create()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/'))
        {
            Fail("give the model a plain folder-safe name");
            return;
        }
        var parent = _dir.Text.Trim();
        if (parent.Length == 0) { Fail("pick a save location"); return; }

        var folder = Path.Combine(parent, name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Fail($"'{folder}' already exists and is not empty");
            return;
        }

        var paletteName = _palette.ItemCount > 0 ? _palette.GetItemText(_palette.Selected) : "neo-terrestria";
        var palette = Registry.LoadPalette(paletteName) ?? BuiltinPalettes.NeoTerrestria();
        try
        {
            var m = new VoxelModel((int)_w.Value, (int)_h.Value, (int)_d.Value, palette);
            ModelStore.Save(m, name, folder);
            Registry.Touch(folder, name);
            var settings = Registry.LoadSettings();
            settings.DefaultNewModelDir = parent;
            settings.LastPalette = paletteName;
            Registry.SaveSettings(settings);
            QueueFree();
            AppHost.Instance.OpenModel(folder);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Fail(e.Message);
        }
    }

    private void Fail(string msg)
    {
        _warn.AddThemeColorOverride("font_color", Ui.Danger);
        _warn.Text = msg;
    }
}
