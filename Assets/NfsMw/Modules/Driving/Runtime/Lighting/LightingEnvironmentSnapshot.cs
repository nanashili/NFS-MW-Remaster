using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace NfsMwRemaster.Lighting
{
    // Restores only properties owned by the studio. Suitable for try/finally render scopes.
    public sealed class LightingEnvironmentSnapshot : IDisposable
    {
        readonly AmbientMode ambient=RenderSettings.ambientMode;
        readonly Color sky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor,fogColor=RenderSettings.fogColor;
        readonly bool fog=RenderSettings.fog;readonly FogMode mode=RenderSettings.fogMode;
        readonly float density=RenderSettings.fogDensity,start=RenderSettings.fogStartDistance,end=RenderSettings.fogEndDistance,reflection=RenderSettings.reflectionIntensity;
        readonly Light key;readonly Color keyColor;readonly float intensity;readonly Quaternion rotation;readonly LightUnit unit;
        bool disposed;
        public LightingEnvironmentSnapshot(Light keyLight=null){key=keyLight;if(key){keyColor=key.color;intensity=key.intensity;rotation=key.transform.rotation;unit=key.lightUnit;}}
        public void Dispose(){if(disposed)return;disposed=true;RenderSettings.ambientMode=ambient;RenderSettings.ambientSkyColor=sky;RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;RenderSettings.fog=fog;RenderSettings.fogMode=mode;RenderSettings.fogColor=fogColor;RenderSettings.fogDensity=density;RenderSettings.fogStartDistance=start;RenderSettings.fogEndDistance=end;RenderSettings.reflectionIntensity=reflection;if(key){key.color=keyColor;key.lightUnit=unit;key.intensity=intensity;key.transform.rotation=rotation;}}
    }
}
