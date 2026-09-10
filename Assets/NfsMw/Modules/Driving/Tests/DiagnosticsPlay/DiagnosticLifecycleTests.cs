using System.Collections;
using NUnit.Framework;
using NfsMwRemaster.Driving;
using UnityEngine;
using UnityEngine.TestTools;
namespace NfsMwRemaster.Diagnostics.PlayTests
{
    public sealed class DiagnosticLifecycleTests
    {
        private GameObject root;
        [TearDown]public void Cleanup(){if(root!=null)Object.DestroyImmediate(root);DiagnosticSession.Reset();}
        [UnityTest]public IEnumerator OverlayCapturesAndReleasesInput()
        {
            root=new GameObject("diagnostics-test");var overlay=root.AddComponent<DiagnosticOverlay>();overlay.SetVisible(true);Assert.True(MapInputFocus.Captured);
            overlay.SetVisible(false);Assert.True(MapInputFocus.Captured,"Closing frame must remain captured");yield return null;Assert.False(MapInputFocus.Captured);
        }
        [UnityTest]public IEnumerator DisableReleasesInputAndProfiler()
        {
            root=new GameObject("diagnostics-test");var overlay=root.AddComponent<DiagnosticOverlay>();overlay.SetVisible(true);overlay.enabled=false;
            yield return null;Assert.False(MapInputFocus.Captured);Assert.IsNull(DiagnosticSession.Hub.Find("unity.performance"));
        }
        [UnityTest]public IEnumerator FixtureDespawnAndPoolReuseGetsNewGeneration()
        {
            root=new GameObject("synthetic-test");var fixture=root.AddComponent<DiagnosticFixture>();root.AddComponent<DiagnosticOverlay>();
            using(var lease=DiagnosticSession.Hub.Subscribe(fixture.Id)){yield return null;DiagnosticSession.Hub.Tick(DiagnosticClock.Now(SamplePhase.LateUpdate));Assert.NotNull(DiagnosticSession.Hub.Find(fixture.Id).Current);}
            long previous=DiagnosticSession.Hub.Find(fixture.Id).Current.Generation;
            root.SetActive(false);Assert.IsNull(DiagnosticSession.Hub.Find(fixture.Id));root.SetActive(true);
            using(var lease=DiagnosticSession.Hub.Subscribe(fixture.Id)){yield return null;DiagnosticSession.Hub.Tick(DiagnosticClock.Now(SamplePhase.LateUpdate));Assert.Greater(DiagnosticSession.Hub.Find(fixture.Id).Current.Generation,previous);}
        }
        [UnityTest]public IEnumerator SessionResetRebindsEnabledObjects()
        {
            root=new GameObject("synthetic-test");var fixture=root.AddComponent<DiagnosticFixture>();root.AddComponent<DiagnosticOverlay>();DiagnosticSession.Reset();
            yield return null;Assert.NotNull(DiagnosticSession.Hub.Find(fixture.Id));Assert.NotNull(DiagnosticSession.Hub.Find("unity.performance"));
        }
    }
}
