using System.IO.Compression;
using Voxelerator.Core;
using Xunit;

namespace Voxelerator.Core.Tests;

internal static class Fixtures
{
    public static Palette Pal() => new("test",
    [
        new PaletteColor("#FF0000", "red"),
        new PaletteColor("#00FF00", "green"),
        new PaletteColor("#0000FF", "blue"),
        new PaletteColor("#FFE08C", "window", [ColorTags.Emissive]),
    ]);

    /// Small asymmetric model exercising all four colors.
    public static VoxelModel House()
    {
        var m = new VoxelModel(6, 5, 4, Pal());
        EditOps.FillBox(m, 0, 0, 0, 5, 0, 3, 1);            // slab
        EditOps.HollowBox(m, 1, 1, 1, 4, 3, 2, 2);          // walls
        m.Set(2, 2, 1, 4);                                  // window
        m.Set(3, 4, 2, 3);                                  // chimney
        return m;
    }

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "voxelerator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}

public class PngTests
{
    [Fact]
    public void IndexedRoundTripsThroughRgba()
    {
        var indices = new byte[] { 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 4, 3 };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 224, 140) };
        var png = Png.WriteIndexed(4, 3, indices, colors);
        var (w, h, rgba) = Png.ReadRgba(png);
        Assert.Equal(4, w);
        Assert.Equal(3, h);
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] == 0) { Assert.Equal(0, rgba[i * 4 + 3]); continue; }
            var (r, g, b) = colors[indices[i] - 1];
            Assert.Equal(r, rgba[i * 4 + 0]);
            Assert.Equal(g, rgba[i * 4 + 1]);
            Assert.Equal(b, rgba[i * 4 + 2]);
            Assert.Equal(255, rgba[i * 4 + 3]);
        }
    }

    [Fact]
    public void WriterIsByteDeterministic()
    {
        var indices = new byte[64];
        for (int i = 0; i < 64; i++) indices[i] = (byte)(i % 5);
        var colors = new List<(byte, byte, byte)> { (1, 2, 3), (4, 5, 6), (7, 8, 9), (10, 11, 12) };
        var a = Png.WriteIndexed(8, 8, indices, colors);
        var b = Png.WriteIndexed(8, 8, indices, colors);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RgbaRoundTrips()
    {
        var rgba = new byte[4 * 2 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7);
        var png = Png.WriteRgba(4, 2, rgba);
        var (w, h, back) = Png.ReadRgba(png);
        Assert.Equal((4, 2), (w, h));
        Assert.Equal(rgba, back);
    }

    /// Foreign PNGs may use any scanline filter — build one with Sub/Up/Avg/
    /// Paeth rows via ZLibStream and make sure the reader unfilters it.
    [Fact]
    public void ReaderHandlesAllFilters()
    {
        const int w = 4, h = 5, bpp = 4;
        var pixels = new byte[w * h * bpp];
        var rng = new Random(42);
        rng.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var raw = new byte[h * (1 + w * bpp)];
        byte[] filters = [0, 1, 2, 3, 4];
        for (int y = 0; y < h; y++)
        {
            byte f = filters[y];
            raw[y * (1 + w * bpp)] = f;
            for (int i = 0; i < w * bpp; i++)
            {
                int cur = pixels[y * w * bpp + i];
                int a = i >= bpp ? pixels[y * w * bpp + i - bpp] : 0;
                int b = y > 0 ? pixels[(y - 1) * w * bpp + i] : 0;
                int c = y > 0 && i >= bpp ? pixels[(y - 1) * w * bpp + i - bpp] : 0;
                int pred = f switch
                {
                    1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0,
                };
                raw[y * (1 + w * bpp) + 1 + i] = (byte)(cur - pred);
            }
        }

        using var zms = new MemoryStream();
        using (var z = new ZLibStream(zms, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(ms, "IHDR", BuildIhdr(w, h));
        WriteChunk(ms, "IDAT", zms.ToArray());
        WriteChunk(ms, "IEND", []);

        var (rw, rh, rgba) = Png.ReadRgba(ms.ToArray());
        Assert.Equal((w, h), (rw, rh));
        Assert.Equal(pixels, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] BuildIhdr(int w, int h)
    {
        var d = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d, w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(4), h);
        d[8] = 8; d[9] = 6;
        return d;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = TestCrc(t, data);
        Span<byte> c = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static uint TestCrc(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (var x in a.Concat(b))
        {
            c ^= x;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}

public class PaletteTests
{
    [Fact]
    public void TagsMapToAlphaConvention()
    {
        var p = Fixtures.Pal();
        Assert.Equal(0f, p.RgbaOf(1).A);                    // matte
        Assert.Equal(1f, p.RgbaOf(4).A);                    // emissive
        var glass = new Palette("g", [new PaletteColor("#9ECCE0", "glass", [ColorTags.Glass])]);
        Assert.Equal(ColorTags.GlassAlpha, glass.RgbaOf(1).A, 3);
    }

    [Fact]
    public void ValidationCatchesProblems()
    {
        var over = new Palette("over");
        for (int i = 0; i < 17; i++) over.Colors.Add(new PaletteColor($"#{i:X2}00{i:X2}", $"c{i}"));
        Assert.Contains(over.Validate(), p => p.Contains("cap is 16"));

        var dup = new Palette("dup");
        dup.Colors.Add(new PaletteColor("#FF0000", "a"));
        dup.Colors.Add(new PaletteColor("#FF0000", "b"));
        Assert.Contains(dup.Validate(), p => p.Contains("duplicate"));

        var bad = new Palette("bad");
        bad.Colors.Add(new PaletteColor("FF0000", "nohash"));
        Assert.Contains(bad.Validate(), p => p.Contains("#RRGGBB"));

        var tag = new Palette("tag");
        tag.Colors.Add(new PaletteColor("#FF0000", "x", ["sparkly"]));
        Assert.Contains(tag.Validate(), p => p.Contains("unknown tag"));
    }

    [Fact]
    public void BuiltinsAreValid()
    {
        foreach (var p in BuiltinPalettes.All())
            Assert.Empty(p.Validate());
    }

    [Fact]
    public void CellIndexIsExactMatch()
    {
        var p = Fixtures.Pal();
        Assert.Equal((byte)1, p.CellIndexOf(255, 0, 0));
        Assert.Null(p.CellIndexOf(254, 0, 0));
    }
}

public class ModelStoreTests : IDisposable
{
    private readonly string _dir = Fixtures.TempDir();
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveLoadRoundTripsExactly()
    {
        var m = Fixtures.House();
        var folder = Path.Combine(_dir, "house");
        ModelStore.Save(m, "house", folder);

        var (back, doc) = ModelStore.Load(folder);
        Assert.Equal(m.Voxels, back.Voxels);
        Assert.Equal((m.SX, m.SY, m.SZ), (back.SX, back.SY, back.SZ));
        Assert.Equal("test", back.Palette.Name);
        Assert.Equal(1, doc.Format);
        Assert.Equal("house", doc.Name);
        Assert.True(back.Palette.Colors[3].Has(ColorTags.Emissive));
    }

    [Fact]
    public void LayerPngBytesAreDeterministic()
    {
        var m = Fixtures.House();
        var a = Path.Combine(_dir, "a");
        var b = Path.Combine(_dir, "b");
        ModelStore.Save(m, "x", a, DateTimeOffset.UnixEpoch);
        ModelStore.Save(m, "x", b, DateTimeOffset.UnixEpoch.AddDays(1));
        foreach (var f in ModelStore.LayerFiles(a))
            Assert.Equal(File.ReadAllBytes(f), File.ReadAllBytes(Path.Combine(b, Path.GetFileName(f))));
    }

    [Fact]
    public void ShrinkingModelDeletesStaleLayers()
    {
        var folder = Path.Combine(_dir, "shrink");
        var tall = new VoxelModel(3, 6, 3, Fixtures.Pal());
        tall.Set(1, 5, 1, 1);
        ModelStore.Save(tall, "s", folder);
        Assert.Equal(6, ModelStore.LayerFiles(folder).Count);

        var shortM = new VoxelModel(3, 2, 3, Fixtures.Pal());
        ModelStore.Save(shortM, "s", folder);
        Assert.Equal(2, ModelStore.LayerFiles(folder).Count);
        Assert.Empty(ModelStore.Validate(folder));
    }

    [Fact]
    public void OffPaletteColorIsRejected()
    {
        var folder = Path.Combine(_dir, "rogue");
        var m = new VoxelModel(2, 1, 2, Fixtures.Pal());
        m.Set(0, 0, 0, 1);
        ModelStore.Save(m, "r", folder);

        // tamper: overwrite the layer with an RGBA png holding a rogue color
        var rgba = new byte[2 * 2 * 4];
        rgba[0] = 12; rgba[1] = 34; rgba[2] = 56; rgba[3] = 255;
        File.WriteAllBytes(Path.Combine(folder, "0001.png"), Png.WriteRgba(2, 2, rgba));

        var ex = Assert.Throws<ModelFormatException>(() => ModelStore.Load(folder));
        Assert.Contains("palette is law", ex.Message);
        Assert.NotEmpty(ModelStore.Validate(folder));
    }

    [Fact]
    public void PartialAlphaIsRejected()
    {
        var folder = Path.Combine(_dir, "alpha");
        var m = new VoxelModel(2, 1, 2, Fixtures.Pal());
        ModelStore.Save(m, "a", folder);
        var rgba = new byte[2 * 2 * 4];
        rgba[0] = 255; rgba[3] = 128;                       // half-transparent red
        File.WriteAllBytes(Path.Combine(folder, "0001.png"), Png.WriteRgba(2, 2, rgba));
        Assert.Throws<ModelFormatException>(() => ModelStore.Load(folder));
    }

    [Fact]
    public void UnknownFormatVersionIsRejected()
    {
        var folder = Path.Combine(_dir, "future");
        ModelStore.Save(Fixtures.House(), "f", folder);
        var manifest = File.ReadAllText(Path.Combine(folder, "model.json")).Replace("\"format\": 1", "\"format\": 99");
        File.WriteAllText(Path.Combine(folder, "model.json"), manifest);
        Assert.Throws<ModelFormatException>(() => ModelStore.Load(folder));
    }

    [Fact]
    public void ManifestlessFolderIsInferred()
    {
        var folder = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(folder);
        var rgba = new byte[3 * 3 * 4];
        void Px(int i, byte r, byte g, byte b) { rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255; }
        Px(0, 200, 10, 10);
        Px(4, 10, 200, 10);
        File.WriteAllBytes(Path.Combine(folder, "0001.png"), Png.WriteRgba(3, 3, rgba));

        var (m, doc) = ModelStore.Load(folder);
        Assert.Equal(2, m.Filled());
        Assert.Equal(2, m.Palette.Colors.Count);
        Assert.Equal("plain", doc.Name);
        Assert.EndsWith("-imported", m.Palette.Name);
    }

    [Fact]
    public void MissingLayerIsRejected()
    {
        var folder = Path.Combine(_dir, "gap");
        ModelStore.Save(Fixtures.House(), "g", folder);
        File.Delete(Path.Combine(folder, "0003.png"));
        var ex = Assert.Throws<ModelFormatException>(() => ModelStore.Load(folder));
        Assert.Contains("missing layer", ex.Message);
    }

    [Fact]
    public void ExtraLayerIsRejected()
    {
        var folder = Path.Combine(_dir, "extra");
        var m = Fixtures.House();
        ModelStore.Save(m, "e", folder);
        File.Copy(Path.Combine(folder, "0001.png"), Path.Combine(folder, ModelStore.LayerFile(m.SY)));
        var ex = Assert.Throws<ModelFormatException>(() => ModelStore.Load(folder));
        Assert.Contains("extra layer", ex.Message);
    }

    [Fact]
    public void CreatedTimestampSurvivesResaves()
    {
        var folder = Path.Combine(_dir, "stamp");
        var m = Fixtures.House();
        var first = ModelStore.Save(m, "t", folder, DateTimeOffset.UnixEpoch);
        var second = ModelStore.Save(m, "t", folder, DateTimeOffset.UnixEpoch.AddDays(3));
        Assert.Equal(first.Created, second.Created);
        Assert.NotEqual(second.Created, second.Updated);
    }
}
