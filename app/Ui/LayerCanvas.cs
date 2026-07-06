using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The primary editing surface: one layer, top-down, drawn like pixel art.
/// Owns the view transform (zoom/pan), tool gestures, onion skin, symmetry
/// guides, and the marquee. All mutations flow through EditorState.
public partial class LayerCanvas : Control
{
    private readonly EditorState _s;

    private float _zoom = 24f;                   // px per cell
    private Vector2 _pan;                        // px offset of cell (0,0)
    private bool _fitted;

    private Vector2I? _hover;
    private Vector2I? _dragAnchor;               // line/rect/ellipse/select anchor
    private bool _painting;                      // brush/eraser stroke open
    private bool _erasingStroke;
    private bool _movingSelection;
    private Vector2I _moveOffset;
    private (int W, int D, byte[] Cells, int SrcX, int SrcZ)? _liftBuffer;

    public event Action<Vector2I?>? HoverChanged;

    public LayerCanvas(EditorState state)
    {
        _s = state;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        _s.Changed += QueueRedraw;
        _s.StructureChanged += () => { _fitted = false; QueueRedraw(); };
        Resized += () => { _fitted = false; QueueRedraw(); };
    }

    public override void _ExitTree()
    {
        _s.Changed -= QueueRedraw;
    }

    // ---- view transform ------------------------------------------------------

    private void FitIfNeeded()
    {
        if (_fitted || Size.X < 10) return;
        _fitted = true;
        _zoom = Mathf.Clamp(0.85f * Math.Min(Size.X / _s.Model.SX, Size.Y / _s.Model.SZ), 2f, 64f);
        _pan = new Vector2(
            (Size.X - _s.Model.SX * _zoom) / 2f,
            (Size.Y - _s.Model.SZ * _zoom) / 2f);
    }

    private Vector2 CellToPx(float x, float z) => new(_pan.X + x * _zoom, _pan.Y + z * _zoom);

    private Vector2I? PxToCell(Vector2 px)
    {
        int x = (int)Mathf.Floor((px.X - _pan.X) / _zoom);
        int z = (int)Mathf.Floor((px.Y - _pan.Y) / _zoom);
        return x < 0 || z < 0 || x >= _s.Model.SX || z >= _s.Model.SZ ? null : new Vector2I(x, z);
    }

    private void ZoomAt(Vector2 mouse, float factor)
    {
        var before = (mouse - _pan) / _zoom;
        _zoom = Mathf.Clamp(_zoom * factor, 2f, 96f);
        _pan = mouse - before * _zoom;
        QueueRedraw();
    }

    // ---- input ------------------------------------------------------------------

