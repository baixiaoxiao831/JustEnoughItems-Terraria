using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;

namespace JustEnoughItemsTerraria;

internal enum BrowserFontFace { Body, Search, Numbers, Italic }

internal static class BrowserFont
{
    private readonly record struct TextKey(string Text, BrowserFontFace Face, int Pixels);
    private sealed record RenderedText(Texture2D Texture)
    {
        internal int LastFrame;
    }

    private static readonly Dictionary<TextKey, RenderedText> labels = new();
    private static int frame;
    private static long pixelsCached;

    // The condensed page counters in the reference use less horizontal space
    // than Consolas' normal advance at the same cap height.
    private static float WidthScale(BrowserFontFace face) => face == BrowserFontFace.Numbers ? .90f : 1f;

    internal static void BeginFrame()
    {
        frame++;
        if (labels.Count <= 512 && pixelsCached <= 4 * 1024 * 1024) return;
        // Dispose only between interface draws, once the previous SpriteBatch has ended.
        foreach (var entry in labels.OrderBy(p => p.Value.LastFrame).ToArray())
        {
            labels.Remove(entry.Key);
            var texture = entry.Value.Texture;
            pixelsCached -= (long)texture.Width * texture.Height;
            texture.Dispose();
            if (labels.Count <= 384 && pixelsCached <= 3 * 1024 * 1024) break;
        }
    }

    internal static Vector2 Measure(string text, BrowserFontFace face, int pixels)
    {
        Vector2 size;
        if (labels.TryGetValue(new(text, face, pixels), out var entry) && !entry.Texture.IsDisposed)
            size = new(entry.Texture.Width, entry.Texture.Height);
        else size = JeiFallbackFont.Measure(text, face, pixels);
        size.X *= WidthScale(face);
        return size;
    }

    internal static void Draw(SpriteBatch batch, string text, BrowserFontFace face, int pixels,
        Vector2 position, Color color, float scale = 1)
    {
        if (string.IsNullOrEmpty(text)) return;
        var key = new TextKey(text, face, pixels);
        if (!labels.TryGetValue(key, out var rendered) || rendered.Texture.IsDisposed ||
            rendered.Texture.GraphicsDevice != batch.GraphicsDevice)
        {
            if (rendered != null)
            {
                pixelsCached -= (long)rendered.Texture.Width * rendered.Texture.Height;
                rendered.Texture.Dispose();
            }
            var texture = Rasterize(batch.GraphicsDevice, text, face, pixels);
            labels[key] = rendered = new RenderedText(texture);
            pixelsCached += (long)texture.Width * texture.Height;
        }
        rendered.LastFrame = frame;
        batch.Draw(rendered.Texture, position, null, color, 0, Vector2.Zero,
            new Vector2(WidthScale(face) * scale, scale), SpriteEffects.None, 0);
    }

    private static Texture2D Rasterize(GraphicsDevice device, string text, BrowserFontFace face, int pixels)
    {
        byte[] alpha = JeiFallbackFont.Rasterize(text, face, pixels, out int width, out int height);
        var colors = new Color[alpha.Length];
        for (int i = 0; i < alpha.Length; i++)
            colors[i] = new Color(alpha[i], alpha[i], alpha[i], alpha[i]);
        var texture = new Texture2D(device, width, height);
        texture.SetData(colors);
        return texture;
    }

    internal static void Load(Mod mod) => JeiFallbackFont.Load(mod);

    internal static void Unload()
    {
        foreach (var entry in labels.Values) entry.Texture.Dispose();
        labels.Clear();
        pixelsCached = 0;
        JeiFallbackFont.Unload();
    }

}
