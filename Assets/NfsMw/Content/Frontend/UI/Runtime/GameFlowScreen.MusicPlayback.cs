using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private const string FrontendMusicSource = "frontend.ea-trax";
        private string frontendMusicRequestedSectionId = string.Empty;
        private int frontendMusicIndex = -1;
        private bool frontendMusicWasVisible;
        private bool frontendMusicVolumeApplied;
        private SensoryMusicProfile frontendMusicProfile;
        private readonly List<MusicSection> frontendMusicSections = new List<MusicSection>();

        private bool IsFrontendMusicVisible()
        {
            return IsVisible && runtime?.AllowsFrontendMusic == true;
        }

        private void TickFrontendMusic()
        {
            bool visible = IsFrontendMusicVisible();
            AdaptiveMusic director = AdaptiveMusic.Instance;
            var settings = preferences?.Current;
            bool enabled = settings == null || settings.eaTraxEnabled;
            if (!visible || director == null || !director.HasStarted || director.Profile == null || !enabled)
            {
                if (frontendMusicVolumeApplied && director != null && settings != null)
                {
                    ApplyFrontendMusicVolume(director, false, settings);
                    frontendMusicVolumeApplied = false;
                }
                if (director != null && (!visible || !enabled)) director.CancelRequestsFromSource(FrontendMusicSource);
                if (!visible || !enabled || director == null || !director.HasStarted || director.Profile == null)
                    ResetFrontendMusicSelection();
                return;
            }

            EnsureFrontendMusicSections(director.Profile);
            if (frontendMusicSections.Count == 0) return;
            if (!frontendMusicWasVisible)
            {
                frontendMusicWasVisible = true;
                frontendMusicRequestedSectionId = string.Empty;
                frontendMusicIndex = settings.eaTraxOrder == MostWantedFrontendMusicOrder.Random
                    ? UnityEngine.Random.Range(0, frontendMusicSections.Count) : 0;
            }

            ApplyFrontendMusicVolume(director, true, settings);
            frontendMusicVolumeApplied = true;
            var transport = director.Transport;
            if (string.IsNullOrEmpty(frontendMusicRequestedSectionId))
            {
                var selected = frontendMusicSections[Mathf.Clamp(frontendMusicIndex, 0, frontendMusicSections.Count - 1)];
                if (selected != null && director.RequestSection(selected.stableId, FrontendMusicSource, 501, 0, MusicQuantization.SectionEnd))
                    frontendMusicRequestedSectionId = selected.stableId;
            }
            else if (transport.activeSectionId == frontendMusicRequestedSectionId)
            {
                var active = director.Profile.FindSection(transport.activeSectionId);
                double duration = active == null ? 0 : active.LoopDurationSeconds();
                double lookahead = director.Profile.playback == null ? .2 : Mathf.Clamp(director.Profile.playback.scheduleLookaheadSeconds, .05f, 4f);
                if (duration > 0 && transport.cuePositionSeconds >= duration - Math.Max(.1, lookahead * 1.5))
                {
                    int nextIndex = NextFrontendMusicIndex(frontendMusicIndex, settings.eaTraxOrder);
                    var next = frontendMusicSections[nextIndex];
                    if (next != null && director.RequestSection(next.stableId, FrontendMusicSource, 501, 0, MusicQuantization.SectionEnd))
                    {
                        frontendMusicIndex = nextIndex;
                        frontendMusicRequestedSectionId = next.stableId;
                    }
                }
            }
        }

        private void EnsureFrontendMusicSections(SensoryMusicProfile profile)
        {
            if (profile == null) return;
            if (ReferenceEquals(frontendMusicProfile, profile)) return;
            frontendMusicProfile = profile;
            frontendMusicSections.Clear();
            if (profile.HasArrangement)
                foreach (var section in profile.sections)
                    if (section != null && section.stems != null && section.stems.Length > 0) frontendMusicSections.Add(section);
        }

        private int NextFrontendMusicIndex(int current, MostWantedFrontendMusicOrder order)
        {
            if (frontendMusicSections.Count <= 1) return 0;
            if (current < 0 || current >= frontendMusicSections.Count)
                return order == MostWantedFrontendMusicOrder.Random
                    ? UnityEngine.Random.Range(0, frontendMusicSections.Count) : 0;
            if (order == MostWantedFrontendMusicOrder.Random)
            {
                int next = UnityEngine.Random.Range(0, frontendMusicSections.Count - 1);
                return next >= current ? next + 1 : next;
            }
            return (current + 1 + frontendMusicSections.Count) % frontendMusicSections.Count;
        }

        private void ApplyFrontendMusicVolume(AdaptiveMusic director, bool frontend, MostWantedFrontendSettings settings)
        {
            var world = director?.AudioWorld;
            if (world?.Preferences == null || settings == null) return;
            float target = frontend ? settings.menuMusic : settings.audio.music;
            if (Mathf.Abs(world.Preferences.music - target) < .0001f) return;
            world.Preferences.music = target;
            world.ApplyPreferences(false);
        }

        private void StopFrontendMusic()
        {
            var director = AdaptiveMusic.Instance;
            var settings = preferences?.Current;
            if (frontendMusicVolumeApplied && director != null && settings != null)
                ApplyFrontendMusicVolume(director, false, settings);
            frontendMusicVolumeApplied = false;
            AdaptiveMusic.Instance?.CancelRequestsFromSource(FrontendMusicSource);
            ResetFrontendMusicSelection();
            frontendMusicProfile = null;
            frontendMusicSections.Clear();
        }

        private void ResetFrontendMusicSelection()
        {
            frontendMusicWasVisible = false;
            frontendMusicRequestedSectionId = string.Empty;
            frontendMusicIndex = -1;
        }
    }
}
