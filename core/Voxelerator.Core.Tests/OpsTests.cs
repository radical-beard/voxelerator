using Voxelerator.Core;
using Xunit;

namespace Voxelerator.Core.Tests;

public class EditOpsTests
{
    private static VoxelModel M(int w = 8, int h = 4, int d = 8) => new(w, h, d, Fixtures.Pal());

    [Fact]
    public void SymmetryModesExpandCorrectly()
    {
        var m = M();
        EditOps.Paint(m, 0, 1, 2, 1, sym: SymmetryMode.X);
        Assert.Equal(1, m.At(1, 0, 2));
        Assert.Equal(1, m.At(6, 0, 2));
        Assert.Equal(2, m.Filled());

        var m2 = M();
        EditOps.Paint(m2, 0, 1, 2, 1, sym: SymmetryMode.XZ);
        Assert.Equal(4, m2.Filled());
        Assert.Equal(1, m2.At(6, 0, 5));

        var m3 = M();
        EditOps.Paint(m3, 0, 1, 2, 1, sym: SymmetryMode.Radial4);
        Assert.Equal(4, m3.Filled());
        Assert.Equal(1, m3.At(5, 0, 1));                    // 90° rotation of (1,2) on 8x8
    }

    [Fact]
    public void RadialSymmetryNeedsSquare()
    {
        var m = new VoxelModel(4, 2, 6, Fixtures.Pal());
        Assert.Throws<InvalidOperationException>(() => EditOps.Paint(m, 0, 1, 1, 1, sym: SymmetryMode.Radial4));
    }

    [Fact]
    public void BrushSizesCoverExpectedCells()
    {
        var m = M();
        EditOps.Paint(m, 0, 4, 4, 1, brushSize: 3);
        Assert.Equal(9, m.Filled());
        var m2 = M();
        EditOps.Paint(m2, 0, 4, 4, 1, brushSize: 2);
        Assert.Equal(4, m2.Filled());
    }

    [Fact]
    public void FloodFillStopsAtBoundaries()
    {
        var m = M(6, 1, 6);
        EditOps.Rect(m, 0, 1, 1, 4, 4, 2, filled: false);   // ring of color 2
        EditOps.FloodFill(m, 0, 2, 2, 3);                   // fill inside with 3
        Assert.Equal(3, m.At(3, 0, 3));
        Assert.Equal(0, m.At(0, 0, 0));                     // outside untouched
        Assert.Equal(2, m.At(1, 0, 1));                     // ring untouched
    }

    [Fact]
    public void LineHitsBothEndpoints()
    {
        var pts = EditOps.LinePoints(0, 0, 5, 3);
        Assert.Contains((0, 0), pts);
        Assert.Contains((5, 3), pts);
    }

    [Fact]
    public void EllipseFillIsSymmetricAndOutlineIsRing()
    {
        var m = M(9, 1, 7);
        EditOps.Ellipse(m, 0, 0, 0, 8, 6, 1, filled: true);
        for (int z = 0; z < 7; z++)
            for (int x = 0; x < 9; x++)
                Assert.Equal(m.At(x, 0, z), m.At(8 - x, 0, 6 - z));   // 180° symmetry

        var o = M(9, 1, 7);
        EditOps.Ellipse(o, 0, 0, 0, 8, 6, 1, filled: false);
        Assert.True(o.Filled() < m.Filled());
        Assert.Equal(0, o.At(4, 0, 3));                     // hollow center
    }

    [Fact]
    public void VolumetricPrimitivesFillExpectedCounts()
    {
        var m = M(8, 8, 8);
        EditOps.FillBox(m, 1, 1, 1, 3, 3, 3, 1);
        Assert.Equal(27, m.Filled());

        var h = M(8, 8, 8);
        EditOps.HollowBox(h, 0, 0, 0, 4, 4, 4, 1);          // 5³ minus 3³ interior
        Assert.Equal(125 - 27, h.Filled());

        var c = M(8, 8, 8);
        EditOps.FillCylinder(c, 4, 4, 0, 3, 2.0, 1);
        int perLayer = c.Filled() / 4;
        Assert.Equal(0, c.Filled() % 4);
        Assert.InRange(perLayer, 9, 13);                    // r=2 disc ≈ 12 cells
    }

