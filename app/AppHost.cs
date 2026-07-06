using Godot;
using Voxelerator.App.Ui;
using Voxelerator.Core;

namespace Voxelerator.App;

// Host shim: builds the whole UI in code (the only editor-authored scene is
// the root main.tscn), routes between the library and editor screens, and
// hands live-panel composition to EvaLuate (`vox` api + scripts/*.evt).
public partial class AppHost : Node
{
    public static AppHost Instance { get; private set; } = null!;

    /// Status/hint providers for the EvaLuate shell strip; the active screen
    /// swaps these in.
    public Func<string> StatusFn = () => "VOXELERATOR  ·  library";
    public Func<string> HintFn = () => "";

    private Control _screens = null!;
    private EditorScreen? _editor;
    private long _shotAtFrame = -1;

    public override void _Ready()
    {
        Instance = this;
        var win = GetWindow();
        win.Title = "Voxelerator";

        // HiDPI: Godot windows are sized in physical pixels and canvas items
        // render 1:1, so on a Retina display everything comes up half-size
        // and unreadable. Scale the content AND the window by the backing
        // scale; embedded dialogs/popups inherit the canvas scale.
        float scale = Mathf.Clamp((float)DisplayServer.ScreenGetScale(), 1f, 3f);
        Ui.Ui.DisplayScale = scale;
        if (scale > 1.01f)
        {
            win.ContentScaleFactor = scale;
            win.Size = new Vector2I((int)(1440 * scale), (int)(900 * scale));
            win.MoveToCenter();
        }
        win.MinSize = new Vector2I((int)(1100 * scale), (int)(700 * scale));

        Registry.SeedBuiltinPalettes();

        var bg = new ColorRect { Color = Ui.Ui.Bg0, MouseFilter = Control.MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bgLayer = new CanvasLayer { Layer = -10 };
        bgLayer.AddChild(bg);
        AddChild(bgLayer);

        _screens = new Control { Theme = Ui.Ui.Build() };
        _screens.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screens);

        ShowLibrary();
        BootEvaluateShell();

        GetTree().AutoAcceptQuit = false;

        // self-screenshot smoke: VOX_SCREENSHOT=<path.png> captures a frame
        // and quits — visual regressions get caught without a human present.
        // VOX_SHOT_STATE=library|editor|voxel stages what gets captured.
        if (System.Environment.GetEnvironmentVariable("VOX_SCREENSHOT") is not null)
        {
            _shotAtFrame = (long)Engine.GetProcessFrames() + 90;
            StageScreenshotState();
        }
    }

    private void BootEvaluateShell()
    {
        try
        {
            var runtime = new Evaluate.EvaluateRuntime();
            runtime.RegisterApi("vox", new VoxApi(this));
            AddChild(runtime);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[voxelerator] EvaLuate shell unavailable ({e.Message}) — running without the strip");
        }
    }

    // ---- routing -----------------------------------------------------------

    public void ShowLibrary()
    {
        SwapScreen(new LibraryScreen());
        _editor = null;
        StatusFn = () => "VOXELERATOR  ·  library";
        HintFn = () => "";
    }

    public void OpenModel(string folder)
    {
        try
        {
            var editor = new EditorScreen(folder);
            SwapScreen(editor);
            _editor = editor;
        }
        catch (Exception e) when (e is ModelFormatException or FormatException or IOException)
        {
            ShowLibrary();
            var dlg = new AcceptDialog { Title = "Can't open model", DialogText = e.Message };
            _screens.AddChild(dlg);
            dlg.PopupCentered();
        }
    }

    private void SwapScreen(Control screen)
    {
        foreach (var child in _screens.GetChildren()) child.QueueFree();
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screens.AddChild(screen);
    }

    // ---- screenshot smoke -----------------------------------------------------

    private void StageScreenshotState()
    {
        var state = System.Environment.GetEnvironmentVariable("VOX_SHOT_STATE") ?? "library";
        if (state == "library") return;

        var dir = Path.Combine(Path.GetTempPath(), "voxelerator-shot", "demo-rocket");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var palette = Registry.LoadPalette("neo-terrestria") ?? BuiltinPalettes.NeoTerrestria();
        var m = DemoModel(palette);
        ModelStore.Save(m, "demo-rocket", dir);
        Registry.Touch(dir, "demo-rocket");
        OpenModel(dir);
        if (state == "voxel")
            Callable.From(() => _editor?.SetVoxelView(true)).CallDeferred();
    }

    /// A little rocket on a pad — enough shape, color, and emissive to judge
    /// the whole pipeline from one frame.
    private static VoxelModel DemoModel(Palette palette)
    {
        var m = new VoxelModel(16, 22, 16, palette);
        EditOps.FillBox(m, 2, 0, 2, 13, 0, 13, 2);               // pad
        EditOps.HollowBox(m, 5, 1, 5, 10, 2, 10, 7);             // base ring
        EditOps.FillCylinder(m, 8, 8, 1, 14, 3.4, 14);           // hull (civic white)
        EditOps.FillCylinder(m, 8, 8, 15, 17, 2.4, 14);          // nose taper
        EditOps.FillCylinder(m, 8, 8, 18, 19, 1.4, 3);           // tip (roof red)
        m.Set(8, 10, 4, 5); m.Set(8, 8, 4, 5); m.Set(8, 6, 4, 5); // windows (emissive)
        EditOps.FillBox(m, 3, 1, 7, 4, 6, 8, 6);                 // gantry (metal light)
        EditOps.FillBox(m, 4, 6, 8, 7, 6, 8, 6);                 // gantry arm
        return m;
    }

    public override void _Process(double delta)
    {
        if (_shotAtFrame < 0 || (long)Engine.GetProcessFrames() < _shotAtFrame) return;
        _shotAtFrame = -1;
        var path = System.Environment.GetEnvironmentVariable("VOX_SCREENSHOT")!;
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[voxelerator] screenshot -> {path}");
        GetTree().Quit();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            _editor?.SaveNow();
            GetTree().Quit();
        }
    }
}
