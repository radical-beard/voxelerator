namespace Voxelerator.Core;

public enum SymmetryMode { None, X, Z, XZ, Radial4 }

/// Every mutation the editor and the MCP server can perform, as pure
/// functions over VoxelModel. In-place ops mutate; shape-changing ops return
/// a new model (dimensions are immutable by construction). Symmetry expands
/// painted points, so mirrored primitives are exact reflections.
public static class EditOps
{
    // ---- symmetry ----------------------------------------------------------

    public static void SymmetryPoints(int x, int z, int w, int d, SymmetryMode mode, HashSet<(int, int)> into)
    {
        into.Add((x, z));
        switch (mode)
        {
            case SymmetryMode.X:
                into.Add((w - 1 - x, z));
                break;
            case SymmetryMode.Z:
                into.Add((x, d - 1 - z));
                break;
            case SymmetryMode.XZ:
                into.Add((w - 1 - x, z));
                into.Add((x, d - 1 - z));
                into.Add((w - 1 - x, d - 1 - z));
                break;
            case SymmetryMode.Radial4:
                if (w != d) throw new InvalidOperationException("radial symmetry needs a square footprint");
                int cx = x, cz = z;
                for (int i = 0; i < 3; i++)
                {
                    (cx, cz) = (w - 1 - cz, cx);      // 90° rotation on the grid
                    into.Add((cx, cz));
                }
                break;
        }
    }

    private static void Apply(VoxelModel m, int y, IEnumerable<(int x, int z)> points, byte idx, SymmetryMode sym)
    {
        var expanded = new HashSet<(int, int)>();
        foreach (var (x, z) in points) SymmetryPoints(x, z, m.SX, m.SZ, sym, expanded);
        foreach (var (x, z) in expanded) m.Set(x, y, z, idx);
    }

    // ---- layer painting ----------------------------------------------------

    /// Square brush of size 1/2/3 centered-ish on (x,z) (size 2 extends +x/+z).
    public static void Paint(VoxelModel m, int y, int x, int z, byte idx,
        int brushSize = 1, SymmetryMode sym = SymmetryMode.None)
    {
        var pts = new List<(int, int)>();
        int lo = brushSize == 3 ? -1 : 0, hi = brushSize >= 2 ? 1 : 0;
        for (int dz = lo; dz <= hi; dz++)
            for (int dx = lo; dx <= hi; dx++)
                pts.Add((x + dx, z + dz));
        Apply(m, y, pts, idx, sym);
    }

    /// Contiguous same-value region on one layer (4-connected).
    public static void FloodFill(VoxelModel m, int y, int x, int z, byte idx)
    {
        if (x < 0 || z < 0 || x >= m.SX || z >= m.SZ) return;
        byte target = m.At(x, y, z);
        if (target == idx) return;
        var stack = new Stack<(int, int)>();
        stack.Push((x, z));
        while (stack.Count > 0)
        {
            var (px, pz) = stack.Pop();
            if (px < 0 || pz < 0 || px >= m.SX || pz >= m.SZ) continue;
            if (m.At(px, y, pz) != target) continue;
            m.Set(px, y, pz, idx);
            stack.Push((px + 1, pz)); stack.Push((px - 1, pz));
            stack.Push((px, pz + 1)); stack.Push((px, pz - 1));
        }
    }

    public static void Line(VoxelModel m, int y, int x0, int z0, int x1, int z1, byte idx,
        SymmetryMode sym = SymmetryMode.None)
        => Apply(m, y, LinePoints(x0, z0, x1, z1), idx, sym);

