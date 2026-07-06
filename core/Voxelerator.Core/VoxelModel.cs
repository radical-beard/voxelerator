namespace Voxelerator.Core;

/// A voxel volume on the layer-stack lattice: x/z horizontal, y up, layer n at
/// height y = n. Cells hold palette indices; 0 = air, 1..16 point into the
/// bound palette. Memory order is y-major, then z, then x (a horizontal layer
/// is one contiguous plane) — identical to Neo Terrestria's VoxelModel so
/// imports are a straight copy.
public sealed class VoxelModel
{
    public readonly int SX, SY, SZ;
    private byte[] _v;
    public Palette Palette;

    /// World scale used by the mesher and renderers: one voxel = 0.5 m
    /// (8 voxels = one 4 m Neo Terrestria cell).
    public const double VoxelMeters = 0.5;

    public VoxelModel(int sx, int sy, int sz, Palette palette)
    {
        if (sx < 1 || sy < 1 || sz < 1)
            throw new ArgumentException($"model dimensions must be >= 1 (got {sx}x{sy}x{sz})");
        SX = sx; SY = sy; SZ = sz;
        _v = new byte[sx * sy * sz];
        Palette = palette;
    }

    public byte At(int x, int y, int z)
        => x < 0 || y < 0 || z < 0 || x >= SX || y >= SY || z >= SZ ? (byte)0 : _v[(y * SZ + z) * SX + x];

    public void Set(int x, int y, int z, byte idx)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SX || y >= SY || z >= SZ) return;
        if (idx > Palette.Colors.Count) return;      // palette is law
        _v[(y * SZ + z) * SX + x] = idx;
    }

    /// Rgba for a cell index (index 0 = air = default).
    public Rgba ColorAt(byte idx) => idx == 0 ? default : Palette.RgbaOf(idx);

    public int Filled()
    {
        int n = 0;
        foreach (var b in _v) if (b != 0) n++;
        return n;
    }

    // ---- raw access for codecs, undo, and bulk ops -------------------------

    /// The live backing array (y-major, then z, then x). Treat as read-only
    /// unless you are a codec or the undo stack.
    public byte[] Voxels => _v;

    public byte[] CloneVoxels() => (byte[])_v.Clone();

    public void RestoreVoxels(byte[] snapshot)
    {
        if (snapshot.Length != _v.Length)
            throw new ArgumentException("snapshot size mismatch");
        snapshot.CopyTo(_v, 0);
    }

    /// Contiguous span of one horizontal layer (z-major rows of x).
    public Span<byte> Layer(int y) => _v.AsSpan(y * SZ * SX, SZ * SX);

    public VoxelModel Clone()
    {
        var m = new VoxelModel(SX, SY, SZ, Palette);
        _v.CopyTo(m._v, 0);
        return m;
    }
}
