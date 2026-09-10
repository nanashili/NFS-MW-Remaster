using System;
using UnityEngine;
namespace NfsMwRemaster.Diagnostics
{
    /// <summary>Explicit synthetic fixture; never represents production telemetry.</summary>
    public sealed class DiagnosticFixture : MonoBehaviour, IDiagnosticProvider
    {
        private IDisposable registration;
        private int epoch=-1;
        private bool demand, geometry;
        private long generation=1;
        private double nextEvent;
        public string Id=>"synthetic.fixture."+GetEntityId();
        public string Category=>"Synthetic fixture";
        public string Label=>"Synthetic brake / safety trace";
        public double Interval=>0.1;
        private void OnEnable(){if(Application.isPlaying && DiagnosticBuild.Enabled)Register();}
        private void Register(){registration?.Dispose();registration=DiagnosticSession.Hub.Register(this);epoch=DiagnosticSession.Epoch;generation++;}
        private void Update(){if(Application.isPlaying && DiagnosticBuild.Enabled && epoch!=DiagnosticSession.Epoch)Register();}
        public void SetDemand(bool active,bool heavy){demand=active&&DiagnosticBuild.Enabled;geometry=heavy;}
        public DiagnosticSnapshot Sample(DiagnosticClock clock)
        {
            if(!demand)return null;
            double desired=2*Math.Sin(clock.realtime),safe=Math.Min(desired,0.4);
            if(clock.realtime>=nextEvent){nextEvent=clock.realtime+1;DiagnosticSession.Hub.Publish(new DiagnosticEvent{clock=clock,provider=Id,code="synthetic.safety.bound",severity=1});}
            return new DiagnosticSnapshot(Id,"synthetic-car",generation,"synthetic.v1",clock,new[]{
                DiagnosticMetric.Number("acceleration.desired",desired,"m/s²"),DiagnosticMetric.Number("acceleration.safety",safe,"m/s²"),
                DiagnosticMetric.Number("acceleration.final",safe,"m/s²"),DiagnosticMetric.State("source","SYNTHETIC TEST DATA"),
                DiagnosticMetric.Missing("hardware.counter")},geometry?new[]{new DiagnosticLine{from=transform.position,to=transform.position+Vector3.forward*10,color=Color.cyan}}:null);
        }
        private void OnDisable(){registration?.Dispose();registration=null;demand=false;}
    }
}
