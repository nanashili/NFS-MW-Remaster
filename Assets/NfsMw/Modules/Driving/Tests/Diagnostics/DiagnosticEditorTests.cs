using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using NfsMwRemaster.Diagnostics.Editor;
namespace NfsMwRemaster.Diagnostics.Tests
{
    public sealed class DiagnosticEditorTests
    {
        private sealed class PreviewProvider:IDiagnosticProvider
        {
            public string Id=>"editor.preview";public string Category=>"Synthetic";public string Label=>"Chart fixture";public double Interval=>0.1;
            public void SetDemand(bool active,bool geometry){}
            public DiagnosticSnapshot Sample(DiagnosticClock c)=>new DiagnosticSnapshot(Id,"fixture",1,"v1",c,new[]{DiagnosticMetric.Number("speed",c.realtime,"m/s")});
        }
        [UnityTest]public IEnumerator EveryPageOpensAndRepaints()
        {
            var provider=new PreviewProvider();using var registration=DiagnosticSession.Hub.Register(provider);using var subscription=DiagnosticSession.Hub.Subscribe(provider.Id);
            for(int i=0;i<5;i++)DiagnosticSession.Hub.Tick(new DiagnosticClock{realtime=i});
            var window=EditorWindow.GetWindow<DiagnosticStudioWindow>();
            yield return null;
            try{for(int i=0;i<9;i++){var serialized=new SerializedObject(window);serialized.FindProperty("tab").intValue=i;serialized.FindProperty("selected").stringValue=provider.Id;serialized.FindProperty("pinned").stringValue="speed";serialized.ApplyModifiedProperties();window.Repaint();yield return null;}}
            finally{window.Close();}
        }
        [Test]public void SampleSceneSavesAndReopensWithBindings()
        {
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/DiagnosticTestSample.unity");
            string workspace=AssetDatabase.GenerateUniqueAssetPath("Assets/DiagnosticTestWorkspace.unity");
            var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool emptyUntitled=string.IsNullOrEmpty(active.path);
            if(emptyUntitled && active.rootCount>0)Assert.Ignore("Save the current untitled scene before running scene-generation tests.");
            if(emptyUntitled)EditorSceneManager.SaveScene(active,workspace);
            try
            {
                DiagnosticSetup.BuildSample(path);var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);EditorSceneManager.CloseScene(scene,true);
                scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try{Assert.AreEqual(2,scene.rootCount);bool found=false;foreach(var root in scene.GetRootGameObjects())if(root.GetComponent<DiagnosticFixture>()!=null){Assert.NotNull(root.GetComponent<DiagnosticOverlay>());found=true;}Assert.True(found);}
                finally{EditorSceneManager.CloseScene(scene,true);}
            }
            finally{AssetDatabase.DeleteAsset(path);if(emptyUntitled){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(workspace);}}
        }
        [Test]public void OverlayCreationSupportsUndo()
        {
            var before=UnityEngine.Object.FindObjectsByType<DiagnosticOverlay>(FindObjectsSortMode.None).Length;
            DiagnosticSetup.AddOverlay();Assert.AreEqual(before+1,UnityEngine.Object.FindObjectsByType<DiagnosticOverlay>(FindObjectsSortMode.None).Length);
            Undo.PerformUndo();Assert.AreEqual(before,UnityEngine.Object.FindObjectsByType<DiagnosticOverlay>(FindObjectsSortMode.None).Length);
        }
    }
}
