using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class RoadWetness : MonoBehaviour
    {
        [Range(0,1)] public float wetness;
        [Min(.01f)] public float transitionSeconds=15;
        [Tooltip("Weather drives this presentation fallback. Vehicle physics never reads it.")]
        public bool acceptWeather;
        static RoadWetness owner;
        static readonly int Property=Shader.PropertyToID("_RacingWetness");
        float current,prior;
        public static float Current => owner ? owner.current : 0;
        void OnEnable()
        {
            if(owner && owner!=this){Debug.LogError("Only one RoadWetness may own the world wetness value.",this);enabled=false;return;}
            owner=this;prior=Shader.GetGlobalFloat(Property);current=wetness;Shader.SetGlobalFloat(Property,current);
        }
        void Update(){current=Mathf.MoveTowards(current,wetness,Time.unscaledDeltaTime/Mathf.Max(.01f,transitionSeconds));Shader.SetGlobalFloat(Property,current);}
        public void ApplyWeather(float target)
        {
            if(!acceptWeather)return;
            wetness=Mathf.Clamp01(float.IsNaN(target)||float.IsInfinity(target)?0:target);
        }
        public void ResetWeatherPresentation()
        {
            wetness = 0;
            current = 0;
            Shader.SetGlobalFloat(Property, 0);
        }
        void OnDisable(){if(owner!=this)return;Shader.SetGlobalFloat(Property,prior);owner=null;}
    }
}
