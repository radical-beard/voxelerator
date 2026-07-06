namespace Voxelerator.Core;

/// Minimal GIF89a encoder for turntable exports. Voxel renders use few
/// distinct colors (palette ≤16 × 6 face shades + background), so an exact
/// global color table almost always fits; overflow falls back to
/// nearest-color mapping against the 256 most frequent.
public static class Gif
{
    /// Frames are RGBA byte arrays (w*h*4, alpha ignored — pass a Background
    /// when rendering). delayCs is per-frame delay in centiseconds.
    public static byte[] Encode(int w, int h, IReadOnlyList<byte[]> frames, int delayCs = 6)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames");

        // ---- global color table from frequency ------------------------------
        var histogram = new Dictionary<int, int>();
        foreach (var f in frames)
            for (int i = 0; i < w * h; i++)
            {
                int rgb = (f[i * 4] << 16) | (f[i * 4 + 1] << 8) | f[i * 4 + 2];
                histogram.TryGetValue(rgb, out int n);
                histogram[rgb] = n + 1;
            }
        var table = histogram.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(256).Select(kv => kv.Key).ToList();
        var lookup = new Dictionary<int, byte>();
        for (int i = 0; i < table.Count; i++) lookup[table[i]] = (byte)i;

        byte Map(int rgb)
        {
            if (lookup.TryGetValue(rgb, out byte idx)) return idx;
            // nearest by squared distance (rare: >256 distinct colors)
            int br = (rgb >> 16) & 255, bg = (rgb >> 8) & 255, bb = rgb & 255;
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < table.Count; i++)
            {
                int tr = (table[i] >> 16) & 255, tg = (table[i] >> 8) & 255, tb = table[i] & 255;
                int d = (tr - br) * (tr - br) + (tg - bg) * (tg - bg) + (tb - bb) * (tb - bb);
                if (d < bestD) { bestD = d; best = i; }
            }
            byte b = (byte)best;
            lookup[rgb] = b;
            return b;
        }

        int tableBits = 1;
        while (1 << tableBits < table.Count) tableBits++;
        tableBits = Math.Max(tableBits, 1);
        int tableSize = 1 << tableBits;

        using var ms = new MemoryStream();
        void W(params byte[] bytes) => ms.Write(bytes);
        void U16(int v) { ms.WriteByte((byte)(v & 255)); ms.WriteByte((byte)(v >> 8)); }

        // header + logical screen descriptor + GCT
        W((byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a');
        U16(w); U16(h);
        ms.WriteByte((byte)(0x80 | (tableBits - 1)));        // GCT present, size
        ms.WriteByte(0); ms.WriteByte(0);                    // bg index, aspect
        for (int i = 0; i < tableSize; i++)
        {
            int rgb = i < table.Count ? table[i] : 0;
            W((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        // NETSCAPE loop-forever extension
        W(0x21, 0xFF, 0x0B);
        W((byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
          (byte)'2', (byte)'.', (byte)'0');
        W(0x03, 0x01, 0x00, 0x00, 0x00);

        foreach (var frame in frames)
        {
            // graphic control extension (delay)
            W(0x21, 0xF9, 0x04, 0x00);
            U16(delayCs);
            W(0x00, 0x00);

            // image descriptor
            ms.WriteByte(0x2C);
            U16(0); U16(0); U16(w); U16(h);
            ms.WriteByte(0);                                 // no local table

            var indices = new byte[w * h];
            for (int i = 0; i < w * h; i++)
                indices[i] = Map((frame[i * 4] << 16) | (frame[i * 4 + 1] << 8) | frame[i * 4 + 2]);
            LzwEncode(ms, indices, Math.Max(2, tableBits));
        }
        ms.WriteByte(0x3B);                                  // trailer
        return ms.ToArray();
    }

    private static void LzwEncode(MemoryStream ms, byte[] indices, int minCodeSize)
    {
        ms.WriteByte((byte)minCodeSize);
        int clear = 1 << minCodeSize, eoi = clear + 1;

        var block = new MemoryStream();
        int bitBuf = 0, bitCount = 0;
        void EmitByteIfFull()
        {
            while (bitCount >= 8)
            {
                block.WriteByte((byte)(bitBuf & 255));
                bitBuf >>= 8;
                bitCount -= 8;
                if (block.Length == 255) Flush();
            }
        }
        void Flush()
        {
            if (block.Length == 0) return;
            ms.WriteByte((byte)block.Length);
            block.WriteTo(ms);
            block.SetLength(0);
        }

        var dict = new Dictionary<(int Prefix, byte K), int>();
        int nextCode = eoi + 1, codeSize = minCodeSize + 1;
        void Emit(int code)
        {
            bitBuf |= code << bitCount;
            bitCount += codeSize;
            EmitByteIfFull();
        }

        Emit(clear);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            byte k = indices[i];
            if (dict.TryGetValue((prefix, k), out int code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            dict[(prefix, k)] = nextCode++;
            if (nextCode - 1 == 1 << codeSize && codeSize < 12) codeSize++;
            if (nextCode >= 4096)
            {
                Emit(clear);
                dict.Clear();
                nextCode = eoi + 1;
                codeSize = minCodeSize + 1;
            }
            prefix = k;
        }
        Emit(prefix);
        Emit(eoi);
        if (bitCount > 0) { block.WriteByte((byte)(bitBuf & 255)); if (block.Length == 255) Flush(); }
        Flush();
        ms.WriteByte(0);                                     // block terminator
    }
}
