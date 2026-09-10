using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class AudioZoneTests
    {
        private static AudioZoneProfile Profile(AudioZoneCategory category = AudioZoneCategory.OpenStreet)
        {
            var profile = ScriptableObject.CreateInstance<AudioZoneProfile>();
            profile.SetDefaults(category, "audio.test." + category.ToString().ToLowerInvariant());
            return profile;
        }

        private static AudioZone Zone(AudioZoneShape shape, AudioZoneProfile profile, Vector3 position = default)
        {
            var root = new GameObject("Audio zone test");
            root.transform.position = position;
            var zone = root.AddComponent<AudioZone>();
            zone.SetStableId("audio.zone.test." + shape.ToString().ToLowerInvariant());
            zone.SetShape(shape);
            zone.SetProfile(profile);
            return zone;
        }

        private static void DestroyZone(AudioZone zone, AudioZoneProfile profile)
        {
            if (zone != null) Object.DestroyImmediate(zone.gameObject);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void BoxMembershipHonoursVerticalBoundsAndOutsideBlend()
        {
            var profile = Profile();
            var zone = Zone(AudioZoneShape.Box, profile);
            try
            {
                zone.SetSize(new Vector3(10, 4, 20));
                Assert.True(zone.Contains(new Vector3(4.9f, 1.9f, 9.9f)));
                Assert.False(zone.Contains(new Vector3(0, 2.1f, 0)));
                Assert.Greater(zone.Weight(new Vector3(5.5f, 0, 0)), 0);
                Assert.AreEqual(0, zone.Weight(new Vector3(9, 0, 0)), 0.0001f);
            }
            finally { DestroyZone(zone, profile); }
        }

        [Test]
        public void SphereAndCapsuleUseThreeDimensionalMembership()
        {
            var sphereProfile = Profile();
            var sphere = Zone(AudioZoneShape.Sphere, sphereProfile);
            var capsuleProfile = Profile(AudioZoneCategory.Tunnel);
            var capsule = Zone(AudioZoneShape.Capsule, capsuleProfile, new Vector3(20, 0, 0));
            try
            {
                sphere.SetRadius(3);
                Assert.True(sphere.Contains(new Vector3(0, 2.9f, 0)));
                Assert.False(sphere.Contains(new Vector3(0, 3.1f, 0)));
                capsule.SetRadius(2); capsule.SetHeight(10);
                Assert.True(capsule.Contains(new Vector3(20, 4, 0)));
                Assert.False(capsule.Contains(new Vector3(20, 6, 0)));
                Assert.True(capsule.Contains(new Vector3(21.5f, 0, 0)));
            }
            finally
            {
                DestroyZone(sphere, sphereProfile);
                DestroyZone(capsule, capsuleProfile);
            }
        }

        [Test]
        public void HysteresisKeepsAnActiveZoneAlivePastItsBlendDistance()
        {
            var profile = Profile(AudioZoneCategory.Underpass);
            profile.SetBlend(2, 1, AnimationCurve.Linear(0, 0, 1, 1));
            var zone = Zone(AudioZoneShape.Box, profile);
            try
            {
                zone.SetSize(new Vector3(2, 2, 2));
                Assert.True(zone.TryEvaluate(new Vector3(3.2f, 0, 0), true, out float heldWeight, out bool inside, out bool held));
                Assert.False(inside);
                Assert.True(held);
                Assert.Greater(heldWeight, 0);
                Assert.False(zone.TryEvaluate(new Vector3(4.1f, 0, 0), true, out _, out _, out _));
            }
            finally { DestroyZone(zone, profile); }
        }

        [Test]
        public void ProfileValidationRejectsDuplicateLayersAndAcceptsDefaults()
        {
            var profile = Profile(AudioZoneCategory.Garage);
            try
            {
                Assert.True(profile.Validate(out string validFailure), validFailure);
                profile.SetAmbience(new[]
                {
                    new AudioZoneAmbienceLayer { id = "same", maxDistance = 20 },
                    new AudioZoneAmbienceLayer { id = "same", maxDistance = 20 }
                });
                Assert.False(profile.Validate(out string failure));
                StringAssert.Contains("unique", failure);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void PortalConnectionIsDirectionalWhenConfiguredAndClosedStateIsAudible()
        {
            var sourceProfile = Profile();
            var targetProfile = Profile(AudioZoneCategory.Tunnel);
            var source = Zone(AudioZoneShape.Box, sourceProfile);
            var target = Zone(AudioZoneShape.Box, targetProfile, new Vector3(20, 0, 0));
            var portalObject = new GameObject("Portal test");
            var portal = portalObject.AddComponent<AudioZonePortal>();
            try
            {
                portal.SetStableId("audio.portal.test");
                portal.SetEndpoints(source, target);
                portal.SetBidirectional(false);
                portal.SetState(AudioZonePortalState.Closed);
                portal.SetTransmission(1, 0.1f);
                portal.SetFilters(22000, 750);
                Assert.True(portal.Connects(source, target));
                Assert.False(portal.Connects(target, source));
                Assert.AreEqual(0.1f, portal.Transmission, 0.0001f);
                Assert.AreEqual(750, portal.LowPassHz, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(portalObject);
                DestroyZone(source, sourceProfile);
                DestroyZone(target, targetProfile);
            }
        }

        [Test]
        public void RuntimeWorldChoosesDeterministicPriorityAndClonesMixState()
        {
            var listenerObject = new GameObject("Listener test");
            var worldObject = new GameObject("Audio world test");
            var lowProfile = Profile(AudioZoneCategory.OpenStreet);
            var highProfile = Profile(AudioZoneCategory.Garage);
            var low = Zone(AudioZoneShape.Box, lowProfile, new Vector3(0, 0, 0));
            var high = Zone(AudioZoneShape.Box, highProfile, new Vector3(0, 0, 0));
            var world = worldObject.AddComponent<AudioZoneWorld>();
            try
            {
                listenerObject.transform.position = Vector3.zero;
                low.SetStableId("audio.zone.test.low"); high.SetStableId("audio.zone.test.high");
                low.SetSize(Vector3.one * 20); high.SetSize(Vector3.one * 20);
                highProfile.SetPriority(100);
                world.Configure(null, AudioZoneListenerPolicy.ExplicitTransform, listenerObject.transform);
                world.SetZones(new[] { low, high, low });
                world.EvaluateNow();
                Assert.AreEqual(high.StableId, world.PrimaryZoneId);
                Assert.AreEqual(2, world.ActiveZoneCount);
                var copy = world.EffectiveMix;
                copy.categoryGains[0] = 99;
                Assert.AreNotEqual(99, world.EffectiveMix.categoryGains[0]);
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(listenerObject);
                DestroyZone(low, lowProfile);
                DestroyZone(high, highProfile);
            }
        }

        [Test]
        public void TraversalValidationRequiresMonotonicSamplesAndMatchingWeights()
        {
            var asset = ScriptableObject.CreateInstance<AudioZoneTraversalAsset>();
            try
            {
                asset.Configure("audio.traversal.test", "Assets/NfsMw/Scenes/Tests/Test.unity", "revision", AudioZoneListenerPolicy.RenderedAudioListener, 0.05f);
                asset.SetSamples(new List<AudioZoneTraversalSample>
                {
                    new AudioZoneTraversalSample { time = 0, zoneIds = new[] { "a" }, weights = new[] { 1f } },
                    new AudioZoneTraversalSample { time = 1, zoneIds = new[] { "a" }, weights = new[] { 0.5f } }
                });
                Assert.True(asset.Validate(out string validFailure), validFailure);
                asset.SetSamples(new List<AudioZoneTraversalSample>
                {
                    new AudioZoneTraversalSample { time = 1 },
                    new AudioZoneTraversalSample { time = 0 }
                });
                Assert.False(asset.Validate(out string failure));
                StringAssert.Contains("monotonic", failure);
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
