using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    /// <summary>Race/mission owners publish source-space route strips. This container never advances gameplay.</summary>
    public sealed class MapRouteOverlays
    {
        public sealed class Strip
        {public string id,roadRevision;public Vector3[] points;public Color color;public bool dashed;}
        private readonly Dictionary<string,Strip> values=new Dictionary<string,Strip>(StringComparer.Ordinal);
        private readonly Dictionary<string,object> tokens=new Dictionary<string,object>(StringComparer.Ordinal);
        private sealed class Lease:IDisposable
        {
            private readonly Action release;public Lease(Action release){this.release=release;}public void Dispose()=>release();
        }
        public IDisposable Publish(string id,string roadRevision,IReadOnlyList<Vector3> points,Color color,bool dashed=false)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(roadRevision)||points==null||points.Count<2||points.Count>10000)throw new ArgumentException("Overlay requires stable identity, revision and 2–10000 points.");
            if(!values.ContainsKey(id)&&values.Count>=16)throw new InvalidOperationException("Map route overlay capacity is 16 strips.");
            var copy=new Vector3[points.Count];for(int i=0;i<copy.Length;i++){var p=points[i];if(!new MapPoint(p.x,p.z).Finite||!float.IsFinite(p.y))throw new ArgumentException("Non-finite overlay point.");copy[i]=p;}
            var token=new object();tokens[id]=token;values[id]=new Strip{id=id,roadRevision=roadRevision,points=copy,color=color,dashed=dashed};
            return new Lease(()=>{if(tokens.TryGetValue(id,out var current)&&ReferenceEquals(current,token)){tokens.Remove(id);values.Remove(id);}});
        }
        public void VisitVisible(IMapKnowledge policy,string revision,Action<Strip> visit)
        {if(policy==null)return;foreach(var strip in values.Values)if(strip.roadRevision==revision&&policy.MarkerVisible(strip.id,"Route"))visit(strip);}
    }
}
