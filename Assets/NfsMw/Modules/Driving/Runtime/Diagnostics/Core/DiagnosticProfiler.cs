using System;
using Unity.Profiling;
namespace NfsMwRemaster.Diagnostics
{
    public sealed class DiagnosticProfiler : IDiagnosticProvider, IDisposable
    {
        private ProfilerRecorder memory, main, render, gpu, draws, batches, setPass, textures, meshes, shadows;
        private bool active;
        public string Id=>"unity.performance";
        public string Category=>"Performance";
        public string Label=>"Unity profiler counters";
        public double Interval=>0.25;
        public void SetDemand(bool enabled,bool geometry)
        {
            if(enabled==active)return;Dispose();if(!enabled)return;active=true;
            try{memory=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"Total Used Memory",1);}catch(Exception){memory=default;}
            try{main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);}catch(Exception){main=default;}
            render=Start(ProfilerCategory.Internal,"Render Thread");gpu=Start(ProfilerCategory.Render,"GPU Frame Time");
            draws=Start(ProfilerCategory.Render,"Draw Calls Count");batches=Start(ProfilerCategory.Render,"Batches Count");
            setPass=Start(ProfilerCategory.Render,"SetPass Calls Count");shadows=Start(ProfilerCategory.Render,"Shadow Casters Count");
            textures=Start(ProfilerCategory.Memory,"Texture Memory");meshes=Start(ProfilerCategory.Memory,"Mesh Memory");
        }
        public DiagnosticSnapshot Sample(DiagnosticClock clock)=>new DiagnosticSnapshot(Id,"process",1,"unity-profiler",clock,new[]{
            memory.Valid && memory.Count>0?DiagnosticMetric.Number("memory.used",memory.LastValue,"bytes"):DiagnosticMetric.Missing("memory.used"),
            main.Valid && main.Count>0?DiagnosticMetric.Number("main.thread.last",main.LastValue/1000000.0,"ms",MetricKind.Duration):DiagnosticMetric.Missing("main.thread.last"),
            Metric(render,"render.thread.last","ms",1e-6),Metric(gpu,"gpu.frame.last","ms",1e-6),
            Metric(draws,"render.draws","count"),Metric(batches,"render.batches","count"),Metric(setPass,"render.setpass","count"),
            Metric(shadows,"render.shadow.casters","count"),Metric(textures,"memory.textures","bytes"),Metric(meshes,"memory.meshes","bytes")});
        static ProfilerRecorder Start(ProfilerCategory category,string counter){try{return ProfilerRecorder.StartNew(category,counter,1);}catch(Exception){return default;}}
        static DiagnosticMetric Metric(ProfilerRecorder recorder,string id,string unit,double scale=1)
            =>recorder.Valid&&recorder.Count>0&&(unit!="ms"||recorder.LastValue>0)?DiagnosticMetric.Number(id,recorder.LastValue*scale,unit,unit=="ms"?MetricKind.Duration:MetricKind.Gauge):DiagnosticMetric.Missing(id);
        public void Dispose()
        {
            memory.Dispose();main.Dispose();render.Dispose();gpu.Dispose();draws.Dispose();batches.Dispose();setPass.Dispose();textures.Dispose();meshes.Dispose();shadows.Dispose();
            memory=main=render=gpu=draws=batches=setPass=textures=meshes=shadows=default;active=false;
        }
    }
}
