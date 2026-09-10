#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Diagnostics
{
    // Opt-in development player measurement. Uses the normal vehicle input/physics/camera path.
    public sealed class RenderingBenchmark : MonoBehaviour, IVehicleInputSource
    {
        [Serializable] sealed class Report
        {
            public string device, graphicsApi, quality, scene;
            public int width, height, frames, frameCap;
            public float maximumDeltaTime;
            public int loadedCells, enabledCellRenderers, cellMaterialSlots, disabledByProbe;
            public int cellTerrains, heightmapTerrains, treeTerrains, treeInstances;
            public int instancedTerrains, minimumFullLodTrees, maximumFullLodTrees;
            public float minimumHeightmapPixelError, maximumHeightmapPixelError;
            public float minimumBasemapDistance, maximumBasemapDistance;
            public float minimumTreeDistance, maximumTreeDistance;
            public float minimumTreeBillboardDistance, maximumTreeBillboardDistance;
            public double medianFrameMs, p95FrameMs, averageFps, distanceMetres, maximumSpeedKph;
            public bool gpuTimingAvailable, wetWeather;
            public bool runInBackground;
            public bool drivingVerified, frameRateVerified;
            public double minimumFps;
            public string probe;
            public int gpuSamples;
            public double averageGpuMs, p95GpuMs;
            public List<string> loadedCellNames = new List<string>();
            public List<DiagnosticMetric> counters = new List<DiagnosticMetric>();
        }
        string output, probe; VehicleController vehicle; bool driving; int disabledByProbe;
        public VehicleInputState Current => new VehicleInputState { Throttle = driving ? 1 : 0 };
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-render-benchmark");
            if(at<0 || at+1>=args.Length)return;
            var host=new GameObject("Rendering benchmark").AddComponent<RenderingBenchmark>();
            host.output=Path.GetFullPath(args[at+1]);DontDestroyOnLoad(host.gameObject);
        }
        void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-render-probe");
            probe=at>=0&&at+1<args.Length?args[at+1]:string.Empty;
            SceneManager.sceneLoaded+=OnSceneLoaded;
        }
        void OnDestroy()=>SceneManager.sceneLoaded-=OnSceneLoaded;
        void OnSceneLoaded(Scene scene,LoadSceneMode mode)
        {
            ApplyBehaviourProbe(scene);
            if(scene.path.StartsWith("Assets/NfsMw/Scenes/World/Streaming/",StringComparison.Ordinal))ApplyCellProbe(scene);
        }
        IEnumerator Start()
        {
            // Automated development-player runs are not the foreground macOS application.
            // Without this, Unity throttles the benchmark itself to roughly five frames per second.
            Application.runInBackground=true;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-render-quality");
            if(at>=0 && at+1<args.Length && int.TryParse(args[at+1],out int quality))HdrpQualityRuntime.SetQuality(quality);
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            // Prevent a slow render frame from scheduling up to 16 expensive physics ticks and
            // starving the benchmark coroutine. Production still has to make each tick fit its budget.
            Time.maximumDeltaTime=.1f;
            at=Array.IndexOf(args,"-render-cap");
            if(at>=0 && at+1<args.Length && int.TryParse(args[at+1],out int cap) && cap>0)Application.targetFrameRate=cap;
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            if(Array.IndexOf(args,"-render-menu")>=0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                yield return new WaitForSecondsRealtime(2);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(output),"boot-menu.png"));
                yield return new WaitForEndOfFrame();
            }
            at=Array.IndexOf(args,"-render-scene");
            string scene=at>=0 && at+1<args.Length?args[at+1]:"Assets/NfsMw/Scenes/Game/DrivingDemo.unity";
            if(!Application.CanStreamedLevelBeLoaded(scene)){Debug.LogError("Benchmark scene is not included in the player: "+scene);Application.Quit(2);yield break;}
            // The ordinary boot flow owns simulation pause and its menu. This explicit benchmark
            // runs a standalone scene, so dispose that owner through its normal restoration path.
            if(GameFlowRuntime.Instance)Destroy(GameFlowRuntime.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(scene,LoadSceneMode.Single);
            string saves=Path.Combine(Path.GetDirectoryName(output),"benchmark-saves",Guid.NewGuid().ToString("N"));
            foreach(var storage in FindObjectsByType<JsonCareerProfileStorage>(FindObjectsInactive.Include))storage.SetDirectory(saves);
            foreach(var profile in FindObjectsByType<CareerProfileSystem>(FindObjectsInactive.Include))
            {profile.ConfigureAutomaticPersistence(false,false,false);profile.ConfigureAutosave(()=>false);}
            bool wetWeather=Array.IndexOf(args,"-render-wet")>=0;
            if(wetWeather)
            {
                foreach(var road in FindObjectsByType<RoadWetness>()){road.transitionSeconds=.01f;road.wetness=1;}
                foreach(var rain in FindObjectsByType<LocalRain>())rain.intensity=1;
            }
            var rig=FindAnyObjectByType<VehicleCameraRig>();
            vehicle=rig && rig.Target?rig.Target.GetComponent<VehicleController>():null;
            if(!vehicle){Debug.LogError("Rendering benchmark requires a bound player vehicle.");Application.Quit(2);yield break;}
            var priorInput=vehicle.InputSourceComponent;vehicle.SetInputSource(this);driving=true;
            var start=vehicle.transform.position;var previous=start;
            var profiler=new DiagnosticProfiler();profiler.SetDemand(true,false);
            var report=new Report{device=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),quality=QualitySettings.names[QualitySettings.GetQualityLevel()],scene=vehicle.gameObject.scene.path,wetWeather=wetWeather,frameCap=Application.targetFrameRate,maximumDeltaTime=Time.maximumDeltaTime,probe=probe,runInBackground=Application.runInBackground};
            at=Array.IndexOf(args,"-render-min-fps");
            if(at>=0 && at+1<args.Length && double.TryParse(args[at+1],System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out double minimumFps))report.minimumFps=Math.Max(0,minimumFps);
            var frames=new List<double>();var sums=new Dictionary<string,double>();var counts=new Dictionary<string,int>();
            var gpuFrames=new List<double>();var timing=new FrameTiming[1];
            ulong lastGpuTimestamp=0;
            var metricTypes=new Dictionary<string,DiagnosticMetric>();
            double began=Time.realtimeSinceStartupAsDouble,last=began,total=0;
            while(Time.realtimeSinceStartupAsDouble-began<30)
            {
                FrameTimingManager.CaptureFrameTimings();
                yield return new WaitForEndOfFrame();double now=Time.realtimeSinceStartupAsDouble,dt=now-last;last=now;
                var position=vehicle.transform.position;float step=Vector3.Distance(previous,position);previous=position;
                report.maximumSpeedKph=Math.Max(report.maximumSpeedKph,vehicle.Body.linearVelocity.magnitude*3.6);
                if(step<20)report.distanceMetres+=step;
                if(Vector3.Distance(start,position)>180){vehicle.ResetVehicle();previous=vehicle.transform.position;}
                if(now-began<10)continue;
                frames.Add(dt*1000);total+=dt;
                if(FrameTimingManager.GetLatestTimings(1,timing)>0 && timing[0].gpuFrameTime>0 && timing[0].frameStartTimestamp!=lastGpuTimestamp)
                {gpuFrames.Add(timing[0].gpuFrameTime);lastGpuTimestamp=timing[0].frameStartTimestamp;}
                var sample=profiler.Sample(DiagnosticClock.Now(SamplePhase.Render));
                for(int i=0;i<sample.Count;i++)
                {
                    var metric=sample[i];metricTypes[metric.id]=metric;
                    if(metric.validity!=MetricValidity.Valid)continue;
                    sums.TryGetValue(metric.id,out double sum);counts.TryGetValue(metric.id,out int count);
                    sums[metric.id]=sum+metric.value;counts[metric.id]=count+1;
                }
            }
            driving=false;vehicle.SetInputSource(priorInput);profiler.Dispose();frames.Sort();report.frames=frames.Count;
            report.drivingVerified=report.distanceMetres>100 && report.maximumSpeedKph>40;
            report.width=Screen.width;report.height=Screen.height;report.averageFps=frames.Count/total;
            report.frameRateVerified=report.minimumFps<=0 || report.averageFps>=report.minimumFps;
            CaptureCellState(report);
            report.medianFrameMs=frames[frames.Count/2];report.p95FrameMs=frames[Math.Min(frames.Count-1,(int)(frames.Count*.95))];
            report.gpuSamples=gpuFrames.Count;
            // A stale singleton from an earlier camera cannot represent a driving interval.
            if(gpuFrames.Count>=30)
            {
                gpuFrames.Sort();report.gpuTimingAvailable=true;
                foreach(double gpu in gpuFrames)report.averageGpuMs+=gpu;report.averageGpuMs/=gpuFrames.Count;
                report.p95GpuMs=gpuFrames[Math.Min(gpuFrames.Count-1,(int)(gpuFrames.Count*.95))];
            }
            foreach(var entry in metricTypes){var metric=entry.Value;if(counts.TryGetValue(entry.Key,out int count)&&count>0){metric.value=sums[entry.Key]/count;metric.validity=MetricValidity.Valid;}report.counters.Add(metric);}
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,"png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(1);
            Application.Quit(report.drivingVerified&&report.frameRateVerified?0:2);
        }

        void ApplyCellProbe(Scene scene)
        {
            foreach(GameObject root in scene.GetRootGameObjects())
            {
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    bool road=HasAncestor(renderer.transform,"Rockport Road Colliders");
                    if(HasProbe("cell-renderers-off") || HasProbe("road-renderers-off")&&road)
                    {
                        if(renderer.enabled){renderer.enabled=false;disabledByProbe++;}
                    }
                    else if(HasProbe("shadows-off"))
                    {
                        renderer.shadowCastingMode=ShadowCastingMode.Off;
                        renderer.receiveShadows=false;
                    }
                }
                foreach(var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    if(HasProbe("terrain-trees-off") || HasProbe("terrains-off"))
                    {
                        if(terrain.drawTreesAndFoliage)
                        {
                            terrain.drawTreesAndFoliage=false;
                            disabledByProbe++;
                        }
                    }
                    if(HasProbe("terrain-heightmap-off") || HasProbe("terrains-off"))
                    {
                        if(terrain.drawHeightmap)
                        {
                            terrain.drawHeightmap=false;
                            disabledByProbe++;
                        }
                    }
                }
            }
        }

        void ApplyBehaviourProbe(Scene scene)
        {
            foreach(GameObject root in scene.GetRootGameObjects())
            {
                if(HasProbe("traffic-off"))
                {
                    foreach(var world in root.GetComponentsInChildren<TrafficWorldDirector>(true))
                        if(world.enabled){world.enabled=false;disabledByProbe++;}
                    foreach(var motor in root.GetComponentsInChildren<RoadVehicleMotor>(true))
                    {
                        if(motor.enabled){motor.enabled=false;disabledByProbe++;}
                        if(motor.Vehicle&&motor.Vehicle.enabled){motor.Vehicle.enabled=false;disabledByProbe++;}
                    }
                }
                if(HasProbe("police-off"))
                {
                    foreach(var director in root.GetComponentsInChildren<VehiclePursuitDirector>(true))
                        if(director.enabled){director.enabled=false;disabledByProbe++;}
                    foreach(var unit in root.GetComponentsInChildren<VehiclePoliceUnit>(true))
                    {
                        if(unit.enabled){unit.enabled=false;disabledByProbe++;}
                        if(unit.Vehicle&&unit.Vehicle.enabled){unit.Vehicle.enabled=false;disabledByProbe++;}
                    }
                }
                if(HasProbe("ocean-off"))
                {
                    foreach(var ocean in root.GetComponentsInChildren<RockportOcean>(true))
                        if(ocean.enabled){ocean.enabled=false;disabledByProbe++;}
                    foreach(var body in root.GetComponentsInChildren<OceanBuoyantBody>(true))
                        if(body.enabled){body.enabled=false;disabledByProbe++;}
                }
                if(HasProbe("destruction-off"))
                    foreach(var world in root.GetComponentsInChildren<DestructionWorld>(true))
                        if(world.enabled){world.enabled=false;disabledByProbe++;}
            }
        }

        void CaptureCellState(Report report)
        {
            report.disabledByProbe=disabledByProbe;
            bool firstTerrain=true,firstHeightmap=true,firstTreeTerrain=true;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                Scene scene=SceneManager.GetSceneAt(i);
                if(!scene.isLoaded || !scene.path.StartsWith("Assets/NfsMw/Scenes/World/Streaming/",StringComparison.Ordinal))continue;
                report.loadedCells++;
                report.loadedCellNames.Add(scene.name);
                foreach(GameObject root in scene.GetRootGameObjects())
                {
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                        if(renderer.enabled&&renderer.gameObject.activeInHierarchy)
                        {
                            report.enabledCellRenderers++;
                            report.cellMaterialSlots+=renderer.sharedMaterials.Length;
                        }
                    foreach(var terrain in root.GetComponentsInChildren<Terrain>(true))
                    {
                        report.cellTerrains++;
                        if(terrain.drawInstanced)report.instancedTerrains++;
                        if(firstTerrain)
                        {
                            report.minimumBasemapDistance=report.maximumBasemapDistance=terrain.basemapDistance;
                            firstTerrain=false;
                        }
                        else
                        {
                            report.minimumBasemapDistance=Math.Min(report.minimumBasemapDistance,terrain.basemapDistance);
                            report.maximumBasemapDistance=Math.Max(report.maximumBasemapDistance,terrain.basemapDistance);
                        }
                        if(terrain.drawHeightmap)
                        {
                            report.heightmapTerrains++;
                            if(firstHeightmap)
                            {
                                report.minimumHeightmapPixelError=report.maximumHeightmapPixelError=terrain.heightmapPixelError;
                                firstHeightmap=false;
                            }
                            else
                            {
                                report.minimumHeightmapPixelError=Math.Min(report.minimumHeightmapPixelError,terrain.heightmapPixelError);
                                report.maximumHeightmapPixelError=Math.Max(report.maximumHeightmapPixelError,terrain.heightmapPixelError);
                            }
                        }
                        if(terrain.drawTreesAndFoliage)
                        {
                            report.treeTerrains++;
                            if(terrain.terrainData)report.treeInstances+=terrain.terrainData.treeInstanceCount;
                            if(firstTreeTerrain)
                            {
                                report.minimumTreeDistance=report.maximumTreeDistance=terrain.treeDistance;
                                report.minimumTreeBillboardDistance=report.maximumTreeBillboardDistance=terrain.treeBillboardDistance;
                                report.minimumFullLodTrees=report.maximumFullLodTrees=terrain.treeMaximumFullLODCount;
                                firstTreeTerrain=false;
                            }
                            else
                            {
                                report.minimumTreeDistance=Math.Min(report.minimumTreeDistance,terrain.treeDistance);
                                report.maximumTreeDistance=Math.Max(report.maximumTreeDistance,terrain.treeDistance);
                                report.minimumTreeBillboardDistance=Math.Min(report.minimumTreeBillboardDistance,terrain.treeBillboardDistance);
                                report.maximumTreeBillboardDistance=Math.Max(report.maximumTreeBillboardDistance,terrain.treeBillboardDistance);
                                report.minimumFullLodTrees=Math.Min(report.minimumFullLodTrees,terrain.treeMaximumFullLODCount);
                                report.maximumFullLodTrees=Math.Max(report.maximumFullLodTrees,terrain.treeMaximumFullLODCount);
                            }
                        }
                    }
                }
            }
        }

        static bool HasAncestor(Transform transform,string name)
        {
            for(Transform current=transform;current;current=current.parent)
                if(current.name==name)return true;
            return false;
        }

        bool HasProbe(string value)=>probe.IndexOf(value,StringComparison.Ordinal)>=0;
    }
}
#endif