    [Fact]
    public void LayerOpsRoundTrip()
    {
        var m = M();
        EditOps.Rect(m, 1, 2, 2, 5, 5, 2, filled: true);
        var copy = EditOps.CopyLayer(m, 1);
        EditOps.PasteLayer(m, 3, copy);
        Assert.Equal(m.Layer(1).ToArray(), m.Layer(3).ToArray());

        EditOps.SwapLayers(m, 1, 2);
        Assert.Equal(0, m.At(3, 1, 3));
        Assert.Equal(2, m.At(3, 2, 3));

        EditOps.ClearLayer(m, 2);
        Assert.Equal(0, m.At(3, 2, 3));
    }

    [Fact]
    public void ExtrudeRepeatsUpwardAndClamps()
    {
        var m = M(4, 5, 4);
        EditOps.Rect(m, 1, 0, 0, 3, 3, 1, filled: true);
        EditOps.Extrude(m, 1, 99);
        for (int y = 2; y < 5; y++) Assert.Equal(16, CountLayer(m, y));
        Assert.Equal(0, CountLayer(m, 0));
    }

    [Fact]
    public void FlipRotateShiftBehave()
    {
        var m = M(4, 1, 4);
        m.Set(0, 0, 1, 1);
        EditOps.FlipLayer(m, 0, alongX: true);
        Assert.Equal(1, m.At(3, 0, 1));

        var r = M(4, 1, 4);
        r.Set(1, 0, 0, 1);                                  // north edge
        EditOps.RotateLayer90(r, 0);
        Assert.Equal(1, r.At(3, 0, 1));                     // now east edge

        var s = M(4, 1, 4);
        s.Set(3, 0, 3, 1);
        EditOps.ShiftLayer(s, 0, 1, 1);                     // clipped away
        Assert.Equal(0, s.Filled());
    }

    [Fact]
    public void InsertDeleteLayerReshape()
    {
        var m = M(3, 3, 3);
        EditOps.Rect(m, 0, 0, 0, 2, 2, 1, filled: true);
        EditOps.Rect(m, 2, 0, 0, 2, 2, 2, filled: true);

        var grown = EditOps.InsertLayer(m, 1);
        Assert.Equal(4, grown.SY);
        Assert.Equal(1, grown.At(1, 0, 1));
        Assert.Equal(0, grown.At(1, 1, 1));                 // inserted blank
        Assert.Equal(2, grown.At(1, 3, 1));                 // shifted up

        var shrunk = EditOps.DeleteLayer(grown, 1);
        Assert.Equal(m.Voxels, shrunk.Voxels);

        var single = new VoxelModel(2, 1, 2, Fixtures.Pal());
        Assert.Throws<InvalidOperationException>(() => EditOps.DeleteLayer(single, 0));
    }

