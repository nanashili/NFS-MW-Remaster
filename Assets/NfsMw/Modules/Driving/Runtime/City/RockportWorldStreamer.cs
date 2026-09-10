using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class RockportWorldChunkDefinition
    {
        public int sceneNumber;
        public string scenePath;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;

        public RockportWorldChunkDefinition() { }

        public RockportWorldChunkDefinition(int number, string path, Bounds bounds)
        {
            sceneNumber = number;
            scenePath = path;
            boundsCenter = bounds.center;
            boundsSize = bounds.size;
        }
    }

    /// <summary>
    /// Streams the authored Rockport sections as additive Unity scenes around the
    /// player. Only a bounded number of nearby sections are resident at once.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class RockportWorldStreamer : MonoBehaviour
    {
        [SerializeField] private Transform focus;
        [SerializeField] private RockportWorldChunkDefinition[] chunks = Array.Empty<RockportWorldChunkDefinition>();
        [SerializeField, Min(1f)] private float loadDistance = 384f;
        [SerializeField, Min(1f)] private float unloadDistance = 768f;
        [SerializeField, Min(1)] private int maximumLoadedChunks = 6;
        [SerializeField, Min(0f)] private float lookAheadSeconds = 2.5f;
        [SerializeField, Min(0f)] private float maximumLookAhead = 300f;
        [SerializeField, Min(0f)] private float unusedAssetCleanupDelay = 8f;

        private readonly HashSet<int> loadedChunks = new HashSet<int>();
        private readonly HashSet<int> unavailableChunks = new HashSet<int>();
        private AsyncOperation pendingOperation;
        private int pendingChunk = -1;
        private bool pendingLoad;
        private bool shuttingDown;
        private bool focusWarningShown;
        private Rigidbody focusBody;
        private bool focusBodyWasKinematic;
        private Vector3 focusStartPosition;
        private Quaternion focusStartRotation;
        private bool focusHeld;
        private bool initialLoadComplete;
        private bool relocationPending;
        private Vector3 relocationPosition;
        private AsyncOperation cleanupOperation;
        private float cleanupNotBefore = float.PositiveInfinity;

        public int LoadedChunkCount => loadedChunks.Count;
        public int ChunkCount => chunks?.Length ?? 0;
        public int UnavailableChunkCount => unavailableChunks.Count;
        public int MaximumLoadedChunks => maximumLoadedChunks;
        public IReadOnlyList<RockportWorldChunkDefinition> Chunks => chunks;
        public bool IsStreaming => pendingOperation != null || cleanupOperation != null;
        public bool IsCleaningUnusedAssets => cleanupOperation != null;
        public bool IsInitialLoadComplete => initialLoadComplete;

        public void Configure(Transform target, RockportWorldChunkDefinition[] definitions,
            float nearDistance = 384f, float farDistance = 768f, int maxLoadedChunks = 6,
            float predictionSeconds = 2.5f, float maxPredictionDistance = 300f,
            float cleanupDelay = 8f)
        {
            focus = target;
            chunks = definitions ?? Array.Empty<RockportWorldChunkDefinition>();
            loadDistance = nearDistance;
            unloadDistance = farDistance;
            maximumLoadedChunks = maxLoadedChunks;
            lookAheadSeconds = predictionSeconds;
            maximumLookAhead = maxPredictionDistance;
            unusedAssetCleanupDelay = cleanupDelay;
            initialLoadComplete = false;
            ValidateConfiguration();
        }

        /// <summary>
        /// Starts loading the destination cell before a recovery, event placement,
        /// or other authored relocation releases the vehicle to physics.
        /// </summary>
        public void PrepareForRelocation(Vector3 destination, Quaternion rotation)
        {
            ResolveFocus();
            relocationPending = true;
            relocationPosition = destination;
            initialLoadComplete = false;
            HoldFocus(destination, rotation);
            TickStreaming();
        }

        private void Awake()
        {
            ValidateConfiguration();
            SeedAlreadyLoadedChunks();
            ResolveFocus();
            HoldFocusForInitialLoad();
            TickStreaming();
            TryCompleteInitialLoad();
        }

        private void Update()
        {
            if (shuttingDown) return;
            ResolveFocus();
            TickStreaming();
            TickUnusedAssetCleanup();
            TryCompleteInitialLoad();
        }

        private void OnDestroy()
        {
            shuttingDown = true;

            if (pendingOperation != null)
            {
                AsyncOperation operation = pendingOperation;
                int chunkIndex = pendingChunk;
                string chunkPath = IsValidChunk(chunkIndex) ? chunks[chunkIndex].scenePath : null;
                operation.completed -= OnOperationCompleted;
                if (pendingLoad)
                    operation.completed += _ => UnloadChunkSceneByPath(chunkPath);
                else
                    loadedChunks.Remove(chunkIndex);
                pendingOperation = null;
                pendingChunk = -1;
                pendingLoad = false;
            }

            foreach (int chunkIndex in loadedChunks)
                if (IsValidChunk(chunkIndex)) UnloadChunkSceneByPath(chunks[chunkIndex].scenePath);
            loadedChunks.Clear();
        }

        private void TickStreaming()
        {
            if (focus == null || chunks == null || chunks.Length == 0
                || pendingOperation != null) return;

            List<int> desired = DetermineDesiredChunks();
            for (int i = 0; i < desired.Count; i++)
            {
                int chunkIndex = desired[i];
                if (!loadedChunks.Contains(chunkIndex) && loadedChunks.Count < maximumLoadedChunks)
                {
                    BeginLoad(chunkIndex);
                    return;
                }
            }

            int unloadIndex = FindChunkToUnload(desired);
            if (unloadIndex >= 0) BeginUnload(unloadIndex);
        }

        private List<int> DetermineDesiredChunks()
        {
            var candidates = new List<ChunkDistance>();
            float loadDistanceSquared = loadDistance * loadDistance;
            Vector3 current = relocationPending ? relocationPosition : focus.position;
            Vector3 predicted = PredictedFocusPosition(current);
            for (int i = 0; i < chunks.Length; i++)
            {
                if (!IsValidChunk(i) || unavailableChunks.Contains(i)) continue;
                float distanceSquared = Mathf.Min(
                    DistanceSquared(chunks[i], current),
                    DistanceSquared(chunks[i], predicted));
                if (distanceSquared <= loadDistanceSquared)
                    candidates.Add(new ChunkDistance(i, distanceSquared));
            }

            candidates.Sort((left, right) =>
            {
                int distanceComparison = left.distanceSquared.CompareTo(right.distanceSquared);
                return distanceComparison != 0
                    ? distanceComparison
                    : left.index.CompareTo(right.index);
            });
            var desired = new List<int>(Mathf.Min(maximumLoadedChunks, candidates.Count));
            for (int i = 0; i < candidates.Count && desired.Count < maximumLoadedChunks; i++)
                desired.Add(candidates[i].index);

            // Keep the world alive when the player is between authored chunk
            // bounds or has spawned just outside the configured load radius.
            if (desired.Count == 0)
            {
                int nearest = FindNearestValidChunk(current, predicted);
                if (nearest >= 0) desired.Add(nearest);
            }

            return desired;
        }

        private int FindChunkToUnload(List<int> desired)
        {
            if (loadedChunks.Count == 0) return -1;

            int farthestIndex = -1;
            float farthestDistance = -1f;
            float unloadDistanceSquared = unloadDistance * unloadDistance;
            Vector3 current = relocationPending ? relocationPosition : focus.position;
            Vector3 predicted = PredictedFocusPosition(current);
            bool desiredChunkMissing = false;
            for (int i = 0; i < desired.Count; i++)
                if (!loadedChunks.Contains(desired[i]))
                {
                    desiredChunkMissing = true;
                    break;
                }

            foreach (int chunkIndex in loadedChunks)
            {
                bool isDesired = desired.Contains(chunkIndex);
                float distanceSquared = Mathf.Min(
                    DistanceSquared(chunks[chunkIndex], current),
                    DistanceSquared(chunks[chunkIndex], predicted));
                bool outsideUnloadRadius = distanceSquared > unloadDistanceSquared;
                bool overResidentBudget = !isDesired
                    && (loadedChunks.Count > maximumLoadedChunks || desiredChunkMissing);
                if (!outsideUnloadRadius && !overResidentBudget) continue;
                if (distanceSquared <= farthestDistance) continue;
                farthestIndex = chunkIndex;
                farthestDistance = distanceSquared;
            }

            return farthestIndex;
        }

        private void BeginLoad(int chunkIndex)
        {
            if (!IsValidChunk(chunkIndex) || unavailableChunks.Contains(chunkIndex)) return;
            RockportWorldChunkDefinition definition = chunks[chunkIndex];
            Scene existing = SceneManager.GetSceneByPath(definition.scenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                loadedChunks.Add(chunkIndex);
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(definition.scenePath))
            {
                unavailableChunks.Add(chunkIndex);
                Debug.LogError("Rockport chunk is not enabled in Build Profiles: " + definition.scenePath, this);
                return;
            }

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(definition.scenePath, LoadSceneMode.Additive);
            }
            catch (Exception exception)
            {
                unavailableChunks.Add(chunkIndex);
                Debug.LogError("Rockport chunk failed to start loading: " + definition.scenePath
                    + ". " + exception.Message, this);
                return;
            }

            if (operation == null)
            {
                unavailableChunks.Add(chunkIndex);
                Debug.LogError("Unity did not start Rockport chunk loading: " + definition.scenePath, this);
                return;
            }

            pendingOperation = operation;
            pendingChunk = chunkIndex;
            pendingLoad = true;
            operation.priority = 10;
            operation.completed += OnOperationCompleted;
        }

        private void BeginUnload(int chunkIndex)
        {
            if (!IsValidChunk(chunkIndex))
            {
                loadedChunks.Remove(chunkIndex);
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(chunks[chunkIndex].scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                loadedChunks.Remove(chunkIndex);
                return;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                Debug.LogWarning("Unity did not start Rockport chunk unloading: " + chunks[chunkIndex].scenePath, this);
                loadedChunks.Remove(chunkIndex);
                return;
            }

            pendingOperation = operation;
            pendingChunk = chunkIndex;
            pendingLoad = false;
            operation.priority = 0;
            operation.completed += OnOperationCompleted;
        }

        private void OnOperationCompleted(AsyncOperation operation)
        {
            if (operation != pendingOperation) return;
            int chunkIndex = pendingChunk;
            bool wasLoad = pendingLoad;
            pendingOperation = null;
            pendingChunk = -1;
            pendingLoad = false;

            if (shuttingDown)
            {
                if (wasLoad && IsValidChunk(chunkIndex)) UnloadChunkSceneByPath(chunks[chunkIndex].scenePath);
                return;
            }

            if (wasLoad) loadedChunks.Add(chunkIndex);
            else
            {
                loadedChunks.Remove(chunkIndex);
                cleanupNotBefore = Time.unscaledTime + unusedAssetCleanupDelay;
            }
        }

        private void HoldFocusForInitialLoad()
        {
            if (focusHeld || initialLoadComplete || focus == null) return;
            HoldFocus(focus.position, focus.rotation);
        }

        private void HoldFocus(Vector3 position, Quaternion rotation)
        {
            if (focusHeld)
            {
                focusStartPosition = position;
                focusStartRotation = rotation;
                return;
            }
            focusBody = focus == null ? null : focus.GetComponentInParent<Rigidbody>();
            if (focusBody == null) return;

            focusStartPosition = position;
            focusStartRotation = rotation;
            focusBodyWasKinematic = focusBody.isKinematic;
            if (!focusBodyWasKinematic)
            {
                focusBody.linearVelocity = Vector3.zero;
                focusBody.angularVelocity = Vector3.zero;
            }
            focusBody.isKinematic = true;
            focusHeld = true;
        }

        private void TryCompleteInitialLoad()
        {
            if (initialLoadComplete || focus == null) return;
            HoldFocusForInitialLoad();
            if (!HasResidentDesiredChunk()) return;

            initialLoadComplete = true;
            if (focusHeld)
            {
                focusBody.position = focusStartPosition;
                focusBody.rotation = focusStartRotation;
                focus.SetPositionAndRotation(focusStartPosition, focusStartRotation);
                focusBody.isKinematic = focusBodyWasKinematic;
                if (!focusBody.isKinematic)
                {
                    focusBody.linearVelocity = Vector3.zero;
                    focusBody.angularVelocity = Vector3.zero;
                }
                focusHeld = false;
                relocationPending = false;
                Physics.SyncTransforms();
            }
            else relocationPending = false;
        }

        private bool HasResidentDesiredChunk()
        {
            List<int> desired = DetermineDesiredChunks();
            for (int i = 0; i < desired.Count; i++)
                if (loadedChunks.Contains(desired[i])) return true;
            return false;
        }

        private void SeedAlreadyLoadedChunks()
        {
            loadedChunks.Clear();
            if (chunks == null) return;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (!IsValidChunk(i)) continue;
                Scene scene = SceneManager.GetSceneByPath(chunks[i].scenePath);
                if (scene.IsValid() && scene.isLoaded) loadedChunks.Add(i);
            }
        }

        private void ResolveFocus()
        {
            if (focus != null) return;
            FreeRoamSession session = FindAnyObjectByType<FreeRoamSession>();
            if (session != null)
            {
                focus = session.transform;
                return;
            }

            VehicleController vehicle = FindAnyObjectByType<VehicleController>();
            if (vehicle != null) focus = vehicle.transform;
            if (focus == null && !focusWarningShown)
            {
                focusWarningShown = true;
                Debug.LogWarning("Rockport world streaming is waiting for a player focus transform.", this);
            }
        }

        private int FindNearestValidChunk(Vector3 current, Vector3 predicted)
        {
            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (!IsValidChunk(i) || unavailableChunks.Contains(i)) continue;
                float distanceSquared = Mathf.Min(
                    DistanceSquared(chunks[i], current),
                    DistanceSquared(chunks[i], predicted));
                if (distanceSquared >= nearestDistance) continue;
                nearestIndex = i;
                nearestDistance = distanceSquared;
            }

            return nearestIndex;
        }

        private Vector3 PredictedFocusPosition(Vector3 current)
        {
            if (relocationPending || lookAheadSeconds <= 0f || maximumLookAhead <= 0f) return current;
            Rigidbody body = focusBody;
            if (body == null && focus != null) body = focus.GetComponentInParent<Rigidbody>();
            if (body == null) return current;
            Vector3 offset = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) * lookAheadSeconds;
            return current + Vector3.ClampMagnitude(offset, maximumLookAhead);
        }

        private void TickUnusedAssetCleanup()
        {
            if (cleanupOperation != null)
            {
                if (cleanupOperation.isDone) cleanupOperation = null;
                return;
            }
            if (pendingOperation != null || Time.unscaledTime < cleanupNotBefore) return;
            cleanupNotBefore = float.PositiveInfinity;
            cleanupOperation = Resources.UnloadUnusedAssets();
        }

        private bool IsValidChunk(int index)
        {
            if (chunks == null || index < 0 || index >= chunks.Length) return false;
            RockportWorldChunkDefinition definition = chunks[index];
            return definition != null && !string.IsNullOrWhiteSpace(definition.scenePath)
                && definition.boundsSize.x > 0f && definition.boundsSize.y > 0f && definition.boundsSize.z > 0f;
        }

        private static float DistanceSquared(RockportWorldChunkDefinition definition, Vector3 point)
        {
            var bounds = new Bounds(definition.boundsCenter, definition.boundsSize);
            Vector3 closest = bounds.ClosestPoint(point);
            return (closest - point).sqrMagnitude;
        }

        private void ValidateConfiguration()
        {
            loadDistance = Mathf.Max(1f, loadDistance);
            unloadDistance = Mathf.Max(loadDistance + 1f, unloadDistance);
            maximumLoadedChunks = Mathf.Max(1, maximumLoadedChunks);
            lookAheadSeconds = Mathf.Max(0f, lookAheadSeconds);
            maximumLookAhead = Mathf.Max(0f, maximumLookAhead);
            unusedAssetCleanupDelay = Mathf.Max(0f, unusedAssetCleanupDelay);
            if (chunks == null) chunks = Array.Empty<RockportWorldChunkDefinition>();

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var numbers = new HashSet<int>();
            for (int i = 0; i < chunks.Length; i++)
            {
                RockportWorldChunkDefinition definition = chunks[i];
                if (definition == null)
                {
                    Debug.LogError("Rockport world streaming contains a null chunk definition.", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.scenePath) || !paths.Add(definition.scenePath))
                    Debug.LogError("Rockport world streaming contains a duplicate or empty scene path at index " + i + ".", this);
                if (!numbers.Add(definition.sceneNumber))
                    Debug.LogError("Rockport world streaming contains duplicate scene number " + definition.sceneNumber + ".", this);
                if (definition.boundsSize.x <= 0f || definition.boundsSize.y <= 0f || definition.boundsSize.z <= 0f)
                    Debug.LogError("Rockport world streaming contains a chunk with empty bounds at index " + i + ".", this);
            }
        }

        private static void UnloadChunkSceneByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }

        private readonly struct ChunkDistance
        {
            public readonly int index;
            public readonly float distanceSquared;

            public ChunkDistance(int chunkIndex, float distance)
            {
                index = chunkIndex;
                distanceSquared = distance;
            }
        }
    }
}
