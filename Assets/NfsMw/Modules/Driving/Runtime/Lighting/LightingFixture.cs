using System;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    public enum FixtureRole { Unclassified, Street, Facade, Tunnel, Garage, Signal, BrakeLight, PoliceLight, ObstacleCue }
    [DisallowMultipleComponent]
    public sealed class LightingFixture : MonoBehaviour
    {
        public string id=Guid.NewGuid().ToString("N"), upstreamId="", districtId="", circuit="default";
        public FixtureRole role;
        public Light source;
        public Renderer emissiveVisual;
        public bool Critical=>role==FixtureRole.Unclassified||role==FixtureRole.Signal||role==FixtureRole.BrakeLight||role==FixtureRole.PoliceLight||role==FixtureRole.ObstacleCue;
        // The existing light owner remains authoritative. No emissive property blocks are written here.
    }
}
