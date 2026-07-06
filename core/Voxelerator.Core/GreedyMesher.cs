namespace Voxelerator.Core;

/// Greedy face-merging mesher: one quad per maximal same-color rectangle per
/// slice, per direction. Face shading is baked into vertex color so the flat
/// voxel look reads even before lighting. Same algorithm (and same shade
/// table) as Neo Terrestria's renderer, so triangle counts shown in the
/// editor are representative of what the game will draw.
public static class GreedyMesher
{
    private static readonly (int dx, int dy, int dz, float shade)[] Dirs =
    [
        (0, 1, 0, 1.00f),   // +y top
        (0, -1, 0, 0.55f),  // -y bottom
        (1, 0, 0, 0.80f),   // +x
        (-1, 0, 0, 0.74f),  // -x
        (0, 0, 1, 0.86f),   // +z
        (0, 0, -1, 0.68f),  // -z
    ];

    public static MeshData Mesh(VoxelModel m)
    {
        var b = new MeshBuilder();
        double s = VoxelModel.VoxelMeters;
        double ox = -m.SX * s / 2, oz = -m.SZ * s / 2;   // center the footprint, y=0 at ground

        foreach (var (dx, dy, dz, shade) in Dirs)
        {
            // sweep axis u = the face normal axis; slice over it
            int axis = dx != 0 ? 0 : dy != 0 ? 1 : 2;
            int su = axis == 0 ? m.SX : axis == 1 ? m.SY : m.SZ;
            int sv = axis == 0 ? m.SY : axis == 1 ? m.SZ : m.SX;   // mask rows
            int sw = axis == 0 ? m.SZ : axis == 1 ? m.SX : m.SY;   // mask cols

            var mask = new byte[sv * sw];
            for (int u = 0; u < su; u++)
            {
                // build mask of visible faces in this slice
                for (int v = 0; v < sv; v++)
                    for (int w = 0; w < sw; w++)
                    {
                        var (x, y, z) = Unmap(axis, u, v, w);
                        byte here = m.At(x, y, z);
                        mask[v * sw + w] = here != 0 && m.At(x + dx, y + dy, z + dz) == 0 ? here : (byte)0;
                    }

                // greedy rectangles over the mask
                for (int v = 0; v < sv; v++)
                    for (int w = 0; w < sw;)
                    {
                        byte c = mask[v * sw + w];
                        if (c == 0) { w++; continue; }
                        int wEnd = w + 1;
                        while (wEnd < sw && mask[v * sw + wEnd] == c) wEnd++;
                        int vEnd = v + 1;
                        while (vEnd < sv)
                        {
                            bool rowOk = true;
                            for (int k = w; k < wEnd; k++) if (mask[vEnd * sw + k] != c) { rowOk = false; break; }
                            if (!rowOk) break;
                            vEnd++;
                        }
                        for (int vv = v; vv < vEnd; vv++)
                            for (int k = w; k < wEnd; k++) mask[vv * sw + k] = 0;

                        EmitRect(b, m, axis, dx + dy + dz > 0, u, v, w, vEnd, wEnd, c, shade, s, ox, oz);
                        w = wEnd;
                    }
            }
        }
        return b.Data;
    }

    private static (int x, int y, int z) Unmap(int axis, int u, int v, int w) => axis switch
    {
        0 => (u, v, w),   // x-normal: v=y, w=z
        1 => (w, u, v),   // y-normal: v=z, w=x
        _ => (v, w, u),   // z-normal: v=x, w=y
    };

    private static void EmitRect(MeshBuilder b, VoxelModel m, int axis, bool positive,
        int u, int v0, int w0, int v1, int w1, byte color, float shade, double s, double ox, double oz)
    {
        double plane = (u + (positive ? 1 : 0)) * s;
        var col = m.ColorAt(color);
        var c = new Rgba(col.R * shade, col.G * shade, col.B * shade, col.A);

        V3d P(double vv, double ww) => axis switch
        {
            0 => new V3d(ox + plane, vv * s, oz + ww * s),
            1 => new V3d(ox + ww * s, plane, oz + vv * s),
            _ => new V3d(ox + vv * s, ww * s, oz + plane),
        };

        V3d a = P(v0, w0), q = P(v1, w0), r = P(v1, w1), d = P(v0, w1);
        // orient so the computed normal matches the face direction — cheaper
        // to check a dot product than to reason about six axis permutations
        var want = axis switch
        {
            0 => new V3d(positive ? 1 : -1, 0, 0),
            1 => new V3d(0, positive ? 1 : -1, 0),
            _ => new V3d(0, 0, positive ? 1 : -1),
        };
        var n = (q - a).Cross(r - a);
        if (n.Dot(want) >= 0) b.AddQuad(a, q, r, d, c);
        else b.AddQuad(a, d, r, q, c);
    }
}
