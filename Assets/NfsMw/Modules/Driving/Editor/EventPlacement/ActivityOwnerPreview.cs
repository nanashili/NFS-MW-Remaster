using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static class ActivityOwnerPreview
    {
        public static string Run(EventPlacementSource source, RacingVehicleSetup vehicle)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before an isolated preview.");
            if (source == null || source.published == null || source.published.Snapshot.adapter != "service") throw new InvalidOperationException("This owner smoke test requires a published service placement.");
            if (vehicle == null) throw new InvalidOperationException("Choose a shared production vehicle setup.");
            var scene = EditorSceneManager.NewPreviewScene();
            RacingVehicleRig rig = null; WorldActivityInstance instance = null;
            try
            {
                var r = source.published.Snapshot;
                rig = new RacingVehicleRig(scene, vehicle, r.interaction + Vector3.up * .8f, r.rotation);
                var activity = new GameObject("Isolated service adapter"); SceneManager.MoveGameObjectToScene(activity, scene);
                activity.transform.SetPositionAndRotation(r.interaction, r.rotation);
                var publication = ScriptableObject.CreateInstance<EventPlacementPublication>();
                // Keep entrance policy; the isolated smoke test never opens the production storefront or saves at a safehouse.
                r.storefront = null; r.serviceKind = WorldLocationKind.Garage;
                publication.Initialize(r);
                try
                {
                    instance = activity.AddComponent<WorldActivityInstance>(); instance.Configure(publication);
                    if (!ActivityRegistry.Register(instance)) throw new InvalidOperationException("This identity is already registered. Close the other test first.");
                    var location = activity.AddComponent<WorldLocation>(); location.Configure(r.id, r.label, r.serviceKind, null);
                    var session = rig.Root.AddComponent<FreeRoamSession>(); session.Configure(rig.Vehicle, null, null, Array.Empty<WorldLocation>(), Array.Empty<FreeRoamEventDefinition>(), false);
                    var facts = new ActivityPreviewFacts(); session.SetActivityCareerFacts(facts);
                    // Unrestricted sample tests the real owner; gated activities remain gated under zero synthetic facts.
                    for (float elapsed = 0; elapsed < r.dwellSeconds + .1f; elapsed += .02f) session.AdvanceActivityInteractions(.02f);
                    if (!session.TryActivatePlacement(instance, out string failure)) throw new InvalidOperationException(failure);
                    if (session.State != FreeRoamState.Location || session.Cash != 0 || rig.Root.GetComponent<CareerProfileSystem>() != null) throw new InvalidOperationException("Isolation/owner assertion failed.");
                    session.ExitActivity();
                    if (session.State != FreeRoamState.Driving) throw new InvalidOperationException(session.Status);
                    return "PASS: shared vehicle + FreeRoamSession entered and exited the isolated garage. Zero wallet/profile owners; no reward or save calls. Storefront behavior, live traffic and road driving were not simulated.";
                }
                finally { if (instance != null) ActivityRegistry.Unregister(instance); UnityEngine.Object.DestroyImmediate(publication); }
            }
            finally { rig?.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
