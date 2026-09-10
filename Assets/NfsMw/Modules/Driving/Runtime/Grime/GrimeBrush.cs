using System;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    public enum GrimePattern { Texture, Dirt, Crack, Repair, TireWear, Oil, Graffiti, Puddle, Concrete }
    public enum GrimeAnchorKind { Mesh, Road, World }
    public enum GrimeBlend { Alpha, Multiply }
    [CreateAssetMenu(menuName = "NFS MW Remaster/Surface Dressing/Brush")]
    public sealed class GrimeBrush : ScriptableObject
    {
        public int schema = 1;
        public string id = Guid.NewGuid().ToString("N"), label = "Road dirt", category = "Roads";
        public GrimePattern pattern = GrimePattern.Dirt;
        public GrimeBlend blend;
        public Texture2D colorOpacity, normalMap;
        public Color tint = new Color(.2f, .17f, .13f, .7f);
        [Range(.05f, 20)] public float width = 1;
        [Range(.1f, 8)] public float aspect = 1;
        [Range(.05f, 2)] public float spacing = .35f;
        [Range(0, 180)] public float rotationJitter = 25;
        [Range(0, .8f)] public float sizeJitter = .15f;
        [Range(0, 1)] public float falloff = .25f, smoothness = .15f, metallic;
        [Range(0, 2)] public float normalStrength = 1;
        [ColorUsage(false, true)] public Color emission = Color.black;
        [Range(.01f, 2)] public float projectionDepth = .25f;
        [Range(0, 85)] public float normalTolerance = 35;
        [Range(.001f, .03f)] public float normalBias = .004f;
        [Range(1, 12)] public int subdivisions = 4;
        [Min(1)] public float fadeStart = 150, fadeEnd = 200;
        [TextArea] public string notes;
    }
}
