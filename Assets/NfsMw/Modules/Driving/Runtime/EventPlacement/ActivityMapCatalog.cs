using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Place in a persistent bootstrap scene to retain unloaded activity markers.</summary>
    public sealed class ActivityMapCatalog : MonoBehaviour
    {
        [SerializeField] private EventPlacementPublication[] publications = Array.Empty<EventPlacementPublication>();
        public string Failure { get; private set; }
        public void Configure(EventPlacementPublication[] values)
        {
            ActivityMapRegistry.Remove(this); publications = values == null ? Array.Empty<EventPlacementPublication>() : (EventPlacementPublication[])values.Clone();
            if (Application.isPlaying && isActiveAndEnabled) Register();
        }
        internal void Register() { ActivityMapRegistry.Register(this, publications, out string failure); Failure = failure; }
        private void OnEnable() { if (Application.isPlaying) Register(); }
        private void OnDisable() => ActivityMapRegistry.Remove(this);
    }
}
