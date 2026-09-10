using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NfsMwRemaster.Diagnostics.Editor
{
    public static class DiagnosticEvidence
    {
        public static void CompileRelease()
        {
            string output=Environment.GetEnvironmentVariable("DIAGNOSTIC_EVIDENCE_DIRECTORY")??Path.GetTempPath();Directory.CreateDirectory(output);
            string scripts=Path.Combine(Path.GetTempPath(),"diagnostic-release-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scripts);
            UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(new UnityEditor.Build.Player.ScriptCompilationSettings
            {target=BuildTarget.StandaloneOSX,group=BuildTargetGroup.Standalone,options=UnityEditor.Build.Player.ScriptCompilationOptions.None},scripts);
            string assemblyPath=Path.Combine(scripts,"NfsMwRemaster.Diagnostics.dll");
            if(!File.Exists(assemblyPath))throw new InvalidOperationException("Release diagnostic assembly was not compiled.");
            var assembly=System.Reflection.Assembly.Load(File.ReadAllBytes(assemblyPath));
            bool enabled=(bool)assembly.GetType("NfsMwRemaster.Diagnostics.DiagnosticBuild",true).GetProperty("Enabled").GetValue(null);
            if(enabled)throw new InvalidOperationException("Release diagnostics gate unexpectedly enabled.");
            var dispatcherType=assembly.GetType("NfsMwRemaster.Diagnostics.DiagnosticCommands",true);
            var dispatcher=Activator.CreateInstance(dispatcherType);
            object sandbox=Enum.ToObject(assembly.GetType("NfsMwRemaster.Diagnostics.DiagnosticCommandContext",true),0);
            object clock=Activator.CreateInstance(assembly.GetType("NfsMwRemaster.Diagnostics.DiagnosticClock",true));
            bool ran=(bool)dispatcherType.GetMethod("TryExecute").Invoke(dispatcher,new[]{(object)"forbidden",1.0,sandbox,true,true,null,clock});
            if(ran)throw new InvalidOperationException("Release command dispatch succeeded.");
            File.WriteAllText(Path.Combine(output,"release-compile.txt"),"StandaloneOSX scripts compiled without DevelopmentBuild. DiagnosticBuild.Enabled=false; command dispatch returned false with authorization and confirmation set. This is a script compilation/gate check, not a launched player or graphics test.\n");
        }
        private sealed class BenchProvider:IDiagnosticProvider
        {
            public int Samples;
            public string Id=>"benchmark";public string Category=>"Synthetic benchmark";public string Label=>"Synthetic benchmark";public double Interval=>0.1;
            public void SetDemand(bool active,bool geometry){}
            public DiagnosticSnapshot Sample(DiagnosticClock clock){Samples++;return new DiagnosticSnapshot(Id,"synthetic",1,"bench.v1",clock,new[]{DiagnosticMetric.Number("synthetic.value",1)});}
        }
        [Serializable]private sealed class Result{public string unity,machine,scope;public int iterations,samples;public double disabledMs,enabledMs;public long disabledBytes,enabledBytes;public bool allocationCounterAvailable;}
        public static void Generate()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run evidence generation in a batch validation copy; use the sample menu interactively.");
            const string folder="Assets/NfsMw/Modules/Driving/Examples/DiagnosticsStudio";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            string scenePath=folder+"/DiagnosticSample.unity";
            var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(active.path))
            {active=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(active,folder+"/EmptyWorkspace.unity");}
            if(!File.Exists(scenePath)){DiagnosticSetup.BuildSample(scenePath);EditorSceneManager.CloseScene(UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath),true);}
            var saved=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
            try
            {
                bool found=false;foreach(var root in saved.GetRootGameObjects())
                    if(root.GetComponent<DiagnosticFixture>()!=null && root.GetComponent<DiagnosticOverlay>()!=null)found=true;
                if(!found)throw new InvalidOperationException("Saved diagnostic sample has missing script bindings.");
            }
            finally{EditorSceneManager.CloseScene(saved,true);}
            var result=new Result{unity=Application.unityVersion,machine=SystemInfo.processorType,scope="Synthetic 100000 scheduler ticks; 1 provider, 1 metric. Includes sample construction/history. Excludes IMGUI, GPU, gameplay. Stopwatch and GC.GetAllocatedBytesForCurrentThread; warmup 1000 calls.",iterations=100000};
            using var hub=new DiagnosticHub();var provider=new BenchProvider();using var registration=hub.Register(provider);
            for(int i=0;i<1000;i++)hub.Tick(new DiagnosticClock{realtime=i*0.001});
            long bytes=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            for(int i=0;i<result.iterations;i++)hub.Tick(new DiagnosticClock{realtime=i*0.001});
            watch.Stop();result.disabledMs=watch.Elapsed.TotalMilliseconds;result.disabledBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            using var demand=hub.Subscribe(provider.Id);bytes=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
            for(int i=0;i<result.iterations;i++)hub.Tick(new DiagnosticClock{realtime=i*0.001});
            watch.Stop();result.enabledMs=watch.Elapsed.TotalMilliseconds;result.enabledBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;result.samples=provider.Samples;
            long controlStart=GC.GetAllocatedBytesForCurrentThread();var control=new byte[65536];GC.KeepAlive(control);
            result.allocationCounterAvailable=GC.GetAllocatedBytesForCurrentThread()>controlStart;
            if(!result.allocationCounterAvailable){result.disabledBytes=-1;result.enabledBytes=-1;result.scope+=" Allocation counter failed the 64 KiB positive control; byte counts unavailable (-1), not zero allocation.";}
            string output=Environment.GetEnvironmentVariable("DIAGNOSTIC_EVIDENCE_DIRECTORY")??Path.GetTempPath();Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"measurements.json"),JsonUtility.ToJson(result,true));
            File.WriteAllText(Path.Combine(output,"synthetic-capture.json"),JsonUtility.ToJson(DiagnosticCapture.Create(hub,"synthetic-benchmark"),true));
            AssetDatabase.SaveAssets();
        }
    }
}
