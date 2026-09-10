using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private readonly List<MusicSection> musicSections = new List<MusicSection>();
        private MostWantedFrontendMusicPreview musicPreview;
        private AdaptiveMusic musicDirector;
        private SensoryAudioWorld menuAudioWorld;
        private Label musicNotice;
        private MusicSection musicPreviewSection;
        public MusicSection PreviewedMusicSection => musicPreview?.IsPlaying == true ? musicPreviewSection : null;

        private void RenderMusic()
        {
            Header("ea™ trax");
            var panel = Panel(content, 184, 129, 1160, 744);
            panel.CornerOutset = 10; panel.HeaderHeight = 40; panel.StripeOpacity = .4f;
            musicSections.Clear();
            musicDirector = AdaptiveMusic.Instance ?? FindAnyObjectByType<AdaptiveMusic>();
            var profile = musicDirector != null ? musicDirector.Profile : frontendContent?.soundtrack;
            if (profile != null && profile.HasArrangement)
            {
                foreach (var section in profile.sections)
                    if (section != null) musicSections.Add(section);
            }
            else if (profile != null && profile.stems != null && profile.stems.Length > 0)
            {
                var stems = new List<MusicStem>();
                foreach (var stem in profile.stems)
                    if (stem != null) stems.Add(new MusicStem { clip = stem.clip, displayName = stem.displayName, gain = 1 });
                musicSections.Add(new MusicSection
                {
                    stableId = "legacy", displayName = "Legacy sensory arrangement", stems = stems.ToArray(),
                    eligibleContexts = AdaptiveMusicContext.All
                });
            }

            var order = Panel(content, 752, 51, 574, 58, true, false); order.Corners = false; order.StripeOpacity = .9f;
            string orderLabel = preferences != null && preferences.Current.eaTraxOrder == MostWantedFrontendMusicOrder.Random ? "Random" : "Sequential";
            Text(order, orderLabel, 48, 0, 478, 58, 38, Color.white, TextAnchor.MiddleCenter);
            OrderArrow(order, 12, false);
            OrderArrow(order, 514, true);
            order.tooltip = orderLabel == "Random" ? "Tracks are selected randomly from the published soundtrack." : "Tracks follow the published soundtrack order.";
            if (musicSections.Count == 0)
            {
                Text(content, "No tracks available.", 265, 309, 1003, 99, 37, Khaki, TextAnchor.MiddleCenter);
            }
            else
            {
                int selected = navigation.Select(navigation.Current.Selection, musicSections.Count);
                var scroll = Place(new ScrollView(ScrollViewMode.Vertical) { name = "music-list" }, 184, 177, 1210, 651);
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                StyleScrollbar(scroll);
                content.Add(scroll);
                for (int i = 0; i < musicSections.Count; i++)
                {
                    int captured = i;
                    var section = musicSections[i];
                    var row = new VisualElement { name = "music-row-" + i };
                    row.style.height = 166; row.style.flexShrink = 0; scroll.Add(row);
                    var button = Button(row, string.Empty, () => SelectMusic(captured), "music-section-" + i, 0, 0, 1158, 146);
                    if (i == selected)
                    {
                        button.style.backgroundColor = new Color(.10f, .065f, 0, .7f);
                        scroll.schedule.Execute(() => scroll.ScrollTo(row));
                    }
                    SplitTrackName(section.displayName, out string number, out string title, out string artist);
                    button.Add(Place(new MostWantedTitleText(number.Length > 0 ? number : (i + 1).ToString("00"), 75, 61, 59), 12, 2, 75, 61));
                    Text(button, title, 100, 2, 795, 43, 32, Khaki);
                    Text(button, artist, 100, 43, 760, 43, 32, i == selected ? Amber : Khaki);
                    if (!string.IsNullOrEmpty(section.album)) Text(button, section.album, 100, 84, 760, 43, 32);
                    Text(button, MusicContextLabel(section.eligibleContexts), 869, 42, 160, 52, 32, Khaki, TextAnchor.MiddleCenter);
                    button.RegisterCallback<FocusInEvent>(_ => navigation.Select(captured, musicSections.Count));
                }
                horizontalNavigation = delta => SelectMusic(navigation.Select(navigation.Current.Selection + delta, musicSections.Count));
            }
            musicNotice = Text(content, string.Empty, 228, 837, 1068, 32, 20, Khaki);
            musicNotice.name = "music-status";
            Footer("Back", GoBack);
            Footer("Accept", GoBack, "Enter", Key.Enter);
            Footer("Re-Order", () => { }, "1", Key.None, false);
            Footer("Preview", PreviewSelectedMusic, "2", Key.Digit2,
                musicSections.Count > 0);
            UpdateMusicNotice();
        }

        private void SelectMusic(int index)
        {
            navigation.Select(index, musicSections.Count);
            navigation.Current.FocusName = "music-section-" + navigation.Current.Selection;
            Render();
        }

        private void PreviewSelectedMusic()
        {
            if (musicSections.Count == 0) return;
            var world = musicDirector != null ? musicDirector.AudioWorld : null;
            if (world == null)
            {
                if (menuAudioWorld == null)
                {
                    var owner = new GameObject("Frontend music preview"); owner.transform.SetParent(transform, false);
                    menuAudioWorld = owner.AddComponent<SensoryAudioWorld>();
                }
                world = menuAudioWorld;
                UpdateMenuMusicVolume();
            }
            var selected = musicSections[navigation.Select(navigation.Current.Selection, musicSections.Count)];
            musicPreviewSection = selected;
            musicPreview ??= new MostWantedFrontendMusicPreview();
            if (!musicPreview.TryPlay(world, selected.displayName, selected.stems, out string failure))
                feedback = failure;
            else feedback = null;
            musicDirector?.SetPreviewActive(musicPreview.IsPlaying || musicPreview.IsLoading);
            UpdateMusicNotice();
        }

        private void UpdateMusicNotice()
        {
            if (musicNotice == null || navigation.Current.Page != MostWantedFrontendPage.Music) return;
            if (musicPreview != null && !string.IsNullOrEmpty(musicPreview.Status))
                musicNotice.text = musicPreview.Status;
            else if (musicDirector == null)
                musicNotice.text = musicSections.Count == 0 ? "No active music service." : string.Empty;
            else
            {
                musicNotice.text = string.Empty;
            }
        }

        private void TickMusicPreview()
        {
            if (musicPreview == null) return;
            if (!IsVisible || navigation.Current.Page != MostWantedFrontendPage.Music) StopMusicPreview();
            else
            {
                UpdateMenuMusicVolume(); musicPreview.Tick();
                musicDirector?.SetPreviewActive(musicPreview.IsPlaying || musicPreview.IsLoading);
            }
        }

        private void StopMusicPreview()
        {
            musicPreview?.Stop();
            musicDirector?.SetPreviewActive(false);
            musicPreviewSection = null;
            if (menuAudioWorld != null) { Destroy(menuAudioWorld.gameObject); menuAudioWorld = null; }
            UpdateMusicNotice();
        }

        private void DisposeMusicPreview()
        { StopMusicPreview(); musicPreview?.Dispose(); musicPreview = null; musicNotice = null; musicDirector = null; musicSections.Clear(); }

        private void UpdateMenuMusicVolume()
        {
            if (menuAudioWorld == null || preferences == null) return;
            var current = preferences.Current;
            menuAudioWorld.Preferences.master = current.audio.master;
            menuAudioWorld.Preferences.music = current.menuMusic;
            menuAudioWorld.ApplyPreferences(false);
        }

        private static void SplitTrackName(string value, out string number, out string title, out string artist)
        {
            MostWantedMusicCard.TrackText(value, out number, out title, out artist);
        }

        private static string MusicContextLabel(AdaptiveMusicContext context)
        {
            if (context == AdaptiveMusicContext.Frontend) return "Menu";
            if (context == AdaptiveMusicContext.None) return "Unassigned";
            return (context & AdaptiveMusicContext.Frontend) != 0 ? "All" : "Race";
        }

        private static void OrderArrow(VisualElement parent, float x, bool right)
        {
            var circle = Place(new VisualElement(), x, 7, 43, 43);
            circle.style.borderTopLeftRadius = circle.style.borderTopRightRadius = circle.style.borderBottomLeftRadius = circle.style.borderBottomRightRadius = 24;
            circle.style.borderTopWidth = circle.style.borderRightWidth = circle.style.borderBottomWidth = circle.style.borderLeftWidth = 3;
            circle.style.borderTopColor = circle.style.borderRightColor = circle.style.borderBottomColor = circle.style.borderLeftColor = Color.white;
            parent.Add(circle);
            var arrow = Image(circle, MostWantedFrontendArt.Find("MostWantedUI/Images/Menus/Navigation/ArrowSkinnier"), 4, 4, 29, 29);
            if (right) arrow.style.scale = new Scale(new Vector3(-1, 1, 1));
        }
    }
}
