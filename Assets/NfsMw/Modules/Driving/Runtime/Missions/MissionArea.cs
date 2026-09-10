using UnityEngine;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    [RequireComponent(typeof(Collider))]
    public sealed class MissionArea : MonoBehaviour
    {
        [SerializeField] private string areaId = "area.destination";
        [SerializeField] private MissionHost host;
        [SerializeField] private Rigidbody player;
        private readonly HashSet<Collider> contacts = new HashSet<Collider>();
        private void OnEnable() { if (host != null) host.RuntimeStarted += Seed; }
        private void Seed() => host.Publish("area.state", areaId, contacts.Count > 0 ? 1 : 0, "inside." + areaId);
        private void OnTriggerEnter(Collider other)
        {
            if (player == null || other.attachedRigidbody != player || host == null) return;
            if (contacts.Add(other) && contacts.Count == 1) host.Publish("area.entered", areaId, 1, "inside." + areaId);
        }
        private void OnTriggerExit(Collider other)
        {
            if (player == null || other.attachedRigidbody != player || host == null) return;
            if (contacts.Remove(other) && contacts.Count == 0) host.Publish("area.left", areaId, 0, "inside." + areaId);
        }
        private void OnDisable()
        {
            if (host != null) { host.RuntimeStarted -= Seed; if (contacts.Count > 0) host.Publish("area.left", areaId, 0, "inside." + areaId); }
            contacts.Clear();
        }
    }
}
