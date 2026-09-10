using System;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Explicit geometry/rig bindings. Positions are metres in +Y-up, +Z-forward vehicle space.
    /// Suspension, tyre radius, mass and drivetrain physics remain on VehicleTuning.
    /// </summary>
    [Serializable]
    public sealed class VehicleAssemblyDefinition
    {
        [Tooltip("Persistent source node. Source scripts are never copied or executed.")]
        public GameObject bodySource;
        [Tooltip("Optional explicit presentation rig within Body Source. Only known presentation data is remapped; source scripts are not copied.")]
        public VehiclePresentationBindings presentationSource;
        public VehicleMirrorRenderer mirrorSource;
        [Tooltip("Exact source renderers excluded from the body (for example separately mapped spinning wheels). No name guessing.")]
        public Renderer[] excludedBodyRenderers = Array.Empty<Renderer>();
        public Vector3 bodyPosition;
        public Vector3 bodyEuler;
        public Vector3 bodyScale = Vector3.one;
        [Tooltip("Collision geometry in metres. This does not author vehicle mass or centre of mass.")]
        public Vector3 colliderCenter;
        public Vector3 colliderSize;
        public VehicleAssemblyWheel[] wheels = new VehicleAssemblyWheel[4]
        { new VehicleAssemblyWheel(), new VehicleAssemblyWheel(), new VehicleAssemblyWheel(), new VehicleAssemblyWheel() };
        public VehicleAssemblySocket[] sockets = Array.Empty<VehicleAssemblySocket>();
        [Tooltip("Explicit player controls. Leave off for an AI/input adapter to supply controls at spawn.")]
        public bool includePlayerInput;
    }

    [Serializable]
    public sealed class VehicleAssemblyWheel
    {
        public GameObject visualSource;
        [Tooltip("Suspension ray origin, not the wheel centre. Order: front left, front right, rear left, rear right.")]
        public Vector3 suspensionAnchor;
        [Tooltip("Rotate the source geometry so its axle is +X. This offset does not rotate the physics ray.")]
        public Vector3 visualEuler;
        public Vector3 visualScale = Vector3.one;
        public bool driven;
        public bool handbrake;
    }

    [Serializable]
    public sealed class VehicleAssemblySocket
    {
        [Tooltip("Stable semantic identity; changing it is an explicit binding migration, not a label rename.")]
        public string id = "";
        public VehicleCustomizationCategory category = VehicleCustomizationCategory.BodyKit;
        public Renderer[] stockRenderers = Array.Empty<Renderer>();
        public Vector3 position, euler;
    }
}
