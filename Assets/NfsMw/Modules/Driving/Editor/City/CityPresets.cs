using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class CityPresets
    {
        public const string Folder = "Assets/NfsMw/Modules/Driving/CityAssets";
        public static void Ensure()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var kit = AssetDatabase.LoadAssetAtPath<CityKit>(Folder+"/IndustrialKit.asset");
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<CityKit>(); kit.contentId=Guid.NewGuid().ToString("N");
                kit.wall=Material("Concrete",new Color(.45f,.43f,.38f)); kit.roof=Material("Roof",new Color(.18f,.2f,.22f));
                kit.trim=Material("Trim",new Color(.65f,.62f,.5f)); kit.glass=Material("Glass",new Color(.17f,.26f,.3f)); kit.yard=Material("Asphalt",new Color(.22f,.23f,.24f));
                AssetDatabase.CreateAsset(kit,Folder+"/IndustrialKit.asset");
            }
            foreach(var use in new[]{CityLandUse.Industrial,CityLandUse.Commercial,CityLandUse.Residential,CityLandUse.Parking,CityLandUse.Park})
            {
                var path=Folder+"/"+use+".asset"; if(AssetDatabase.LoadAssetAtPath<CityStyle>(path)!=null) continue;
                var style=ScriptableObject.CreateInstance<CityStyle>();style.contentId=Guid.NewGuid().ToString("N");style.kit=kit;style.defaultUse=use;
                style.description=use+" starter style; customize the kit and parcel settings for the district.";
                AssetDatabase.CreateAsset(style,path);
            }
            AssetDatabase.SaveAssets();
        }
        private static Material Material(string name,Color color)
        {
            var path=Folder+"/"+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path); if(material!=null)return material;
            var shader=Shader.Find("HDRP/Lit"); if(shader==null)throw new InvalidOperationException("HDRP Lit shader is unavailable.");
            material=new Material(shader){name=name,color=color};AssetDatabase.CreateAsset(material,path);return material;
        }
        public static void CaptureSelection()
        {
            var source=Selection.activeGameObject; if(source==null)throw new ArgumentException("Select a city mesh group or prefab.");
            var bounds=CityMapLibrary.RenderBounds(source); if(bounds.size.sqrMagnitude<.001f)throw new ArgumentException("Selection has no renderer bounds.");
            Ensure(); var path=EditorUtility.SaveFilePanelInProject("Save city kit",source.name+" Kit","asset","Choose the kit asset location.",Folder);if(string.IsNullOrEmpty(path))return;
            var root=new GameObject(source.name+" Exterior");
            try
            {
                var copy=UnityEngine.Object.Instantiate(source,root.transform); copy.transform.position=source.transform.position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                copy.transform.rotation=source.transform.rotation;copy.transform.localScale=source.transform.lossyScale;
                foreach(var marker in copy.GetComponentsInChildren<CityMapPart>(true))UnityEngine.Object.DestroyImmediate(marker);
                foreach(var marker in copy.GetComponentsInChildren<CityGeneratedInstance>(true))UnityEngine.Object.DestroyImmediate(marker);
                var prefabPath=AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path,"prefab"));
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                var kit=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<CityKit>(Folder+"/IndustrialKit.asset"));kit.contentId=Guid.NewGuid().ToString("N");kit.exteriorPrefab=prefab;kit.prefabDimensions=bounds.size;
                AssetDatabase.CreateAsset(kit,AssetDatabase.GenerateUniqueAssetPath(path));Selection.activeObject=kit;
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void ProposeServiceRoad(CityDistrict district,CityParcel parcel)
        {
            if(district==null||parcel==null||!parcel.entrance.enabled)throw new ArgumentException("Enable and position a parcel entrance first.");
            var point=district.ToWorld(new Vector2(parcel.entrance.localPosition.x,parcel.entrance.localPosition.z),parcel.padHeight);
            var road=RoadAuthoringCommands.Create(RoadProfileInspector.GetLocalStreetProfile(),new[]{point-Vector3.forward*parcel.entrance.approachLength,point});
            road.name="Service road proposal — "+parcel.label;Selection.activeGameObject=road.gameObject;
        }
        public static void ExportReport(CityDistrict district,CityPlan plan)
        {
            if(district==null)throw new ArgumentException("Select a district.");
            var path=EditorUtility.SaveFilePanel("Export city report","",district.name+"-report","json");if(string.IsNullOrEmpty(path))return;
            File.WriteAllText(path,JsonUtility.ToJson(new Report { source=EditorJsonUtility.ToJson(district,true),fingerprint=CityPlanning.Fingerprint(district),instanceCount=plan?.instances.Count??0,diagnostics=plan?.diagnostics.ToArray()??Array.Empty<CityDiagnostic>()},true));
        }
        [Serializable] private sealed class Report { public string source,fingerprint; public int instanceCount; public CityDiagnostic[] diagnostics; }
    }
}
