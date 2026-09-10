using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
namespace NfsMwRemaster.Diagnostics.Tests
{
    public sealed class DiagnosticTests
    {
        private sealed class Provider:IDiagnosticProvider
        {
            public string Id=>"fixture";public string Category=>"Test";public string Label=>"Test";public double Interval=>0.1;
            public int samples,starts,stops;public bool fail;public long generation=1;public bool active;
            public void SetDemand(bool on,bool geometry){active=on;if(on)starts++;else stops++;}
            public DiagnosticSnapshot Sample(DiagnosticClock clock){samples++;if(fail)throw new Exception("secret");return Snapshot(clock,generation);}
        }
        private static DiagnosticSnapshot Snapshot(DiagnosticClock clock,long generation=1)=>new DiagnosticSnapshot("fixture","entity",generation,"v1",clock,new[]{DiagnosticMetric.Number("speed",2,"m/s")});
        private static DiagnosticClock Clock(double time)=>new DiagnosticClock{realtime=time,game=time/2,dsp=time+4,tick=10,phase=SamplePhase.OwnerBoundary};
        [Test] public void RingKeepsNewestAndCountsLoss(){var ring=new DiagnosticRing<int>(3);for(int i=0;i<100000;i++)ring.Add(i);Assert.AreEqual(99997,ring[0]);Assert.AreEqual(99997,ring.Overwritten);Assert.AreEqual(3,ring.Count);}
        [Test] public void RingClearReleasesHistory(){var r=new DiagnosticRing<object>(2);r.Add(new object());r.Clear();Assert.AreEqual(0,r.Count);Assert.Throws<ArgumentOutOfRangeException>(()=>{_=r[0];});}
        [TestCase(0)] [TestCase(-1)] [TestCase(20000)] public void InvalidRingBudget(int size)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new DiagnosticRing<int>(size));
        [Test] public void SnapshotCopiesMetricArray(){var m=new[]{DiagnosticMetric.Number("x",3)};var s=new DiagnosticSnapshot("p","e",1,"v",Clock(0),m);m[0]=DiagnosticMetric.Number("x",90);Assert.AreEqual(3,s[0].value);}
        [Test] public void SnapshotRejectsDuplicateMetrics()=>Assert.Throws<ArgumentException>(()=>new DiagnosticSnapshot("p","e",1,"v",Clock(0),new[]{DiagnosticMetric.Number("x",1),DiagnosticMetric.Number("x",2)}));
        [Test] public void NonFiniteIsUnknown(){Assert.AreEqual(MetricValidity.Unknown,DiagnosticMetric.Number("x",double.NaN).validity);Assert.AreEqual("Unsupported",DiagnosticMetric.Missing("x").ToString());}
        [Test] public void SnapshotRejectsInvalidClock()=>Assert.Throws<ArgumentException>(()=>Snapshot(Clock(double.NaN)));
        [Test] public void SnapshotRejectsMetricBudget()=>Assert.Throws<ArgumentException>(()=>new DiagnosticSnapshot("p","e",1,"v",Clock(0),new DiagnosticMetric[65]));
        [Test] public void NoConsumersNoSampling(){using var hub=new DiagnosticHub();var p=new Provider();using var registration=hub.Register(p);hub.Tick(Clock(1));Assert.AreEqual(0,p.samples);}
        [Test] public void DemandStopsOnLastConsumer(){using var hub=new DiagnosticHub();var p=new Provider();using var r=hub.Register(p);var a=hub.Subscribe(p.Id);var b=hub.Subscribe(p.Id);a.Dispose();Assert.True(p.active);b.Dispose();Assert.False(p.active);}
        [Test] public void SampleRateIsBounded(){using var hub=new DiagnosticHub();var p=new Provider();using var r=hub.Register(p);using var s=hub.Subscribe(p.Id);for(int i=0;i<100;i++)hub.Tick(Clock(i*0.001));Assert.AreEqual(1,p.samples);}
        [Test] public void PooledGenerationDoesNotInheritHistory(){using var h=new DiagnosticHub();var p=new Provider();using var r=h.Register(p);using var s=h.Subscribe(p.Id);h.Tick(Clock(0));h.Tick(Clock(1));p.generation++;h.Tick(Clock(2));Assert.AreEqual(1,h.Find(p.Id).History.Count);}
        [Test] public void ProviderFailureIsIsolated(){using var h=new DiagnosticHub();var p=new Provider{fail=true};using var r=h.Register(p);using var s=h.Subscribe(p.Id);Assert.DoesNotThrow(()=>h.Tick(Clock(1)));Assert.AreEqual("Provider sample failed",h.Find(p.Id).Error);Assert.AreEqual(1,h.Events.Count);}
        [Test] public void OldLeaseCannotRemoveNewRegistration(){using var h=new DiagnosticHub();var p=new Provider();var a=h.Register(p);a.Dispose();var b=h.Register(p);a.Dispose();Assert.NotNull(h.Find(p.Id));b.Dispose();}
        [Test] public void OldSubscriptionCannotDisableReusedProvider(){using var h=new DiagnosticHub();var p=new Provider();var a=h.Register(p);var old=h.Subscribe(p.Id);a.Dispose();using var b=h.Register(p);using var current=h.Subscribe(p.Id);old.Dispose();Assert.True(p.active);}
        [Test] public void UnregisterStopsSampling(){using var h=new DiagnosticHub();var p=new Provider();var r=h.Register(p);using var s=h.Subscribe(p.Id);r.Dispose();Assert.False(p.active);h.Tick(Clock(1));Assert.AreEqual(0,p.samples);}
        [Test] public void EventStormIsBounded(){using var h=new DiagnosticHub();for(int i=0;i<100000;i++)h.Publish(new DiagnosticEvent{clock=Clock(1),code="storm"});Assert.AreEqual(256,h.Events.Count);Assert.AreEqual(99744,h.RejectedEvents);}
        [Test] public void CaptureDisclosesLoss(){using var h=new DiagnosticHub();for(int i=0;i<2000;i++)h.Publish(new DiagnosticEvent{clock=Clock(i)});var c=DiagnosticCapture.Create(h,"test");Assert.AreEqual(976,c.overwritten);Assert.AreEqual(1024,c.events.Length);}
        [Test] public void CaptureRoundTripsSchema(){using var h=new DiagnosticHub();var p=new Provider();using var r=h.Register(p);using var s=h.Subscribe(p.Id);h.Tick(Clock(1));var c=DiagnosticCapture.Parse(JsonUtility.ToJson(DiagnosticCapture.Create(h,"test")));Assert.AreEqual(1,c.records.Length);Assert.AreEqual(2,c.records[0].metrics[0].value);Assert.AreEqual("provider-1",c.records[0].provider);}
        [Test] public void UnknownSchemaRejected()=>Assert.Throws<InvalidDataException>(()=>DiagnosticCapture.Parse("{\"schema\":99,\"records\":[],\"events\":[],\"providers\":[]}"));
        [TestCase("/Users/person/secret")] [TestCase("Bearer abc")] [TestCase("account 123")] [TestCase("name@example.com")] public void Redaction(string value)=>Assert.AreEqual("[redacted]",DiagnosticCapture.Redact(value));
        [Test] public void CancelledExportLeavesNoFile(){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");var token=new CancellationToken(true);Assert.ThrowsAsync<System.Threading.Tasks.TaskCanceledException>(()=>DiagnosticCapture.ExportAsync(path,"{}",token));Assert.False(File.Exists(path));}
        [Test] public void ExportPreservesExistingFile(){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");File.WriteAllText(path,"original");try{Assert.ThrowsAsync<IOException>(()=>DiagnosticCapture.ExportAsync(path,"{}",CancellationToken.None));Assert.AreEqual("original",File.ReadAllText(path));Assert.AreEqual(1,Directory.GetFiles(Path.GetDirectoryName(path),Path.GetFileName(path)+"*").Length);}finally{File.Delete(path);}}
        private sealed class Command:IDiagnosticCommand{public int calls;public string Id=>"sandbox.test";public string SideEffects=>"Test memory only";public bool Reversible=>false;public bool RequiresConfirmation=>true;public bool Validate(double value)=>value>=0&&value<=1;public void Execute(double value){calls++;}}
        [TestCase(false,true,DiagnosticCommandContext.Sandbox)] [TestCase(true,false,DiagnosticCommandContext.Sandbox)] [TestCase(true,true,DiagnosticCommandContext.LiveProfile)]
        public void ForbiddenCommandCannotExecute(bool auth,bool confirm,DiagnosticCommandContext context){var registry=new DiagnosticCommands();var cmd=new Command();registry.Register(cmd);Assert.False(registry.TryExecute(cmd.Id,1,context,confirm,auth,null,Clock(0)));Assert.AreEqual(0,cmd.calls);}
        [Test] public void AuthorizedSandboxCommandAudited(){var registry=new DiagnosticCommands();var cmd=new Command();registry.Register(cmd);using var hub=new DiagnosticHub();Assert.True(registry.TryExecute(cmd.Id,1,DiagnosticCommandContext.Sandbox,true,true,hub,Clock(0)));Assert.AreEqual(1,cmd.calls);Assert.AreEqual("command.succeeded",hub.Events[0].code);}
        [Test] public void InvalidCommandValueDenied(){var registry=new DiagnosticCommands();var cmd=new Command();registry.Register(cmd);Assert.False(registry.TryExecute(cmd.Id,double.NaN,DiagnosticCommandContext.Sandbox,true,true,null,Clock(0)));Assert.AreEqual(0,cmd.calls);}
        [Test] public void ProfilerDisposesRepeatedly(){var p=new DiagnosticProfiler();p.Dispose();p.SetDemand(true,false);p.SetDemand(false,false);p.Dispose();}
    }
}
