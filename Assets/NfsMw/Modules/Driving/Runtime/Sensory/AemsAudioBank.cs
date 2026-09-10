using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class AemsRecording
    {
        public int sampleId, loopStart, loopEnd;
        public AudioClip clip;
    }
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Recovered AEMS bank")]
    public sealed class AemsAudioBank : ScriptableObject
    {
        public string sourceHash = "", evidence = "";
        public AemsProgram[] programs = Array.Empty<AemsProgram>();
        public AemsRecording[] recordings = Array.Empty<AemsRecording>();
    }
    [Serializable] public sealed class MostWantedSurfaceAudio
    { public SensorySurface surface; public int roadLoop = -1, enter = -1, exit = -1, skid; }
    [Serializable] public sealed class MostWantedVehicleAudio
    {
        public int schema;
        public int carId = 66;
        public EngineAudioRegion acceleration, deceleration;
        public MostWantedSurfaceAudio[] surfaces = Array.Empty<MostWantedSurfaceAudio>();
        public float idleRpm = 1100, maximumRpm = 7900, masterGain = .8f;
        public AemsAudioBank engine, sweeteners, transmission, whine, shifts, skids, road, wind, nitrous;
        public string evidence = "", fingerprint = "";
        public bool Validate(out string failure)
        {
            failure = "Incomplete or invalid recovered Most Wanted audio setup.";
            if (schema != 1 || acceleration == null || deceleration == null || !acceleration.IsValid || !deceleration.IsValid
                || surfaces == null || surfaces.Length == 0 || Array.Exists(surfaces, s => s == null)
                || !SensoryMath.IsFinite(idleRpm) || !SensoryMath.IsFinite(maximumRpm) || idleRpm <= 0 || maximumRpm <= idleRpm
                || !SensoryMath.IsFinite(masterGain) || masterGain < 0 || masterGain > 1) return false;
            if (!Bank(engine, "CAR", 26) || !Bank(sweeteners, "CAR_SWTN", 7) || !Bank(sweeteners, "CAR_Sputter", 11)
                || !Bank(transmission, "CAR_TRANNY", 9) || whine != null && !Bank(whine, "CAR_WHINE", 9)
                || !Bank(shifts, "FX_SHIFTING_01", 6) || !Bank(skids, "FX_SKID", 22) || !Bank(road, "FX_ROADNOISE", 11)
                || !Bank(road, "FX_ROADNOISE_TRANS", 11) || !Bank(wind, "FX_WIND", 14) || !Bank(nitrous, "FX_NITROUS", 9)) return false;
            failure = ""; return true;
        }
        private static bool Bank(AemsAudioBank bank, string name, int parameters)
        {
            if (bank == null || bank.programs == null || bank.recordings == null || bank.recordings.Length == 0) return false;
            var program = Array.Find(bank.programs, p => p != null && p.interfaceName == name);
            if (program == null || program.schema != 1 || program.parameterCount != parameters || program.players == null || program.players.Length == 0) return false;
            foreach (var recording in bank.recordings)
                if (recording == null || recording.clip == null || recording.sampleId < 1 || recording.loopStart < 0 || recording.loopEnd < 0
                    || recording.loopEnd > recording.clip.samples || recording.loopEnd > 0 && recording.loopEnd <= recording.loopStart) return false;
            return true;
        }
    }
}
