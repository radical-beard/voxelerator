using System.Buffers.Binary;
using System.IO.Compression;

namespace Voxelerator.Core;

/// Hand-rolled PNG codec — the format is the contract, so the writer is fully
/// deterministic: fixed chunk layout, stored (uncompressed) deflate blocks, no
/// timestamps. Same voxel data → byte-identical file, forever, on every
/// platform. Layers are tiny (W×D bytes), so stored blocks cost nothing.
///
/// Writer emits 8-bit indexed (PLTE + tRNS, cell index = palette byte) or
/// 8-bit RGBA (for renders/thumbnails). Reader additionally accepts plain
/// 8-bit RGB/RGBA files with any scanline filter, so stacks drawn in other
/// tools import cleanly. No interlace, no 16-bit — rejected with clear errors.
public static class Png
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    // ---- writing -----------------------------------------------------------

    /// `indices` is w*h cell values (0 = air, transparent). `colors[i]` is the
    /// RGB of cell value i+1.
    public static byte[] WriteIndexed(int w, int h, ReadOnlySpan<byte> indices,
        IReadOnlyList<(byte R, byte G, byte B)> colors)
    {
        if (indices.Length != w * h) throw new ArgumentException("indices must be w*h");
        if (colors.Count > Palette.MaxColors) throw new ArgumentException("too many palette colors");

        var plte = new byte[(colors.Count + 1) * 3];        // entry 0 = air (black)
        for (int i = 0; i < colors.Count; i++)
        {
            plte[(i + 1) * 3 + 0] = colors[i].R;
            plte[(i + 1) * 3 + 1] = colors[i].G;
            plte[(i + 1) * 3 + 2] = colors[i].B;
        }

        var raw = new byte[h * (1 + w)];                    // filter byte 0 + row
        for (int y = 0; y < h; y++)
            indices.Slice(y * w, w).CopyTo(raw.AsSpan(y * (1 + w) + 1, w));

        using var ms = new MemoryStream();
        ms.Write(Signature);
        WriteChunk(ms, "IHDR", Ihdr(w, h, colorType: 3));
        WriteChunk(ms, "PLTE", plte);
        WriteChunk(ms, "tRNS", [0]);                        // index 0 fully transparent
        WriteChunk(ms, "IDAT", ZlibStored(raw));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// `rgba` is w*h*4 bytes.
    public static byte[] WriteRgba(int w, int h, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != w * h * 4) throw new ArgumentException("rgba must be w*h*4");
        var raw = new byte[h * (1 + w * 4)];
        for (int y = 0; y < h; y++)
            rgba.Slice(y * w * 4, w * 4).CopyTo(raw.AsSpan(y * (1 + w * 4) + 1, w * 4));

        using var ms = new MemoryStream();
        ms.Write(Signature);
        WriteChunk(ms, "IHDR", Ihdr(w, h, colorType: 6));
        WriteChunk(ms, "IDAT", ZlibStored(raw));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] Ihdr(int w, int h, byte colorType)
    {
        var d = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(0), w);
        BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(4), h);
        d[8] = 8;               // bit depth
        d[9] = colorType;       // 3 indexed / 6 rgba
        d[10] = 0; d[11] = 0; d[12] = 0;
        return d;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        Span<byte> t = stackalloc byte[4];
        for (int i = 0; i < 4; i++) t[i] = (byte)type[i];
        s.Write(t);
        s.Write(data);
        uint crc = Crc32.Compute(t, 0);
        crc = Crc32.Compute(data, crc ^ 0xFFFFFFFF) ; // continue over data
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    /// zlib wrapper around stored deflate blocks: header 78 01, blocks of at
    /// most 65535 bytes, adler32 trailer. Deterministic by construction.
    private static byte[] ZlibStored(ReadOnlySpan<byte> raw)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x01);
        int off = 0;
        while (true)
        {
            int n = Math.Min(65535, raw.Length - off);
            bool final = off + n == raw.Length;
            ms.WriteByte((byte)(final ? 1 : 0));
            ms.WriteByte((byte)(n & 0xFF)); ms.WriteByte((byte)(n >> 8));
            ms.WriteByte((byte)(~n & 0xFF)); ms.WriteByte((byte)((~n >> 8) & 0xFF));
            ms.Write(raw.Slice(off, n));
            off += n;
            if (final) break;
        }
        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        ms.Write(adler);
        return ms.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    // ---- reading -----------------------------------------------------------

    public static (int W, int H, byte[] Rgba) ReadRgba(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new FormatException("not a PNG file");

        int w = 0, h = 0; byte bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? plte = null, trns = null;
        using var idat = new MemoryStream();

        int p = 8;
        while (p + 8 <= file.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(p));
            string type = System.Text.Encoding.ASCII.GetString(file, p + 4, 4);
            if (p + 12 + len > file.Length) throw new FormatException("truncated PNG chunk");
            var data = file.AsSpan(p + 8, len);
            switch (type)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(data);
                    h = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4));
                    bitDepth = data[8]; colorType = data[9]; interlace = data[12];
                    break;
                case "PLTE": plte = data.ToArray(); break;
                case "tRNS": trns = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
                case "IEND": p = file.Length; continue;
            }
            p += 12 + len;
        }

        if (w <= 0 || h <= 0) throw new FormatException("PNG missing IHDR");
        if (bitDepth != 8) throw new FormatException($"unsupported PNG bit depth {bitDepth} (need 8)");
        if (interlace != 0) throw new FormatException("interlaced PNG not supported");
        int bpp = colorType switch
        {
            3 => 1, 2 => 3, 6 => 4,
            _ => throw new FormatException($"unsupported PNG color type {colorType} (need indexed/RGB/RGBA)"),
        };
        if (colorType == 3 && plte is null) throw new FormatException("indexed PNG missing PLTE");

        var raw = Inflate(idat.ToArray(), h * (1 + w * bpp));
        var pixels = Unfilter(raw, w, h, bpp);

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            switch (colorType)
            {
                case 3:
                {
                    byte idx = pixels[i];
                    if (idx * 3 + 2 >= plte!.Length) throw new FormatException($"palette index {idx} outside PLTE");
                    rgba[i * 4 + 0] = plte[idx * 3 + 0];
                    rgba[i * 4 + 1] = plte[idx * 3 + 1];
                    rgba[i * 4 + 2] = plte[idx * 3 + 2];
                    rgba[i * 4 + 3] = trns is not null && idx < trns.Length ? trns[idx] : (byte)255;
                    break;
                }
                case 2:
                    rgba[i * 4 + 0] = pixels[i * 3 + 0];
                    rgba[i * 4 + 1] = pixels[i * 3 + 1];
                    rgba[i * 4 + 2] = pixels[i * 3 + 2];
                    rgba[i * 4 + 3] = 255;
                    break;
                default:
                    Array.Copy(pixels, i * 4, rgba, i * 4, 4);
                    break;
            }
        }
        return (w, h, rgba);
    }

    private static byte[] Inflate(byte[] zlib, int expected)
    {
        if (zlib.Length < 2) throw new FormatException("empty IDAT");
        if ((zlib[0] & 0x0F) != 8) throw new FormatException("IDAT is not deflate");
        if ((zlib[1] & 0x20) != 0) throw new FormatException("preset dictionary not supported");
        using var src = new MemoryStream(zlib, 2, zlib.Length - 2);
        using var inf = new DeflateStream(src, CompressionMode.Decompress);
        var outBuf = new byte[expected];
        int read = 0;
        while (read < expected)
        {
            int n = inf.Read(outBuf, read, expected - read);
            if (n == 0) throw new FormatException("PNG pixel data shorter than expected");
            read += n;
        }
        return outBuf;
    }

    private static byte[] Unfilter(byte[] raw, int w, int h, int bpp)
    {
        int stride = w * bpp;
        var outBuf = new byte[h * stride];
        for (int y = 0; y < h; y++)
        {
            byte filter = raw[y * (1 + stride)];
            var src = raw.AsSpan(y * (1 + stride) + 1, stride);
            var dst = outBuf.AsSpan(y * stride, stride);
            var prev = y > 0 ? outBuf.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? dst[i - bpp] : 0;                   // left
                int b = y > 0 ? prev[i] : 0;                           // up
                int c = y > 0 && i >= bpp ? prev[i - bpp] : 0;         // up-left
                int x = src[i];
                dst[i] = filter switch
                {
                    0 => (byte)x,
                    1 => (byte)(x + a),
                    2 => (byte)(x + b),
                    3 => (byte)(x + (a + b) / 2),
                    4 => (byte)(x + Paeth(a, b, c)),
                    _ => throw new FormatException($"unknown PNG filter {filter}"),
                };
            }
        }
        return outBuf;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// Standard PNG CRC: pass 0 to start, XOR the running value with
    /// 0xFFFFFFFF to continue over another buffer.
    public static uint Compute(ReadOnlySpan<byte> data, uint start)
    {
        uint c = start ^ 0xFFFFFFFF;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
