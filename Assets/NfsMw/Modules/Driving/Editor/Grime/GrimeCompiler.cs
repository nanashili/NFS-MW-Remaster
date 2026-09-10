using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
namespace NfsMwRemaster.Driving.Editor
{
    public sealed class GrimeBuild : IDisposable
    {
        public readonly List<GrimeChunk> chunks = new List<GrimeChunk>();
        public readonly List<string> diagnostics = new List<string>();
        public string fingerprint;
        public int stamps, rejected, vertices;
        public double milliseconds;
        public bool valid = true;
        public void Dispose() { foreach (var c in chunks) { if(c.mesh && !AssetDatabase.Contains(c.mesh)) UnityEngine.Object.DestroyImmediate(c.mesh); if(c.material && !AssetDatabase.Contains(c.material)) UnityEngine.Object.DestroyImmediate(c.material); } chunks.Clear(); }
    }
    public static class GrimeCompiler
    {
        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> triangles = new List<int>();
            public readonly HashSet<string> strokes = new HashSet<string>();
            public string layer; public GrimeBrush brush; public int order;
        }
        public static GrimeBuild Build(GrimeCanvas canvas, Func<float,bool> cancel = null)
        {
            var result = new GrimeBuild(); var watch = System.Diagnostics.Stopwatch.StartNew();
            var batches = new Dictionary<string, Batch>();
            try
            {
                if (!canvas || string.IsNullOrEmpty(canvas.id) || !float.IsFinite(canvas.maximumSlope) || canvas.maximumSlope<0 || canvas.maximumSlope>180 || canvas.schema != 1 || !float.IsFinite(canvas.chunkSize) || canvas.chunkSize < 5 || canvas.maximumStamps < 1 || canvas.maximumStamps > 50000 || canvas.maximumDraws < 1 || canvas.maximumDraws > 1024) throw new InvalidOperationException("Invalid canvas schema or budgets.");
                if(canvas.transform.lossyScale!=Vector3.one) throw new InvalidOperationException("Dressing canvas requires unit world scale; scale receivers instead.");
                if (canvas.layers == null || canvas.layers.Count>100 || canvas.strokes == null || canvas.masks == null || canvas.layers.Any(l=>l==null || string.IsNullOrEmpty(l.id) || !float.IsFinite(l.opacity) || l.opacity<0 || l.opacity>1) || canvas.layers.Select(l=>l.id).Distinct().Count()!=canvas.layers.Count) throw new InvalidOperationException("Invalid or duplicate layer IDs.");
                if (canvas.strokes.Any(s=>s==null || string.IsNullOrEmpty(s.id)) || canvas.strokes.Select(s=>s.id).Distinct().Count()!=canvas.strokes.Count) throw new InvalidOperationException("Invalid or duplicate stroke IDs.");
                foreach (var mask in canvas.masks) if(mask == null || string.IsNullOrEmpty(mask.id) || (!string.IsNullOrEmpty(mask.layerId)&&!canvas.layers.Any(l=>l.id==mask.layerId)) || mask.polygon==null || mask.polygon.Count<3 || mask.polygon.Count>256 || !GrimeGeometry.Finite(mask.origin) || !GrimeGeometry.Finite(mask.euler) || !float.IsFinite(mask.depth) || mask.depth<=0 || mask.polygon.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y))) throw new InvalidOperationException("Mask needs a finite polygon and positive depth.");
                if(canvas.masks.Where(m=>m.inclusion).Any(m=>!Convex(m.polygon))) throw new InvalidOperationException("Inclusion polygons must be convex and nondegenerate.");
                if(UnityEngine.Object.FindObjectsByType<GrimeCanvas>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(other=>other!=canvas && other.id==canvas.id && other.hideFlags==HideFlags.None)) throw new InvalidOperationException("Duplicate canvas identity. Assign fresh source IDs on the copied canvas.");
                var revisions = new System.Text.StringBuilder(GrimeGeometry.Revision + GrimeTransfer.Export(canvas));
                var duplicateReceivers = UnityEngine.Object.FindObjectsByType<GrimeReceiver>(FindObjectsInactive.Include,FindObjectsSortMode.None).GroupBy(r=>r.id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
                var meshData = new Dictionary<GrimeReceiver,(Vector3[],int[])>();
                var meshRevisions = new Dictionary<GrimeReceiver,string>();
                var brushRevisions = new Dictionary<GrimeBrush,string>();
                int processed = 0, attempted = 0;
                foreach (var stroke in canvas.strokes)
                {
                    if (cancel?.Invoke(processed++/(float)Math.Max(1,canvas.strokes.Count)) == true) throw new OperationCanceledException();
                    var layer = canvas.layers.FirstOrDefault(l=>l.id==stroke.layerId);
                    if(layer==null) throw new InvalidOperationException($"Stroke {stroke.id}: missing layer.");
                    if(!stroke.enabled || !layer.enabled) continue;
                    var brush = stroke.brush;
                    if(!brush || string.IsNullOrEmpty(brush.id) || brush.schema!=1 || stroke.samples==null || stroke.samples.Count==0) throw new InvalidOperationException($"Stroke {stroke.id}: missing brush or anchors.");
                    if(brushRevisions.Keys.Any(other=>other!=brush&&other.id==brush.id)) throw new InvalidOperationException("Duplicate brush ID. Give the copied brush a fresh ID.");
                    if(!brushRevisions.TryGetValue(brush,out var br)) brushRevisions[brush]=br=GrimeGeometry.BrushRevision(brush);
                    revisions.Append(br);
                    if(!FiniteColor(brush.tint)||!FiniteColor(brush.emission)) throw new InvalidOperationException("Non-finite brush color.");
                    if(brush.pattern==GrimePattern.Texture && !brush.colorOpacity) throw new InvalidOperationException("Texture brush needs a color/opacity texture.");
                    if(stroke.brushRevision!=br) throw new InvalidOperationException($"Stroke {stroke.id}: brush changed. Review and accept brush revision.");
                    if(!float.IsFinite(stroke.width) || stroke.width<.05f || stroke.width>20 || !float.IsFinite(stroke.rotation) || !float.IsFinite(stroke.opacity) || stroke.opacity<0 || stroke.opacity>1 || !float.IsFinite(brush.aspect) || brush.aspect<.1f || brush.aspect>8 || !float.IsFinite(brush.spacing) || brush.spacing<.05f || brush.spacing>2 || brush.subdivisions<1 || brush.subdivisions>12 || !float.IsFinite(brush.projectionDepth) || brush.projectionDepth<.01f || brush.projectionDepth>2 || brush.fadeEnd<=brush.fadeStart) throw new InvalidOperationException("Brush/stroke dimensions or fade range are invalid.");
                    if(!float.IsFinite(brush.normalBias)||brush.normalBias<.001f||brush.normalBias>.03f||!float.IsFinite(brush.normalTolerance)||brush.normalTolerance<0||brush.normalTolerance>85||!float.IsFinite(brush.sizeJitter)||brush.sizeJitter<0||brush.sizeJitter>.8f||!float.IsFinite(brush.rotationJitter)||brush.rotationJitter<0||brush.rotationJitter>180||!float.IsFinite(brush.fadeStart)||!float.IsFinite(brush.fadeEnd)||brush.fadeStart<1||!float.IsFinite(brush.falloff)||brush.falloff<0||brush.falloff>1||!float.IsFinite(brush.smoothness)||brush.smoothness<0||brush.smoothness>1||!float.IsFinite(brush.metallic)||brush.metallic<0||brush.metallic>1||!float.IsFinite(brush.normalStrength)||brush.normalStrength<0||brush.normalStrength>2||!Enum.IsDefined(typeof(GrimePattern),brush.pattern)||!Enum.IsDefined(typeof(GrimeBlend),brush.blend)) throw new InvalidOperationException("Brush channels or projection settings are invalid.");
                    if(brush.normalMap) { var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(brush.normalMap)) as TextureImporter; if(importer==null || importer.textureType!=TextureImporterType.NormalMap) throw new InvalidOperationException("Normal channel requires a texture imported as Normal map."); }
                    // A path has exactly one receiver. Crossing receiver boundaries requires another stroke.
                    var receiver=stroke.samples[0]?.receiver;
                    if(!GrimeGeometry.ReceiverAllowed(canvas,receiver,out var why)) throw new InvalidOperationException(why);
                    if(duplicateReceivers.Contains(receiver.id)) throw new InvalidOperationException("Duplicate receiver identity. Register distinct IDs before authoring anchors.");
                    if(stroke.samples.Any(a=>a==null || a.receiver!=receiver)) throw new InvalidOperationException("Split strokes at receiver boundaries.");
                    if(!meshRevisions.TryGetValue(receiver,out var mr)) meshRevisions[receiver]=mr=GrimeGeometry.MeshRevision(receiver);
                    revisions.Append(mr).Append(receiver.transform.localToWorldMatrix.ToString("R"));
                    if(!meshData.TryGetValue(receiver,out var data)) meshData[receiver]=data=(receiver.Mesh.vertices,receiver.Mesh.triangles);
                    var points=new List<Vector3>(); var normals=new List<Vector3>(); var tangents=new List<Vector3>();
                    foreach(var anchor in stroke.samples)
                    {
                        if(!GrimeGeometry.Resolve(anchor,out var p,out var n,out var t,out why,mr,data.Item1,data.Item2)) throw new InvalidOperationException($"Stroke {stroke.id}: {why}");
                        if(Vector3.Angle(n,Vector3.up)>canvas.maximumSlope) throw new InvalidOperationException("Receiver slope excluded by canvas.");
                        points.Add(p); normals.Add(n); tangents.Add(t);
                    }
                    var locations=GrimeGeometry.Resample(points,Mathf.Max(.01f,stroke.width*brush.spacing));
                    int segment=0; float pathStation=0; float step=Mathf.Max(.01f,stroke.width*brush.spacing);
                    for(int k=0;k<locations.Count;k++)
                    {
                        if(++attempted>canvas.maximumStamps) throw new InvalidOperationException("Stamp budget exceeded. Split the canvas or reduce density.");
                        if(k%128==0 && cancel?.Invoke(processed/(float)Math.Max(1,canvas.strokes.Count))==true) throw new OperationCanceledException();
                        float station=k*step;
                        while(segment+1<points.Count-1 && pathStation+Vector3.Distance(points[segment],points[segment+1])<station) { pathStation+=Vector3.Distance(points[segment],points[segment+1]); segment++; }
                        float u=segment+1<points.Count ? Mathf.InverseLerp(0,Vector3.Distance(points[segment],points[segment+1]),station-pathStation) : 0;
                        Vector3 normal=Vector3.Slerp(normals[segment],normals[Math.Min(segment+1,normals.Count-1)],u).normalized;
                        Vector3 tangent=points.Count>1 ? Vector3.ProjectOnPlane(points[Math.Min(segment+1,points.Count-1)]-points[segment],normal).normalized : tangents[0];
                        if(tangent.sqrMagnitude<.1f) tangent=tangents[segment];
                        float size=stroke.width*Mathf.Lerp(1-brush.sizeJitter,1+brush.sizeJitter,GrimeGeometry.Noise(stroke.seed,stroke.id,k,0));
                        tangent=Quaternion.AngleAxis(stroke.rotation+Mathf.Lerp(-brush.rotationJitter,brush.rotationJitter,GrimeGeometry.Noise(stroke.seed,stroke.id,k,1)),normal)*tangent;
                        var right=Vector3.Cross(normal,tangent).normalized; var center=locations[k]; int n=brush.subdivisions;
                        var vs=new List<Vector3>(); var ns=new List<Vector3>(); bool ok=true;
                        for(int y=0;y<=n&&ok;y++) for(int x=0;x<=n;x++)
                        {
                            var origin=center+right*((x/(float)n-.5f)*size)+tangent*((y/(float)n-.5f)*size*brush.aspect);
                            if(!receiver.surface.Raycast(new Ray(origin+normal*brush.projectionDepth, -normal),out var hit,brush.projectionDepth*2) || Vector3.Angle(normal,hit.normal)>brush.normalTolerance) { ok=false; break; }
                            vs.Add(hit.point); ns.Add(hit.normal);
                        }
                        if(ok && !MasksAllow(canvas,stroke.layerId,vs)) ok=false;
                        if(!ok) { result.rejected++; continue; }
                        string key=stroke.layerId+"/"+brush.id+"/"+GrimeGeometry.Reference(brush)+"/"+Mathf.FloorToInt(center.x/canvas.chunkSize)+","+Mathf.FloorToInt(center.y/canvas.chunkSize)+","+Mathf.FloorToInt(center.z/canvas.chunkSize);
                        if(!batches.TryGetValue(key,out var batch)) batches[key]=batch=new Batch{layer=layer.id,brush=brush,order=canvas.layers.IndexOf(layer)};
                        if(batch.vertices.Count+vs.Count>60000) throw new InvalidOperationException("Chunk vertex budget exceeded. Reduce chunk size or density.");
                        if(batches.Count>canvas.maximumDraws) throw new InvalidOperationException("Draw budget exceeded. Increase chunk size or reduce brush/layer combinations.");
                        int offset=batch.vertices.Count; batch.strokes.Add(stroke.id);
                        for(int j=0;j<vs.Count;j++) { batch.vertices.Add(vs[j]+ns[j]*brush.normalBias); batch.normals.Add(ns[j]); batch.uv.Add(new Vector2((j%(n+1))/(float)n,(j/(n+1))/(float)n)); batch.colors.Add(new Color(1,1,1,stroke.opacity*layer.opacity)); }
                        for(int y=0;y<n;y++) for(int x=0;x<n;x++) { int a=offset+y*(n+1)+x,b=a+1,c=a+n+1,d=c+1; batch.triangles.AddRange(new[]{a,c,b,b,c,d}); }
                        result.stamps++;
                    }
                }
                var materials=new Dictionary<string,Material>();
                foreach(var entry in batches.OrderBy(p=>p.Key,StringComparer.Ordinal))
                {
                    var b=entry.Value; string materialKey=b.layer+GrimeGeometry.Reference(b.brush);
                    if(!materials.TryGetValue(materialKey,out var material)) materials[materialKey]=material=CreateMaterial(b.brush,b.order);
                    var mesh=new Mesh{name="Grime chunk"}; mesh.SetVertices(b.vertices); mesh.SetNormals(b.normals); mesh.SetUVs(0,b.uv); mesh.SetColors(b.colors); mesh.SetTriangles(b.triangles,0); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    result.vertices+=mesh.vertexCount; result.chunks.Add(new GrimeChunk{key=entry.Key,layerId=b.layer,strokeIds=b.strokes.OrderBy(s=>s,StringComparer.Ordinal).ToArray(),mesh=mesh,material=material});
                }
                result.fingerprint=Hash128.Compute(revisions.ToString()).ToString();
                if(result.rejected>0) result.diagnostics.Add($"{result.rejected} footprints rejected by depth, angle, boundary or protection masks. Review coverage before baking.");
            }
            catch(OperationCanceledException) { result.Dispose(); throw; }
            catch(Exception ex) { result.valid=false; result.diagnostics.Add(ex.Message); }
            watch.Stop(); result.milliseconds=watch.Elapsed.TotalMilliseconds; return result;
        }
        // Conservative convex footprint versus mask bounding box: protects even sub-grid thin markings.
        // Inclusion requires all footprint vertices inside; exclusion rejects any overlapping mask bounds.
        public static bool MasksAllow(GrimeCanvas canvas,string layer,IReadOnlyList<Vector3> vertices)
        {
            foreach(var mask in canvas.masks.Where(m=>m.enabled && (string.IsNullOrEmpty(m.layerId)||m.layerId==layer)))
            {
                if(mask.inclusion) { if(vertices.Any(v=>!mask.Contains(v))) return false; }
                else
                {
                    var inv=Quaternion.Inverse(Quaternion.Euler(mask.euler)); var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity); var max=-min;
                    foreach(var v in vertices) { var p=inv*(v-mask.origin); min=Vector3.Min(min,p); max=Vector3.Max(max,p); }
                    float x0=mask.polygon.Min(p=>p.x),x1=mask.polygon.Max(p=>p.x),z0=mask.polygon.Min(p=>p.y),z1=mask.polygon.Max(p=>p.y);
                    if(max.y>=-mask.depth/2 && min.y<=mask.depth/2 && max.x>=x0&&min.x<=x1&&max.z>=z0&&min.z<=z1) return false;
                }
            }
            return true;
        }
        static bool FiniteColor(Color c)=>float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a);
        static bool Convex(List<Vector2> polygon)
        {
            float sign=0;
            for(int i=0;i<polygon.Count;i++) { var a=polygon[(i+1)%polygon.Count]-polygon[i];var b=polygon[(i+2)%polygon.Count]-polygon[(i+1)%polygon.Count];float cross=a.x*b.y-a.y*b.x;if(Mathf.Abs(cross)<.00001f)continue;if(sign!=0&&Mathf.Sign(cross)!=sign)return false;sign=Mathf.Sign(cross); }
            // Reject self intersections even when winding turns have the same sign.
            for(int i=0;i<polygon.Count;i++)for(int j=i+2;j<polygon.Count;j++){if(i==0&&j==polygon.Count-1)continue;var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var c=polygon[j];var d=polygon[(j+1)%polygon.Count];float Cross(Vector2 p,Vector2 q)=>p.x*q.y-p.y*q.x;if(Cross(b-a,c-a)*Cross(b-a,d-a)<=0&&Cross(d-c,a-c)*Cross(d-c,b-c)<=0)return false;}
            return sign!=0;
        }
        public static Material CreateMaterial(GrimeBrush b,int order)
        {
            var shader=Shader.Find(b.blend==GrimeBlend.Multiply?"NFS/Authoring Grime Multiply":"NFS/Authoring Grime"); if(!shader) throw new InvalidOperationException("Grime shader missing.");
            var m=new Material(shader){name=b.label,renderQueue=3000+Math.Min(order,99)};
            m.SetTexture("_BaseMap",b.colorOpacity); m.SetTexture("_BumpMap",b.normalMap); m.SetColor("_BaseColor",b.tint); m.SetColor("_EmissionColor",b.emission);
            m.SetFloat("_Smoothness",b.smoothness); m.SetFloat("_Metallic",b.metallic); m.SetFloat("_NormalStrength",b.normalMap?b.normalStrength:0); m.SetFloat("_Falloff",b.falloff); m.SetFloat("_Pattern",b.colorOpacity?0:(int)b.pattern); m.SetFloat("_FadeStart",b.fadeStart); m.SetFloat("_FadeEnd",b.fadeEnd);
            if(b.blend!=GrimeBlend.Multiply)m.SetFloat("_EnableBlendModePreserveSpecularLighting",0);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
            if(b.blend==GrimeBlend.Multiply) Rendering.GrimeMultiplyGUI.Configure(m);
            m.renderQueue=3000+Math.Min(order,99); return m;
        }
    }
}
