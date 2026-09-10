using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NfsMwRemaster.Diagnostics
{
    [Serializable] public sealed class DiagnosticRecord
    {
        public string provider, entity, revision;
        public long generation;
        public DiagnosticClock clock;
        public DiagnosticMetric[] metrics;
    }
    [Serializable] public sealed class DiagnosticCapture
    {
        public int schema=1;
        public string mode="Recorded diagnostics; no simulation rewind", build;
        public long overwritten, rejected;
        public bool truncated, textIncluded;
        public DiagnosticRecord[] records;
        public DiagnosticEvent[] events;
        public string[] providers;
        public string[] unavailableProviders;
        public double[] sampleIntervals;
        public static DiagnosticCapture Create(DiagnosticHub hub, string build, bool includeText=false)
        {
            var result=new DiagnosticCapture{build=DiagnosticSnapshot.Bound(build,128),textIncluded=includeText,rejected=hub.RejectedEvents,overwritten=hub.Events.Overwritten};
            var rows=new List<DiagnosticRecord>();var ids=new List<string>();var eventList=new List<DiagnosticEvent>();
            foreach(var c in hub.Channels)
            {
                ids.Add(c.Provider.Id);result.overwritten+=c.History.Overwritten;
                for(int i=0;i<c.History.Count;i++)
                {
                    if(rows.Count>=2048){result.truncated=true;break;}
                    var s=c.History[i];var m=new DiagnosticMetric[s.Count];
                    for(int j=0;j<m.Length;j++){m[j]=s[j];if(m[j].kind==MetricKind.State && !includeText)m[j].text="[text excluded]";else m[j].text=Redact(m[j].text);}
                    rows.Add(new DiagnosticRecord{provider=Alias(ids.Count),entity="generation-"+s.Generation,generation=s.Generation,
                        revision=Redact(s.Revision),clock=s.Clock,metrics=m});
                }
            }
            for(int i=0;i<hub.Events.Count;i++)
            {var e=hub.Events[i];int index=ids.IndexOf(e.provider);e.provider=index<0 ? "unregistered" : Alias(index+1);e.code=includeText?Redact(e.code):"[event text excluded]";eventList.Add(e);}
            result.providers=new string[ids.Count];for(int i=0;i<ids.Count;i++)result.providers[i]=Alias(i+1);
            var missing=new List<string>();result.sampleIntervals=new double[ids.Count];
            for(int i=0;i<ids.Count;i++){var channel=hub.Find(ids[i]);result.sampleIntervals[i]=channel.Provider.Interval;if(channel.Current==null || channel.Error.Length>0)missing.Add(Alias(i+1));}
            result.unavailableProviders=missing.ToArray();
            rows.Sort((a,b)=>a.clock.realtime.CompareTo(b.clock.realtime));result.records=rows.ToArray();result.events=eventList.ToArray();return result;
        }
        private static string Alias(int index)=>"provider-"+index;
        public static string Redact(string text)
        {
            if(string.IsNullOrEmpty(text))return "";
            string lower=text.ToLowerInvariant();
            if(text.Contains("/") || text.Contains("\\") || text.Contains("@") || lower.Contains("token") || lower.Contains("password") || lower.Contains("bearer") || lower.Contains("account"))return "[redacted]";
            return DiagnosticSnapshot.Bound(text,256);
        }
        public static DiagnosticCapture Parse(string json)
        {
            if(json==null || Encoding.UTF8.GetByteCount(json)>16*1024*1024)throw new InvalidDataException("Capture exceeds 16 MiB.");
            var c=JsonUtility.FromJson<DiagnosticCapture>(json);
            if(c==null || c.schema!=1 || c.records==null || c.records.Length>2048 || c.events==null || c.events.Length>1024 || c.providers==null || c.providers.Length>128)
                throw new InvalidDataException("Unsupported schema or capture budget.");
            if(c.sampleIntervals!=null && c.sampleIntervals.Length!=c.providers.Length || c.unavailableProviders!=null && c.unavailableProviders.Length>128)
                throw new InvalidDataException("Invalid provider manifest.");
            foreach(var r in c.records)
            {
                if(r==null || r.metrics==null)throw new InvalidDataException("Missing record.");
                var normalized=new DiagnosticSnapshot(r.provider,r.entity,r.generation,r.revision,r.clock,r.metrics);
                r.revision=normalized.Revision;
                for(int i=0;i<r.metrics.Length;i++)r.metrics[i]=normalized[i];
            }
            for(int i=0;i<c.events.Length;i++)
            {var e=c.events[i];if(!double.IsFinite(e.clock.realtime))throw new InvalidDataException("Invalid event clock.");e.provider=DiagnosticSnapshot.Bound(e.provider,128);e.code=DiagnosticSnapshot.Bound(e.code,128);c.events[i]=e;}
            return c;
        }
        // JSON is frozen on the main thread. Only bounded UTF-8 file I/O happens on the worker.
        public static Task ExportAsync(string path,string json,CancellationToken token)
        {
            if(string.IsNullOrWhiteSpace(path) || json==null || Encoding.UTF8.GetByteCount(json)>16*1024*1024)throw new ArgumentException("Invalid export path or size.");
            return Task.Run(()=>{
                string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{token.ThrowIfCancellationRequested();File.WriteAllText(stage,json,new UTF8Encoding(false));token.ThrowIfCancellationRequested();
                    if(File.Exists(path))throw new IOException("Choose a new file; existing captures are never overwritten.");File.Move(stage,path);}
                finally{if(File.Exists(stage))File.Delete(stage);}
            },token);
        }
    }
}
