namespace Voxelerator.Core;

public enum RenderView { Top, Front, Side, Iso }

public sealed record RenderOptions
{
    public int Size { get; init; } = 512;
    public RenderView View { get; init; } = RenderView.Iso;
    /// Orthographic by default; true = perspective projection.
    public bool Perspective { get; init; }
    /// 0 = day, 1 = full night (matte colors dim, emissive-tagged colors hold).
    public float Night { get; init; }
    /// Hide all layers above this one (cutaway view). Null = whole model.
    public int? CutawayAboveLayer { get; init; }
    /// Null = transparent background.
    public Rgba? Background { get; init; }
    /// Extra rotation of the eye around +y, in degrees — turntable frames.
    public double YawDegrees { get; init; }
}

/// Headless z-buffer rasterizer over the greedy mesh — the render the MCP
/// server hands back to an LLM and the library thumbnail both come from here,
/// so what agents see is what the mesher actually built. Deterministic pure
/// math; no engine, no GPU.
public static class SoftwareRenderer
{
    public static byte[] RenderPng(VoxelModel m, RenderOptions o)
    {
        var rgba = RenderRgba(m, o);
        return Png.WriteRgba(o.Size, o.Size, rgba);
    }

    public static byte[] RenderRgba(VoxelModel m, RenderOptions o)
    {
        var model = m;
        if (o.CutawayAboveLayer is int cut && cut < m.SY - 1)
        {
            model = m.Clone();
            for (int y = cut + 1; y < model.SY; y++) EditOps.ClearLayer(model, y);
        }

        var mesh = GreedyMesher.Mesh(model);
        int size = o.Size;
        var pix = new byte[size * size * 4];
        var depth = new float[size * size];
        Array.Fill(depth, float.MaxValue);

        if (o.Background is Rgba bg)
        {
            for (int i = 0; i < size * size; i++)
            {
                pix[i * 4 + 0] = (byte)Math.Clamp(bg.R * 255, 0, 255);
                pix[i * 4 + 1] = (byte)Math.Clamp(bg.G * 255, 0, 255);
                pix[i * 4 + 2] = (byte)Math.Clamp(bg.B * 255, 0, 255);
                pix[i * 4 + 3] = 255;
            }
        }
        if (mesh.IsEmpty) return pix;

        // ---- camera ----------------------------------------------------------
        double s = VoxelModel.VoxelMeters;
        var center = new V3d(0, model.SY * s / 2, 0);
        double radius = Math.Sqrt(Math.Pow(model.SX * s / 2, 2) + Math.Pow(model.SY * s / 2, 2) + Math.Pow(model.SZ * s / 2, 2));
        radius = Math.Max(radius, 0.75);

        var (eyeDir, up) = o.View switch
        {
            RenderView.Top => (new V3d(0, 1, 0), new V3d(0, 0, -1)),
            RenderView.Front => (new V3d(0, 0.02, 1).Normalized(), new V3d(0, 1, 0)),
            RenderView.Side => (new V3d(1, 0.02, 0).Normalized(), new V3d(0, 1, 0)),
            _ => (new V3d(1, 0.85, 1).Normalized(), new V3d(0, 1, 0)),
        };
        if (o.YawDegrees != 0 && o.View != RenderView.Top)
        {
            double a = o.YawDegrees * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            eyeDir = new V3d(eyeDir.X * ca + eyeDir.Z * sa, eyeDir.Y, -eyeDir.X * sa + eyeDir.Z * ca);
        }
        double dist = radius * 2.6;
        var eye = center + eyeDir * dist;

        var fwd = (center - eye).Normalized();
        var right = fwd.Cross(up).Normalized();
        var camUp = right.Cross(fwd);

        double halfW = radius * 1.12;                     // ortho half-extent
        double fov = 40.0 * Math.PI / 180.0;
        double f = 1.0 / Math.Tan(fov / 2);

        Span<double> sx = stackalloc double[3];
        Span<double> sy = stackalloc double[3];
        Span<double> sz = stackalloc double[3];

        int triCount = mesh.Indices.Count / 3;
        for (int t = 0; t < triCount; t++)
        {
            bool ok = true;
            for (int k = 0; k < 3; k++)
            {
                int vi = mesh.Indices[t * 3 + k];
                var p = new V3d(mesh.Positions[vi * 3], mesh.Positions[vi * 3 + 1], mesh.Positions[vi * 3 + 2]) - eye;
                double cx = p.Dot(right), cy = p.Dot(camUp), cz = p.Dot(fwd);
                if (o.Perspective)
                {
                    if (cz < 0.05) { ok = false; break; }   // behind the eye
                    sx[k] = (cx * f / cz * 0.5 + 0.5) * size;
                    sy[k] = (0.5 - cy * f / cz * 0.5) * size;
                    sz[k] = cz;
                }
                else
                {
                    sx[k] = (cx / halfW * 0.5 + 0.5) * size;
                    sy[k] = (0.5 - cy / halfW * 0.5) * size;
                    sz[k] = cz;
                }
            }
            if (!ok) continue;

            int vi0 = mesh.Indices[t * 3];
            float r0 = mesh.Colors[vi0 * 4], g0 = mesh.Colors[vi0 * 4 + 1],
                  b0 = mesh.Colors[vi0 * 4 + 2], a0 = mesh.Colors[vi0 * 4 + 3];

            // night response: matte dims, emissive holds and pops
            bool emissive = a0 >= 0.99f;
            float dim = emissive ? 1f : 1f - 0.68f * Math.Clamp(o.Night, 0f, 1f);
            byte cr = (byte)Math.Clamp(r0 * dim * 255, 0, 255);
            byte cg = (byte)Math.Clamp(g0 * dim * 255, 0, 255);
            byte cb = (byte)Math.Clamp(b0 * dim * 255, 0, 255);
            if (emissive && o.Night > 0.01f)
            {
                cr = (byte)Math.Clamp(cr * 1.25f + 20, 0, 255);
                cg = (byte)Math.Clamp(cg * 1.25f + 20, 0, 255);
                cb = (byte)Math.Clamp(cb * 1.25f + 20, 0, 255);
            }

            RasterTri(pix, depth, size,
                sx[0], sy[0], sz[0], sx[1], sy[1], sz[1], sx[2], sy[2], sz[2], cr, cg, cb);
        }
        return pix;
    }

    private static void RasterTri(byte[] pix, float[] depth, int size,
        double x0, double y0, double z0, double x1, double y1, double z1,
        double x2, double y2, double z2, byte r, byte g, byte b)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxX = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxY = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
        if (minX > maxX || minY > maxY) return;

        double area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (Math.Abs(area) < 1e-9) return;
        double inv = 1.0 / area;

        for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                double cx = px + 0.5, cy = py + 0.5;
                double w0 = ((x1 - cx) * (y2 - cy) - (y1 - cy) * (x2 - cx)) * inv;
                double w1 = ((x2 - cx) * (y0 - cy) - (y2 - cy) * (x0 - cx)) * inv;
                double w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = (float)(w0 * z0 + w1 * z1 + w2 * z2);
                int i = py * size + px;
                if (z >= depth[i]) continue;
                depth[i] = z;
                pix[i * 4 + 0] = r; pix[i * 4 + 1] = g; pix[i * 4 + 2] = b; pix[i * 4 + 3] = 255;
            }
    }
}
