using Voxelerator.Core;
using Xunit;

namespace Voxelerator.Core.Tests;

public class MesherTests
{
    [Fact]
    public void SingleVoxelIsTwelveTriangles()
    {
        var m = new VoxelModel(3, 3, 3, Fixtures.Pal());
        m.Set(1, 1, 1, 1);
        var mesh = GreedyMesher.Mesh(m);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(24, mesh.VertexCount);
    }

    [Fact]
    public void AdjacentSameColorMerges()
    {
        var m = new VoxelModel(4, 1, 1, Fixtures.Pal());
        m.Set(0, 0, 0, 1); m.Set(1, 0, 0, 1); m.Set(2, 0, 0, 1); m.Set(3, 0, 0, 1);
        // a 4x1x1 bar of one color greedy-meshes to exactly 6 quads
        Assert.Equal(12, GreedyMesher.Mesh(m).TriangleCount);
    }

    [Fact]
    public void DifferentColorsDoNotMerge()
    {
        var m = new VoxelModel(2, 1, 1, Fixtures.Pal());
        m.Set(0, 0, 0, 1); m.Set(1, 0, 0, 2);
        // two cubes, hidden shared faces: 10 visible quads
        Assert.Equal(20, GreedyMesher.Mesh(m).TriangleCount);
    }

    [Fact]
    public void EmptyModelIsEmptyMesh()
    {
        var m = new VoxelModel(4, 4, 4, Fixtures.Pal());
        Assert.True(GreedyMesher.Mesh(m).IsEmpty);
    }

    [Fact]
    public void GroundSitsAtYZeroCenteredFootprint()
    {
        var m = new VoxelModel(2, 1, 2, Fixtures.Pal());
        EditOps.FillBox(m, 0, 0, 0, 1, 0, 1, 1);
        var mesh = GreedyMesher.Mesh(m);
        float minY = float.MaxValue, minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            minY = Math.Min(minY, mesh.Positions[i * 3 + 1]);
            minX = Math.Min(minX, mesh.Positions[i * 3]);
            maxX = Math.Max(maxX, mesh.Positions[i * 3]);
        }
        Assert.Equal(0f, minY, 3);
        Assert.Equal(-maxX, minX, 3);                       // centered on the footprint
    }
}

public class StatsTests
{
    [Fact]
    public void CountsAndTrianglesAgree()
    {
        var m = Fixtures.House();
        var s = Stats.Compute(m);
        Assert.Equal(m.Filled(), s.Filled);
        Assert.Equal(GreedyMesher.Mesh(m).TriangleCount, s.Triangles);
        Assert.Equal(4, s.ColorsUsed);
        Assert.NotNull(s.Bounds);
        Assert.Equal((6, 4, 5), (s.W, s.D, s.H));
    }
}

public class RendererTests
{
    [Fact]
    public void RenderIsDeterministicAndNonEmpty()
    {
        var m = Fixtures.House();
        var o = new RenderOptions { Size = 96 };
        var a = SoftwareRenderer.RenderPng(m, o);
        var b = SoftwareRenderer.RenderPng(m, o);
        Assert.Equal(a, b);

        var rgba = SoftwareRenderer.RenderRgba(m, o);
        Assert.Contains(rgba.Where((_, i) => i % 4 == 3), x => x != 0);
    }

    [Fact]
    public void TopViewShowsTheSlabColor()
    {
        var m = new VoxelModel(2, 1, 2, Fixtures.Pal());
        EditOps.FillBox(m, 0, 0, 0, 1, 0, 1, 1);            // red slab
        var rgba = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 64, View = RenderView.Top });
        int c = (32 * 64 + 32) * 4;
        Assert.True(rgba[c + 3] == 255);
        Assert.True(rgba[c] > rgba[c + 1]);                 // red dominates
    }

    [Fact]
    public void CutawayHidesUpperLayers()
    {
        var m = new VoxelModel(3, 6, 3, Fixtures.Pal());
        EditOps.FillBox(m, 0, 0, 0, 2, 5, 2, 1);
        var full = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 64, View = RenderView.Front });
        var cut = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 64, View = RenderView.Front, CutawayAboveLayer = 1 });
        int Filled(byte[] px) { int n = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) n++; return n; }
        Assert.True(Filled(cut) < Filled(full));
    }

    [Fact]
    public void NightDimsMatteButNotEmissive()
    {
        var m = new VoxelModel(2, 1, 2, Fixtures.Pal());
        m.Set(0, 0, 0, 1);                                  // matte red
        m.Set(1, 0, 1, 4);                                  // emissive window
        var day = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 64, View = RenderView.Top });
        var night = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 64, View = RenderView.Top, Night = 1f });

        (long matte, long glow) Sum(byte[] px)
        {
            long ma = 0, gl = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                if (px[i] > px[i + 1] && px[i] > px[i + 2]) ma += px[i];        // reddish
                else gl += px[i] + px[i + 1];                                    // warm window
            }
            return (ma, gl);
        }
        var d = Sum(day);
        var n = Sum(night);
        Assert.True(n.matte < d.matte);                     // matte dimmed
        Assert.True(n.glow >= d.glow);                      // emissive held/boosted
    }

    [Fact]
    public void PerspectiveAndAllViewsRender()
    {
        var m = Fixtures.House();
        foreach (var view in new[] { RenderView.Top, RenderView.Front, RenderView.Side, RenderView.Iso })
        {
            var px = SoftwareRenderer.RenderRgba(m, new RenderOptions { Size = 48, View = view, Perspective = view == RenderView.Iso });
            Assert.Contains(px.Where((_, i) => i % 4 == 3), a => a != 0);
        }
    }
}

