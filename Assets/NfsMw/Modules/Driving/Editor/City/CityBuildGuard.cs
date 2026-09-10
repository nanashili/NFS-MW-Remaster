using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityBuildGuard : IProcessSceneWithReport
    {
        public int callbackOrder => -10;
        public static List<CityDiagnostic> Validate(CityDistrict d)
        {
            var result=new List<CityDiagnostic>(); CityPlanning.ValidateSource(d,result);
            if(result.Any(r=>r.severity==CitySeverity.Error)) return result;
            if(d.publication==null) result.Add(new CityDiagnostic("CITY_UNPUBLISHED",d.id,"District has no committed publication."));
            else if(d.publication.Fingerprint!=CityPlanning.Fingerprint(d)) result.Add(new CityDiagnostic("CITY_STALE_OUTPUT",d.id,"Committed output is stale. Review and commit a new preview."));
            var locations=d.GetComponent<CityLocations>();
            if(locations==null||locations.Publication!=d.publication) result.Add(new CityDiagnostic("CITY_LOCATIONS",d.id,"Runtime locations do not match this district publication."));
            try
            {
                var generated=CityCommands.Existing(d);
                var plan=CityPlanning.Build(d);result.AddRange(plan.diagnostics.Where(r=>r.severity==CitySeverity.Error));
                var diff=CityCommands.Diff(d,plan);
                if(diff.create.Count+diff.update.Count+diff.delete.Count>0)
                    result.Add(new CityDiagnostic("CITY_OUTPUT_REVISION",d.id,"Generated instances do not match the publication: "+diff));
                foreach(var item in generated.Values)
                {
                    if(!item.gameObject.activeInHierarchy || item.GetComponentInChildren<Renderer>()==null)
                        result.Add(new CityDiagnostic("CITY_OUTPUT",item.key,"Generated visual is missing or inactive; inspect the instance."));
                    if(item.state==CityOwnership.Generated && (Vector3.Distance(item.transform.localPosition,item.plannedPosition)>.001f || Quaternion.Angle(item.transform.localRotation,Quaternion.Euler(item.plannedEuler))>.01f || Vector3.Distance(item.transform.localScale,item.plannedScale)>.001f))
                        result.Add(new CityDiagnostic("CITY_OUTPUT_TRANSFORM",item.key,"Generated placement was edited without an override."));
                }
                foreach(var state in d.overrides)
                    if(state.state!=CityOwnership.Detached && !generated.ContainsKey(state.key)) result.Add(new CityDiagnostic("CITY_MISSING_PIN",state.key,"Pinned/overridden object is missing. Restore, remap or release its record."));
            }
            catch(ArgumentException e) { result.Add(new CityDiagnostic("CITY_OUTPUT_ID",d.id,e.Message)); }
            return result;
        }
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            if(report==null) return;
            var districts=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CityDistrict>(true)).ToArray();
            var identities=new HashSet<string>();
            foreach(var d in districts)
            {
                if(!identities.Add(d.id)) throw new BuildFailedException("CITY_DUPLICATE_DISTRICT: "+d.id);
                var errors=Validate(d).Where(r=>r.severity==CitySeverity.Error).ToArray();
                if(errors.Length>0) throw new BuildFailedException(string.Join("\n",errors.Select(e=>e.ToString())));
                UnityEngine.Object.DestroyImmediate(d);
            }
        }
    }
}
