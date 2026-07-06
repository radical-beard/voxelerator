using System.Text;

namespace Voxelerator.Core;

/// Token-cheap layer encoding for the MCP surface: one character per voxel,
/// rows are z from the top of the image, '.' = air, palette index 1–16 as a
/// base-36 digit ('1'…'9', 'a'…'g').
public static class TextGrid
{
    public static char CharFor(byte idx) => idx switch
    {
        0 => '.',
        <= 9 => (char)('0' + idx),
        <= Palette.MaxColors => (char)('a' + idx - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(idx)),
    };

    public static byte IndexFor(char c) => c switch
    {
        '.' => 0,
        >= '1' and <= '9' => (byte)(c - '0'),
        >= 'a' and <= 'g' => (byte)(c - 'a' + 10),
        _ => throw new FormatException($"invalid text-grid character '{c}' (use '.', '1'-'9', 'a'-'g')"),
    };

    public static string EncodeLayer(VoxelModel m, int y)
    {
        var sb = new StringBuilder((m.SX + 1) * m.SZ);
        for (int z = 0; z < m.SZ; z++)
        {
            for (int x = 0; x < m.SX; x++) sb.Append(CharFor(m.At(x, y, z)));
            if (z < m.SZ - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// Parses a grid into (w, d, cells z-major). Rows must be equal length.
    public static (int W, int D, byte[] Cells) Decode(string grid)
    {
        var rows = grid.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) throw new FormatException("empty text grid");
        int w = rows[0].Length;
        var cells = new byte[w * rows.Length];
        for (int z = 0; z < rows.Length; z++)
        {
            if (rows[z].Length != w)
                throw new FormatException($"ragged text grid: row {z} has {rows[z].Length} chars, expected {w}");
            for (int x = 0; x < w; x++) cells[z * w + x] = IndexFor(rows[z][x]);
        }
        return (w, rows.Length, cells);
    }
}
