using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedFrontendMusicTests
    {
        private GameObject root;
        private SensoryAudioWorld world;
        private AudioClip clip;
        private MostWantedFrontendMusicPreview preview;
        private bool previousAudioPause;

        [SetUp]
        public void SetUp()
        {
            previousAudioPause = AudioListener.pause;
            AudioListener.pause = true;
            root = new GameObject("Frontend music preview fixture");
            world = root.AddComponent<SensoryAudioWorld>();
            if (Field(typeof(SensoryAudioWorld), "budget").GetValue(world) == null)
                Method(typeof(SensoryAudioWorld), "Awake").Invoke(world, null);
            clip = AudioClip.Create("Frontend silent test clip", 44100, 1, 44100, false);
            Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            preview = new MostWantedFrontendMusicPreview();
        }

        [TearDown]
        public void TearDown()
        {
            preview?.Dispose();
            if (world != null)
            {
                // The existing runtime owner uses delayed Destroy. Clean up its native clip
                // explicitly in this EditMode fixture before its component is destroyed.
                var field = Field(typeof(SensoryAudioWorld), "nativeSilence");
                if (field.GetValue(world) is AudioClip silence) Object.DestroyImmediate(silence);
                field.SetValue(world, null);
            }
            if (root != null) Object.DestroyImmediate(root);
            if (clip != null) Object.DestroyImmediate(clip);
            AudioListener.pause = previousAudioPause;
        }

        [Test]
        public void PreviewUsesTheExistingPoolAndDoesNotResumeGameplayAudio()
        {
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.True, failure);
            Assert.That(preview.IsPlaying, Is.True);
            Assert.That(preview.VoiceCount, Is.EqualTo(1));
            Assert.That(world.ActiveVoices, Is.EqualTo(1));
            Assert.That(AudioListener.pause, Is.True);
            AudioSource source = FindPlayingSource();
            Assert.That(source.ignoreListenerPause, Is.True);
            Assert.That(source.spatialBlend, Is.Zero);
            Assert.That(source.loop, Is.False);
        }

        [Test]
        public void StopReleasesOnlyPreviewLeasesAndResetsPauseExemption()
        {
            var gameplay = world.Play(clip, SensoryCategory.Music, null, Vector3.zero, .5f, 1, 10);
            Assert.That(world.Owns(gameplay), Is.True);
            Assert.That(preview.TryPlay(world, "Test section", Stems(2), out string failure), Is.True, failure);
            Assert.That(world.ActiveVoices, Is.EqualTo(3));
            preview.Stop();
            Assert.That(world.Owns(gameplay), Is.True);
            Assert.That(world.ActiveVoices, Is.EqualTo(1));
            foreach (var source in root.GetComponentsInChildren<AudioSource>())
                Assert.That(source.ignoreListenerPause, Is.False);
        }

        [Test]
        public void AFullPoolRejectsPreviewWithoutEvictingExistingVoices()
        {
            var original = new List<FeedbackVoiceLease>();
            for (int i = 0; i < world.VoiceLimit; i++)
                original.Add(world.Play(clip, SensoryCategory.Music, null, Vector3.zero, .5f, 1, 0));
            Assert.That(world.ActiveVoices, Is.EqualTo(world.VoiceLimit));
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.False);
            Assert.That(failure, Does.Contain("no room"));
            foreach (var lease in original) Assert.That(world.Owns(lease), Is.True);
            Assert.That(preview.VoiceCount, Is.Zero);
        }

        [Test]
        public void ResumingGameplayStopsThePreview()
        {
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.True, failure);
            AudioListener.pause = false;
            preview.Tick();
            Assert.That(preview.IsPlaying, Is.False);
            Assert.That(world.ActiveVoices, Is.Zero);
        }

        [Test]
        public void PreviewDeadlineStopsVoicesWithoutDependingOnThePausedDspClock()
        {
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.True, failure);
            Field(typeof(MostWantedFrontendMusicPreview), "stopAt").SetValue(preview, Time.unscaledTimeAsDouble - .1);
            preview.Tick();
            Assert.That(preview.IsPlaying, Is.False);
            Assert.That(world.ActiveVoices, Is.Zero);
            Assert.That(preview.Status, Does.Contain("finished"));
            Assert.That(AudioListener.pause, Is.True);
        }

        [Test]
        public void UnpausedGameplayCannotStartAMenuPreview()
        {
            AudioListener.pause = false;
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.False);
            Assert.That(failure, Does.Contain("Pause gameplay"));
            Assert.That(world.ActiveVoices, Is.Zero);
        }

        [Test]
        public void InvalidSectionsDoNotAcquireVoicesOrMutatePublishedStems()
        {
            var invalid = new[] { new MusicStem { clip = clip, gain = float.NaN } };
            Assert.That(preview.TryPlay(world, "Invalid", invalid, out _), Is.False);
            Assert.That(float.IsNaN(invalid[0].gain), Is.True);
            Assert.That(preview.TryPlay(world, "Empty", Array.Empty<MusicStem>(), out _), Is.False);
            Assert.That(preview.TryPlay(world, "Missing", new[] { new MusicStem() }, out _), Is.False);
            Assert.That(preview.TryPlay(world, "Too large", Stems(MostWantedFrontendMusicPreview.MaximumStems + 1), out _), Is.False);
            Assert.That(world.ActiveVoices, Is.Zero);
        }

        [Test]
        public void GameplayAndDspScheduledVoicesCannotBypassTheListenerPause()
        {
            var spatial = world.Play(clip, SensoryCategory.Player, null, Vector3.zero, 1, 1, 0, ignoreListenerPause: true);
            var scheduled = world.Play(clip, SensoryCategory.Music, null, Vector3.zero, 1, 1, 0,
                scheduled: AudioSettings.dspTime + 1, ignoreListenerPause: true);
            Assert.That(world.Owns(spatial), Is.False);
            Assert.That(world.Owns(scheduled), Is.False);
            Assert.That(world.ActiveVoices, Is.Zero);
        }

        [Test]
        public void FrontendDeadlinesUseAnExplicitUnscaledClockAndResetOnRelease()
        {
            var rejected = world.Play(clip, SensoryCategory.Music, null, Vector3.zero, 1, 1, 0,
                scheduled: Time.unscaledTimeAsDouble + 1, scheduleOnUnscaledClock: true);
            Assert.That(world.Owns(rejected), Is.False);
            var lease = world.Play(clip, SensoryCategory.Music, null, Vector3.zero, 1, 1, 0,
                scheduled: Time.unscaledTimeAsDouble + 1, ignoreListenerPause: true, scheduleOnUnscaledClock: true);
            Assert.That(world.Owns(lease), Is.True);
            var source = FindPlayingSource();
            Assert.That(source.ignoreListenerPause, Is.True);
            Assert.That(AudioListener.pause, Is.True);
            world.Release(lease);
            Assert.That(source.ignoreListenerPause, Is.False);
            Assert.That(source.clip, Is.Null);
        }

        [Test]
        public void DisposeIsIdempotentAndDoesNotAdmitAnotherPreview()
        {
            Assert.That(preview.TryPlay(world, "Test section", Stems(1), out string failure), Is.True, failure);
            preview.Dispose(); preview.Dispose();
            Assert.That(world.ActiveVoices, Is.Zero);
            Assert.That(preview.TryPlay(world, "Another", Stems(1), out _), Is.False);
        }

        private MusicStem[] Stems(int count)
        {
            var stems = new MusicStem[count];
            for (int i = 0; i < count; i++) stems[i] = new MusicStem { clip = clip, gain = .5f };
            return stems;
        }

        private AudioSource FindPlayingSource()
        {
            foreach (var source in root.GetComponentsInChildren<AudioSource>())
                if (source.clip == clip && source.ignoreListenerPause) return source;
            Assert.Fail("No pooled preview source was configured.");
            return null;
        }

        private static FieldInfo Field(Type type, string name)
            => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.FullName, name);
        private static MethodInfo Method(Type type, string name)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, name);
    }
}