/// Registry tests own the OverrideDataDir hook; everything touching it shares
/// the "registry" collection so xunit never runs them in parallel.
[Collection("registry")]
public class RegistryTests : IDisposable
{
    private readonly string _dir;

    public RegistryTests()
    {
        _dir = Fixtures.TempDir();
        Registry.OverrideDataDir = _dir;
    }

    public void Dispose()
    {
        Registry.OverrideDataDir = null;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RecentsTrackOrderAndResolveNames()
    {
        var a = Path.Combine(_dir, "models", "alpha");
        var b = Path.Combine(_dir, "models", "beta");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Registry.Touch(a, "alpha", DateTimeOffset.UnixEpoch);
        Registry.Touch(b, "beta", DateTimeOffset.UnixEpoch.AddMinutes(1));
        Registry.Touch(a, "alpha", DateTimeOffset.UnixEpoch.AddMinutes(2));

        var recents = Registry.LoadRecents();
        Assert.Equal(2, recents.Models.Count);
        Assert.Equal("alpha", recents.Models[0].Name);      // most recent first
        Assert.Equal(Path.GetFullPath(a), Registry.ResolveName("ALPHA"));
        Assert.Null(Registry.ResolveName("gamma"));

        Registry.Forget(a);
        Assert.Single(Registry.LoadRecents().Models);
    }

    [Fact]
    public void BackupsRotateToThree()
    {
        var model = Path.Combine(_dir, "m1");
        Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(model, "model.json"), "{}");
        for (int i = 0; i < 5; i++)
            Registry.Snapshot(model, DateTimeOffset.UnixEpoch.AddMinutes(i));
        var dir = Path.Combine(Registry.BackupsDir, Registry.IdFor(model));
        Assert.Equal(Registry.BackupsKept, Directory.GetDirectories(dir).Length);
    }

    [Fact]
    public void BuiltinPalettesSeedOnceAndLoad()
    {
        Registry.SeedBuiltinPalettes();
        var all = Registry.LoadPalettes();
        Assert.Contains(all, p => p.Name == "neo-terrestria");
        Assert.Contains(all, p => p.Name == "neo-terrestria-decor");
        Assert.Contains(all, p => p.Name == "primer");

        // user edits survive re-seeding
        var custom = Registry.LoadPalette("primer")!;
        custom.Colors.RemoveAt(custom.Colors.Count - 1);
        Registry.SavePalette(custom);
        Registry.SeedBuiltinPalettes();
        Assert.Equal(custom.Colors.Count, Registry.LoadPalette("primer")!.Colors.Count);
    }

    [Fact]
    public void PaletteRoundTripsWithTags()
    {
        Registry.SavePalette(Fixtures.Pal());
        var back = Registry.LoadPalette("test")!;
        Assert.Equal(4, back.Colors.Count);
        Assert.True(back.Colors[3].Has(ColorTags.Emissive));
        Assert.Equal("#FF0000", back.Colors[0].Hex);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var s = Registry.LoadSettings();
        s.DefaultNewModelDir = "/tmp/somewhere";
        s.OnionOpacity = 0.4f;
        Registry.SaveSettings(s);
        var back = Registry.LoadSettings();
        Assert.Equal("/tmp/somewhere", back.DefaultNewModelDir);
        Assert.Equal(0.4f, back.OnionOpacity, 3);
    }
}
