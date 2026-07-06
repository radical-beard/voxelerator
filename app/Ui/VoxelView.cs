using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The 3D half of the editor: orbit camera (orthographic or perspective),
/// direct voxel editing via a grid ray-walk (no physics), cutaway mode, the
/// active-layer plate, slot-boundary ground grid, night slider, turntable.
public partial class VoxelView : SubViewportContainer
{
    private readonly EditorState _s;
    private SubViewport _vp = null!;
    private Camera3D _cam = null!;
    private MeshInstance3D _meshInst = null!;
    private MeshInstance3D _layerPlate = null!;
    private ShaderMaterial _mat = null!;

    private float _yaw = -0.6f, _pitch = -0.5f, _dist;
    private Vector3 _target;
    private Vector2 _pressPos;
    private bool _meshDirty = true;

    public bool Orthographic = true;
    public bool Cutaway;
    public bool Turntable;
    public float Night;

    public VoxelView(EditorState state)
    {
        _s = state;
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        _s.Changed += () => _meshDirty = true;
        _s.StructureChanged += () => { _meshDirty = true; Frame(); };
    }

    public override void _Ready()
    {
        _vp = new SubViewport
        {
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_vp);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Ui.Bg0,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.1f,
        };
        _vp.AddChild(new WorldEnvironment { Environment = env });

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _meshInst = new MeshInstance3D { MaterialOverride = _mat };
        _vp.AddChild(_meshInst);

        _vp.AddChild(BuildGroundGrid());

