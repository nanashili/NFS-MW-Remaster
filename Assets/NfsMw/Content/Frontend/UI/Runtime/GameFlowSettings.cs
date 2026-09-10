using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Game Flow Settings")]
    public sealed class GameFlowSettings : ScriptableObject
    {
        [SerializeField] private string worldScenePath = "Assets/NfsMw/Scenes/World/RockportMap.unity";
        [SerializeField] private string defaultAlias = "free_roam_profile";
        [SerializeField, Min(0)] private float bootSeconds = 1;
        [SerializeField] private bool requireTitleConfirmation;
        [SerializeField, Min(0)] private float eventPreparationSeconds = 0.6f;
        [SerializeField, Min(10)] private float sceneLoadTimeoutSeconds = 90;
        [SerializeField] private bool pauseOnFocusLoss = true;
        [SerializeField] private MostWantedFrontendContent frontendContent;
        [SerializeField] private MostWantedShowroomDefinition showroom;
        public string WorldScenePath => worldScenePath;
        public string DefaultAlias => defaultAlias;
        public float BootSeconds => Mathf.Max(0, bootSeconds);
        public bool RequireTitleConfirmation => requireTitleConfirmation;
        public float EventPreparationSeconds => Mathf.Max(0, eventPreparationSeconds);
        public float SceneLoadTimeoutSeconds => Mathf.Max(10, sceneLoadTimeoutSeconds);
        public bool PauseOnFocusLoss => pauseOnFocusLoss;
        public MostWantedFrontendContent FrontendContent => frontendContent;
        public MostWantedShowroomDefinition Showroom => showroom;
    }
}
