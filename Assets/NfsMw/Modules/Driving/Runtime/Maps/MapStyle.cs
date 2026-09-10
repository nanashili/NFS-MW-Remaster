using UnityEngine;
namespace NfsMwRemaster.Maps
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Maps/Style")]
    public sealed class MapStyle : ScriptableObject
    {
        public int schema = 1;
        public Color background = new Color(.035f,.045f,.045f), road = new Color(.65f,.67f,.60f), outline = new Color(.12f,.14f,.13f);
        public Color highway = new Color(.84f,.80f,.63f), bridge = Color.white, tunnel = new Color(.4f,.55f,.6f);
        public Color route = new Color(.7f,1,.12f), player = Color.white, marker = new Color(1,.7f,.15f), text = Color.white;
        public Color tileGrid = new Color(.3f,.5f,.5f,.4f), missing = new Color(.55f,.1f,.3f);
        [Range(1,10)] public float minimumRoadPixels = 2;
        [Range(0,6)] public float outlinePixels = 2;
        [Range(1,12)] public float routePixels = 4;
        [Range(10,30)] public int fontSize = 14;
        [Range(0,64)] public float safeMargin = 16;
        public Font font;
        public bool highContrast;
    }
}
