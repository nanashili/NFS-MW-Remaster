using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor.Workspace;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    public sealed class HdrpRenderingView : RacingModuleView
    {
        readonly MaskChannel[] channels={new MaskChannel(),new MaskChannel{fallback=1},new MaskChannel{fallback=1},new MaskChannel{fallback=.5f}};
        int size=1024;
        string report="Scan materials and loaded scenes to check the rendering setup.";
        Vector2 scroll;
        public HdrpRenderingView() { Root.Add(new IMGUIContainer(Draw)); }
        public override void SetContext(RacingEditingContext context) { }
        public override void Dispose() { }
        void Draw()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("HDRP rendering",EditorStyles.boldLabel);
            int quality=EditorGUILayout.Popup("Quality preset",QualitySettings.GetQualityLevel(),QualitySettings.names);
            if(quality!=QualitySettings.GetQualityLevel())HdrpQualityRuntime.SetQuality(quality);
            if(GUILayout.Button("Audit materials and loaded scenes"))report=Audit();
            EditorGUILayout.HelpBox(report,MessageType.Info);
            EditorGUILayout.Space();EditorGUILayout.LabelField("Pack HDRP Mask Map",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("R: metallic · G: ambient occlusion · B: detail mask · A: smoothness. Sources must be linear data maps. Enable inversion for roughness. Missing textures use the constant. Output is a new asset; sources stay intact.",MessageType.None);
            string[] labels={"Metallic → R","Occlusion → G","Detail mask → B","Smoothness → A"};
            for(int i=0;i<4;i++)
            {
                var c=channels[i];c.texture=(Texture2D)EditorGUILayout.ObjectField(labels[i],c.texture,typeof(Texture2D),false);
                if(c.texture){c.channel=(TextureChannel)EditorGUILayout.EnumPopup("Source channel",c.channel);c.invert=EditorGUILayout.Toggle("Invert (roughness)",c.invert);}
                else c.fallback=EditorGUILayout.Slider("Constant",c.fallback,0,1);
                channels[i]=c;
            }
            size=EditorGUILayout.IntPopup("Output resolution",size,new[]{"256","512","1024","2048","4096"},new[]{256,512,1024,2048,4096});
            if(GUILayout.Button("Save new Mask Map…"))
            {
                string path=EditorUtility.SaveFilePanelInProject("Save Mask Map","MaskMap","png","Save a new packed texture.");
                if(!string.IsNullOrEmpty(path))try{Selection.activeObject=HdrpTexturePacking.Pack(path,size,channels[0],channels[1],channels[2],channels[3]);report="Created "+path;}catch(Exception ex){report=ex.Message;}
            }
            if(GUILayout.Button("Show reusable material templates"))Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/NfsMw/Modules/Driving/Data/Rendering/Materials");
            EditorGUILayout.EndScrollView();
        }
        public static string Audit()
        {
            var text=new StringBuilder();int materials=0,issues=0;
            if(!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)){text.AppendLine("Active pipeline is not HDRP.");issues++;}
            foreach(var path in AssetDatabase.FindAssets("t:Material",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            foreach(var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                materials++;
                if(!m.shader || !m.shader.isSupported || m.GetTag("RenderPipeline",false)!="HDRenderPipeline") {text.AppendLine("Unsupported shader: "+path+" / "+m.name);issues++;}
                if(m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") is Texture2D mask && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mask)) is TextureImporter importer && importer.sRGBTexture)
                {text.AppendLine("Mask map must be linear: "+AssetDatabase.GetAssetPath(mask));issues++;}
            }
            var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach(var camera in cameras)if(!camera.GetComponent<HDAdditionalCameraData>()){text.AppendLine("Camera needs HDRP data: "+camera.name);issues++;}
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if(!light.GetComponent<HDAdditionalLightData>() || (light.type==LightType.Directional && light.lightUnit!=LightUnit.Lux)){text.AppendLine("Review light data/units: "+light.name);issues++;}
            foreach(var probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
                if(probe.mode==ReflectionProbeMode.Baked && !probe.bakedTexture){text.AppendLine("Reflection probe has no bake: "+probe.name);issues++;}
            return materials+" materials; "+cameras.Length+" loaded cameras; "+issues+" findings.\n"+text;
        }
    }
}
