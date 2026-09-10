using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Editor
{
    public static class LightingCommands
    {
        public static void Edit(Object target,string label,Action action){Undo.RecordObject(target,label);action();EditorUtility.SetDirty(target);if(target is Component c){PrefabUtility.RecordPrefabInstancePropertyModifications(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);}}
        public static AtmosphereProfile CreateProfile(string path,AtmosphereProfile from=null)
        {
            var profile=from?Object.Instantiate(from):ScriptableObject.CreateInstance<AtmosphereProfile>();profile.id=Guid.NewGuid().ToString("N");profile.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(profile,AssetDatabase.GenerateUniqueAssetPath(path));Undo.RegisterCreatedObjectUndo(profile,"Create atmosphere profile");return profile;
        }
        public static AtmosphereController CreateController()
        {var go=new GameObject("Lighting & Atmosphere");Undo.RegisterCreatedObjectUndo(go,"Create atmosphere controller");var c=Undo.AddComponent<AtmosphereController>(go);Selection.activeGameObject=go;return c;}
        public static AtmosphereZone AddZone(AtmosphereController owner,Vector3 position)
        {
            int group=Undo.GetCurrentGroup();var go=new GameObject("District atmosphere");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,owner.gameObject.scene);go.transform.position=position;Undo.RegisterCreatedObjectUndo(go,"Create atmosphere zone");var zone=Undo.AddComponent<AtmosphereZone>(go);zone.profile=owner.fallback;
            Edit(owner,"Register atmosphere zone",()=>owner.zones=owner.zones.Concat(new[]{zone}).ToArray());Undo.CollapseUndoOperations(group);Selection.activeGameObject=go;return zone;
        }
        public static void RegisterSelection(AtmosphereController owner,bool geometry,bool fixtures,bool probes)
        {
            int group=Undo.GetCurrentGroup();var rs=owner.bakeGeometry.Where(r=>r).ToList();var fs=owner.fixtures.Where(f=>f).ToList();var ps=owner.probes.Where(p=>p).ToList();
            foreach(var go in Selection.gameObjects)
            {
                if(go.scene!=owner.gameObject.scene)throw new InvalidOperationException("Select objects in the controller scene only.");
            }
            foreach(var go in Selection.gameObjects)
            {
                if(geometry)rs.AddRange(go.GetComponentsInChildren<MeshRenderer>(true));
                if(fixtures)foreach(var light in go.GetComponentsInChildren<Light>(true))
                {if(light==owner.keyLight)continue;var binding=light.GetComponent<LightingFixture>()??Undo.AddComponent<LightingFixture>(light.gameObject);Edit(binding,"Bind existing light",()=>binding.source=light);fs.Add(binding);}
                if(probes)foreach(var probe in go.GetComponentsInChildren<ReflectionProbe>(true))
                {var binding=probe.GetComponent<LightingProbeBinding>()??Undo.AddComponent<LightingProbeBinding>(probe.gameObject);Edit(binding,"Bind existing probe",()=>binding.probe=probe);ps.Add(binding);}
            }
            Edit(owner,"Register lighting sources",()=>{owner.bakeGeometry=rs.Distinct().ToArray();owner.fixtures=fs.Distinct().ToArray();owner.probes=ps.Distinct().ToArray();});Undo.CollapseUndoOperations(group);
        }
        public static void NewId(Component target)
        {Edit(target,"Remap lighting identity",()=>{string id=Guid.NewGuid().ToString("N");if(target is AtmosphereZone z)z.id=id;else if(target is LightingFixture f)f.id=id;else if(target is LightingProbeBinding p)p.id=id;else if(target is AtmosphereController c)c.id=id;else throw new InvalidOperationException("Select a lighting binding.");});}
        public static int SetCircuit(AtmosphereController owner,string circuit,float intensity,float range,LightShadows shadows)
        {
            if(intensity<0||float.IsNaN(intensity)||float.IsInfinity(intensity)||range<=0||float.IsNaN(range)||float.IsInfinity(range))throw new InvalidOperationException("Intensity and range must be finite and positive.");
            var lights=owner.fixtures.Where(f=>f&&f.source&&!f.Critical&&f.circuit==circuit).Select(f=>f.source).Distinct().ToArray();
            int group=Undo.GetCurrentGroup();foreach(var light in lights)Edit(light,"Set fixture circuit",()=>{light.intensity=intensity;light.range=range;light.shadows=shadows;});Undo.CollapseUndoOperations(group);return lights.Length;
        }
        public static void ApplyDraft(AtmosphereProfile source,AtmosphereProfile draft,string expectedRevision)
        {if(!source||!draft||!draft.IsValid)throw new InvalidOperationException("A valid source and draft are required.");if(source.Revision!=expectedRevision)throw new InvalidOperationException("Source changed since audition started. Reload draft before applying.");string id=source.id,name=source.name;var flags=source.hideFlags;Edit(source,"Apply atmosphere draft",()=>{EditorUtility.CopySerialized(draft,source);source.id=id;source.name=name;source.hideFlags=flags;});AssetDatabase.SaveAssetIfDirty(source);}
        public static void ApplyBake(AtmosphereController owner,LightingBakeSet stage)
        {
            if(!stage||stage.schemaVersion!=1||stage.sourceFingerprint!=LightingAudit.Fingerprint(owner))throw new InvalidOperationException("Stage is missing or stale. Rebuild from current sources.");
            var bindings=owner.probes.Where(p=>p&&p.probe).ToDictionary(p=>p.id,p=>p.probe);
            if(stage.reflections.Length!=bindings.Count||stage.reflections.Any(r=>r==null||!r.texture||!bindings.ContainsKey(r.probeId))||stage.reflections.Select(r=>r.probeId).Distinct().Count()!=bindings.Count)throw new InvalidOperationException("Probe identities changed or staged textures are incomplete. Remap and restage.");
            int group=Undo.GetCurrentGroup();foreach(var entry in stage.reflections){var probe=bindings[entry.probeId];Edit(probe,"Approve reflection capture",()=>{probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=entry.texture;});}
            Edit(owner,"Approve lighting bake",()=>owner.approvedBake=stage);Undo.CollapseUndoOperations(group);
        }
    }
}
