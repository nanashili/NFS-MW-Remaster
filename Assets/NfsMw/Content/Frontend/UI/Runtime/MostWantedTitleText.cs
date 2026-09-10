using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving
{
    /// <summary>Recovered title atlas and advances, drawn at the panel's display scale.</summary>
    public sealed class MostWantedTitleText : VisualElement
    {
        [Serializable] private sealed class Catalog { public FontMetrics[] fonts; }
        [Serializable] private sealed class FontMetrics
        { public string name, textureResourcePath; public float atlasWidth, atlasHeight, lineHeight; public Glyph[] glyphs; public Kerning[] kerning; }
        [Serializable] private sealed class Glyph
        { public int codepoint; public float x, y, width, height, advance, bearingX, offsetY; }
        [Serializable] private sealed class Kerning { public int leftCodepoint, rightCodepoint; public float advanceAdjustment; }
        private static FontMetrics metrics;
        private static Texture2D atlas;
        private static readonly Dictionary<int, Glyph> glyphs = new Dictionary<int, Glyph>();
        private static readonly Dictionary<long, float> pairs = new Dictionary<long, float>();

        public MostWantedTitleText(string text, float width, float height, float size, bool right = false)
        {
            pickingMode = PickingMode.Ignore;
            if (metrics == null)
            {
                var source = Resources.Load<TextAsset>("MostWantedUI/Fonts/FontMetrics");
                if (source != null)
                    foreach (var font in JsonUtility.FromJson<Catalog>(source.text).fonts)
                        if (font.name == "FONT_MW_TITLE") metrics = font;
                if (metrics == null) return;
                atlas = Resources.Load<Texture2D>(metrics.textureResourcePath);
                foreach (var glyph in metrics.glyphs) glyphs[glyph.codepoint] = glyph;
                foreach (var pair in metrics.kerning) pairs[((long)pair.leftCodepoint << 32) | (uint)pair.rightCodepoint] = pair.advanceAdjustment;
            }
            if (atlas == null) return;
            float scale = size / metrics.lineHeight;
            float advance = 0;
            int previous = -1;
            foreach (char letter in text)
            {
                if (!glyphs.TryGetValue(letter, out var glyph)) continue;
                advance += Pair(previous, letter) + glyph.advance;
                previous = letter;
            }
            scale = Mathf.Min(scale, width / Mathf.Max(1, advance));
            float x = right ? width - advance * scale : 0;
            float top = (height - metrics.lineHeight * scale) * .5f;
            previous = -1;
            foreach (char letter in text)
            {
                if (!glyphs.TryGetValue(letter, out var glyph)) continue;
                x += Pair(previous, letter) * scale;
                if (glyph.width > 0 && glyph.height > 0)
                {
                    var image = MostWantedFrontendStyle.Image(this, atlas, x + glyph.bearingX * scale,
                        top + glyph.offsetY * scale, glyph.width * scale, glyph.height * scale, Color.white, ScaleMode.StretchToFill);
                    image.uv = new Rect(glyph.x / metrics.atlasWidth, 1 - (glyph.y + glyph.height) / metrics.atlasHeight,
                        glyph.width / metrics.atlasWidth, glyph.height / metrics.atlasHeight);
                }
                x += glyph.advance * scale;
                previous = letter;
            }
        }
        private static float Pair(int left, int right) => pairs.TryGetValue(((long)left << 32) | (uint)right, out float adjustment) ? adjustment : 0;
    }
}
