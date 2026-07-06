using Voxelerator.Core;
using Xunit;

namespace Voxelerator.Core.Tests;

public class PaletteSlotOpsTests
{
    [Fact]
    public void RemoveSlotShiftsIndicesAndRemaps()
    {
        var m = new VoxelModel(4, 1, 1, Fixtures.Pal());
        m.Set(0, 0, 0, 1); m.Set(1, 0, 0, 2); m.Set(2, 0, 0, 3); m.Set(3, 0, 0, 4);
        EditOps.RemovePaletteSlot(m, 2, remapTo: 4);         // green voxels become window

        Assert.Equal(3, m.Palette.Colors.Count);
        Assert.Equal(1, m.At(0, 0, 0));                      // red unchanged
        Assert.Equal(3, m.At(1, 0, 0));                      // remapped to window (now slot 3)
        Assert.Equal(2, m.At(2, 0, 0));                      // blue shifted down
        Assert.Equal(3, m.At(3, 0, 0));                      // window shifted down
        Assert.Equal("blue", m.Palette.Colors[1].Name);
    }

    [Fact]
    public void RemoveSlotToAirErases()
    {
        var m = new VoxelModel(2, 1, 1, Fixtures.Pal());
        m.Set(0, 0, 0, 2); m.Set(1, 0, 0, 4);
        EditOps.RemovePaletteSlot(m, 2, remapTo: 0);
        Assert.Equal(0, m.At(0, 0, 0));
        Assert.Equal(3, m.At(1, 0, 0));
    }

    [Fact]
    public void RemoveSlotDoesNotMutateUndoSnapshots()
    {
        var session = new EditSession(new VoxelModel(2, 1, 1, Fixtures.Pal()));
        session.Do(m => m.Set(0, 0, 0, 3));
        session.Do(m => EditOps.RemovePaletteSlot(m, 1, 0));
        Assert.Equal(3, session.Model.Palette.Colors.Count);
        Assert.True(session.Undo());
        Assert.Equal(4, session.Model.Palette.Colors.Count); // snapshot's palette untouched
        Assert.Equal(3, session.Model.At(0, 0, 0));
    }

    [Fact]
    public void SwapSlotsRenumbersVoxelsInvisibly()
    {
        var m = new VoxelModel(2, 1, 1, Fixtures.Pal());
        m.Set(0, 0, 0, 1); m.Set(1, 0, 0, 3);
        var beforeRgb = m.ColorAt(m.At(0, 0, 0));
        EditOps.SwapPaletteSlots(m, 1, 3);
        Assert.Equal(3, m.At(0, 0, 0));                      // renumbered…
        Assert.Equal(beforeRgb, m.ColorAt(m.At(0, 0, 0)));   // …same visible color
        Assert.Equal("blue", m.Palette.Colors[0].Name);
    }
}

public class BatchTests
{
    [Fact]
    public void BatchIsOneUndoStep()
    {
        var s = new EditSession(new VoxelModel(4, 1, 4, Fixtures.Pal()));
        s.BeginBatch();
        for (int i = 0; i < 4; i++) s.Model.Set(i, 0, 0, 1);
        s.CommitBatch();
        Assert.Equal(4, s.Model.Filled());
        Assert.True(s.Undo());
        Assert.Equal(0, s.Model.Filled());
        Assert.False(s.Undo());                              // exactly one step
    }

    [Fact]
    public void EmptyBatchLeavesNoUndoStep()
    {
        var s = new EditSession(new VoxelModel(2, 1, 2, Fixtures.Pal()));
        s.BeginBatch();
        s.CommitBatch();
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void AbandonRollsBack()
    {
        var s = new EditSession(new VoxelModel(2, 1, 2, Fixtures.Pal()));
        s.BeginBatch();
        s.Model.Set(0, 0, 0, 1);
        s.AbandonBatch();
        Assert.Equal(0, s.Model.Filled());
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void NestedBatchThrows()
    {
        var s = new EditSession(new VoxelModel(2, 1, 2, Fixtures.Pal()));
        s.BeginBatch();
        Assert.Throws<InvalidOperationException>(s.BeginBatch);
        s.AbandonBatch();
    }
}

public class GifTests
{
    private static byte[] Frame(int w, int h, byte r, byte g, byte b)
    {
        var f = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            f[i * 4] = r; f[i * 4 + 1] = g; f[i * 4 + 2] = b; f[i * 4 + 3] = 255;
        }
        return f;
    }

    [Fact]
    public void EncodesValidHeaderAndLoops()
    {
        var frames = new List<byte[]> { Frame(8, 8, 200, 0, 0), Frame(8, 8, 0, 200, 0) };
        var gif = Gif.Encode(8, 8, frames, delayCs: 5);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        Assert.Equal(0x3B, gif[^1]);                         // trailer
        Assert.Contains((byte)0x21, gif);                    // extensions present
        // NETSCAPE loop block
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(gif));
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        var m = Fixtures.House();
        var frames = new List<byte[]>();
        for (int i = 0; i < 4; i++)
            frames.Add(SoftwareRenderer.RenderRgba(m, new RenderOptions
            {
                Size = 64, YawDegrees = i * 90, Background = new Rgba(0.05f, 0.05f, 0.07f),
            }));
        var a = Gif.Encode(64, 64, frames);
        var b = Gif.Encode(64, 64, frames);
        Assert.Equal(a, b);
        Assert.True(a.Length > 200);
    }

    [Fact]
    public void SurvivesManyColors()
    {
        // gradient frame forces the nearest-color fallback path
        var f = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int i = y * 32 + x;
                f[i * 4] = (byte)(x * 8); f[i * 4 + 1] = (byte)(y * 8); f[i * 4 + 2] = (byte)(x * y / 4); f[i * 4 + 3] = 255;
            }
        var gif = Gif.Encode(32, 32, [f]);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
    }
}
