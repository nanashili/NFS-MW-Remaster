using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var root=new GameObject("TEMP replacement tree lineup");RenderTexture target=null;Texture2D image=null;VolumeProfile profile=null;Material groundMaterial=null;
        try{
            for(int kind=0;kind<5;kind++){
                int variant=kind==0?1:2;var obj=new GameObject("Independent model "+kind);obj.transform.SetParent(root.transform,false);obj.transform.position=new Vector3((kind-2)*15,500,0);obj.transform.localScale=Vector3.one*(kind==3?9:kind==1?23:18);
                obj.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(RockportReplacementTrees.Folder+"Meshes/Tree_"+kind+"_"+variant+"_LOD0.asset");
                obj.AddComponent<MeshRenderer>().sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(RockportReplacementTrees.Folder+"Materials/Tree_"+kind+"_"+variant+"_0.mat"),AssetDatabase.LoadAssetAtPath<Material>(RockportReplacementTrees.Folder+"Materials/Tree_"+kind+"_"+variant+"_1.mat")};
            }
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform,false);ground.transform.position=new Vector3(0,499.8f,0);ground.transform.localScale=new Vector3(200,.4f,140);
            groundMaterial=new Material(Shader.Find("HDRP/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.16f,.18f,.13f));groundMaterial.SetFloat("_Smoothness",.1f);HDMaterial.ValidateMaterial(groundMaterial);ground.GetComponent<MeshRenderer>().sharedMaterial=groundMaterial;
            var camera=new GameObject("TEMP tree lineup camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.enabled=false;camera.transform.position=new Vector3(0,514,-83);camera.transform.LookAt(new Vector3(0,511,0));camera.fieldOfView=52;camera.nearClipPlane=.1f;camera.farClipPlane=300;
            var hd=camera.gameObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
            var sun=new GameObject("TEMP lineup sun").AddComponent<Light>();sun.transform.SetParent(root.transform,false);sun.type=LightType.Directional;sun.intensity=85000;sun.transform.rotation=Quaternion.Euler(48,-25,0);sun.gameObject.AddComponent<HDAdditionalLightData>();
            var fill=new GameObject("TEMP lineup fill").AddComponent<Light>();fill.transform.SetParent(root.transform,false);fill.type=LightType.Directional;fill.intensity=18000;fill.transform.rotation=Quaternion.Euler(50,140,0);fill.gameObject.AddComponent<HDAdditionalLightData>();
            var volume=root.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100000;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
            var exposure=profile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12);
            profile.Add<VisualEnvironment>(true).skyType.Override((int)SkyType.Gradient);var sky=profile.Add<GradientSky>(true);sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier);sky.multiplier.Override(10000);sky.top.Override(new Color(.15f,.3f,.5f));sky.middle.Override(new Color(.6f,.7f,.76f));
            target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);target.Create();var request=new RenderPipeline.StandardRequest{destination=target};for(int i=0;i<5;i++)RenderPipeline.SubmitRenderRequest(camera,request);
            var prior=RenderTexture.active;RenderTexture.active=target;image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();RenderTexture.active=prior;
            File.WriteAllBytes("Art/RockportTrees/replacement-tree-lineup.png",image.EncodeToPNG());result.Log("Rendered five independently generated tree families.");
        }finally{
            UnityEngine.Object.DestroyImmediate(root);if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(image)UnityEngine.Object.DestroyImmediate(image);if(profile)UnityEngine.Object.DestroyImmediate(profile);if(groundMaterial)UnityEngine.Object.DestroyImmediate(groundMaterial);
        }
    }
}
