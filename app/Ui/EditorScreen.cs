using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The editor: top bar (navigation, undo, layer, view controls), tool rail,
/// layer canvas ⇄ voxel view, scrubber, palette/stats dock. Owns shortcuts,
/// autosave, the external-change poll, and the export/reshape dialogs.
public partial class EditorScreen : Control
{
    private readonly EditorState _s;
    private LayerCanvas _canvas = null!;
    private VoxelView _voxel = null!;
    private Control _canvasWrap = null!, _voxelWrap = null!;
    private PaletteDock _palette = null!;
    private Label _layerLabel = null!, _coordsLabel = null!, _dirtyDot = null!;
    private Button _orthoBtn = null!, _cutBtn = null!, _spinBtn = null!, _viewBtn = null!;
    private HSlider _night = null!;
    private Label _toast = null!;
    private double _toastT;

    private readonly Dictionary<ToolKind, Button> _toolButtons = new();
    private bool _voxelMode, _spaceMomentary;
    private long _lastSeenVersion;
    private double _sinceEdit;

    public EditorScreen(string folder) { _s = new EditorState(folder); }

    public override void _Ready()
    {
        _lastSeenVersion = _s.Session.Version;

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        root.AddChild(BuildTopBar());
        root.AddChild(Ui.Divider());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        root.AddChild(body);

        body.AddChild(BuildToolRail());
        body.AddChild(VLine());

        // center: canvas ⇄ voxel view
        var center = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _canvas = new LayerCanvas(_s);
        _canvasWrap = Wrap(_canvas);
        _voxel = new VoxelView(_s);
        _voxelWrap = Wrap(_voxel);
        _voxelWrap.Visible = false;
        center.AddChild(_canvasWrap);
        center.AddChild(_voxelWrap);
        body.AddChild(center);

        body.AddChild(VLine());
        body.AddChild(new ScrubberStrip(_s));
        body.AddChild(VLine());
        body.AddChild(BuildRightDock());

        _toast = new Label { Visible = false };
        _toast.AddThemeFontSizeOverride("font_size", 13);
        _toast.AddThemeColorOverride("font_color", Ui.Text);
        var toastPanel = new PanelContainer();
        toastPanel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg3, Ui.Primary, Ui.Radius, 10));
        toastPanel.AddChild(_toast);
        toastPanel.SetAnchorsPreset(LayoutPreset.CenterBottom);
        toastPanel.Position += new Vector2(0, -46);
        toastPanel.GrowHorizontal = GrowDirection.Both;
        toastPanel.GrowVertical = GrowDirection.Begin;
        _toastPanel = toastPanel;
        toastPanel.Visible = false;
        AddChild(toastPanel);

        _canvas.HoverChanged += c => _coordsLabel.Text =
            c is { } cc ? $"({cc.X}, {_s.Layer}, {cc.Y})" : "";

        AppHost.Instance.StatusFn = Status;
        AppHost.Instance.HintFn = Hint;
        SelectTool(ToolKind.Brush);
        UpdateChrome();
        _s.Changed += UpdateChrome;
        _s.StructureChanged += UpdateChrome;
    }

    private Control _toastPanel = null!;

    private static Control Wrap(Control inner)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 10);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        inner.SizeFlagsVertical = SizeFlags.ExpandFill;
        margin.AddChild(inner);
        return margin;
    }

    private static ColorRect VLine() => new() { Color = Ui.StrokeSoft, CustomMinimumSize = new Vector2(1, 0) };

    // ---- top bar ---------------------------------------------------------------

    private Control BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.StrokeSoft, 0, 8));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", Ui.Gap);
        bar.AddChild(h);

        var back = Ui.GhostButton("‹  Library");
        back.Pressed += () => { SaveNow(); AppHost.Instance.ShowLibrary(); };
        h.AddChild(back);

        var name = Ui.Title(_s.Name, 17);
        h.AddChild(name);
        _dirtyDot = new Label { Text = "●", TooltipText = "unsaved changes (autosaves continuously)" };
        _dirtyDot.AddThemeColorOverride("font_color", Ui.Glow);
        _dirtyDot.AddThemeFontSizeOverride("font_size", 10);
        h.AddChild(_dirtyDot);
        h.AddChild(Ui.Dim($"  {_s.Model.SX}×{_s.Model.SZ}×{_s.Model.SY} · {_s.Model.Palette.Name}", 12));

        h.AddChild(Ui.Stretch());

        var undo = Ui.GhostButton("↩ Undo");
        undo.Pressed += _s.Undo;
        var redo = Ui.GhostButton("↪ Redo");
        redo.Pressed += _s.Redo;
        h.AddChild(undo);
        h.AddChild(redo);
        h.AddChild(Ui.HSpace(6));

        var down = Ui.GhostButton("−");
        down.TooltipText = "layer down  [ [ ]";
        down.Pressed += () => _s.SetLayer(_s.Layer - 1);
        _layerLabel = Ui.Title("0", 15);
        _layerLabel.CustomMinimumSize = new Vector2(64, 0);
        _layerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var up = Ui.GhostButton("+");
        up.TooltipText = "layer up  [ ] ]";
        up.Pressed += () => _s.SetLayer(_s.Layer + 1);
        h.AddChild(down);
        h.AddChild(_layerLabel);
        h.AddChild(up);
        h.AddChild(Ui.HSpace(6));

        _viewBtn = Ui.PrimaryButton("3D  ⇥");
        _viewBtn.TooltipText = "toggle voxel view (Tab, hold Space to peek)";
        _viewBtn.Pressed += () => SetVoxelView(!_voxelMode);
        h.AddChild(_viewBtn);

        _orthoBtn = Ui.GhostButton("ortho");
        _orthoBtn.TooltipText = "orthographic ⇄ perspective (P)";
        _orthoBtn.Pressed += () => { _voxel.Orthographic = !_voxel.Orthographic; UpdateChrome(); };
        _cutBtn = Ui.GhostButton("cutaway");
        _cutBtn.TooltipText = "hide layers above the active one (K)";
        _cutBtn.Pressed += () => { _voxel.Cutaway = !_voxel.Cutaway; _s.NotifyChangedOnly(); UpdateChrome(); };
        _spinBtn = Ui.GhostButton("spin");
        _spinBtn.TooltipText = "turntable (T)";
        _spinBtn.Pressed += () => { _voxel.Turntable = !_voxel.Turntable; UpdateChrome(); };
        _night = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(90, 0), TooltipText = "night — emissive colors glow" };
        _night.ValueChanged += v => _voxel.Night = (float)v;
        h.AddChild(_orthoBtn);
        h.AddChild(_cutBtn);
        h.AddChild(_spinBtn);
        h.AddChild(_night);

        var export = new MenuButton { Text = "Export ▾" };
        export.AddThemeStyleboxOverride("normal", Ui.Flat(Ui.Bg2, Ui.Stroke, Ui.Radius));
        export.AddThemeStyleboxOverride("hover", Ui.Flat(Ui.Bg3, Ui.Stroke, Ui.Radius));
        var pop = export.GetPopup();
        pop.AddItem("Export model folder…", 0);
        pop.AddItem("Screenshot PNG…", 1);
        pop.AddItem("Turntable GIF…", 2);
        pop.IdPressed += id =>
        {
            switch (id)
            {
                case 0: ExportFolder(); break;
                case 1: ScreenshotPng(); break;
                case 2: TurntableGif(); break;
            }
        };
        h.AddChild(export);
        return bar;
    }

    // ---- tool rail ---------------------------------------------------------------

    private Control BuildToolRail()
    {
        var rail = new PanelContainer { CustomMinimumSize = new Vector2(58, 0) };
        rail.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.StrokeSoft, 0, 6));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 5);
        rail.AddChild(v);

        var group = new ButtonGroup();
        void Tool(ToolKind kind, string glyph, string tip, string key)
        {
            var b = Ui.ToolButton(glyph, $"{tip}  [{key}]");
            b.ButtonGroup = group;
            b.Pressed += () => SelectTool(kind);
            _toolButtons[kind] = b;
            v.AddChild(b);
        }
        Tool(ToolKind.Brush, "✏", "brush — right-click erases", "B");
        Tool(ToolKind.Eraser, "◻", "eraser", "E");
        Tool(ToolKind.Bucket, "▨", "bucket fill (contiguous)", "G");
        Tool(ToolKind.Line, "╱", "line — drag", "L");
        Tool(ToolKind.Rect, "▭", "rectangle — drag (F fills)", "R");
        Tool(ToolKind.Ellipse, "◯", "ellipse — drag (F fills)", "O");
        Tool(ToolKind.Picker, "◉", "eyedropper (or Alt-click any time)", "I");
        Tool(ToolKind.Select, "⬚", "marquee select / move — ⌘C ⌘X ⌘V, arrows nudge", "M");

        v.AddChild(Ui.Divider());

        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 2);
        var sizeGroup = new ButtonGroup();
        foreach (var size in new[] { 1, 2, 3 })
        {
            var b = new Button { Text = size.ToString(), ToggleMode = true, ButtonGroup = sizeGroup, CustomMinimumSize = new Vector2(14, 24), TooltipText = $"brush size {size}" };
            b.AddThemeStyleboxOverride("normal", Ui.Flat(Ui.Bg2, Ui.StrokeSoft, 4, 2));
            b.AddThemeStyleboxOverride("pressed", Ui.Flat(Ui.PrimaryDim, Ui.Primary, 4, 2));
            b.AddThemeFontSizeOverride("font_size", 11);
            if (size == 1) b.ButtonPressed = true;
            b.Pressed += () => _s.BrushSize = size;
            sizeRow.AddChild(b);
        }
        v.AddChild(sizeRow);

        _fillBtn = Ui.ToolButton("fill", "filled shapes for rect/ellipse  [F]");
        _fillBtn.CustomMinimumSize = new Vector2(44, 28);
        _fillBtn.Pressed += () => { _s.ShapeFilled = _fillBtn.ButtonPressed; };
        v.AddChild(_fillBtn);

        _symBtn = Ui.ToolButton("sym", "symmetry: none → X → Z → XZ → radial  [S]");
        _symBtn.CustomMinimumSize = new Vector2(44, 28);
        _symBtn.ToggleMode = false;
        _symBtn.Pressed += CycleSymmetry;
        v.AddChild(_symBtn);

        _onionBtn = Ui.ToolButton("onion", "onion skin: layer below (⇧O adds layer above)");
        _onionBtn.CustomMinimumSize = new Vector2(44, 28);
        _onionBtn.ButtonPressed = true;
        _onionBtn.Pressed += () => { _s.OnionBelow = _onionBtn.ButtonPressed; _s.NotifyChangedOnly(); };
        v.AddChild(_onionBtn);

        var onionOpacity = new HSlider
        {
            MinValue = 0.05, MaxValue = 0.6, Step = 0.05, Value = _s.OnionOpacity,
            CustomMinimumSize = new Vector2(44, 12),
            TooltipText = "onion skin opacity",
        };
        onionOpacity.ValueChanged += val =>
        {
            _s.OnionOpacity = (float)val;
            var settings = Registry.LoadSettings();
            settings.OnionOpacity = (float)val;
            Registry.SaveSettings(settings);
            _s.NotifyChangedOnly();
        };
        v.AddChild(onionOpacity);

        v.AddChild(Ui.Divider());

        var layerMenu = new MenuButton { Text = "layer ▾", CustomMinimumSize = new Vector2(44, 28) };
        layerMenu.AddThemeFontSizeOverride("font_size", 11);
        var lp = layerMenu.GetPopup();
        lp.AddItem("Copy layer", 0); lp.AddItem("Paste layer", 1); lp.AddItem("Duplicate upward", 2);
        lp.AddSeparator();
        lp.AddItem("Insert blank above", 3); lp.AddItem("Insert blank below", 4); lp.AddItem("Delete layer", 5);
        lp.AddSeparator();
        lp.AddItem("Swap with above", 6); lp.AddItem("Swap with below", 7);
        lp.AddSeparator();
        lp.AddItem("Flip layer ⇋ x", 8); lp.AddItem("Flip layer ⇵ z", 9); lp.AddItem("Rotate layer 90°", 10);
        lp.AddItem("Extrude upward ×N…", 11); lp.AddItem("Clear layer", 12);
        lp.IdPressed += LayerMenu;
        v.AddChild(layerMenu);

        var modelMenu = new MenuButton { Text = "model ▾", CustomMinimumSize = new Vector2(44, 28) };
        modelMenu.AddThemeFontSizeOverride("font_size", 11);
        var mp = modelMenu.GetPopup();
        mp.AddItem("Mirror ⇋ x", 0); mp.AddItem("Mirror ⇵ z", 1); mp.AddItem("Rotate 90°", 2);
        mp.AddSeparator();
        mp.AddItem("Trim to content", 3); mp.AddItem("Resize canvas…", 4);
        mp.AddItem("Translate contents…", 5);
        mp.IdPressed += ModelMenu;
        v.AddChild(modelMenu);

        return rail;
    }

    private Button _fillBtn = null!, _symBtn = null!, _onionBtn = null!;

    // ---- behavior ---------------------------------------------------------------

    private void SelectTool(ToolKind kind)
    {
        _s.Tool = kind;
        if (_toolButtons.TryGetValue(kind, out var b)) b.ButtonPressed = true;
        if (kind != ToolKind.Select) _canvas.ClearSelection();
    }

    private void CycleSymmetry()
    {
        _s.Symmetry = _s.Symmetry switch
        {
            SymmetryMode.None => SymmetryMode.X,
            SymmetryMode.X => SymmetryMode.Z,
            SymmetryMode.Z => SymmetryMode.XZ,
            SymmetryMode.XZ => _s.Model.SX == _s.Model.SZ ? SymmetryMode.Radial4 : SymmetryMode.None,
            _ => SymmetryMode.None,
        };
        Toast(_s.Symmetry == SymmetryMode.None ? "symmetry off" : $"symmetry: {_s.Symmetry}");
        _s.NotifyChangedOnly();
        UpdateChrome();
    }

    private void LayerMenu(long id)
    {
        int y = _s.Layer;
        switch (id)
        {
            case 0: _s.LayerClipboard = EditOps.CopyLayer(_s.Model, y); Toast($"layer {y} copied"); break;
            case 1:
                if (_s.LayerClipboard is { } clip && clip.Length == _s.Model.SX * _s.Model.SZ)
                    _s.Do(m => EditOps.PasteLayer(m, y, clip));
                else Toast("layer clipboard is empty (or wrong size)");
                break;
            case 2:
                if (y + 1 < _s.Model.SY) { var c = EditOps.CopyLayer(_s.Model, y); _s.Do(m => EditOps.PasteLayer(m, y + 1, c)); _s.SetLayer(y + 1); }
                else { _s.Swap(m => EditOps.InsertLayer(m, y + 1, EditOps.CopyLayer(m, y))); _s.SetLayer(y + 1); }
                break;
            case 3: _s.Swap(m => EditOps.InsertLayer(m, y + 1)); _s.SetLayer(y + 1); break;
            case 4: _s.Swap(m => EditOps.InsertLayer(m, y)); break;
            case 5:
                if (_s.Model.SY <= 1) { Toast("a model needs at least one layer"); break; }
                _s.Swap(m => EditOps.DeleteLayer(m, y));
                break;
            case 6: if (y + 1 < _s.Model.SY) { _s.Do(m => EditOps.SwapLayers(m, y, y + 1)); _s.SetLayer(y + 1); } break;
            case 7: if (y > 0) { _s.Do(m => EditOps.SwapLayers(m, y, y - 1)); _s.SetLayer(y - 1); } break;
            case 8: _s.Do(m => EditOps.FlipLayer(m, y, alongX: true)); break;
            case 9: _s.Do(m => EditOps.FlipLayer(m, y, alongX: false)); break;
            case 10:
                if (_s.Model.SX == _s.Model.SZ) _s.Do(m => EditOps.RotateLayer90(m, y));
                else Toast("per-layer rotate needs a square footprint — rotate the whole model instead");
                break;
            case 11: AskCount("Extrude upward", $"repeat layer {y} into the N layers above", 1, 64, n => _s.Do(m => EditOps.Extrude(m, y, n))); break;
            case 12: _s.Do(m => EditOps.ClearLayer(m, y)); break;
        }
    }

    private void ModelMenu(long id)
    {
        switch (id)
        {
            case 0: _s.Do(m => EditOps.MirrorModel(m, alongX: true)); break;
            case 1: _s.Do(m => EditOps.MirrorModel(m, alongX: false)); break;
            case 2: _s.Swap(EditOps.RotateModel90); break;
            case 3: _s.Swap(EditOps.Trim); Toast("trimmed to content"); break;
            case 4: ShowResizeDialog(); break;
            case 5: ShowTranslateDialog(); break;
        }
    }

    private void ShowTranslateDialog()
    {
        var dlg = new Window { Title = "Translate contents", Size = new Vector2I(440, 190), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        col.AddChild(Ui.Dim("shift every voxel by (x, layers, z) — clipped at the edges"));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        SpinBox S() => new() { MinValue = -512, MaxValue = 512, Value = 0, UpdateOnTextChanged = true };
        var dx = S(); var dy = S(); var dz = S();
        row.AddChild(Ui.Dim("x")); row.AddChild(dx);
        row.AddChild(Ui.Dim("y")); row.AddChild(dy);
        row.AddChild(Ui.Dim("z")); row.AddChild(dz);
        col.AddChild(row);
        col.AddChild(Ui.Stretch());
        var ok = Ui.PrimaryButton("Translate");
        ok.Pressed += () =>
        {
            int mx = (int)dx.Value, my = (int)dy.Value, mz = (int)dz.Value;
            if (mx != 0 || my != 0 || mz != 0)
                _s.Do(m => EditOps.TranslateModel(m, mx, my, mz));
            dlg.QueueFree();
        };
        var btns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        btns.AddChild(ok);
        col.AddChild(btns);
        AddChild(dlg);
        dlg.PopupCentered();
    }

    // ---- view switching -------------------------------------------------------------

    public void SetVoxelView(bool on)
    {
        _voxelMode = on;
        _canvasWrap.Visible = !on;
        _voxelWrap.Visible = on;
        if (on) _voxel.GrabFocus();
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        _layerLabel.Text = $"{_s.Layer} / {_s.Model.SY - 1}";
        _viewBtn.Text = _voxelMode ? "2D  ⇥" : "3D  ⇥";
        _orthoBtn.Visible = _cutBtn.Visible = _spinBtn.Visible = _night.Visible = _voxelMode;
        _orthoBtn.Text = _voxel is { Orthographic: false } ? "persp" : "ortho";
        _cutBtn.Modulate = _voxel is { Cutaway: true } ? new Color(Ui.Glow, 1) : Colors.White;
        _spinBtn.Modulate = _voxel is { Turntable: true } ? new Color(Ui.Glow, 1) : Colors.White;
        _symBtn.Text = _s.Symmetry switch
        {
            SymmetryMode.None => "sym",
            SymmetryMode.X => "sym X",
            SymmetryMode.Z => "sym Z",
            SymmetryMode.XZ => "sym XZ",
            _ => "sym R4",
        };
        _dirtyDot.Visible = _s.Dirty;
    }

    // ---- shortcuts ---------------------------------------------------------------------

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey k || !IsVisibleInTree()) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;

        // Space peek — swallow before focused buttons see it
        if (k.Keycode == Key.Space && !k.Echo)
        {
            if (k.Pressed && !_voxelMode) { _spaceMomentary = true; SetVoxelView(true); }
            else if (!k.Pressed && _spaceMomentary) { _spaceMomentary = false; SetVoxelView(false); }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!k.Pressed) return;
        bool cmd = k.MetaPressed || k.CtrlPressed;

        if (cmd)
        {
            switch (k.Keycode)
            {
                case Key.Z when k.ShiftPressed: _s.Redo(); break;
                case Key.Z: _s.Undo(); break;
                case Key.Y: _s.Redo(); break;
                case Key.S: SaveNow(); break;
                case Key.C: _canvas.CopySelection(cut: false); Toast("copied"); break;
                case Key.X: _canvas.CopySelection(cut: true); Toast("cut"); break;
                case Key.V: _canvas.PasteClipboard(); break;
                case Key.A: _canvas.SelectAll(); break;
                case Key.E: ExportFolder(); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (k.Keycode)
        {
            case Key.Tab: SetVoxelView(!_voxelMode); break;
            case Key.B: SelectTool(ToolKind.Brush); break;
            case Key.E: SelectTool(ToolKind.Eraser); break;
            case Key.G: SelectTool(ToolKind.Bucket); break;
            case Key.L: SelectTool(ToolKind.Line); break;
            case Key.R: SelectTool(ToolKind.Rect); break;
            case Key.O when k.ShiftPressed: _s.OnionAbove = !_s.OnionAbove; _s.NotifyChangedOnly(); break;
            case Key.O: SelectTool(ToolKind.Ellipse); break;
            case Key.I: SelectTool(ToolKind.Picker); break;
            case Key.M: SelectTool(ToolKind.Select); break;
            case Key.F: _s.ShapeFilled = !_s.ShapeFilled; _fillBtn.ButtonPressed = _s.ShapeFilled; break;
            case Key.S: CycleSymmetry(); break;
            case Key.Bracketleft: _s.SetLayer(_s.Layer - 1); break;
            case Key.Bracketright: _s.SetLayer(_s.Layer + 1); break;
            case Key.Home: _s.SetLayer(_s.NonEmptySpan().First); break;
            case Key.End: _s.SetLayer(_s.NonEmptySpan().Last); break;
            case Key.P when _voxelMode: _voxel.Orthographic = !_voxel.Orthographic; UpdateChrome(); break;
            case Key.K when _voxelMode: _voxel.Cutaway = !_voxel.Cutaway; _s.NotifyChangedOnly(); UpdateChrome(); break;
            case Key.T when _voxelMode: _voxel.Turntable = !_voxel.Turntable; UpdateChrome(); break;
            case Key.Delete or Key.Backspace: _canvas.DeleteSelection(); break;
            case Key.Escape: _canvas.ClearSelection(); break;
            case Key.Left: NudgeOrNothing(-1, 0, k.ShiftPressed); break;
            case Key.Right: NudgeOrNothing(1, 0, k.ShiftPressed); break;
            case Key.Up: NudgeOrNothing(0, -1, k.ShiftPressed); break;
            case Key.Down: NudgeOrNothing(0, 1, k.ShiftPressed); break;
            case >= Key.Key1 and <= Key.Key9:
            {
                int slot = (int)k.Keycode - (int)Key.Key0;
                if (slot <= _s.Model.Palette.Colors.Count) { _s.Color = (byte)slot; _s.NotifyChangedOnly(); }
                break;
            }
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void NudgeOrNothing(int dx, int dz, bool big)
    {
        if (_s.Selection is null) return;
        int f = big ? 8 : 1;
        _canvas.NudgeSelection(dx * f, dz * f);
    }

    // ---- per-frame upkeep -------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_s.Session.Version != _lastSeenVersion)
        {
            _lastSeenVersion = _s.Session.Version;
            _sinceEdit = 0;
            UpdateChrome();
        }
        else _sinceEdit += delta;

        _s.TickAutosave(delta, _sinceEdit);
        if (_s.PollExternalChanges()) Toast("reloaded external changes (undo to reject)");

        if (_toastPanel.Visible)
        {
            _toastT -= delta;
            if (_toastT <= 0) _toastPanel.Visible = false;
        }
    }

    // ---- persistence & export ------------------------------------------------------------

    public void SaveNow()
    {
        if (_s.Dirty) _s.SaveNow();
        Toast("saved");
        UpdateChrome();
    }

    private void ExportFolder()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Export: pick the parent folder (a folder named after the model is created inside)",
        };
        AddChild(fd);
        fd.DirSelected += dir =>
        {
            var dest = Path.Combine(dir, _s.Name);
            ModelStore.Save(_s.Model, _s.Name, dest);
            Toast($"exported → {dest}");
        };
        fd.PopupCentered(new Vector2I(900, 600));
    }

    private void ScreenshotPng()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentFile = $"{_s.Name}.png",
        };
        AddChild(fd);
        fd.FileSelected += path =>
        {
            var png = SoftwareRenderer.RenderPng(_s.Model, new RenderOptions
            {
                Size = 1024,
                Perspective = !_voxel.Orthographic,
                Night = _voxel.Night,
                CutawayAboveLayer = _voxel.Cutaway ? _s.Layer : null,
            });
            File.WriteAllBytes(path, png);
            Toast($"screenshot → {path}");
        };
        fd.PopupCentered(new Vector2I(900, 600));
    }

    private void TurntableGif()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentFile = $"{_s.Name}-turntable.gif",
        };
        AddChild(fd);
        fd.FileSelected += path =>
        {
            var frames = new List<byte[]>();
            for (int i = 0; i < 36; i++)
                frames.Add(SoftwareRenderer.RenderRgba(_s.Model, new RenderOptions
                {
                    Size = 320,
                    YawDegrees = i * 10,
                    Night = _voxel.Night,
                    Background = new Rgba(0.055f, 0.05f, 0.07f),
                }));
            File.WriteAllBytes(path, Gif.Encode(320, 320, frames, delayCs: 6));
            Toast($"turntable → {path}");
        };
        fd.PopupCentered(new Vector2I(900, 600));
    }

    // ---- misc ------------------------------------------------------------------------------

    private void Toast(string msg)
    {
        _toast.Text = msg;
        _toastPanel.Visible = true;
        _toast.Visible = true;
        _toastT = 1.8;
    }

    private void AskCount(string title, string prompt, int min, int max, Action<int> apply)
    {
        var dlg = new Window { Title = title, Size = new Vector2I(360, 160), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        col.AddChild(Ui.Dim(prompt));
        var spin = new SpinBox { MinValue = min, MaxValue = max, Value = min };
        col.AddChild(spin);
        var ok = Ui.PrimaryButton("Apply");
        ok.Pressed += () => { apply((int)spin.Value); dlg.QueueFree(); };
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(ok);
        col.AddChild(row);
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ShowResizeDialog()
    {
        var dlg = new Window { Title = "Resize canvas", Size = new Vector2I(440, 240), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        col.AddChild(Ui.Dim("new dimensions (content outside the new bounds is clipped)"));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        SpinBox S(int v) => new() { MinValue = 1, MaxValue = 4096, Value = v, UpdateOnTextChanged = true };
        var w = S(_s.Model.SX); var d = S(_s.Model.SZ); var h = S(_s.Model.SY);
        row.AddChild(w); row.AddChild(Ui.Dim("×")); row.AddChild(d); row.AddChild(Ui.Dim("×")); row.AddChild(h);
        col.AddChild(row);
        var center = new CheckBox { Text = "keep content centered (footprint) — ground stays at layer 0", ButtonPressed = true };
        col.AddChild(center);
        col.AddChild(Ui.Stretch());
        var ok = Ui.PrimaryButton("Resize");
        ok.Pressed += () =>
        {
            int nw = (int)w.Value, nd = (int)d.Value, nh = (int)h.Value;
            int ax = center.ButtonPressed ? (nw - _s.Model.SX) / 2 : 0;
            int az = center.ButtonPressed ? (nd - _s.Model.SZ) / 2 : 0;
            _s.Swap(m => EditOps.Resize(m, nw, nh, nd, ax, 0, az));
            dlg.QueueFree();
        };
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        btnRow.AddChild(ok);
        col.AddChild(btnRow);
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private Control BuildRightDock()
    {
        var dock = new PanelContainer { CustomMinimumSize = new Vector2(224, 0) };
        dock.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.StrokeSoft, 0, 10));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", Ui.GapL);
        dock.AddChild(v);

        _palette = new PaletteDock(_s);
        _palette.ManageRequested += () => PaletteManager.Show(this, _s);
        v.AddChild(_palette);
        v.AddChild(Ui.Divider());
        v.AddChild(new StatsPanel(_s));
        v.AddChild(Ui.Divider());
        _coordsLabel = Ui.Dim("", 12);
        v.AddChild(_coordsLabel);
        v.AddChild(Ui.Stretch());
        return dock;
    }

    private string Status()
    {
        var sym = _s.Symmetry == SymmetryMode.None ? "" : $"  ·  sym {_s.Symmetry}";
        return $"VOXELERATOR  ·  {_s.Name}  ·  {_s.Model.SX}×{_s.Model.SZ}×{_s.Model.SY}" +
               $"  ·  layer {_s.Layer}/{_s.Model.SY - 1}  ·  {_s.Tool.ToString().ToLowerInvariant()}{sym}" +
               $"  ·  {(_s.Dirty ? "editing…" : "saved")}";
    }

    private string Hint() => _voxelMode
        ? "click place · ⌘click remove · ⌥click pick · RMB orbit · shift-drag pan · wheel zoom · P projection · K cutaway · T spin · Tab back"
        : "B✏ E◻ G▨ L╱ R▭ O◯ I◉ M⬚ · S sym · F fill · [ ] layer · ⇧O onion↑ · Space peek 3D · Tab 3D · ⌘Z undo · ⌘S save";

    public override void _ExitTree()
    {
        AppHost.Instance.StatusFn = () => "VOXELERATOR  ·  library";
        AppHost.Instance.HintFn = () => "";
        if (_s.Dirty) _s.SaveNow();
        _s.Dispose();
    }
}
