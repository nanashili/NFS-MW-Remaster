using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving
{
    /// <summary>Draws the recovered body glyphs while retaining Label text and accessibility semantics.</summary>
    public sealed class MostWantedBodyText : Label
    {
        [Serializable] private sealed class Catalog { public Metrics[] fonts; }
        [Serializable] private sealed class Metrics
        { public string name, textureResourcePath; public float atlasWidth, atlasHeight, lineHeight; public Glyph[] glyphs; public Pair[] kerning; }
        [Serializable] private sealed class Glyph
        { public int codepoint; public float x, y, width, height, advance, bearingX, offsetY; }
        [Serializable] private sealed class Pair { public int leftCodepoint, rightCodepoint; public float advanceAdjustment; }
        private static Metrics metrics;
        private static Texture2D atlas;
        private static readonly Dictionary<int, Glyph> glyphs = new Dictionary<int, Glyph>();
        private static readonly Dictionary<long, float> pairs = new Dictionary<long, float>();
        private readonly float textSize;

        public MostWantedBodyText(string value, float size) : base(value)
        {
            textSize = size;
            // Native glyphs have zero size; the source atlas supplies the visible text.
            style.fontSize = 0;
            generateVisualContent += Draw;
        }

        private static float Kerning(int left, int right) => pairs.TryGetValue(((long)left << 32) | (uint)right, out float value) ? value : 0;
        private static bool Load()
        {
            if (metrics != null) return atlas != null;
            var source = Resources.Load<TextAsset>("MostWantedUI/Fonts/FontMetrics");
            if (source == null) return false;
            foreach (var font in JsonUtility.FromJson<Catalog>(source.text).fonts)
                if (font.name == "FONT_MW_BODY") metrics = font;
            if (metrics == null) return false;
            atlas = Resources.Load<Texture2D>(metrics.textureResourcePath);
            foreach (var glyph in metrics.glyphs) glyphs[glyph.codepoint] = glyph;
            foreach (var pair in metrics.kerning) pairs[((long)pair.leftCodepoint << 32) | (uint)pair.rightCodepoint] = pair.advanceAdjustment;
            return atlas != null;
        }

        public static float MeasureWidth(string value, float size)
        { return Load() ? Measure(value ?? string.Empty) * size / 18f : 0; }

        private static float Measure(string value)
        {
            float width = 0; int previous = -1;
            foreach (char letter in value)
                if (glyphs.TryGetValue(letter, out var glyph))
                { width += Kerning(previous, letter) + glyph.advance; previous = letter; }
            return width;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!Load() || string.IsNullOrEmpty(text) || contentRect.width <= 0) return;
            float scale = textSize / 18f;
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                if (resolvedStyle.whiteSpace == WhiteSpace.NoWrap) { lines.Add(paragraph); continue; }
                string line = string.Empty;
                foreach (string word in paragraph.Split(' '))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Measure(next) * scale > contentRect.width)
                    { lines.Add(line); line = word; }
                    else line = next;
                }
                lines.Add(line);
            }
            var alignment = resolvedStyle.unityTextAlign;
            float totalHeight = lines.Count * metrics.lineHeight * scale;
            float top = alignment >= TextAnchor.LowerLeft ? contentRect.height - totalHeight
                : alignment >= TextAnchor.MiddleLeft ? (contentRect.height - totalHeight) * .5f : 0;
            var tint = (Color32)resolvedStyle.color;
            foreach (string line in lines)
            {
                float width = Measure(line) * scale;
                int horizontal = (int)alignment % 3;
                float x = horizontal == 2 ? contentRect.width - width : horizontal == 1 ? (contentRect.width - width) * .5f : 0;
                int previous = -1;
                foreach (char letter in line)
                {
                    if (!glyphs.TryGetValue(letter, out var glyph)) continue;
                    x += Kerning(previous, letter) * scale;
                    if (glyph.width > 0 && glyph.height > 0)
                    {
                        var mesh = context.Allocate(4, 6, atlas);
                        float l = x + glyph.bearingX * scale, t = top + glyph.offsetY * scale;
                        float r = l + glyph.width * scale, b = t + glyph.height * scale;
                        var uv = mesh.uvRegion;
                        float u0 = uv.x + glyph.x / metrics.atlasWidth * uv.width;
                        float u1 = u0 + glyph.width / metrics.atlasWidth * uv.width;
                        float v0 = uv.y + (1 - glyph.y / metrics.atlasHeight) * uv.height;
                        float v1 = v0 - glyph.height / metrics.atlasHeight * uv.height;
                        mesh.SetNextVertex(new Vertex { position = new Vector3(l, t, Vertex.nearZ), tint = tint, uv = new Vector2(u0, v0) });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(r, t, Vertex.nearZ), tint = tint, uv = new Vector2(u1, v0) });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(r, b, Vertex.nearZ), tint = tint, uv = new Vector2(u1, v1) });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(l, b, Vertex.nearZ), tint = tint, uv = new Vector2(u0, v1) });
                        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                        mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
                    }
                    x += glyph.advance * scale; previous = letter;
                }
                top += metrics.lineHeight * scale;
            }
        }
    }
}
