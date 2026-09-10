using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Typed local-world implementation. Persistent world changes belong in the settlement transaction.
    public sealed class MissionWorldActions : MonoBehaviour, IMissionActions
    {
        [Serializable] public sealed class Binding { public string id; public GameObject target; public GameObject prefab; public Transform spawn; }
        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        private readonly Dictionary<string, GameObject> spawned = new Dictionary<string, GameObject>();
        private readonly Dictionary<GameObject, bool> original = new Dictionary<GameObject, bool>();
        private string owner;
        private int attempt;
        private readonly HashSet<string> keep = new HashSet<string>();
        private readonly List<string> remove = new List<string>();
        public void Reconcile(string claimId, int currentAttempt, IReadOnlyList<MissionAction> desired)
        {
            if (owner != null && owner != claimId) throw new InvalidOperationException("World-action adapter already has a mission owner.");
            foreach (var action in desired)
            {
                var binding = Resolve(action.binding);
                if (action.kind == "spawn") { if (binding.prefab == null || binding.spawn == null) throw new ArgumentException("Missing prefab/spawn: " + action.binding); }
                else if (action.kind == "active") { if (binding.target == null || (action.value != "true" && action.value != "false")) throw new ArgumentException("Invalid active-state action: " + action.id); }
                else throw new ArgumentException("Unsupported world action: " + action.kind);
            }
            if (attempt != currentAttempt) Clear();
            owner = claimId; attempt = currentAttempt; keep.Clear();
            foreach (var action in desired) keep.Add(action.id);
            remove.Clear(); foreach (var entry in spawned) if (!keep.Contains(entry.Key)) remove.Add(entry.Key);
            foreach (string key in remove) { Retire(spawned[key]); spawned.Remove(key); }
            foreach (var pair in original) if (pair.Key != null) pair.Key.SetActive(pair.Value);
            foreach (var action in desired)
            {
                var binding = Resolve(action.binding);
                if (action.kind == "spawn")
                { if (!spawned.ContainsKey(action.id)) spawned.Add(action.id, Instantiate(binding.prefab, binding.spawn.position, binding.spawn.rotation)); }
                else { if (!original.ContainsKey(binding.target)) original.Add(binding.target, binding.target.activeSelf); binding.target.SetActive(action.value == "true"); }
            }
            if (desired.Count == 0) { original.Clear(); owner = null; }
        }
        private Binding Resolve(string id)
        {
            Binding found = null;
            foreach (var binding in bindings) if (binding != null && binding.id == id)
            { if (found != null) throw new ArgumentException("Duplicate binding: " + id); found = binding; }
            return found ?? throw new ArgumentException("Missing mission binding: " + id);
        }
        private static void Retire(GameObject target) { if (target == null) return; target.SetActive(false); Destroy(target); }
        private void Clear()
        {
            foreach (var target in spawned.Values) Retire(target); spawned.Clear();
            foreach (var pair in original) if (pair.Key != null) pair.Key.SetActive(pair.Value); original.Clear(); owner = null;
        }
        private void OnDisable() => Clear();
    }
}
