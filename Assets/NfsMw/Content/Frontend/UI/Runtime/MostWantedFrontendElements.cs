using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving
{
    /// <summary>Original texture lookup is manifest-driven; missing artwork never changes gameplay.</summary>
    public static class MostWantedFrontendArt
    {
        [Serializable] private sealed class Manifest { public TextureEntry[] textures = Array.Empty<TextureEntry>(); }
        [Serializable] private sealed class TextureEntry
        { public string name, resourcePath; public int width, height; }
        private static Dictionary<string, string> paths;
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static Texture2D Find(params string[] names)
        {
            if (paths == null)
            {
                paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var source = Resources.Load<TextAsset>("MostWantedUI/Catalog");
                if (source != null)
                {
                    var manifest = JsonUtility.FromJson<Manifest>(source.text);
                    foreach (var entry in manifest?.textures ?? Array.Empty<TextureEntry>())
                        if (!string.IsNullOrEmpty(entry.name) && !string.IsNullOrEmpty(entry.resourcePath)) paths[entry.name] = entry.resourcePath;
                }
            }
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (!textures.TryGetValue(name, out var texture))
                {
                    texture = name.StartsWith("MostWantedUI/", StringComparison.Ordinal)
                        ? Resources.Load<Texture2D>(name)
                        : paths.TryGetValue(name, out var path) ? Resources.Load<Texture2D>(path) : null;
                    textures[name] = texture;
                }
                if (texture != null) return texture;
            }
            return null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { paths = null; textures.Clear(); }
    }

    public static class MostWantedFrontendStyle
    {
        public const float Width = 1536, Height = 992;
        public static readonly Color Amber = new Color32(216, 153, 0, 255);
        public static readonly Color Khaki = new Color32(199, 200, 159, 255);
        public static readonly Color Muted = new Color32(115, 115, 108, 255);

        public static T Place<T>(T element, float x, float y, float width, float height) where T : VisualElement
        {
            element.style.position = Position.Absolute;
            element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height;
            return element;
        }
        public static VisualElement Box(VisualElement parent, string name, float x, float y, float w, float h)
        {
            var element = Place(new VisualElement { name = name }, x, y, w, h); parent.Add(element); return element;
        }
        public static Label Text(VisualElement parent, string value, float x, float y, float w, float h,
            int size = 30, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Place(new MostWantedBodyText(value ?? string.Empty, size), x, y, w, h);
            label.pickingMode = PickingMode.Ignore;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.color = color ?? Color.white; label.style.unityTextAlign = alignment;
            label.style.whiteSpace = WhiteSpace.Normal; label.style.paddingLeft = label.style.paddingRight = 0;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            parent.Add(label); return label;
        }
        public static Image Image(VisualElement parent, Texture texture, float x, float y, float w, float h,
            Color? tint = null, ScaleMode mode = ScaleMode.ScaleToFit)
        {
            var image = Place(new Image { image = texture, scaleMode = mode, tintColor = tint ?? Color.white,
                pickingMode = PickingMode.Ignore }, x, y, w, h);
            parent.Add(image); return image;
        }
    }

    /// <summary>Geometry retains crisp diagonal stripes and four separated corner brackets at any resolution.</summary>
    public sealed class MostWantedPanel : VisualElement
    {
        public bool Stripes { get; set; }
        public bool Corners { get; set; } = true;
        public bool HeaderBands { get; set; }
        public float CornerOutset { get; set; }
        public float HeaderHeight { get; set; } = 66;
        public Color CornerColor { get; set; } = Color.white;
        private Color fill = new Color(0,0,0,.96f);
        public Color Fill { get => fill; set { fill=value; style.backgroundColor=value; } }
        public float StripeOpacity { get; set; } = .165f;
        public MostWantedPanel() { pickingMode = PickingMode.Ignore; Fill=fill; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            var p = context.painter2D;
            if (Stripes) Stripe(p, 0, h, w, StripeOpacity);
            if (HeaderBands)
            {
                Rect(p, 0, 0, w, HeaderHeight, new Color(0, 0, 0, .8f)); Stripe(p, 0, HeaderHeight, w, .96f);
                Rect(p, 0, h - 48, w, 48, new Color(0, 0, 0, .65f)); Stripe(p, h - 48, 48, w, .96f);
                Rect(p, 0, HeaderHeight, w, 7, new Color(.22f, .22f, .21f, .9f));
                Rect(p, 0, h - 55, w, 7, new Color(.22f, .22f, .21f, .9f));
            }
            if (!Corners) return;
            p.strokeColor = CornerColor; p.lineWidth = 4;
            Corner(p, 2 - CornerOutset, 2 - CornerOutset, 1, 1); Corner(p, w - 2 + CornerOutset, 2 - CornerOutset, -1, 1);
            Corner(p, 2 - CornerOutset, h - 2 + CornerOutset, 1, -1); Corner(p, w - 2 + CornerOutset, h - 2 + CornerOutset, -1, -1);
        }
        private static void Corner(Painter2D p, float x, float y, float dx, float dy)
        { p.BeginPath(); p.MoveTo(new Vector2(x + dx * 23, y)); p.LineTo(new Vector2(x, y)); p.LineTo(new Vector2(x, y + dy * 19)); p.Stroke(); }
        internal static void Rect(Painter2D p, float x, float y, float w, float h, Color color)
        {
            p.fillColor = color; p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }
        private static void Stripe(Painter2D p, float y, float h, float w, float opacity)
        {
            // Clipped convex polygons: no oversized stripe vertices can bleed into the scene.
            for (float x = -h - 30; x < w; x += 30)
            {
                var polygon = new List<Vector2> { new Vector2(x, y+h), new Vector2(x+13,y+h),new Vector2(x+h+13,y),new Vector2(x+h,y) };
                polygon = Clip(polygon, 0, true); polygon = Clip(polygon, w, false);
                if (polygon.Count < 3) continue;
                float shade = opacity * .15f;
                p.fillColor = new Color(shade,shade,shade,1); p.BeginPath(); p.MoveTo(polygon[0]);
                for (int i=1;i<polygon.Count;i++) p.LineTo(polygon[i]); p.ClosePath(); p.Fill();
            }
        }
        private static List<Vector2> Clip(List<Vector2> source, float edge, bool left)
        {
            var result = new List<Vector2>();
            if (source.Count == 0) return result;
            var previous = source[source.Count - 1]; bool wasInside = left ? previous.x >= edge : previous.x <= edge;
            foreach (var current in source)
            {
                bool inside = left ? current.x >= edge : current.x <= edge;
                if (inside != wasInside) result.Add(Vector2.LerpUnclamped(previous,current,(edge-previous.x)/(current.x-previous.x)));
                if (inside) result.Add(current); previous=current;wasInside=inside;
            }
            return result;
        }
    }

    /// <summary>Readable geometric fallbacks are only used when the original named texture is unavailable.</summary>
    public sealed class MostWantedIcon : VisualElement
    {
        private readonly string kind;
        private readonly Color color;
        public MostWantedIcon(string icon, Color tint)
        { kind=icon ?? "car";color=tint;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw; }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D; float w=contentRect.width,h=contentRect.height;
            p.strokeColor=color;p.fillColor=color;p.lineWidth=Mathf.Min(w,h)*.055f;
            Vector2 V(float x,float y)=>new Vector2(x*w,y*h);
            void Line(params Vector2[] points) { p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke(); }
            if(kind=="left"||kind=="right")
            { float sign=kind=="right"?1:-1;p.lineWidth=w*.19f;Line(V(.5f-sign*.16f,.18f),V(.5f+sign*.16f,.5f),V(.5f-sign*.16f,.82f));return; }
            if(kind=="flag")
            {
                Line(V(.18f,.92f),V(.18f,.12f),V(.87f,.19f),V(.78f,.69f),V(.18f,.61f));
                for(int row=0;row<3;row++)for(int column=0;column<4;column++)if((row+column)%2==0)
                    MostWantedPanel.Rect(p,(.22f+column*.13f)*w,(.24f+row*.12f)*h,w*.115f,h*.105f,color);
                return;
            }
            if(kind=="clock"||kind=="camera")
            {
                p.BeginPath();p.Arc(V(.5f,.54f),w*.3f,0,360);p.Stroke();
                if(kind=="clock"){Line(V(.5f,.54f),V(.66f,.34f));Line(V(.4f,.11f),V(.61f,.11f));}
                else {Line(V(.12f,.35f),V(.12f,.84f),V(.89f,.84f),V(.89f,.35f),V(.12f,.35f));Line(V(.35f,.22f),V(.65f,.22f));}
                return;
            }
            if(kind=="badge"||kind=="bounty"||kind=="blacklist")
            {
                Line(V(.18f,.12f),V(.82f,.12f),V(.86f,.48f),V(.73f,.77f),V(.5f,.93f),V(.27f,.77f),V(.14f,.48f),V(.18f,.12f));
                Line(V(.32f,.38f),V(.68f,.38f));Line(V(.32f,.53f),V(.68f,.53f));Line(V(.32f,.68f),V(.61f,.68f));return;
            }
            if(kind=="wrench"||kind=="paint"||kind=="options")
            {Line(V(.23f,.85f),V(.67f,.42f),V(.87f,.31f),V(.83f,.13f),V(.65f,.31f),V(.54f,.2f),V(.65f,.05f),V(.43f,.12f),V(.4f,.37f),V(.08f,.73f),V(.23f,.85f));return;}
            if(kind=="music")
            {Line(V(.31f,.73f),V(.31f,.2f),V(.78f,.09f),V(.78f,.65f));p.BeginPath();p.Arc(V(.21f,.77f),w*.12f,0,360);p.Fill();p.BeginPath();p.Arc(V(.67f,.69f),w*.12f,0,360);p.Fill();return;}
            if(kind=="check") {Line(V(.17f,.52f),V(.4f,.75f),V(.85f,.18f));return;}
            Line(V(.12f,.72f),V(.12f,.42f),V(.26f,.3f),V(.36f,.13f),V(.72f,.13f),V(.82f,.33f),V(.91f,.43f),V(.91f,.72f),V(.12f,.72f));
            Line(V(.25f,.33f),V(.77f,.33f));Line(V(.2f,.54f),V(.35f,.54f));Line(V(.69f,.54f),V(.84f,.54f));
            MostWantedPanel.Rect(p,w*.18f,h*.73f,w*.17f,h*.12f,color);MostWantedPanel.Rect(p,w*.7f,h*.73f,w*.17f,h*.12f,color);
        }
    }
}
