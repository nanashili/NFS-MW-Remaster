using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Diagnostics
{
    public sealed class DiagnosticChannel
    {
        public IDiagnosticProvider Provider { get; internal set; }
        public readonly DiagnosticRing<DiagnosticSnapshot> History = new DiagnosticRing<DiagnosticSnapshot>(128);
        public DiagnosticSnapshot Current { get; internal set; }
        public string Error { get; internal set; } = "";
        public int Consumers { get; internal set; }
        internal int geometry;
        internal double due;
        public double SampleMilliseconds { get; internal set; }
    }
    public sealed class DiagnosticHub : IDisposable
    {
        private readonly Dictionary<string,DiagnosticChannel> channels = new Dictionary<string,DiagnosticChannel>();
        public IEnumerable<DiagnosticChannel> Channels => channels.Values;
        public int Count => channels.Count;
        public readonly DiagnosticRing<DiagnosticEvent> Events = new DiagnosticRing<DiagnosticEvent>(1024);
        public long RejectedEvents { get; private set; }
        private double eventWindow = double.NaN;
        private int windowEvents;
        private bool disposed;
        private bool ticking;
        private readonly List<DiagnosticChannel> tickChannels=new List<DiagnosticChannel>(128);
        private static readonly ProfilerMarker sampleMarker = new ProfilerMarker("Diagnostics.Sample");
        public IDisposable Register(IDiagnosticProvider provider)
        {
            if(disposed)throw new ObjectDisposedException(nameof(DiagnosticHub));
            if(provider==null || string.IsNullOrWhiteSpace(provider.Id) || provider.Id.Length>128 || channels.Count>=128 ||
                !double.IsFinite(provider.Interval) || provider.Interval<0.02 || provider.Interval>60)throw new ArgumentException("Invalid provider or registration budget.");
            var channel=new DiagnosticChannel{Provider=provider};channels.Add(provider.Id,channel);
            return new Lease(()=>{if(channels.TryGetValue(provider.Id,out var found)&&ReferenceEquals(found,channel)){SafeDemand(channel,false,false);channels.Remove(provider.Id);}});
        }
        public DiagnosticChannel Find(string id) => id != null && channels.TryGetValue(id,out var channel) ? channel : null;
        public IDisposable Subscribe(string id, bool geometry=false)
        {
            var c=Find(id) ?? throw new ArgumentException("Provider is unavailable.");c.Consumers++;if(geometry)c.geometry++;
            SafeDemand(c,true,c.geometry>0);
            return new Lease(()=>{c.Consumers=Math.Max(0,c.Consumers-1);if(geometry)c.geometry=Math.Max(0,c.geometry-1);
                if(!disposed && ReferenceEquals(Find(id),c))SafeDemand(c,c.Consumers>0,c.geometry>0);});
        }
        private static void SafeDemand(DiagnosticChannel c,bool active,bool geometry)
        { try{c.Provider.SetDemand(active,active&&geometry);}catch(Exception){c.Error="Provider demand failed";} }
        public void Tick(DiagnosticClock clock)
        {
            if(disposed || ticking || !double.IsFinite(clock.realtime))return;
            ticking=true;
            tickChannels.Clear();foreach(var c in channels.Values)tickChannels.Add(c);
            try { using(sampleMarker.Auto()) foreach(var c in tickChannels)
            {
                if(disposed || !ReferenceEquals(Find(c.Provider.Id),c))continue;
                if(c.Consumers==0 || clock.realtime<c.due)continue;
                c.due=clock.realtime+c.Provider.Interval;long start=Stopwatch.GetTimestamp();
                try
                {
                    var s=c.Provider.Sample(clock);
                    if(s!=null && !disposed && ReferenceEquals(Find(c.Provider.Id),c))
                    {
                        if(s.ProviderId!=c.Provider.Id)throw new InvalidOperationException();
                        if(c.Current!=null && (s.EntityId!=c.Current.EntityId || s.Generation!=c.Current.Generation))c.History.Clear();
                        if(c.Current==null || s.Clock.realtime!=c.Current.Clock.realtime || s.Generation!=c.Current.Generation)c.History.Add(s);
                        c.Current=s;c.Error="";
                    }
                    else c.Error="No owner sample available";
                }
                catch(Exception){c.Error="Provider sample failed";Publish(new DiagnosticEvent{clock=clock,provider=c.Provider.Id,code="provider.sample.failed",severity=2});}
                c.SampleMilliseconds=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            }} finally {ticking=false;tickChannels.Clear();}
        }
        public void Publish(DiagnosticEvent value)
        {
            if(disposed || !double.IsFinite(value.clock.realtime))return;
            if(double.IsNaN(eventWindow) || value.clock.realtime-eventWindow>=1 || value.clock.realtime<eventWindow){eventWindow=value.clock.realtime;windowEvents=0;}
            if(windowEvents++>=256){RejectedEvents++;return;}
            value.provider=DiagnosticSnapshot.Bound(value.provider,128);value.code=DiagnosticSnapshot.Bound(value.code,128);
            value.severity=Math.Clamp(value.severity,0,3);Events.Add(value);
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var c in channels.Values)SafeDemand(c,false,false);channels.Clear();Events.Clear();}
        private sealed class Lease:IDisposable { private Action release;public Lease(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();} }
    }
    public static class DiagnosticSession
    {
        public static DiagnosticHub Hub {get;private set;}=new DiagnosticHub();
        public static int Epoch {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset(){Hub.Dispose();Hub=new DiagnosticHub();Epoch++;}
    }
}
