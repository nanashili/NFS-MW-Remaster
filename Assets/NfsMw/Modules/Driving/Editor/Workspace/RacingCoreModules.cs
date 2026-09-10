using UnityEditor;
using NfsMwRemaster.Driving.Editor.Weather;
using NfsMwRemaster.Driving.Editor.WorldValidation;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    [InitializeOnLoad]
    public static class RacingCoreModules
    {
        static RacingCoreModules()
        {
            RacingModuleRegistry.Register(new RacingModuleDescriptor("hdrp-rendering","HDRP Rendering",RacingToolGroup.Diagnostics,"material mask texture lighting quality shader",
                new System.Type[0],()=>new Rendering.HdrpRenderingView(),null,
                "HDRP material and scene audits, scalable quality presets and explicit Mask Map packing."));
            RacingModuleRegistry.Register(new RacingModuleDescriptor("race-routes","Race Routes",RacingToolGroup.Gameplay,"race circuit sprint checkpoint",
                new[]{typeof(RaceRouteDefinition)},()=>new RaceRouteView(),target=>EditorWindow.GetWindow<RaceRouteWindow>("Race Routes").OpenDocument(target),
                "Route authoring and isolated tests use the existing route and vehicle owners."));
            RacingModuleRegistry.Register(new RacingModuleDescriptor("event-placement","Event Placement",RacingToolGroup.Gameplay,"activity marker access staging",
                new[]{typeof(EventPlacementSource),typeof(WorldActivityDefinition)},()=>new EventPlacementView(),target=>EditorWindow.GetWindow<EventPlacementWindow>("Event Placement").OpenDocument(target),
                "Scene placements reference shared definitions. Publishing is explicit; scene changes use Undo."));
            RacingModuleRegistry.Register(new RacingModuleDescriptor("validation","Validation",RacingToolGroup.Diagnostics,"world dashboard diagnostics rules",
                new System.Type[0],()=>new WorldValidationView(),target=>WorldValidationWindow.Open(),
                "Reports describe their scan scope and omitted checks. Validation jobs remain owned by the validation service when this view closes."));
            RacingModuleRegistry.Register(new RacingModuleDescriptor("vehicle-framework","Vehicle Framework",RacingToolGroup.Vehicles,"vehicle profile car tuning physics upgrades customization garage",
                new[]{typeof(VehicleProfileDraft)},()=>new VehicleFrameworkWorkspaceView(),target=>{var window=EditorWindow.GetWindow<VehicleProfilesWindow>("Vehicle Profiles"); window.OpenDraft(target as VehicleProfileDraft);},
                "The workspace and Vehicle Profile Studio share the canonical definition, installed configuration and resolver."));
            RacingModuleRegistry.Register(new RacingModuleDescriptor("weather","Weather & Time",RacingToolGroup.World,"weather rain climate clouds fog day night wetness storm",
                new[]{typeof(DynamicWeatherWorld)},()=>new WeatherStudioView(),target=>WeatherStudioWindow.OpenFocused(target as DynamicWeatherWorld),
                "One fixed-tick simulation feeds the existing HDRP atmosphere owner and bounded presentation adapters; quality never changes gameplay state."));
        }
    }
}