    public override void _GuiInput(InputEvent ev)
    {
        FitIfNeeded();
        switch (ev)
        {
            case InputEventMagnifyGesture mg:
                ZoomAt(mg.Position, mg.Factor);
                AcceptEvent();
                return;
            case InputEventPanGesture pg:
                _pan -= pg.Delta * 14f;
                QueueRedraw();
                AcceptEvent();
                return;
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                return;
            case InputEventMouseMotion mm:
                MouseMove(mm);
                return;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        GrabFocus();
        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { ZoomAt(mb.Position, 1.12f); return; }
        if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { ZoomAt(mb.Position, 1f / 1.12f); return; }

        var cell = PxToCell(mb.Position);
        bool erase = mb.ButtonIndex == MouseButton.Right;
        if (mb.ButtonIndex != MouseButton.Left && !erase) return;

        if (mb.Pressed)
        {
            if (mb.AltPressed && cell is { } pickAt)         // quick eyedrop
            {
                byte v = _s.Model.At(pickAt.X, _s.Layer, pickAt.Y);
                if (v != 0) { _s.Color = v; _s.NotifyChangedOnly(); }
                return;
            }
            if (cell is null) return;

            switch (_s.Tool)
            {
                case ToolKind.Brush or ToolKind.Eraser:
                    _painting = true;
                    _erasingStroke = erase || _s.Tool == ToolKind.Eraser;
                    _s.Session.BeginBatch();
                    PaintAt(cell.Value);
                    break;
                case ToolKind.Bucket:
                    _s.Do(m => EditOps.FloodFill(m, _s.Layer, cell.Value.X, cell.Value.Y, erase ? (byte)0 : _s.Color));
                    break;
                case ToolKind.Picker:
                {
                    byte v = _s.Model.At(cell.Value.X, _s.Layer, cell.Value.Y);
                    if (v != 0) { _s.Color = v; _s.NotifyChangedOnly(); }
                    break;
                }
                case ToolKind.Line or ToolKind.Rect or ToolKind.Ellipse:
                    _dragAnchor = cell;
                    break;
                case ToolKind.Select:
                    if (_s.Selection is { } sel && Inside(sel, cell.Value))
                    {
                        _movingSelection = true;
                        _moveOffset = Vector2I.Zero;
                        _dragAnchor = cell;
                        LiftSelection(sel);
                    }
                    else
                    {
                        _dragAnchor = cell;
                        _s.Selection = (cell.Value.X, cell.Value.Y, 1, 1);
                    }
                    break;
            }
            QueueRedraw();
        }
        else
        {
            if (_painting)
            {
                _painting = false;
                _s.Session.CommitBatch();
                _s.NotifyChangedOnly();
            }
            else if (_movingSelection)
            {
                DropSelection();
            }
            else if (_dragAnchor is { } a && _hover is { } b)
            {
                byte color = erase ? (byte)0 : _s.Color;
                var sym = EffectiveSym();
                switch (_s.Tool)
                {
                    case ToolKind.Line:
                        _s.Do(m => EditOps.Line(m, _s.Layer, a.X, a.Y, b.X, b.Y, color, sym));
                        break;
                    case ToolKind.Rect:
                        _s.Do(m => EditOps.Rect(m, _s.Layer, a.X, a.Y, b.X, b.Y, color, _s.ShapeFilled, sym));
                        break;
                    case ToolKind.Ellipse:
                        _s.Do(m => EditOps.Ellipse(m, _s.Layer, a.X, a.Y, b.X, b.Y, color, _s.ShapeFilled, sym));
                        break;
                    case ToolKind.Select:
                        _s.Selection = Normalize(a, b);
                        _s.NotifyChangedOnly();
                        break;
                }
                _dragAnchor = null;
            }
            QueueRedraw();
        }
    }

    private void MouseMove(InputEventMouseMotion mm)
    {
        var cell = PxToCell(mm.Position);
        if (cell != _hover)
        {
            _hover = cell;
            HoverChanged?.Invoke(cell);
            QueueRedraw();
        }

        if ((mm.ButtonMask & MouseButtonMask.Middle) != 0 ||
            ((mm.ButtonMask & MouseButtonMask.Left) != 0 && mm.AltPressed && _s.Tool == ToolKind.Select))
        {
            _pan += mm.Relative;
            QueueRedraw();
            return;
        }

        if (_painting && cell is { } c) { PaintAt(c); }
        else if (_movingSelection && _dragAnchor is { } a && cell is { } b)
        {
            _moveOffset = new Vector2I(b.X - a.X, b.Y - a.Y);
            QueueRedraw();
        }
        else if (_dragAnchor is { } anchor && _s.Tool == ToolKind.Select && cell is { } b2)
        {
            _s.Selection = Normalize(anchor, b2);
            QueueRedraw();
        }
    }

    private void PaintAt(Vector2I cell)
    {
        byte color = _erasingStroke ? (byte)0 : _s.Color;
        EditOps.Paint(_s.Model, _s.Layer, cell.X, cell.Y, color, _s.BrushSize, EffectiveSym());
        _s.NotifyChangedOnly();
        QueueRedraw();
    }

    private SymmetryMode EffectiveSym()
        => _s.Symmetry == SymmetryMode.Radial4 && _s.Model.SX != _s.Model.SZ ? SymmetryMode.None : _s.Symmetry;

    // ---- selection ops (called from screen shortcuts too) -------------------------

    private static bool Inside((int X, int Z, int W, int D) sel, Vector2I c)
        => c.X >= sel.X && c.Y >= sel.Z && c.X < sel.X + sel.W && c.Y < sel.Z + sel.D;

    private static (int, int, int, int) Normalize(Vector2I a, Vector2I b)
    {
        int x0 = Math.Min(a.X, b.X), z0 = Math.Min(a.Y, b.Y);
        int x1 = Math.Max(a.X, b.X), z1 = Math.Max(a.Y, b.Y);
        return (x0, z0, x1 - x0 + 1, z1 - z0 + 1);
    }

    private void LiftSelection((int X, int Z, int W, int D) sel)
    {
        _s.Session.BeginBatch();
        var cells = new byte[sel.W * sel.D];
        for (int z = 0; z < sel.D; z++)
            for (int x = 0; x < sel.W; x++)
            {
                cells[z * sel.W + x] = _s.Model.At(sel.X + x, _s.Layer, sel.Z + z);
                _s.Model.Set(sel.X + x, _s.Layer, sel.Z + z, 0);
            }
        _liftBuffer = (sel.W, sel.D, cells, sel.X, sel.Z);
    }

    private void DropSelection()
    {
        if (_liftBuffer is not { } buf) { _movingSelection = false; return; }
        int nx = buf.SrcX + _moveOffset.X, nz = buf.SrcZ + _moveOffset.Y;
        for (int z = 0; z < buf.D; z++)
            for (int x = 0; x < buf.W; x++)
            {
                byte v = buf.Cells[z * buf.W + x];
                if (v != 0) _s.Model.Set(nx + x, _s.Layer, nz + z, v);
            }
        _s.Session.CommitBatch();
        _s.Selection = (Math.Clamp(nx, 0, _s.Model.SX - 1), Math.Clamp(nz, 0, _s.Model.SZ - 1), buf.W, buf.D);
        _liftBuffer = null;
        _movingSelection = false;
        _moveOffset = Vector2I.Zero;
        _s.NotifyChangedOnly();
    }

    public void CopySelection(bool cut)
    {
        if (_s.Selection is not { } sel) return;
        var cells = new byte[sel.W * sel.D];
        for (int z = 0; z < sel.D; z++)
            for (int x = 0; x < sel.W; x++)
                cells[z * sel.W + x] = _s.Model.At(sel.X + x, _s.Layer, sel.Z + z);
        _s.CellClipboard = (sel.W, sel.D, cells);
        if (cut)
            _s.Do(m =>
            {
                for (int z = 0; z < sel.D; z++)
                    for (int x = 0; x < sel.W; x++)
                        m.Set(sel.X + x, _s.Layer, sel.Z + z, 0);
            });
    }

    public void PasteClipboard()
    {
        if (_s.CellClipboard is not { } clip) return;
        int px = _s.Selection?.X ?? 0, pz = _s.Selection?.Z ?? 0;
        _s.Do(m =>
        {
            for (int z = 0; z < clip.D; z++)
                for (int x = 0; x < clip.W; x++)
                {
                    byte v = clip.Cells[z * clip.W + x];
                    if (v != 0) m.Set(px + x, _s.Layer, pz + z, v);
                }
        });
        _s.Selection = (px, pz, clip.W, clip.D);
        _s.Tool = ToolKind.Select;
    }

    public void DeleteSelection()
    {
        if (_s.Selection is not { } sel) return;
        _s.Do(m =>
        {
            for (int z = 0; z < sel.D; z++)
                for (int x = 0; x < sel.W; x++)
                    m.Set(sel.X + x, _s.Layer, sel.Z + z, 0);
        });
    }

    public void NudgeSelection(int dx, int dz)
    {
        if (_s.Selection is not { } sel) return;
        _s.Do(m =>
        {
            var cells = new byte[sel.W * sel.D];
            for (int z = 0; z < sel.D; z++)
                for (int x = 0; x < sel.W; x++)
                {
                    cells[z * sel.W + x] = m.At(sel.X + x, _s.Layer, sel.Z + z);
                    m.Set(sel.X + x, _s.Layer, sel.Z + z, 0);
                }
            for (int z = 0; z < sel.D; z++)
                for (int x = 0; x < sel.W; x++)
                {
                    byte v = cells[z * sel.W + x];
                    if (v != 0) m.Set(sel.X + dx + x, _s.Layer, sel.Z + dz + z, v);
                }
        });
        _s.Selection = (sel.X + dx, sel.Z + dz, sel.W, sel.D);
    }

    public void SelectAll()
    {
        _s.Tool = ToolKind.Select;
        _s.Selection = (0, 0, _s.Model.SX, _s.Model.SZ);
        _s.NotifyChangedOnly();
    }

    public void ClearSelection()
    {
        _s.Selection = null;
        _s.NotifyChangedOnly();
    }

    // ---- drawing ---------------------------------------------------------------

    public override void _Draw()
    {
        FitIfNeeded();
        var m = _s.Model;

        // checkerboard ground
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
                DrawCell(x, z, ((x + z) & 1) == 0 ? Ui.CanvasDark : Ui.CanvasLight);

        // onion skins
        if (_s.OnionBelow && _s.Layer > 0) DrawLayerCells(_s.Layer - 1, _s.OnionOpacity);
        if (_s.OnionAbove && _s.Layer < m.SY - 1) DrawLayerCells(_s.Layer + 1, _s.OnionOpacity * 0.7f);

        // the active layer
        DrawLayerCells(_s.Layer, 1f);

        // grid lines (fine + a purple-tinted major line every 8 = one NT cell)
        if (_zoom >= 6)
        {
            var fine = new Color(1, 1, 1, 0.07f);
            var major = Ui.Glow with { A = 0.30f };
            for (int x = 0; x <= m.SX; x++)
                DrawLine(CellToPx(x, 0), CellToPx(x, m.SZ), x % 8 == 0 ? major : fine, 1);
            for (int z = 0; z <= m.SZ; z++)
                DrawLine(CellToPx(0, z), CellToPx(m.SX, z), z % 8 == 0 ? major : fine, 1);
        }
        DrawRect(new Rect2(CellToPx(0, 0), new Vector2(m.SX, m.SZ) * _zoom), Ui.Stroke, false, 1.5f);

        // symmetry guides
        var sym = EffectiveSym();
        if (sym is SymmetryMode.X or SymmetryMode.XZ or SymmetryMode.Radial4)
            DrawDashedLine(CellToPx(m.SX / 2f, 0), CellToPx(m.SX / 2f, m.SZ), Ui.Glow with { A = 0.5f }, 1, 6);
        if (sym is SymmetryMode.Z or SymmetryMode.XZ or SymmetryMode.Radial4)
            DrawDashedLine(CellToPx(0, m.SZ / 2f), CellToPx(m.SX, m.SZ / 2f), Ui.Glow with { A = 0.5f }, 1, 6);

        DrawDragPreview();
        DrawSelectionOverlay();
        DrawHover();
    }

    private void DrawCell(int x, int z, Color c)
        => DrawRect(new Rect2(CellToPx(x, z), new Vector2(_zoom, _zoom)), c);

    private void DrawLayerCells(int layer, float alpha)
    {
        var m = _s.Model;
        bool moving = _movingSelection && layer == _s.Layer;
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
            {
                byte v = m.At(x, layer, z);
                if (v == 0) continue;
                var rgba = m.ColorAt(v);
                DrawCell(x, z, new Color(rgba.R, rgba.G, rgba.B, alpha));
                if (rgba.A >= 0.99f && alpha >= 1f)          // emissive: tiny glow tick
                    DrawRect(new Rect2(CellToPx(x, z) + new Vector2(_zoom * 0.35f, _zoom * 0.35f),
                        new Vector2(_zoom * 0.3f, _zoom * 0.3f)), new Color(1, 1, 1, 0.35f));
            }

        if (moving && _liftBuffer is { } buf)
            for (int z = 0; z < buf.D; z++)
                for (int x = 0; x < buf.W; x++)
                {
                    byte v = buf.Cells[z * buf.W + x];
                    if (v == 0) continue;
                    var rgba = m.ColorAt(v);
                    DrawCell(buf.SrcX + _moveOffset.X + x, buf.SrcZ + _moveOffset.Y + z,
                        new Color(rgba.R, rgba.G, rgba.B, 0.85f));
                }
    }

    private void DrawDragPreview()
    {
        if (_dragAnchor is not { } a || _hover is not { } b) return;
        if (_s.Tool is not (ToolKind.Line or ToolKind.Rect or ToolKind.Ellipse)) return;

        var pts = _s.Tool switch
        {
            ToolKind.Line => EditOps.LinePoints(a.X, a.Y, b.X, b.Y),
            ToolKind.Ellipse => EditOps.EllipsePoints(a.X, a.Y, b.X, b.Y, _s.ShapeFilled),
            _ => RectPoints(a, b),
        };
        var expanded = new HashSet<(int, int)>();
        foreach (var (x, z) in pts)
            EditOps.SymmetryPoints(x, z, _s.Model.SX, _s.Model.SZ, EffectiveSym(), expanded);

        var rgba = _s.Model.ColorAt(_s.Color);
        var ghost = new Color(rgba.R, rgba.G, rgba.B, 0.55f);
        foreach (var (x, z) in expanded)
            if (x >= 0 && z >= 0 && x < _s.Model.SX && z < _s.Model.SZ)
                DrawCell(x, z, ghost);
    }

    private List<(int, int)> RectPoints(Vector2I a, Vector2I b)
    {
        var (x0, z0, w, d) = Normalize(a, b);
        var pts = new List<(int, int)>();
        for (int z = z0; z < z0 + d; z++)
            for (int x = x0; x < x0 + w; x++)
                if (_s.ShapeFilled || x == x0 || x == x0 + w - 1 || z == z0 || z == z0 + d - 1)
                    pts.Add((x, z));
        return pts;
    }

    private void DrawSelectionOverlay()
    {
        if (_s.Selection is not { } sel) return;
        var pos = CellToPx(sel.X + (_movingSelection ? _moveOffset.X : 0), sel.Z + (_movingSelection ? _moveOffset.Y : 0));
        var rect = new Rect2(pos, new Vector2(sel.W, sel.D) * _zoom);
        DrawRect(rect, Ui.PrimaryDim with { A = 0.10f });
        DrawRect(rect, Ui.Glow, false, 1.5f);
    }

    private void DrawHover()
    {
        if (_hover is not { } h || _painting || _movingSelection) return;
        int lo = _s.BrushSize == 3 ? -1 : 0, hi = _s.BrushSize >= 2 ? 1 : 0;
        bool brushy = _s.Tool is ToolKind.Brush or ToolKind.Eraser;
        var rect = brushy
            ? new Rect2(CellToPx(h.X + lo, h.Y + lo), new Vector2(hi - lo + 1, hi - lo + 1) * _zoom)
            : new Rect2(CellToPx(h.X, h.Y), new Vector2(_zoom, _zoom));
        DrawRect(rect, Ui.Glow with { A = 0.9f }, false, 1.5f);
    }
}
