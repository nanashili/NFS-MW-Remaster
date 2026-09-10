using System;
using NfsMwRemaster.Driving;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class AdaptiveMusicTests
    {
        [Test]
        public void QuantizationUsesTheDspOriginAndLeadTime()
        {
            Assert.AreEqual(4d, MusicTiming.NextBeat(3.95, 0, 120, 0.02), 0.00001);
            Assert.AreEqual(6d, MusicTiming.NextBar(3.95, 0, 120, 4, 0.1), 0.00001);
            Assert.AreEqual(8d, MusicTiming.NextBoundary(6.01, 2, 120, 4, MusicQuantization.Bar, 0, 0), 0.00001);
            Assert.AreEqual(12d, MusicTiming.NextMarker(7.1, 2, 120, 4, 16, 0.1), 0.00001);
        }

        [Test]
        public void InvalidTimingInputsFailClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MusicTiming.BeatDuration(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MusicTiming.NextBar(0, 0, 120, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MusicTiming.NextBeat(0, 0, double.NaN, 0));
        }

        [Test]
        public void AdvancedProfileRejectsDuplicateSectionIds()
        {
            var profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
            try
            {
                profile.profileId = "test.profile"; profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion; profile.sourceRevision = "test";
                profile.sections = new[]
                {
                    Section("same"), Section("same")
                };
                Assert.False(profile.Validate(out string failure), failure);
                StringAssert.Contains("Duplicate section ID", failure);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void LegacyUnassignedStemIdsRemainLoadableForMigration()
        {
            var profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
            try
            {
                profile.profileId = "legacy.profile"; profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion; profile.sourceRevision = "legacy";
                profile.stems = new[] { new SensoryMusicStem { stableId = "legacy-stem" } };
                Assert.True(profile.Validate(out string failure), failure);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ContextPriorityMatchesFidelityArbitrationOrder()
        {
            Assert.Greater(AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Pursuit), AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Race));
            Assert.Greater(AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Escape), AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Pursuit));
            Assert.Greater(AdaptiveMusic.ContextPriority(AdaptiveMusicContext.Pause), AdaptiveMusic.ContextPriority(AdaptiveMusicContext.FreeRoam));
        }

        [Test]
        public void SectionLoopDurationUsesMusicalGrid()
        {
            var section = Section("loop");
            section.bpm = 120; section.beatsPerBar = 4; section.bars = 8;
            Assert.AreEqual(16d, section.LoopDurationSeconds(), 0.00001);
            section.loopStartBeat = 4; section.loopEndBeat = 12;
            Assert.AreEqual(4d, section.LoopDurationSeconds(), 0.00001); // Eight beats at 120 BPM.
        }

        [Test]
        public void DefaultSectionDoesNotSelectAnIneligibleSection()
        {
            var profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
            try
            {
                profile.sections = new[] { Section("free-roam") };
                profile.sections[0].eligibleContexts = AdaptiveMusicContext.FreeRoam;
                Assert.AreEqual(string.Empty, profile.DefaultSectionFor(AdaptiveMusicContext.Pursuit));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void DefaultSectionUsesHighestPriorityEligibleCue()
        {
            var profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
            try
            {
                profile.sections = new[]
                {
                    Section("low"), Section("high")
                };
                profile.cues = new[]
                {
                    new MusicCue { stableId = "cue.low", context = AdaptiveMusicContext.FreeRoam, defaultSectionId = "low", priority = 10 },
                    new MusicCue { stableId = "cue.high", context = AdaptiveMusicContext.FreeRoam, defaultSectionId = "high", priority = 20 }
                };
                Assert.AreEqual("high", profile.DefaultSectionFor(AdaptiveMusicContext.FreeRoam));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        private static MusicSection Section(string id)
        {
            return new MusicSection
            {
                stableId = id, displayName = id, eligibleContexts = AdaptiveMusicContext.FreeRoam,
                bpm = 120, beatsPerBar = 4, bars = 8, harmonicFamily = "test",
                stems = Array.Empty<MusicStem>()
            };
        }
    }
}
