using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace NfsMwRemaster.Lighting.Editor
{
    public static class LightingAudit
    {
        public static string Capabilities()
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if(!pipeline)return "Unsupported active pipeline. Select an HDRP quality level.";
            var settings=pipeline.currentPlatformRenderPipelineSettings;
            return $"Unity {Application.unityVersion} • HDRP • {QualitySettings.activeColorSpace}\n"+
                $"Quality: {QualitySettings.names[QualitySettings.GetQualityLevel()]} / {pipeline.name}\n"+
                $"SSR: {settings.supportSSR} • Volumetrics: {settings.supportVolumetrics} • SSGI: {settings.supportSSGI}\n"+
                "Physical sunlight (lux), EV100 exposure, HDRP skies, height/volumetric fog and staged reflection cubemaps.\n"+
                "APV needs a baked lighting set. Windows ray tracing is optional; Metal uses raster rendering.\n"+
                "Use Screen Space Overlay for HUD. Layers 28–31 are reserved for camera atmospheres; 27 for Studio preview.";
        }
        public static string Fingerprint(AtmosphereController owner)
        {
            var text=new StringBuilder("lighting-v1\n");text.Append(owner.fallback?owner.fallback.Revision:"missing").Append(owner.quality);
            foreach(var zone in owner.zones.OrderBy(z=>z?z.id:"",StringComparer.Ordinal))
                if(zone)text.Append(zone.id).Append(zone.cellId).Append(zone.priority).Append(zone.size.ToString("R",CultureInfo.InvariantCulture)).Append(zone.blendMeters.ToString("R",CultureInfo.InvariantCulture)).Append(zone.categories).Append(zone.overrideExposure).Append(zone.exposureOverride.ToString("R",CultureInfo.InvariantCulture)).Append(zone.transform.localToWorldMatrix.ToString("R",CultureInfo.InvariantCulture)).Append(zone.profile?zone.profile.Revision:"missing");else text.Append("missing-zone");
            foreach(var renderer in owner.bakeGeometry.OrderBy(r=>r?GlobalObjectId.GetGlobalObjectIdSlow(r).ToString():"",StringComparer.Ordinal))
            {
                if(!renderer){text.Append("missing-geometry");continue;}
                text.Append(GlobalObjectId.GetGlobalObjectIdSlow(renderer)).Append(renderer.transform.localToWorldMatrix.ToString("R",CultureInfo.InvariantCulture)).Append(renderer.enabled).Append(renderer.gameObject.activeInHierarchy);
                var filter=renderer.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh)AppendAsset(text,filter.sharedMesh);
                foreach(var material in renderer.sharedMaterials)AppendAsset(text,material);
            }
            if(owner.keyLight)AppendLight(text,owner.keyLight);
            foreach(var fixture in owner.fixtures.OrderBy(f=>f?f.id:"",StringComparer.Ordinal))if(fixture){text.Append(fixture.id).Append(fixture.role).Append(fixture.upstreamId).Append(fixture.circuit);if(fixture.source)AppendLight(text,fixture.source);}
            foreach(var probe in owner.probes.OrderBy(p=>p?p.id:"",StringComparer.Ordinal))if(probe&&probe.probe){text.Append(probe.id).Append(probe.probe.transform.localToWorldMatrix.ToString("R",CultureInfo.InvariantCulture)).Append(probe.probe.size.ToString("R",CultureInfo.InvariantCulture)).Append(probe.probe.center.ToString("R",CultureInfo.InvariantCulture)).Append(probe.probe.cullingMask);}
            var pipeline=GraphicsSettings.currentRenderPipeline;if(pipeline)AppendAsset(text,pipeline);
            text.Append(QualitySettings.activeColorSpace).Append(Application.unityVersion);return Hash128.Compute(text.ToString()).ToString();
        }
        static void AppendLight(StringBuilder text,Light light)
        {text.Append(light.transform.localToWorldMatrix.ToString("R",CultureInfo.InvariantCulture)).Append(light.color.ToString("R",CultureInfo.InvariantCulture)).Append(light.intensity.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(light.range.ToString("R",CultureInfo.InvariantCulture)).Append(light.spotAngle.ToString("R",CultureInfo.InvariantCulture)).Append(light.innerSpotAngle.ToString("R",CultureInfo.InvariantCulture)).Append(light.type).Append(light.enabled).Append(light.gameObject.activeInHierarchy).Append(light.shadows).Append(light.shadowStrength.ToString("R",CultureInfo.InvariantCulture)).Append(light.lightmapBakeType).Append(light.bounceIntensity.ToString("R",CultureInfo.InvariantCulture)).Append(light.cullingMask);if(light.cookie)AppendAsset(text,light.cookie);}
        static void AppendAsset(StringBuilder text,UnityEngine.Object asset)
        {if(!asset){text.Append("missing-asset");return;}string path=AssetDatabase.GetAssetPath(asset);text.Append(AssetDatabase.AssetPathToGUID(path)).Append(AssetDatabase.GetAssetDependencyHash(path));if(asset is Material material)text.Append(EditorJsonUtility.ToJson(material));}
        public static List<string> Validate(AtmosphereController owner,Vector3 point)
        {
            var issues=new List<string>();if(!owner){issues.Add("ERROR: Select a controller.");return issues;}
            if(!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset))issues.Add("ERROR: Active quality must use HDRP.");
            if(!owner.fallback||!owner.fallback.IsValid)issues.Add("ERROR: Missing/invalid fallback profile or unsupported schema.");
            if(!owner.worldCamera)issues.Add("ERROR: Assign a world camera explicitly.");else
            {var data=owner.worldCamera.GetComponent<HDAdditionalCameraData>();if(!data)issues.Add("ERROR: World camera needs HDRP camera data.");}
            var owners=UnityEngine.Object.FindObjectsByType<AtmosphereController>();if(owners.Any(o=>o!=owner&&o.worldCamera==owner.worldCamera&&o.enabled))issues.Add("ERROR: Multiple owners target the same camera.");
            CheckIds(owner.zones.Select(z=>z?z.id:null),"zone",issues);CheckIds(owner.fixtures.Select(f=>f?f.id:null),"fixture",issues);CheckIds(owner.probes.Select(p=>p?p.id:null),"probe",issues);
            foreach(var z in owner.zones)if(z){if(!z.profile||!z.profile.IsValid)issues.Add("ERROR: Invalid profile on "+z.name);if(z.size.x<=0||z.size.y<=0||z.size.z<=0)issues.Add("ERROR: Zone size must be positive: "+z.name);if(string.IsNullOrWhiteSpace(z.cellId))issues.Add("REVIEW: Zone has no streaming cell ID: "+z.name);}
            if(owner.fallback)
            {if(Mathf.Abs(owner.fallback.look.exposure)>3)issues.Add("REVIEW: Extreme manual exposure; review tunnel exit and signals.");if(!string.IsNullOrEmpty(owner.fallback.bakedScenarioId))issues.Add((owner.fallback.allowRealtimeFallback?"REVIEW: ":"ERROR: ")+"Baked scenario switching has no installed adapter. Only realtime look/fallback is supported.");}
            var lights=owner.fixtures.Where(f=>f&&f.source&&f.source.enabled).Select(f=>f.source).Distinct().ToArray();int realtime=lights.Count(l=>l.lightmapBakeType!=LightmapBakeType.Baked),shadows=lights.Count(l=>l.lightmapBakeType!=LightmapBakeType.Baked&&l.shadows!=LightShadows.None);
            if(owner.fallback&&(realtime>owner.fallback.realtimeBudget||shadows>owner.fallback.shadowBudget))issues.Add($"REVIEW: Fixture budget exceeded: {realtime} realtime/mixed, {shadows} shadowed (design budgets, not measured cost).");
            int overlap=lights.Count(l=>l.type==LightType.Directional||Vector3.Distance(l.transform.position,point)<l.range);issues.Add($"INFO: {overlap} light influence spheres contain inspection point; spot cone/occlusion not evaluated.");
            foreach(var f in owner.fixtures)if(f){if(!f.source&&!f.emissiveVisual)issues.Add("ERROR: Missing fixture references: "+f.name);if(string.IsNullOrWhiteSpace(f.upstreamId))issues.Add("REVIEW: Explicit upstream fixture ID missing: "+f.name);if(f.Critical)issues.Add("INFO: Critical fixture protected from quality edits: "+f.name);}
            if(owner.bakeGeometry.Length==0||owner.bakeGeometry.Any(r=>!r))issues.Add("ERROR: Bake/preview geometry must be explicitly assigned and complete.");
            if(owner.bakeGeometry.Any(r=>r&&!(r is MeshRenderer)))issues.Add("REVIEW: Only static MeshRenderers are copied to isolated preview. Skinned vehicles require an approved static swatch.");
            if(owner.probes.Length==0)issues.Add("REVIEW: No reflection probe bindings.");
            foreach(var p in owner.probes)if(p&&!p.probe)issues.Add("ERROR: Missing reflection probe: "+p.name);
            bool covered=owner.probes.Any(p=>p&&p.probe&&new Bounds(p.probe.transform.position+p.probe.center,p.probe.size).Contains(point));if(!covered)issues.Add("REVIEW: Inspection point lies outside registered reflection bounds.");
            if(owner.approvedBake&&owner.approvedBake.sourceFingerprint!=Fingerprint(owner))issues.Add("ERROR: Approved reflections are stale. Stage a replacement; previous textures remain assigned.");
            if(!LightmapSettings.lightProbes||LightmapSettings.lightProbes.count==0)issues.Add("REVIEW: No baked indirect probes available; reflection captures do not bake vehicle indirect light.");
            issues.Add("REVIEW: Sky, exposure mode and tonemapping switch at 50% blend. Keep modes equal across adjacent profiles. HDRP fog uses extinction distance; legacy fog modes are density hints.");
            return issues;
        }
        static void CheckIds(IEnumerable<string> ids,string kind,List<string> issues){var seen=new HashSet<string>();foreach(var id in ids)if(string.IsNullOrWhiteSpace(id)||!seen.Add(id))issues.Add("ERROR: Missing/duplicate "+kind+" identity. Use deliberate New ID on the selected binding.");}
    }
}
