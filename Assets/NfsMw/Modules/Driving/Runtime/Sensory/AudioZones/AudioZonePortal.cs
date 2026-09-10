using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Authored connection between acoustic spaces. It is not a road or navigation link.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioZonePortal : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private AudioZone sourceZone;
        [SerializeField] private AudioZone targetZone;
        [SerializeField] private AudioZonePortalState state = AudioZonePortalState.WorldControlled;
        [SerializeField, Range(0, 1)] private float openTransmission = 1;
        [SerializeField, Range(0, 1)] private float closedTransmission = 0.08f;
        [SerializeField, Range(20, 22000)] private float openLowPassHz = 22000;
        [SerializeField, Range(20, 22000)] private float closedLowPassHz = 900;
        [SerializeField] private bool bidirectional = true;
        [SerializeField, TextArea(1, 4)] private string authoringNotes = string.Empty;

        public string StableId => stableId;
        public AudioZone SourceZone => sourceZone;
        public AudioZone TargetZone => targetZone;
        public AudioZonePortalState State => state;
        public bool Bidirectional => bidirectional;
        public float Transmission => state == AudioZonePortalState.Closed ? closedTransmission : openTransmission;
        public float LowPassHz => state == AudioZonePortalState.Closed ? closedLowPassHz : openLowPassHz;
        public string AuthoringNotes => authoringNotes;

        public bool Connects(AudioZone from, AudioZone to)
        {
            if (from == null || to == null) return false;
            return sourceZone == from && targetZone == to || bidirectional && sourceZone == to && targetZone == from;
        }

        public void SetStableId(string value) => stableId = value ?? string.Empty;
        public void SetEndpoints(AudioZone source, AudioZone target) { sourceZone = source; targetZone = target; }
        public void SetState(AudioZonePortalState value) => state = value;
        public void SetBidirectional(bool value) => bidirectional = value;
        public void SetTransmission(float open, float closed)
        {
            openTransmission = Mathf.Clamp01(SensoryMath.Finite(open));
            closedTransmission = Mathf.Clamp01(SensoryMath.Finite(closed));
        }
        public void SetFilters(float open, float closed)
        {
            openLowPassHz = Mathf.Clamp(SensoryMath.Finite(open), 20, 22000);
            closedLowPassHz = Mathf.Clamp(SensoryMath.Finite(closed), 20, 22000);
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = AudioZoneStableId.Create("audio.portal", name);
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = AudioZoneStableId.Create("audio.portal", name);
            openTransmission = Mathf.Clamp01(SensoryMath.Finite(openTransmission));
            closedTransmission = Mathf.Clamp01(SensoryMath.Finite(closedTransmission));
            openLowPassHz = Mathf.Clamp(SensoryMath.Finite(openLowPassHz), 20, 22000);
            closedLowPassHz = Mathf.Clamp(SensoryMath.Finite(closedLowPassHz), 20, 22000);
        }
    }
}
