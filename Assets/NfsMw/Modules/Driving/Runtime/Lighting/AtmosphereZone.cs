using System;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    [DisallowMultipleComponent]
    public sealed class AtmosphereZone : MonoBehaviour
    {
        public string id=Guid.NewGuid().ToString("N"), cellId="", upstreamDistrictId="";
        public AtmosphereProfile profile;
        public int priority;
        public Vector3 size=new Vector3(30,10,60);
        public LookCategory categories=LookCategory.All;
        public Vector4 blendMeters=new Vector4(10,20,15,20);
        public bool overrideExposure;
        [Range(-5,5)] public float exposureOverride;
        public AtmosphereLook Look {get{var v=profile.look;if(overrideExposure)v.exposure=exposureOverride;return v;}}
        public Vector4 Weights(Vector3 point)
        {
            var local=transform.InverseTransformPoint(point);
            var delta=Vector3.Max(new Vector3(Mathf.Abs(local.x),Mathf.Abs(local.y),Mathf.Abs(local.z))-size*.5f,Vector3.zero);
            float distance=Vector3.Scale(delta,Abs(transform.lossyScale)).magnitude;
            return new Vector4(W(distance,blendMeters.x,LookCategory.Lighting),W(distance,blendMeters.y,LookCategory.Fog),W(distance,blendMeters.z,LookCategory.Exposure),W(distance,blendMeters.w,LookCategory.Reflections));
        }
        float W(float distance,float blend,LookCategory category)=> (categories&category)==0?0:distance<=0?1:blend<=0?0:Mathf.Clamp01(1-distance/blend);
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        void OnDrawGizmosSelected(){Gizmos.color=new Color(.2f,.7f,1,.8f);Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Vector3.zero,size);}
    }
}
