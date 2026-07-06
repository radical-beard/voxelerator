using Voxelerator.Core;

namespace Voxelerator.App;

public enum ToolKind { Brush, Eraser, Bucket, Line, Rect, Ellipse, Picker, Select }

/// Everything the editor panels share about one open model: the undo-bearing
/// session, active tool state, selection/clipboards, autosave, backups, and
/// the file watcher that turns MCP edits into undo steps. Pure C# — Godot
/// nodes subscribe to Changed/StructureChanged and marshal to the main thread
/// themselves.
public sealed class EditorState : IDisposable
{
    public readonly string Folder;
    public string Name { get; private set; }
    public EditSession Session { get; private set; }
    public VoxelModel Model => Session.Model;

    // ---- tool state ----------------------------------------------------------
    public int Layer;
    public byte Color = 1;
    public ToolKind Tool = ToolKind.Brush;
    public int BrushSize = 1;
    public SymmetryMode Symmetry = SymmetryMode.None;
    public bool ShapeFilled;
    public bool OnionBelow = true;
    public bool OnionAbove;
    public float OnionOpacity;

    // ---- selection & clipboards ------------------------------------------------
    /// Marquee on the active layer, in cells (position + size), or null.
    public (int X, int Z, int W, int D)? Selection;
    public (int W, int D, byte[] Cells)? CellClipboard;
    public byte[]? LayerClipboard;

    // ---- events ------------------------------------------------------------------
    /// Voxels or selection changed — repaint.
    public event Action? Changed;
    /// Dimensions/layer count changed — rebuild structural UI too.
    public event Action? StructureChanged;

    private long _savedVersion;
    public bool Dirty => Session.Version != _savedVersion;

    private readonly FileSystemWatcher? _watcher;
    private long _suppressWatchUntilTicks;
    private volatile bool _externalDirty;
    private bool _snapshotTaken;
    private double _sinceAutosave;

    public EditorState(string folder)
    {
        Folder = folder;
        var (model, doc) = ModelStore.Load(folder);
        Name = doc.Name;

        // Adopt the shared library copy of the palette when it's compatible:
        // that's how a palette recolor in one model reaches every model bound
        // to the same name. The embedded copy stays the portable fallback.
        var shared = Registry.LoadPalette(model.Palette.Name);
        if (shared is not null)
        {
            int maxUsed = 0;
            foreach (var b in model.Voxels) if (b > maxUsed) maxUsed = b;
            if (shared.Colors.Count >= maxUsed) model.Palette = shared;
        }

        Session = new EditSession(model);
        _savedVersion = Session.Version;
        OnionOpacity = Registry.LoadSettings().OnionOpacity;
        Registry.Touch(folder, Name);

        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnDiskChanged;
            _watcher.Created += OnDiskChanged;
            _watcher.Deleted += OnDiskChanged;
            _watcher.Renamed += (_, _) => _externalDirty = true;
        }
        catch (Exception)
        {
            _watcher = null;                    // watching is best-effort
        }
    }

    private void OnDiskChanged(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressWatchUntilTicks)) return;
        if (e.Name is { } n && n.EndsWith(".tmp", StringComparison.Ordinal)) return;
        _externalDirty = true;
    }

    // ---- edit plumbing --------------------------------------------------------

    public void Do(Action<VoxelModel> edit)
    {
        Session.Do(edit);
        Changed?.Invoke();
    }

    public void Swap(Func<VoxelModel, VoxelModel> produce)
    {
        Session.Swap(produce);
        ClampLayer();
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!Session.Undo()) return;
        ClampLayer();
        Selection = null;
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!Session.Redo()) return;
        ClampLayer();
        Selection = null;
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    public void SetLayer(int y)
    {
        Layer = Math.Clamp(y, 0, Model.SY - 1);
        Selection = null;
        Changed?.Invoke();
    }

    private void ClampLayer() => Layer = Math.Clamp(Layer, 0, Model.SY - 1);

    /// First/last layer that holds any voxels (both 0 when empty).
    public (int First, int Last) NonEmptySpan()
    {
        var b = EditOps.Bounds(Model);
        return b is { } bb ? (bb.y0, bb.y1) : (0, 0);
    }

    // ---- persistence -------------------------------------------------------------

    public void SaveNow()
    {
        if (!_snapshotTaken)
        {
            try { Registry.Snapshot(Folder); } catch (IOException) { /* best-effort */ }
            _snapshotTaken = true;
        }
        SuppressWatcher();
        ModelStore.Save(Model, Name, Folder);
        _savedVersion = Session.Version;
        Registry.Touch(Folder, Name);
        try
        {
            File.WriteAllBytes(Registry.ThumbnailPath(Folder),
                SoftwareRenderer.RenderPng(Model, new RenderOptions { Size = 256, Background = new Rgba(0.086f, 0.078f, 0.11f) }));
        }
        catch (IOException) { /* thumbnails are derived; never block a save */ }
    }

    /// Call every frame from the screen; autosaves 2 s after the last edit
    /// (and no later than 60 s while continuously editing).
    public void TickAutosave(double dt, double sinceLastEdit)
    {
        _sinceAutosave += dt;
        if (!Dirty) { _sinceAutosave = 0; return; }
        if (sinceLastEdit > 2.0 || _sinceAutosave > 60.0)
        {
            SaveNow();
            _sinceAutosave = 0;
        }
    }

    /// True when the folder changed under us (MCP or another tool) and the
    /// reload was absorbed as an undo step. Call from the main thread.
    public bool PollExternalChanges()
    {
        if (!_externalDirty) return false;
        _externalDirty = false;
        try
        {
            var (reloaded, doc) = ModelStore.Load(Folder);
            if (reloaded.Voxels.AsSpan().SequenceEqual(Model.Voxels) &&
                reloaded.SX == Model.SX && reloaded.SY == Model.SY && reloaded.SZ == Model.SZ)
                return false;                    // our own write echoed back
            Name = doc.Name;
            Session.AbsorbExternal(reloaded);
            _savedVersion = Session.Version;     // disk state IS the saved state
            ClampLayer();
            Selection = null;
            StructureChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }
        catch (Exception)
        {
            return false;                        // half-written external state; next event retries
        }
    }

    private void SuppressWatcher()
        => Interlocked.Exchange(ref _suppressWatchUntilTicks, DateTime.UtcNow.AddSeconds(1.5).Ticks);

    public void NotifyChangedOnly() => Changed?.Invoke();

    public void Dispose() => _watcher?.Dispose();
}
