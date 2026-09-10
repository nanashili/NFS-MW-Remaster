using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public enum MostWantedFrontendPage
    {
        MainMenu, Career, Safehouse, Blacklist, RivalBio, RivalCar, RaceEvents,
        Milestones, Bounty, Garage, Customize, Paint, Cart, Options, Audio, Video,
        AdvancedVideo, Gameplay, Player, Controls, Music, Credits, Lan, Online,
        Showcase, Pause, Loading, Results, Faulted, Title, AliasPrompt, AliasEntry, CareerLoad, CareerNew,
        QuickRace, ChallengeSeries, AliasManager, GameStats
    }

    /// <summary>Presentation history only. Never owns scenes, saves, rewards or purchases.</summary>
    public sealed class MostWantedFrontendNavigation
    {
        private readonly List<Entry> history = new List<Entry>();
        public sealed class Entry
        {
            public MostWantedFrontendPage Page { get; }
            public int Selection { get; set; }
            public string FocusName { get; set; } = string.Empty;
            public Entry(MostWantedFrontendPage page) { Page = page; }
        }
        public Entry Current => history[history.Count - 1];
        public int Depth => history.Count;
        public bool CanGoBack => history.Count > 1;

        public MostWantedFrontendNavigation(MostWantedFrontendPage root = MostWantedFrontendPage.MainMenu) => Reset(root);
        public void Reset(MostWantedFrontendPage root)
        { history.Clear(); history.Add(new Entry(root)); }
        public void Push(MostWantedFrontendPage page)
        {
            if (Current.Page == page) return;
            // A bounded stack protects against accidentally cyclic menu definitions.
            if (history.Count >= 32) throw new InvalidOperationException("Frontend navigation depth exceeded.");
            history.Add(new Entry(page));
        }
        public bool Back()
        {
            if (!CanGoBack) return false;
            history.RemoveAt(history.Count - 1); return true;
        }
        public int Select(int requested, int count, bool wrap = true)
        {
            Current.Selection = count <= 0 ? 0 : wrap ? ((requested % count) + count) % count
                : Math.Max(0, Math.Min(count - 1, requested));
            return Current.Selection;
        }
        public static bool IsSettings(MostWantedFrontendPage page) => page == MostWantedFrontendPage.Audio
            || page == MostWantedFrontendPage.Video || page == MostWantedFrontendPage.AdvancedVideo
            || page == MostWantedFrontendPage.Gameplay || page == MostWantedFrontendPage.Player
            || page == MostWantedFrontendPage.Controls;
    }
}
