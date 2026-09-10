using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Authored spatial ambience through the same voice admission system; optional native AudioReverbZones handle local acoustics.</summary>
    public sealed class AmbientAudioEmitter : MonoBehaviour
    {
        [SerializeField] private SensoryAudioWorld world;
        [SerializeField] private AudioClip loop;
        [SerializeField, Range(0, 1)] private float gain = 0.3f;
        [SerializeField, Range(10, 120)] private float activationDistance = 80;
        private FeedbackVoiceLease voice;
        private float nextCheck;
        public void Configure(SensoryAudioWorld audio, AudioClip recording) { world = audio; loop = recording; }
        private void Update()
        {
            if (world == null || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;
            bool nearby = world.Listener != null && (world.Listener.position - transform.position).sqrMagnitude < activationDistance * activationDistance;
            if (!nearby) { world.Release(voice); return; }
            if (!world.Owns(voice)) voice = world.Play(loop, SensoryCategory.Environment, transform, Vector3.zero, gain, 1, 220, true);
            world.UpdateVoice(voice, gain);
        }
        private void OnDisable() { if (world != null) world.Release(voice); }
    }
}
