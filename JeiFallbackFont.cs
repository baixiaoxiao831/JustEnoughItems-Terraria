using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

// Load the bundled glyphs on first use on all platforms.
// Rows are grayscale coverage. Measurements and pixels share the same glyph bounds.
internal static class JeiFallbackFont
{
    private readonly record struct Glyph(short Left, short Top, byte Width, byte Height, float Advance, int Offset);
    private readonly record struct Bounds(int Left, int Top, int Width, int Height);
    private static Mod mod;
    private static Dictionary<int, Glyph> glyphs;
    private static byte[] coverage;
    private static int em, ascent, descent;

    internal static void Load(Mod owner) => mod = owner;

    internal static void Unload()
    {
        glyphs = null;
        coverage = null;
        mod = null;
    }

    private static void EnsureLoaded()
    {
        if (glyphs != null) return;
        using var source = mod.GetFileStream("Data/PortableFont.bin");
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new BinaryReader(zip);
        if (reader.ReadUInt32() != 0x3146474A) throw new InvalidDataException("Unknown portable font format");
        em = reader.ReadUInt16();
        ascent = reader.ReadUInt16();
        descent = reader.ReadUInt16();
        int count = reader.ReadInt32();
        var table = new Dictionary<int, Glyph>(count);
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            int code = reader.ReadInt32();
            var glyph = new Glyph(reader.ReadInt16(), reader.ReadInt16(), reader.ReadByte(),
                reader.ReadByte(), reader.ReadUInt16() / 64f, offset);
            table.Add(code, glyph);
            offset += glyph.Width * glyph.Height;
        }
        var bytes = new byte[offset];
        zip.ReadExactly(bytes);
        coverage = bytes;
        glyphs = table;
    }

    private static Glyph GetGlyph(Rune rune)
    {
        if (glyphs.TryGetValue(rune.Value, out var glyph)) return glyph;
        // Join controls and variation selectors have no visible advance on their own.
        if (rune.Value is 0x200C or 0x200D or >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF)
            return default;
        return glyphs[0x25A1];
    }

    private static Bounds Layout(string text, float scale, float slant)
    {
        float pen = 0, left = 0, right = 0, top = -ascent, bottom = descent;
        foreach (Rune rune in text.EnumerateRunes())
        {
            Glyph g = GetGlyph(rune);
            if (g.Width > 0)
            {
                float first = pen + g.Left - slant * g.Top;
                left = Math.Min(left, first - slant * g.Height);
                right = Math.Max(right, first + g.Width);
                top = Math.Min(top, g.Top);
                bottom = Math.Max(bottom, g.Top + g.Height);
            }
            pen += g.Advance;
        }
        int x = (int)MathF.Floor(left * scale), y = (int)MathF.Floor(top * scale);
        int width = (int)MathF.Ceiling(Math.Max(right, pen) * scale) - x;
        int height = (int)MathF.Ceiling(bottom * scale) - y;
        return new(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    internal static Vector2 Measure(string text, BrowserFontFace face, int pixels)
    {
        EnsureLoaded();
        var bounds = Layout(text, Math.Max(1, pixels) / (float)em, face == BrowserFontFace.Italic ? .18f : 0f);
        return new(bounds.Width, bounds.Height);
    }

    internal static byte[] Rasterize(string text, BrowserFontFace face, int pixels, out int width, out int height)
    {
        EnsureLoaded();
        float scale = Math.Max(1, pixels) / (float)em;
        float slant = face == BrowserFontFace.Italic ? .18f : 0f;
        Bounds bounds = Layout(text, scale, slant);
        width = bounds.Width;
        height = bounds.Height;
        var image = new byte[checked(width * height)];
        float pen = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            Glyph g = GetGlyph(rune);
            if (g.Width != 0) Paint(g, pen, slant, scale, bounds, image);
            pen += g.Advance;
        }
        return image;
    }

    private static void Paint(Glyph g, float pen, float slant, float scale, Bounds b, byte[] output)
    {
        float glyphLeft = pen + g.Left;
        int firstRow = Math.Max(0, (int)MathF.Floor(g.Top * scale) - b.Top);
        int endRow = Math.Min(b.Height, (int)MathF.Ceiling((g.Top + g.Height) * scale) - b.Top);
        for (int y = firstRow; y < endRow; y++)
        {
            float baselineY = (y + b.Top + .5f) / scale;
            float rowLeft = glyphLeft - slant * baselineY;
            float sourceY = baselineY - g.Top - .5f;
            int y0 = (int)MathF.Floor(sourceY);
            float fractionY = sourceY - y0;
            int first = Math.Max(0, (int)MathF.Floor(rowLeft * scale) - b.Left);
            int end = Math.Min(b.Width, (int)MathF.Ceiling((rowLeft + g.Width) * scale) - b.Left);
            for (int x = first; x < end; x++)
            {
                float sourceX = (x + b.Left + .5f) / scale - rowLeft - .5f;
                int x0 = (int)MathF.Floor(sourceX);
                float fractionX = sourceX - x0;
                float a = MathHelper.Lerp(Sample(g, x0, y0), Sample(g, x0 + 1, y0), fractionX);
                float c = MathHelper.Lerp(Sample(g, x0, y0 + 1), Sample(g, x0 + 1, y0 + 1), fractionX);
                int alpha = (int)MathF.Round(MathHelper.Lerp(a, c, fractionY));
                int dest = y * b.Width + x;
                output[dest] += (byte)((alpha * (255 - output[dest]) + 127) / 255);
            }
        }
    }

    private static byte Sample(Glyph g, int x, int y) => (uint)x < g.Width && (uint)y < g.Height
        ? coverage[g.Offset + y * g.Width + x] : (byte)0;
}
