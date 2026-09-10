using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Lighting.Editor;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Tests
{
    [Category("LightingGraphics")]
    public sealed class LightingGraphicsTests
    {
        [Test] public void MetalPreviewChangesPixelsAndRestoresEnvironment()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Requires graphics-enabled Unity.");
            var scene=EditorSceneManager.NewPreviewScene();var profile=ScriptableObject.CreateInstance<AtmosphereProfile>();var material=new Material(Shader.Find("HDRP/Lit"));Texture2D a=null,b=null,d=null;
            var sourceSky=RenderSettings.ambientSkyColor;bool sourceFog=RenderSettings.fog;int scenes=EditorSceneManager.previewSceneCount;
            try
            {
                var go=new GameObject("Owner");SceneManager.MoveGameObjectToScene(go,scene);var owner=go.AddComponent<AtmosphereController>();owner.fallback=profile;
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(cube,scene);cube.GetComponent<Renderer>().sharedMaterial=material;owner.bakeGeometry=new[]{cube.GetComponent<Renderer>()};
                bool dirtyBefore=SceneManager.GetActiveScene().isDirty;
                using(var preview=new LightingPreview(owner)){a=preview.Capture(profile.look,new Vector3(0,1,-4),Quaternion.Euler(10,0,0),128,128);var look=profile.look;look.exposure=-4;b=preview.Capture(look,new Vector3(0,1,-4),Quaternion.Euler(10,0,0),128,128);}
                d=LightingComparison.Difference(a,b,out var difference);Assert.AreEqual(dirtyBefore,SceneManager.GetActiveScene().isDirty,"Preview must not dirty the active production scene.");Assert.Greater(difference,.01f,"Actual rendered look must change pixels.");Assert.AreEqual(sourceSky,RenderSettings.ambientSkyColor);Assert.AreEqual(sourceFog,RenderSettings.fog);Assert.AreEqual(scenes,EditorSceneManager.previewSceneCount);Assert.AreSame(material,cube.GetComponent<Renderer>().sharedMaterial);
            }
            finally{foreach(var image in new[]{a,b,d})if(image)Object.DestroyImmediate(image);Object.DestroyImmediate(material);Object.DestroyImmediate(profile);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
