using System;
using UnityEngine;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    /// <summary>A track announcement lives across menu rebuilds and uses the playing transport.</summary>
    public sealed class MostWantedMusicOverlay : MonoBehaviour
    {
        private PanelSettings panel;
        private UIDocument document;
        private VisualElement canvas;
        private MostWantedMusicCard card;
        private string announcedSection = string.Empty;
        private double announcedAt;
        private bool wasVisible, wasFrontend;
        public MostWantedMusicCard Card => card;

        private void Awake()
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "EA TRAX announcement";
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("GameFlowTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int((int)Width, (int)Height);
            panel.screenMatchMode = PanelScreenMatchMode.Expand;
            panel.sortingOrder = 1100;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            canvas = Box(root, "ea-trax-canvas", 0, 0, Width, Height);
            canvas.pickingMode = PickingMode.Ignore;
            canvas.style.left = canvas.style.top = Length.Percent(50);
            canvas.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            card = new MostWantedMusicCard();
            canvas.Add(card);
            card.ShowAtAge(MostWantedMusicCard.Duration);
        }

        private void Update()
        {
            var director = AdaptiveMusic.Instance;
            var application = GameFlowRuntime.Instance;
            bool frontend = application?.AllowsFrontendMusic == true;
            bool driving = application == null ? !AudioListener.pause : application.AllowsWorldInput;
            bool enabled = application?.Preferences?.Current.eaTraxEnabled ?? true;
            var section = director?.Profile?.FindSection(director.Transport.activeSectionId);
            var preview = application?.GetComponent<GameFlowScreen>()?.PreviewedMusicSection;
            if (preview != null) section = preview;
            string sectionKey = (preview == null ? string.Empty : "preview:") + section?.stableId;
            bool visible = enabled && section != null && (frontend || driving);
            if (!visible)
            {
                canvas.style.display = DisplayStyle.None;
                wasVisible = false;
                return;
            }
            canvas.style.display = DisplayStyle.Flex;
            card.style.top = frontend ? 758 : 256;
            card.SetAmber(!frontend);
            if (!wasVisible || wasFrontend != frontend || announcedSection != sectionKey)
            {
                announcedSection = sectionKey;
                announcedAt = Time.unscaledTimeAsDouble;
                card.SetTrack(section);
            }
            wasVisible = true; wasFrontend = frontend;
            card.ShowAtAge((float)(Time.unscaledTimeAsDouble - announcedAt));
        }

        private void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }
    }

    /// <summary>Recovered EA badge, expanding panel, three lines and right-hand TRAX tab.</summary>
    public sealed class MostWantedMusicCard : VisualElement
    {
        public const float Duration = 5.9f;
        private readonly VisualElement reveal, disc, copy;
        private readonly Image panelImage, tab;
        private readonly Label title, artist, album;
        private readonly Ring ring;
        private float panelWidth = 600;
        private bool amber;
        public float AnimationAge { get; private set; }
        public float PanelReveal { get; private set; }

        public MostWantedMusicCard()
        {
            name = "ea-trax-announcement";
            pickingMode = PickingMode.Ignore;
            Place(this, -3, 758, 1100, 128);
            reveal = Box(this, "ea-trax-panel-reveal", 64, 8, 0, 114);
            reveal.style.overflow = Overflow.Hidden;
            panelImage = Image(reveal, Art("MusicPanelBlack"), 0, 0, panelWidth, 114, Color.white, ScaleMode.StretchToFill);
            tab = Image(this, Art("MusicTabBlack"), 64, 8, 36, 114, Color.white, ScaleMode.StretchToFill);
            copy = Box(reveal, "ea-trax-text", 78, 12, 950, 106);
            title = Text(copy, string.Empty, 0, 0, 950, 34, 28, Color.white);
            artist = Text(copy, string.Empty, 0, 32, 950, 34, 28, Khaki);
            album = Text(copy, string.Empty, 0, 64, 950, 34, 28, Khaki);
            title.name = "ea-trax-title"; artist.name = "ea-trax-artist"; album.name = "ea-trax-album";
            title.style.whiteSpace = artist.style.whiteSpace = album.style.whiteSpace = WhiteSpace.NoWrap;
            disc = Box(this, "ea-trax-badge", 0, 0, 128, 128);
            disc.style.backgroundColor = new Color(.025f, .025f, .025f, 1);
            disc.style.borderTopLeftRadius = disc.style.borderTopRightRadius =
                disc.style.borderBottomLeftRadius = disc.style.borderBottomRightRadius = 64;
            disc.style.overflow = Overflow.Hidden;
            Image(disc, MostWantedFrontendArt.Find("MostWantedUI/Images/Shared/Shapes/DiagonalStripesWhite"),
                6, 6, 116, 116, new Color(.20f, .20f, .20f), ScaleMode.StretchToFill);
            Image(disc, MostWantedFrontendArt.Find("MostWantedUI/Images/Branding/Game/EA"),
                9, 36, 108, 55, Color.white, ScaleMode.StretchToFill);
            ring = Place(new Ring(), 0, 0, 128, 128);
            Add(ring);
            IgnorePicking(this);
        }

        public void SetTrack(MusicSection section)
        {
            TrackText(section.displayName, out _, out string titleText, out string artistText);
            title.text = titleText; artist.text = artistText; album.text = section.album ?? string.Empty;
            float textWidth = Mathf.Max(MostWantedBodyText.MeasureWidth(titleText, 28),
                MostWantedBodyText.MeasureWidth(artistText, 28), MostWantedBodyText.MeasureWidth(album.text, 28));
            panelWidth = Mathf.Clamp(textWidth + 230, 450, 1000);
            panelImage.style.width = panelWidth;
        }

        public void SetAmber(bool value)
        {
            if (amber == value) return;
            amber = value;
            panelImage.image = Art(value ? "MusicPanelAmber" : "MusicPanelBlack");
            tab.image = Art(value ? "MusicTabAmber" : "MusicTabBlack");
        }

        public void ShowAtAge(float age)
        {
            AnimationAge = age;
            style.display = age >= Duration || age < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            PanelReveal = Ease((age - .40f) / .35f) * (1 - Ease((age - 4.80f) / .35f));
            reveal.style.width = panelWidth * PanelReveal;
            tab.style.left = 64 + panelWidth * PanelReveal;
            tab.style.opacity = PanelReveal;
            copy.style.opacity = Ease((age - .58f) / .18f) * (1 - Ease((age - 4.75f) / .15f));
            disc.style.opacity = Ease((age - .20f) / .22f) * (1 - Ease((age - 5.12f) / .30f));
            ring.Progress = Ease(age / .35f) * (1 - Ease((age - 5.45f) / .45f));
            ring.Rotation = age < .75f || age > 5.12f ? age * 190 : -155;
            ring.MarkDirtyRepaint();
        }

        public static void TrackText(string value, out string number, out string title, out string artist)
        {
            value ??= string.Empty; number = string.Empty;
            int context = value.LastIndexOf(" [", StringComparison.Ordinal);
            if (context >= 0) value = value.Substring(0, context);
            int ordinal = value.IndexOf(". ", StringComparison.Ordinal);
            if (ordinal > 0 && ordinal <= 2 && int.TryParse(value.Substring(0, ordinal), out int parsed))
            { number = parsed.ToString("00"); value = value.Substring(ordinal + 2); }
            int separator = value.IndexOf(" - ", StringComparison.Ordinal);
            artist = separator >= 0 ? value.Substring(0, separator) : string.Empty;
            title = separator >= 0 ? value.Substring(separator + 3) : value;
        }

        private static float Ease(float value) { value = Mathf.Clamp01(value); return value * value * (3 - 2 * value); }
        private static Texture2D Art(string name) => MostWantedFrontendArt.Find("MostWantedUI/Images/HUD/Music/" + name);
        private static void IgnorePicking(VisualElement element)
        { element.pickingMode = PickingMode.Ignore; foreach (var child in element.Children()) IgnorePicking(child); }

        private sealed class Ring : VisualElement
        {
            public float Progress, Rotation;
            public Ring() { generateVisualContent += Draw; }
            private void Draw(MeshGenerationContext context)
            {
                if (Progress <= 0) return;
                var painter = context.painter2D;
                painter.lineWidth = 2.5f;
                painter.strokeColor = new Color(.80f, .63f, .33f, .22f);
                painter.BeginPath(); painter.Arc(new Vector2(64, 64), 61, Rotation, Rotation + 359.9f * Progress); painter.Stroke();
                painter.lineWidth = 3;
                painter.strokeColor = new Color(1, .91f, .62f, .9f);
                painter.BeginPath(); painter.Arc(new Vector2(64, 64), 61, Rotation, Rotation + 100 * Progress); painter.Stroke();
            }
        }
    }
}