        _layerPlate = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One },     // scaled per model
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Ui.Glow with { A = 0.14f },
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = false,
            },
        };
        _vp.AddChild(_layerPlate);

        _cam = new Camera3D { Near = 0.05f, Far = 500f, Fov = 40f };
        _vp.AddChild(_cam);

        Frame();
        RebuildMesh();
    }

    /// Reset the camera to frame the whole model.
    public void Frame()
    {
        double s = VoxelModel.VoxelMeters;
        var m = _s.Model;
        _target = new Vector3(0, (float)(m.SY * s * 0.45), 0);
        float radius = (float)Math.Sqrt(m.SX * m.SX + m.SY * m.SY + m.SZ * m.SZ) * (float)s * 0.5f;
        _dist = Math.Max(1.2f, radius * 2.4f);
    }

    // ---- per-frame -----------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Turntable) _yaw += (float)delta * 0.5f;
        if (_meshDirty) { _meshDirty = false; RebuildMesh(); }

        var rot = Basis.FromEuler(new Vector3(_pitch, _yaw, 0));
        var back = rot * new Vector3(0, 0, 1);
        _cam.Position = _target + back * _dist;
        _cam.Basis = rot;
        if (Orthographic)
        {
            _cam.Projection = Camera3D.ProjectionType.Orthogonal;
            _cam.Size = _dist * 0.8f;
        }
        else
        {
            _cam.Projection = Camera3D.ProjectionType.Perspective;
        }
        _mat.SetShaderParameter("night", Night);

        double s = VoxelModel.VoxelMeters;
        _layerPlate.Visible = true;
        _layerPlate.Scale = new Vector3((float)(_s.Model.SX * s) + 0.3f, 1, (float)(_s.Model.SZ * s) + 0.3f);
        _layerPlate.Position = new Vector3(0, (float)((_s.Layer + 1) * s) + 0.01f, 0);
    }

    private void RebuildMesh()
    {
        var model = _s.Model;
        if (Cutaway && _s.Layer < model.SY - 1)
        {
            model = model.Clone();
            for (int y = _s.Layer + 1; y < model.SY; y++) EditOps.ClearLayer(model, y);
        }
        var data = GreedyMesher.Mesh(model);
        if (data.IsEmpty) { _meshInst.Mesh = null; return; }

        var verts = new Vector3[data.VertexCount];
        var normals = new Vector3[data.VertexCount];
        var colors = new Color[data.VertexCount];
        for (int i = 0; i < data.VertexCount; i++)
        {
            verts[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
            normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
            colors[i] = new Color(data.Colors[i * 4], data.Colors[i * 4 + 1], data.Colors[i * 4 + 2], data.Colors[i * 4 + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _meshInst.Mesh = mesh;
    }

    /// Slot-boundary ground grid: fine lines per voxel row would be noise —
    /// draw the 8-voxel (one 4 m Neo Terrestria cell) boundaries in purple
    /// plus a soft outer frame, floating a hair above the ground plane.
    private Node3D BuildGroundGrid()
    {
        var im = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        double s = VoxelModel.VoxelMeters;
        var m = _s.Model;
        float ox = (float)(-m.SX * s / 2), oz = (float)(-m.SZ * s / 2);
        float w = (float)(m.SX * s), d = (float)(m.SZ * s);

        im.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);
        void Line(Vector3 a, Vector3 b, Color c)
        {
            im.SurfaceSetColor(c);
            im.SurfaceAddVertex(a);
            im.SurfaceSetColor(c);
            im.SurfaceAddVertex(b);
        }
        var slot = Ui.Glow with { A = 0.35f };
        var frame = Ui.TextDim with { A = 0.5f };
        for (int x = 0; x <= m.SX; x += 8)
            Line(new Vector3(ox + (float)(x * s), 0.005f, oz), new Vector3(ox + (float)(x * s), 0.005f, oz + d),
                x % 8 == 0 ? slot : frame);
        for (int z = 0; z <= m.SZ; z += 8)
            Line(new Vector3(ox, 0.005f, oz + (float)(z * s)), new Vector3(ox + w, 0.005f, oz + (float)(z * s)), slot);
        // outer frame
        Line(new Vector3(ox, 0.005f, oz), new Vector3(ox + w, 0.005f, oz), frame);
        Line(new Vector3(ox + w, 0.005f, oz), new Vector3(ox + w, 0.005f, oz + d), frame);
        Line(new Vector3(ox + w, 0.005f, oz + d), new Vector3(ox, 0.005f, oz + d), frame);
        Line(new Vector3(ox, 0.005f, oz + d), new Vector3(ox, 0.005f, oz), frame);
        im.SurfaceEnd();

        return new MeshInstance3D { Mesh = im };
    }

    // ---- input -------------------------------------------------------------------

    public override void _GuiInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMagnifyGesture mg:
                _dist = Mathf.Clamp(_dist / mg.Factor, 0.8f, 220f);
                AcceptEvent();
                return;
            case InputEventPanGesture pg:
                _yaw -= pg.Delta.X * 0.02f;
                _pitch = Mathf.Clamp(_pitch - pg.Delta.Y * 0.02f, -1.5f, 0.35f);
                AcceptEvent();
                return;
            case InputEventMouseButton mb:
                // shift+wheel walks the active-layer plate; plain wheel zooms
                if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
                {
                    if (mb.ShiftPressed) { _s.SetLayer(_s.Layer + 1); if (Cutaway) _meshDirty = true; }
                    else _dist = Mathf.Clamp(_dist * 0.92f, 0.8f, 220f);
                }
                if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                {
                    if (mb.ShiftPressed) { _s.SetLayer(_s.Layer - 1); if (Cutaway) _meshDirty = true; }
                    else _dist = Mathf.Clamp(_dist / 0.92f, 0.8f, 220f);
                }
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed) { _pressPos = mb.Position; GrabFocus(); }
                    else if ((_pressPos - mb.Position).Length() < 5f) ClickEdit(mb.Position, mb);
                }
                return;
            case InputEventMouseMotion mm:
                if ((mm.ButtonMask & MouseButtonMask.Right) != 0)
                {
                    _yaw -= mm.Relative.X * 0.008f;
                    _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.008f, -1.5f, 0.35f);
                }
                else if ((mm.ButtonMask & MouseButtonMask.Middle) != 0 ||
                         ((mm.ButtonMask & MouseButtonMask.Left) != 0 && mm.ShiftPressed))
                {
                    var rot = Basis.FromEuler(new Vector3(_pitch, _yaw, 0));
                    _target += rot * new Vector3(-mm.Relative.X, mm.Relative.Y, 0) * (_dist * 0.0012f);
                }
                else if ((mm.ButtonMask & MouseButtonMask.Left) != 0 && (_pressPos - mm.Position).Length() >= 5f)
                {
                    _yaw -= mm.Relative.X * 0.008f;          // LMB-drag orbits too
                    _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.008f, -1.5f, 0.35f);
                }
                return;
        }
    }

    /// Click = edit: place with the active color, ⌘/Ctrl-click removes,
    /// Alt-click eyedrops. Grid ray-walk, no physics.
    private void ClickEdit(Vector2 pos, InputEventMouseButton mb)
    {
        var origin = _cam.ProjectRayOrigin(pos);
        var dir = _cam.ProjectRayNormal(pos);
        int maxY = Cutaway ? _s.Layer : _s.Model.SY - 1;
        var hit = RayWalk(origin, dir, maxY);
        if (hit is not { } h) return;

        if (mb.AltPressed)
        {
            byte v = _s.Model.At(h.Cell.X, h.Cell.Y, h.Cell.Z);
            if (v != 0) { _s.Color = v; _s.NotifyChangedOnly(); }
            return;
        }
        if (mb.CtrlPressed || mb.MetaPressed)
        {
            _s.Do(m => m.Set(h.Cell.X, h.Cell.Y, h.Cell.Z, 0));
            return;
        }
        var p = h.Prev;
        if (p.Y < 0 || p.Y >= _s.Model.SY || p.X < 0 || p.X >= _s.Model.SX || p.Z < 0 || p.Z >= _s.Model.SZ)
            return;
        byte color = _s.Color;
        _s.Do(m => m.Set(p.X, p.Y, p.Z, color));
        _s.SetLayer(p.Y);                                    // follow the build upward
    }

    private (Vector3I Cell, Vector3I Prev)? RayWalk(Vector3 origin, Vector3 dir, int maxY)
    {
        double s = VoxelModel.VoxelMeters;
        var m = _s.Model;
        // to cell space
        var p = new Vector3(
            (float)((origin.X + m.SX * s / 2) / s),
            (float)(origin.Y / s),
            (float)((origin.Z + m.SZ * s / 2) / s));
        var d = dir.Normalized();

        int x = (int)Mathf.Floor(p.X), y = (int)Mathf.Floor(p.Y), z = (int)Mathf.Floor(p.Z);
        int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        float Next(float c, float o, float dd) => dd == 0 ? float.MaxValue : (((dd > 0 ? Mathf.Floor(o) + 1 : Mathf.Ceil(o) - 1) - o) / dd);
        float tx = Next(x, p.X, d.X), ty = Next(y, p.Y, d.Y), tz = Next(z, p.Z, d.Z);
        float dtx = d.X == 0 ? float.MaxValue : Math.Abs(1 / d.X);
        float dty = d.Y == 0 ? float.MaxValue : Math.Abs(1 / d.Y);
        float dtz = d.Z == 0 ? float.MaxValue : Math.Abs(1 / d.Z);

        var prev = new Vector3I(x, y, z);
        for (int i = 0; i < 4096; i++)
        {
            if (x >= 0 && z >= 0 && x < m.SX && z < m.SZ && y >= 0 && y <= maxY && m.At(x, y, z) != 0)
                return (new Vector3I(x, y, z), prev);
            if (y < 0 && d.Y < 0) break;                     // fell under the world

            prev = new Vector3I(x, y, z);
            if (tx <= ty && tx <= tz) { x += sx; tx += dtx; }
            else if (ty <= tz) { y += sy; ty += dty; }
            else { z += sz; tz += dtz; }

            // ground-plane placement: crossing y=0 downward inside the footprint
            if (y == -1 && prev.Y == 0 && prev.X >= 0 && prev.Z >= 0 && prev.X < m.SX && prev.Z < m.SZ)
                return (new Vector3I(prev.X, -1, prev.Z), prev);
        }
        return null;
    }
}