    [Fact]
    public void ResizeTrimTranslateRotateModel()
    {
        var m = M(4, 4, 4);
        m.Set(1, 1, 1, 3);
        var big = EditOps.Resize(m, 8, 8, 8, 2, 2, 2);
        Assert.Equal(3, big.At(3, 3, 3));

        var trimmed = EditOps.Trim(big);
        Assert.Equal((1, 1, 1), (trimmed.SX, trimmed.SY, trimmed.SZ));
        Assert.Equal(3, trimmed.At(0, 0, 0));

        var t = M(4, 4, 4);
        t.Set(0, 0, 0, 1);
        EditOps.TranslateModel(t, 1, 1, 1);
        Assert.Equal(1, t.At(1, 1, 1));
        Assert.Equal(1, t.Filled());

        var r = new VoxelModel(3, 1, 5, Fixtures.Pal());
        r.Set(0, 0, 0, 2);
        var rot = EditOps.RotateModel90(r);
        Assert.Equal((5, 1, 3), (rot.SX, rot.SY, rot.SZ));
        Assert.Equal(2, rot.At(4, 0, 0));

        var mm = M(4, 2, 4);
        mm.Set(0, 0, 0, 1);
        EditOps.MirrorModel(mm, alongX: true);
        Assert.Equal(1, mm.At(3, 0, 0));

        var rc = M();
        rc.Set(0, 0, 0, 1); rc.Set(1, 0, 0, 2);
        EditOps.ReplaceColor(rc, 1, 4);
        Assert.Equal(4, rc.At(0, 0, 0));
        Assert.Equal(2, rc.At(1, 0, 0));
    }

    private static int CountLayer(VoxelModel m, int y)
    {
        int n = 0;
        foreach (var b in m.Layer(y)) if (b != 0) n++;
        return n;
    }
}

public class TextGridTests
{
    [Fact]
    public void EncodeDecodeRoundTrips()
    {
        var m = new VoxelModel(4, 2, 3, Fixtures.Pal());
        m.Set(0, 1, 0, 1);
        m.Set(3, 1, 2, 4);
        var grid = TextGrid.EncodeLayer(m, 1);
        Assert.Equal("1...\n....\n...4", grid);

        var (w, d, cells) = TextGrid.Decode(grid);
        Assert.Equal((4, 3), (w, d));
        Assert.Equal(1, cells[0]);
        Assert.Equal(4, cells[2 * 4 + 3]);
    }

    [Fact]
    public void SixteenColorAlphabetHolds()
    {
        Assert.Equal('9', TextGrid.CharFor(9));
        Assert.Equal('a', TextGrid.CharFor(10));
        Assert.Equal('g', TextGrid.CharFor(16));
        Assert.Equal(16, TextGrid.IndexFor('g'));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextGrid.CharFor(17));
        Assert.Throws<FormatException>(() => TextGrid.IndexFor('z'));
    }

    [Fact]
    public void RaggedGridsAreRejected()
        => Assert.Throws<FormatException>(() => TextGrid.Decode("ab\nabc"));
}

public class EditSessionTests
{
    [Fact]
    public void UndoRedoSpanShapeChanges()
    {
        var s = new EditSession(new VoxelModel(3, 2, 3, Fixtures.Pal()));
        s.Do(m => m.Set(1, 0, 1, 1));
        s.Swap(m => EditOps.InsertLayer(m, 2));
        Assert.Equal(3, s.Model.SY);

        Assert.True(s.Undo());
        Assert.Equal(2, s.Model.SY);
        Assert.Equal(1, s.Model.At(1, 0, 1));

        Assert.True(s.Undo());
        Assert.Equal(0, s.Model.Filled());

        Assert.True(s.Redo());
        Assert.True(s.Redo());
        Assert.Equal(3, s.Model.SY);
        Assert.False(s.Redo());
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        var s = new EditSession(new VoxelModel(2, 1, 2, Fixtures.Pal()));
        s.Do(m => m.Set(0, 0, 0, 1));
        s.Undo();
        s.Do(m => m.Set(1, 0, 1, 2));
        Assert.False(s.Redo());
        Assert.Equal(2, s.Model.At(1, 0, 1));
    }

    [Fact]
    public void ExternalEditsLandOnUndoStack()
    {
        var s = new EditSession(new VoxelModel(2, 1, 2, Fixtures.Pal()));
        var external = new VoxelModel(2, 1, 2, Fixtures.Pal());
        external.Set(0, 0, 0, 3);
        s.AbsorbExternal(external);
        Assert.Equal(3, s.Model.At(0, 0, 0));
        s.Undo();
        Assert.Equal(0, s.Model.Filled());
    }
}
