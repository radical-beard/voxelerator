namespace Voxelerator.Core;

/// Engine-agnostic mesh buffers. The Godot layer copies these straight into
/// ArrayMesh arrays; the software renderer and tests consume them headlessly.
public sealed class MeshData
{
    public readonly List<float> Positions = new();   // xyz triplets
    public readonly List<float> Normals = new();     // xyz triplets, flat-shaded
    public readonly List<float> Colors = new();      // rgba; a per the Rgba convention
    public readonly List<int> Indices = new();

    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Indices.Count / 3;
    public bool IsEmpty => Indices.Count == 0;
}

/// Quad-oriented builder. Godot front faces wind clockwise (seen from the
/// front); AddQuad takes corners counter-clockwise as seen from OUTSIDE the
/// surface and emits them in Godot's order, with one flat normal from the
/// cross product.
public sealed class MeshBuilder
{
    private readonly MeshData _m = new();
    public MeshData Data => _m;

    public void AddQuad(V3d a, V3d b, V3d c, V3d d, Rgba color)
    {
        var n = (b - a).Cross(c - a);
        double len = Math.Sqrt(n.LengthSq);
        if (len < 1e-12) return;                     // degenerate sliver
        n = new V3d(n.X / len, n.Y / len, n.Z / len);

        int baseIdx = _m.VertexCount;
        Push(a, n, color); Push(b, n, color); Push(c, n, color); Push(d, n, color);
        // counter-clockwise input -> clockwise emission for Godot's front face
        _m.Indices.Add(baseIdx); _m.Indices.Add(baseIdx + 2); _m.Indices.Add(baseIdx + 1);
        _m.Indices.Add(baseIdx); _m.Indices.Add(baseIdx + 3); _m.Indices.Add(baseIdx + 2);
    }

    private void Push(V3d p, V3d n, Rgba c)
    {
        _m.Positions.Add((float)p.X); _m.Positions.Add((float)p.Y); _m.Positions.Add((float)p.Z);
        _m.Normals.Add((float)n.X); _m.Normals.Add((float)n.Y); _m.Normals.Add((float)n.Z);
        _m.Colors.Add(c.R); _m.Colors.Add(c.G); _m.Colors.Add(c.B); _m.Colors.Add(c.A);
    }
}
