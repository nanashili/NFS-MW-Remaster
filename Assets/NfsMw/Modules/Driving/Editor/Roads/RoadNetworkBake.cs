using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RoadNetworkBake
    {
        internal static RoadLaneConnection[] MatchingConnections(RoadNetworkAuthoring source)
        {
            ValidateSource(source);
            var lanes = new List<RoadBakedLane>();
            foreach (var road in source.Roads)
                using (var build = RoadMeshBuilder.Build(road))
                    for (int band = 0; band < road.Bands.Length; band++)
                        if (road.Profile.bands[band].direction != RoadTravelDirection.None) lanes.Add(BakeLane(source, road, build, band));
            var starts = new Dictionary<Vector3Int, List<RoadBakedLane>>();
            foreach (var lane in lanes)
            {
                var cell = Vector3Int.FloorToInt(lane.Sample(0).position / 0.05f);
                if (!starts.TryGetValue(cell, out var bucket)) starts.Add(cell, bucket = new List<RoadBakedLane>());
                bucket.Add(lane);
            }
            var matches = new List<RoadLaneConnection>();
            foreach (var lane in lanes)
            {
                var end = lane.Sample(lane.Length); var cell = Vector3Int.FloorToInt(end.position / 0.05f);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (starts.TryGetValue(cell + new Vector3Int(x, y, z), out var bucket))
                        foreach (var next in bucket) if (Continuous(end, next.Sample(0)))
                            matches.Add(new RoadLaneConnection { from = lane.Id, to = next.Id });
            }
            return matches.OrderBy(link => link.from.ToString(), StringComparer.Ordinal).ThenBy(link => link.to.ToString(), StringComparer.Ordinal).ToArray();
        }

        public static bool IsCurrent(RoadNetworkAuthoring source)
        {
            if (source == null || source.Baked == null || source.Baked.SchemaVersion != RoadNetworkAsset.CurrentSchema) return false;
            try { ValidateSource(source); return source.Baked.Fingerprint == RoadFingerprint.Compute(source); }
            catch (ArgumentException) { return false; }
        }

        public static RoadNetworkAsset Publish(RoadNetworkAuthoring source, string assetPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("ROAD_PLAY: exit Play mode before baking.");
            ValidateSource(source);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.Ordinal)
                || assetPath.Contains("..") || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(assetPath)))
                throw new ArgumentException("ROAD_PATH: choose an .asset path inside an existing Assets folder.");
            var old = source.GetComponentsInChildren<RoadGeneratedNetwork>(true).Where(root => root.Asset != null && root.Asset.NetworkId == source.Id).ToArray();
            foreach (var root in old) ValidateOwnership(root);
            var builds = new List<RoadMeshBuild>(); var meshes = new List<Mesh>();
            RoadNetworkAsset asset = null; GameObject staged = null; string createdPath = null; int undoGroup = -1;
            try
            {
                var chunks = new List<RoadBakedChunk>(); var lanes = new List<RoadBakedLane>();
                foreach (var road in source.Roads.OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
                {
                    var build = RoadMeshBuilder.Build(road); builds.Add(build);
                    foreach (var chunk in build.Chunks)
                    {
                        var band = road.Profile.bands[chunk.BandIndex];
                        if (band.material == null || band.collision && band.surface == null) throw new ArgumentException("ROAD_SURFACE: assign a material and a collision surface profile to every physical band.");
                        var mesh = UnityEngine.Object.Instantiate(chunk.Mesh); meshes.Add(mesh);
                        chunks.Add(new RoadBakedChunk(road.Id, road.Bands[chunk.BandIndex].id, mesh, chunk.Origin, band.material, band.surface, band.collision));
                    }
                    for (int band = 0; band < road.Bands.Length; band++)
                        if (road.Profile.bands[band].direction != RoadTravelDirection.None) lanes.Add(BakeLane(source, road, build, band));
                }
                lanes.Sort((a, b) => string.Compare(a.Id.ToString(), b.Id.ToString(), StringComparison.Ordinal));
                ValidateConnections(lanes);
                string fingerprint = RoadFingerprint.Compute(source);
                asset = ScriptableObject.CreateInstance<RoadNetworkAsset>(); asset.name = "Road Network " + source.Id;
                asset.Initialize(source.Id, fingerprint, lanes.ToArray(), chunks.ToArray(), source.Baked != null ? source.Baked.NextCompatibilityId : 0);
                RoadNetwork.ValidatePublication(asset);
                staged = CreateGeometry(source, asset); staged.SetActive(false);
                createdPath = AssetDatabase.GenerateUniqueAssetPath(assetPath); AssetDatabase.CreateAsset(asset, createdPath);
                foreach (var mesh in meshes) AssetDatabase.AddObjectToAsset(mesh, asset);
                EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Publish road network");
                undoGroup = group;
                Undo.RegisterCreatedObjectUndo(staged, "Publish road geometry"); Undo.RecordObject(source, "Publish road data");
                var service = source.GetComponent<RoadNetwork>(); if (service == null) service = Undo.AddComponent<RoadNetwork>(source.gameObject);
                Undo.RecordObject(service, "Publish runtime network"); service.ConfigureBaked(asset); EditorUtility.SetDirty(service);
                source.SetPublication(asset); staged.SetActive(true);
                foreach (var root in old) Undo.DestroyObjectImmediate(root.gameObject);
                RoadAuthoringCommands.Changed(source); Undo.CollapseUndoOperations(group);
                foreach (var road in source.Roads) RoadPreview.Remove(road);
                return asset;
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                if (staged != null) UnityEngine.Object.DestroyImmediate(staged);
                if (createdPath != null) AssetDatabase.DeleteAsset(createdPath);
                foreach (var mesh in meshes) if (mesh != null && !AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
                if (asset != null && !AssetDatabase.Contains(asset)) UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
            finally { foreach (var build in builds) build.Dispose(); }
        }

        private static RoadBakedLane BakeLane(RoadNetworkAuthoring source, RoadAuthoring road, RoadMeshBuild build, int bandIndex)
        {
            var band = road.Profile.bands[bandIndex]; var id = road.Bands[bandIndex].id;
            var stations = new SortedSet<float>();
            foreach (var chunk in build.Chunks) if (chunk.BandIndex == bandIndex) foreach (float s in chunk.Stations) stations.Add(s);
            var ordered = stations.ToList(); if (band.direction == RoadTravelDirection.Reverse) ordered.Reverse();
            var samples = new RoadLaneSample[ordered.Count]; float distance = 0;
            float height = 0, shapeSlope = 0;
            for (int i = 1; i < band.shape.Length; i++)
            {
                var a = band.shape[i - 1]; var b = band.shape[i];
                if (a.fraction <= 0.5f && b.fraction >= 0.5f && b.fraction > a.fraction)
                {
                    shapeSlope = (b.height - a.height) / (b.fraction - a.fraction);
                    height = Mathf.Lerp(a.height, b.height, (0.5f - a.fraction) / (b.fraction - a.fraction)); break;
                }
            }
            for (int i = 0; i < samples.Length; i++)
            {
                float s = ordered[i]; var frame = build.Geometry.Sample(s);
                var position = RoadMeshBuilder.BandPoint(road, build.Geometry, bandIndex, s, 0.5f, height);
                if (i > 0)
                {
                    float segment = Vector3.Distance(samples[i - 1].position, position);
                    if (segment < 0.00001f) throw new ArgumentException("ROAD_LANE: coincident lane samples.");
                    distance += segment;
                }
                float direction = band.direction == RoadTravelDirection.Reverse ? -1 : 1;
                samples[i] = new RoadLaneSample { station = s, distance = distance, width = RoadMeshBuilder.Width(road, bandIndex, s),
                    position = position, forward = frame.Forward * direction, left = frame.Left * direction, up = frame.Up };
            }
            // Lane tangents include lateral width/bank changes, rather than copying the reference tangent.
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i].forward = (samples[Mathf.Min(i + 1, samples.Length - 1)].position - samples[Mathf.Max(0, i - 1)].position).normalized;
                float s = samples[i].station;
                var frame = build.Geometry.Sample(s);
                var across = -frame.Left * samples[i].width + frame.Up * (band.crossfall * samples[i].width + shapeSlope);
                if (band.direction == RoadTravelDirection.Reverse) across = -across;
                samples[i].up = Vector3.Cross(samples[i].forward, across).normalized;
                samples[i].left = Vector3.Cross(samples[i].forward, samples[i].up).normalized;
            }
            int compatibilityId = -1;
            if (source.Baked != null && source.Baked.SchemaVersion == RoadNetworkAsset.CurrentSchema)
                foreach (var old in source.Baked.Lanes) if (old.Id == id) { compatibilityId = old.CompatibilityId; break; }
            return new RoadBakedLane(id, road.Id, road.Profile.roadClass, road.Profile.speedLimit, samples,
                source.connections.Where(link => link.from == id).Select(link => link.to).OrderBy(value => value.ToString(), StringComparer.Ordinal).ToArray(), band.surface, compatibilityId);
        }

        private static GameObject CreateGeometry(RoadNetworkAuthoring source, RoadNetworkAsset asset)
        {
            var root = new GameObject("Baked Roads"); root.SetActive(false); root.transform.SetParent(source.transform, false);
            root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity;
            try
            {
                root.AddComponent<RoadGeneratedNetwork>().Initialize(asset);
                for (int i = 0; i < asset.Chunks.Count; i++)
                {
                    var chunk = asset.Chunks[i]; var child = new GameObject("Road chunk " + i); child.transform.SetParent(root.transform, false);
                    child.transform.position = chunk.Origin; child.AddComponent<RoadGeneratedChunk>().Initialize(asset, i);
                    child.AddComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = chunk.Material;
                    if (chunk.Collision) { child.AddComponent<MeshCollider>().sharedMesh = chunk.Mesh; child.AddComponent<VehicleSurface>().SetProfile(chunk.Surface); }
                }
                return root;
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }

        private static void ValidateSource(RoadNetworkAuthoring source)
        {
            if (source == null || !source.Id.IsValid || source.Roads == null || source.Roads.Length == 0 || source.connections == null) throw new ArgumentException("ROAD_NETWORK: missing source identity or roads.");
            var ids = new HashSet<RoadId> { source.Id }; var laneIds = new HashSet<RoadId>();
            foreach (var road in source.Roads)
            {
                RoadGeometry.Validate(road);
                RoadMeshBuilder.ValidateBands(road);
                if (!ids.Add(road.Id) || road.gameObject.scene != source.gameObject.scene || road.GetComponentInParent<RoadNetworkAuthoring>() != source)
                    throw new ArgumentException("ROAD_OWNER: each road must have a unique identity and belong to this network in the same scene.");
                foreach (var band in road.Bands) if (!ids.Add(band.id)) throw new ArgumentException("ROAD_DUPLICATE_ID: duplicated road or lane; create a fresh road with the road tool to assign new identities.");
                for (int i = 0; i < road.Bands.Length; i++) if (road.Profile.bands[i].direction != RoadTravelDirection.None) laneIds.Add(road.Bands[i].id);
            }
            var links = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in source.connections)
                if (link == null || !laneIds.Contains(link.from) || !laneIds.Contains(link.to) || !links.Add(link.from + ":" + link.to))
                    throw new ArgumentException("ROAD_CONNECTION: missing lane or duplicate connection.");
        }

        private static void ValidateConnections(List<RoadBakedLane> lanes)
        {
            var byId = lanes.ToDictionary(lane => lane.Id);
            foreach (var lane in lanes) foreach (var nextId in lane.Successors)
            {
                var next = byId[nextId]; var a = lane.Sample(lane.Length); var b = next.Sample(0);
                if (!Continuous(a, b))
                    throw new ArgumentException("ROAD_CONNECTION_GEOMETRY: connected lane ends must meet within 5 cm with compatible width and direction; author a transition road for a turn.");
            }
        }

        private static bool Continuous(RoadLaneSample a, RoadLaneSample b) =>
            Vector3.Distance(a.position, b.position) <= 0.05f && Vector3.Dot(a.forward, b.forward) >= 0.95f && Mathf.Abs(a.width - b.width) <= 0.05f;

        private static void ValidateOwnership(RoadGeneratedNetwork root)
        {
            foreach (var component in root.GetComponents<Component>())
                if (!(component is Transform) && !(component is RoadGeneratedNetwork)) throw new ArgumentException("ROAD_OWNERSHIP: generated root has user components; move them before rebuilding.");
            foreach (Transform child in root.transform)
            {
                var marker = child.GetComponent<RoadGeneratedChunk>();
                if (marker == null || marker.Asset != root.Asset || child.childCount != 0) throw new ArgumentException("ROAD_OWNERSHIP: generated output contains user children; move them before rebuilding.");
                foreach (var component in child.GetComponents<Component>())
                    if (!(component is Transform) && !(component is RoadGeneratedChunk) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is MeshCollider) && !(component is VehicleSurface))
                        throw new ArgumentException("ROAD_OWNERSHIP: generated chunk has user components; move them before rebuilding.");
            }
        }
    }
}
