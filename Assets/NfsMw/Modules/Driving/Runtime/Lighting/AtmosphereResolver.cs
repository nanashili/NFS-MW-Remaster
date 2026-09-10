using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    public static class AtmosphereResolver
    {
        public static AtmosphereLook Resolve(AtmosphereProfile fallback,IEnumerable<AtmosphereZone> zones,Vector3 point,List<string> contributors=null)
        {
            if(!fallback||!fallback.IsValid)throw new InvalidOperationException("A valid fallback profile is required.");
            var ordered=new List<AtmosphereZone>();var ids=new HashSet<string>();
            foreach(var zone in zones)if(zone&&zone.isActiveAndEnabled)
            {if(string.IsNullOrWhiteSpace(zone.id)||!ids.Add(zone.id))throw new InvalidOperationException("Zone IDs must be nonempty and unique. Remap duplicate IDs explicitly.");if(!zone.profile||!zone.profile.IsValid)throw new InvalidOperationException("Missing or invalid profile on zone "+zone.name);ordered.Add(zone);}
            ordered.Sort((a,b)=>{int p=a.priority.CompareTo(b.priority);return p!=0?p:string.CompareOrdinal(a.id,b.id);});
            var result=fallback.look;contributors?.Clear();
            foreach(var zone in ordered){var w=zone.Weights(point);if(w.sqrMagnitude==0)continue;result.Blend(zone.Look,w);contributors?.Add(zone.name+" ["+zone.id+"] "+w);}
            return result;
        }
        public static AtmosphereLook Step(AtmosphereLook from,AtmosphereLook to,AtmosphereProfile policy,float seconds)
        {
            if(seconds<0||float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            from.Blend(to,new Vector4(Weight(seconds,policy.lightingSeconds),Weight(seconds,policy.fogSeconds),Weight(seconds,policy.exposureSeconds),Weight(seconds,policy.reflectionSeconds)));return from;
        }
        static float Weight(float dt,float seconds)=>1-Mathf.Exp(-dt/Mathf.Max(.01f,seconds));
    }
}