    public static List<(int x, int z)> LinePoints(int x0, int z0, int x1, int z1)
    {
        var pts = new List<(int, int)>();
        int dx = Math.Abs(x1 - x0), dz = -Math.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1, err = dx + dz;
        while (true)
        {
            pts.Add((x0, z0));
            if (x0 == x1 && z0 == z1) break;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x0 += sx; }
            if (e2 <= dx) { err += dx; z0 += sz; }
        }
        return pts;
    }

    public static void Rect(VoxelModel m, int y, int x0, int z0, int x1, int z1, byte idx,
        bool filled, SymmetryMode sym = SymmetryMode.None)
    {
        Order(ref x0, ref x1); Order(ref z0, ref z1);
        var pts = new List<(int, int)>();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (filled || x == x0 || x == x1 || z == z0 || z == z1)
                    pts.Add((x, z));
        Apply(m, y, pts, idx, sym);
    }

    /// Ellipse inscribed in the (inclusive) bounding box. Fill = cell-center
    /// inside test; outline = filled cells with at least one 4-neighbor out.
    public static void Ellipse(VoxelModel m, int y, int x0, int z0, int x1, int z1, byte idx,
        bool filled, SymmetryMode sym = SymmetryMode.None)
        => Apply(m, y, EllipsePoints(x0, z0, x1, z1, filled), idx, sym);

    public static List<(int x, int z)> EllipsePoints(int x0, int z0, int x1, int z1, bool filled)
    {
        Order(ref x0, ref x1); Order(ref z0, ref z1);
        double cx = (x0 + x1 + 1) / 2.0, cz = (z0 + z1 + 1) / 2.0;
        double rx = (x1 - x0 + 1) / 2.0, rz = (z1 - z0 + 1) / 2.0;
        bool Inside(int x, int z)
        {
            if (x < x0 || x > x1 || z < z0 || z > z1) return false;
            double nx = (x + 0.5 - cx) / rx, nz = (z + 0.5 - cz) / rz;
            return nx * nx + nz * nz <= 1.0;
        }
        var pts = new List<(int, int)>();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!Inside(x, z)) continue;
                bool edge = !Inside(x - 1, z) || !Inside(x + 1, z) || !Inside(x, z - 1) || !Inside(x, z + 1);
                if (filled || edge) pts.Add((x, z));
            }
        return pts;
    }

    // ---- volumetric primitives (inclusive coordinates) ---------------------

    public static void FillBox(VoxelModel m, int x0, int y0, int z0, int x1, int y1, int z1, byte idx)
    {
        Order(ref x0, ref x1); Order(ref y0, ref y1); Order(ref z0, ref z1);
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    m.Set(x, y, z, idx);
    }

    public static void HollowBox(VoxelModel m, int x0, int y0, int z0, int x1, int y1, int z1, byte idx)
    {
        Order(ref x0, ref x1); Order(ref y0, ref y1); Order(ref z0, ref z1);
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (x == x0 || x == x1 || y == y0 || y == y1 || z == z0 || z == z1)
                        m.Set(x, y, z, idx);
    }

    /// Vertical (y-axis) cylinder: circle center (cx, cz) in cell units,
    /// radius r in cells, layers y0..y1 inclusive.
    public static void FillCylinder(VoxelModel m, double cx, double cz, int y0, int y1, double r, byte idx)
    {
        Order(ref y0, ref y1);
        int xLo = (int)Math.Floor(cx - r), xHi = (int)Math.Ceiling(cx + r);
        int zLo = (int)Math.Floor(cz - r), zHi = (int)Math.Ceiling(cz + r);
        for (int y = y0; y <= y1; y++)
            for (int z = zLo; z <= zHi; z++)
                for (int x = xLo; x <= xHi; x++)
                {
                    double dx = x + 0.5 - cx, dz = z + 0.5 - cz;
                    if (dx * dx + dz * dz <= r * r) m.Set(x, y, z, idx);
                }
    }

    // ---- layer operations (in place) ---------------------------------------

    public static byte[] CopyLayer(VoxelModel m, int y) => m.Layer(y).ToArray();

    public static void PasteLayer(VoxelModel m, int y, ReadOnlySpan<byte> data)
    {
        if (data.Length != m.SX * m.SZ) throw new ArgumentException("layer data size mismatch");
        data.CopyTo(m.Layer(y));
    }

    public static void ClearLayer(VoxelModel m, int y) => m.Layer(y).Clear();

    public static void SwapLayers(VoxelModel m, int a, int b)
    {
        if (a == b) return;
        var tmp = CopyLayer(m, a);
        m.Layer(b).CopyTo(m.Layer(a));
        PasteLayer(m, b, tmp);
    }

    /// Repeat layer y into the n layers above it (clamped to the model top).
    public static void Extrude(VoxelModel m, int y, int n)
    {
        var src = CopyLayer(m, y);
        for (int i = 1; i <= n && y + i < m.SY; i++)
            PasteLayer(m, y + i, src);
    }

    public static void FlipLayer(VoxelModel m, int y, bool alongX)
    {
        var src = CopyLayer(m, y);
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
            {
                byte v = src[z * m.SX + x];
                if (alongX) m.Set(m.SX - 1 - x, y, z, v);
                else m.Set(x, y, m.SZ - 1 - z, v);
            }
    }

    /// 90° clockwise (viewed from above). Needs a square footprint in place.
    public static void RotateLayer90(VoxelModel m, int y)
    {
        if (m.SX != m.SZ) throw new InvalidOperationException("per-layer rotate needs a square footprint (rotate the whole model instead)");
        var src = CopyLayer(m, y);
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
                m.Set(m.SX - 1 - z, y, x, src[z * m.SX + x]);
    }

    /// Shift layer contents, clipping at the edges.
    public static void ShiftLayer(VoxelModel m, int y, int dx, int dz)
    {
        var src = CopyLayer(m, y);
        ClearLayer(m, y);
        for (int z = 0; z < m.SZ; z++)
            for (int x = 0; x < m.SX; x++)
            {
                byte v = src[z * m.SX + x];
                if (v != 0) m.Set(x + dx, y, z + dz, v);
            }
    }

    // ---- whole-model operations ---------------------------------------------

    public static void MirrorModel(VoxelModel m, bool alongX)
    {
        for (int y = 0; y < m.SY; y++) FlipLayer(m, y, alongX);
    }

    public static void ReplaceColor(VoxelModel m, byte from, byte to)
    {
        var v = m.Voxels;
        for (int i = 0; i < v.Length; i++) if (v[i] == from) v[i] = to;
    }

    /// Remove palette slot `slot` (1-based): cells using it get `remapTo`
    /// (0 = air, or any other pre-removal slot number), every higher slot
    /// shifts down one, and the palette entry disappears. One atomic op so
    /// model and palette can never disagree.
    public static void RemovePaletteSlot(VoxelModel m, byte slot, byte remapTo)
    {
        if (slot < 1 || slot > m.Palette.Colors.Count) throw new ArgumentOutOfRangeException(nameof(slot));
        if (remapTo == slot) throw new ArgumentException("cannot remap a slot to itself");
        if (remapTo > m.Palette.Colors.Count) throw new ArgumentOutOfRangeException(nameof(remapTo));
        m.Palette = m.Palette.Clone();                       // undo snapshots share the old instance
        byte finalTarget = remapTo > slot ? (byte)(remapTo - 1) : remapTo;
        var v = m.Voxels;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == slot) v[i] = 255;                    // sentinel
            else if (v[i] > slot && v[i] != 255) v[i]--;
        }
        for (int i = 0; i < v.Length; i++) if (v[i] == 255) v[i] = finalTarget;
        m.Palette.Colors.RemoveAt(slot - 1);
    }

    /// Swap two palette slots (reorder): colors trade places and every cell
    /// renumbers, so nothing changes visually.
    public static void SwapPaletteSlots(VoxelModel m, byte a, byte b)
    {
        if (a == b) return;
        if (a < 1 || b < 1 || a > m.Palette.Colors.Count || b > m.Palette.Colors.Count)
            throw new ArgumentOutOfRangeException();
        m.Palette = m.Palette.Clone();                       // undo snapshots share the old instance
        (m.Palette.Colors[a - 1], m.Palette.Colors[b - 1]) = (m.Palette.Colors[b - 1], m.Palette.Colors[a - 1]);
        var v = m.Voxels;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == a) v[i] = b;
            else if (v[i] == b) v[i] = a;
        }
    }

    public static void TranslateModel(VoxelModel m, int dx, int dy, int dz)
    {
        var snap = m.CloneVoxels();
        Array.Clear(m.Voxels);
        for (int y = 0; y < m.SY; y++)
            for (int z = 0; z < m.SZ; z++)
                for (int x = 0; x < m.SX; x++)
                {
                    byte v = snap[(y * m.SZ + z) * m.SX + x];
                    if (v != 0) m.Set(x + dx, y + dy, z + dz, v);
                }
    }

    // ---- shape-changing operations (return a new model) ----------------------

    public static VoxelModel InsertLayer(VoxelModel m, int at, byte[]? content = null)
    {
        if (at < 0 || at > m.SY) throw new ArgumentOutOfRangeException(nameof(at));
        var r = new VoxelModel(m.SX, m.SY + 1, m.SZ, m.Palette);
        for (int y = 0; y < m.SY; y++)
            m.Layer(y).CopyTo(r.Layer(y < at ? y : y + 1));
        if (content is not null) PasteLayer(r, at, content);
        return r;
    }

    public static VoxelModel DeleteLayer(VoxelModel m, int at)
    {
        if (m.SY <= 1) throw new InvalidOperationException("a model needs at least one layer");
        if (at < 0 || at >= m.SY) throw new ArgumentOutOfRangeException(nameof(at));
        var r = new VoxelModel(m.SX, m.SY - 1, m.SZ, m.Palette);
        for (int y = 0; y < m.SY; y++)
        {
            if (y == at) continue;
            m.Layer(y).CopyTo(r.Layer(y < at ? y : y - 1));
        }
        return r;
    }

    /// Anchor is where the old content sits in the new volume, in cells
    /// (0,0,0 = min corner). Content outside the new bounds is clipped.
    public static VoxelModel Resize(VoxelModel m, int w, int h, int d, int ax = 0, int ay = 0, int az = 0)
    {
        var r = new VoxelModel(w, h, d, m.Palette);
        for (int y = 0; y < m.SY; y++)
            for (int z = 0; z < m.SZ; z++)
                for (int x = 0; x < m.SX; x++)
                {
                    byte v = m.At(x, y, z);
                    if (v != 0) r.Set(x + ax, y + ay, z + az, v);
                }
        return r;
    }

    /// Content bounding box, or null when the model is empty.
    public static (int x0, int y0, int z0, int x1, int y1, int z1)? Bounds(VoxelModel m)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = -1, y1 = -1, z1 = -1;
        for (int y = 0; y < m.SY; y++)
            for (int z = 0; z < m.SZ; z++)
                for (int x = 0; x < m.SX; x++)
                    if (m.At(x, y, z) != 0)
                    {
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                        x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                    }
        return x1 < 0 ? null : (x0, y0, z0, x1, y1, z1);
    }

    public static VoxelModel Trim(VoxelModel m)
    {
        var b = Bounds(m);
        if (b is null) return m.Clone();
        var (x0, y0, z0, x1, y1, z1) = b.Value;
        return Resize(m, x1 - x0 + 1, y1 - y0 + 1, z1 - z0 + 1, -x0, -y0, -z0);
    }

    /// 90° clockwise about +y (viewed from above); footprint W/D swap.
    public static VoxelModel RotateModel90(VoxelModel m)
    {
        var r = new VoxelModel(m.SZ, m.SY, m.SX, m.Palette);
        for (int y = 0; y < m.SY; y++)
            for (int z = 0; z < m.SZ; z++)
                for (int x = 0; x < m.SX; x++)
                {
                    byte v = m.At(x, y, z);
                    if (v != 0) r.Set(m.SZ - 1 - z, y, x, v);
                }
        return r;
    }

    private static void Order(ref int a, ref int b) { if (a > b) (a, b) = (b, a); }
}
