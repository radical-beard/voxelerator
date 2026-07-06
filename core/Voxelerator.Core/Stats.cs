namespace Voxelerator.Core;

public sealed record ModelStats(
    int W, int D, int H,
    int Filled,
    int Triangles,
    int[] PerColor,          // index 0 unused; 1..16 = cell counts per palette slot
    (int X0, int Y0, int Z0, int X1, int Y1, int Z1)? Bounds)
{
    public int ColorsUsed
    {
        get
        {
            int n = 0;
            for (int i = 1; i < PerColor.Length; i++) if (PerColor[i] > 0) n++;
            return n;
        }
    }
}

public static class Stats
{
    /// Triangle count runs the real greedy mesher, so the number the editor
    /// shows is the number the game will draw.
    public static ModelStats Compute(VoxelModel m)
    {
        var perColor = new int[Palette.MaxColors + 1];
        int filled = 0;
        foreach (var b in m.Voxels)
            if (b != 0) { filled++; perColor[b]++; }
        int tris = GreedyMesher.Mesh(m).TriangleCount;
        return new ModelStats(m.SX, m.SZ, m.SY, filled, tris, perColor, EditOps.Bounds(m));
    }
}
