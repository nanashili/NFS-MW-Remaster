using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RaceRouteSuggestion { TravelTime, Distance }
    [Serializable] public sealed class RaceRoutePath
    {
        public string id=Guid.NewGuid().ToString("N"),label="Main";
        public RacingRouteSpan[] spans=Array.Empty<RacingRouteSpan>();
    }
    [Serializable] public sealed class RaceRouteLeg
    {
        public string id=Guid.NewGuid().ToString("N"),label="Sector";
        public RaceRoutePath[] paths={new RaceRoutePath()};
        [Min(.5f)] public float gateHeight=5;
        [Min(1)] public float gateSpacing=40;
    }
    [CreateAssetMenu(menuName="NFS MW Remaster/Races/Route",fileName="RaceRoute")]
    public sealed class RaceRouteDefinition : ScriptableObject
    {
        public const int CurrentSchema=1;
        [HideInInspector] public int schema=CurrentSchema;
        [HideInInspector] public string id=Guid.NewGuid().ToString("N");
        public string displayName="New race",localizationKey,tags;
        public RoadNetworkAsset network;
        public FreeRoamEventKind policy=FreeRoamEventKind.Sprint;
        [Range(1,100)] public int laps=1;
        [Min(1)] public float timeLimit=180;
        [Min(0)] public float targetSpeedKph=100;
        [Min(1)] public float minimumWidth=3,maximumGradeDegrees=20;
        [Range(1,180)] public float maximumTurnDegrees=140;
        public SensorySurfaceProfile requiredSurface;
        public RoadClass[] excludedRoadClasses=Array.Empty<RoadClass>();
        public RaceRouteLeg[] legs=Array.Empty<RaceRouteLeg>();
        public RacingVehicleSetup vehicle;
        public RacingLineSource racingLine;
        public RaceRoutePublication published;
        [Min(1)] public int gridCount=4;
        [Min(1)] public float vehicleWidth=2.5f,vehicleLength=5,vehicleHeight=3.2f;
        [Min(.1f)] public float gridGap=2,finishRunoff=15;
        public bool sideBySideGrid;
        [TextArea] public string notes;
    }
    [Serializable] public struct RaceRouteGate
    {
        public string id,laneId;
        public Vector3 position,forward,up;
        public float width,height,distance;
        public bool Cross(Vector3 previous,Vector3 current,out float fraction)
        {
            fraction=0;float a=Vector3.Dot(previous-position,forward),b=Vector3.Dot(current-position,forward);
            if(!float.IsFinite(a)||!float.IsFinite(b)||a>=0||b<0||b-a<.00001f)return false;
            fraction=-a/(b-a);var offset=Vector3.Lerp(previous,current,fraction)-position;
            return Mathf.Abs(Vector3.Dot(offset,Vector3.Cross(up,forward).normalized))<=width/2 && Mathf.Abs(Vector3.Dot(offset,up))<=height/2;
        }
    }
    [Serializable] public sealed class RaceRouteBakedPath { public string id;public float length;public RaceRouteGate[] gates; }
    [Serializable] public sealed class RaceRouteBakedLeg { public string id;public RaceRouteBakedPath[] paths; }
    [Serializable] public sealed class RaceRouteIssue
    {
        public string rule,owner,message;public bool error;
        public RaceRouteIssue(string rule,string owner,string message,bool error=true){this.rule=rule;this.owner=owner;this.message=message;this.error=error;}
        public override string ToString()=>rule+": "+message;
    }
}
