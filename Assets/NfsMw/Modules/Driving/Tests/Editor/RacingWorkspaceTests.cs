using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.Workspace;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RacingWorkspaceTests
    {
        [Test]
        public void HistoryCopiesLinksAndTruncatesForwardBranch()
        {
            var history=new RacingNavigationHistory();
            var a=RacingDocumentLink.For("race-routes",null,"a");
            history.Push(a);a.elementId="changed";
            history.Push(RacingDocumentLink.For("event-placement",null,"b"));
            Assert.That(history.Back().elementId,Is.EqualTo("a"));
            history.Push(RacingDocumentLink.For("validation",null,"c"));
            Assert.That(history.CanForward,Is.False);
            Assert.That(history.Count,Is.EqualTo(2));
        }

        [Test]
        public void HistoryIsBoundedAndDoesNotDuplicateCurrentTarget()
        {
            var history=new RacingNavigationHistory();
            for(int i=0;i<100;i++)history.Push(RacingDocumentLink.For("race-routes",null,i.ToString()));
            history.Push(history.Current);
            Assert.That(history.Count,Is.EqualTo(64));
            for(int i=0;i<64;i++)history.Back();
            Assert.That(history.Current.elementId,Is.EqualTo("36"));
        }

        [Test]
        public void UnsavedDocumentsResolveOnlyInTheirOriginalSession()
        {
            var asset=ScriptableObject.CreateInstance<RaceRouteDefinition>();
            try
            {
                var link=RacingDocumentLink.For("race-routes",asset);
                Assert.That(link.Copy().Resolve(),Is.SameAs(asset));
                Assert.That(link.objectId,Does.StartWith("session:"));
                var restored=JsonUtility.FromJson<RacingDocumentLink>(JsonUtility.ToJson(link));
                Assert.That(restored.Resolve(),Is.Null,"Do not resolve another object by a recycled instance ID.");
            }
            finally{UnityEngine.Object.DestroyImmediate(asset);}
        }

        [Test]
        public void MissingIdentityNeverFallsBackToAnAssetPath()
        {
            var link=new RacingDocumentLink{moduleId="race-routes",objectId="missing",assetPath="Assets/SomeReplacement.asset"};
            Assert.That(link.Resolve(),Is.Null);
        }

        [Test]
        public void CommandRevalidatesImmediatelyBeforeMutation()
        {
            bool allowed=true,mutated=false;
            var command=new RacingCommand("test.edit","Edit","Change test source","Undo",_=>allowed?"":"Source became read-only",_=>mutated=true);
            Assert.That(command.UnavailableReason(null),Is.Empty);
            allowed=false;
            Assert.Throws<InvalidOperationException>(()=>command.Execute(null));
            Assert.That(mutated,Is.False);
        }

        [Test]
        public void PreviewLeaseRejectsConcurrentOwnerAndObsoleteRelease()
        {
            string resource="workspace-test-"+Guid.NewGuid();
            var first=RacingPreviewSessions.Acquire(resource,"first",()=>{});
            try{Assert.Throws<InvalidOperationException>(()=>RacingPreviewSessions.Acquire(resource,"second",()=>{}));}
            finally{first.Dispose();}
            using(var second=RacingPreviewSessions.Acquire(resource,"second",()=>{}))
            {
                first.Dispose();
                Assert.Throws<InvalidOperationException>(()=>RacingPreviewSessions.Acquire(resource,"third",()=>{}));
            }
        }

        [Test]
        public void UnpublishedRouteCannotCreateAnEvent()
        {
            var route=ScriptableObject.CreateInstance<RaceRouteDefinition>();
            try
            {
                Assert.That(RacingRouteEventWorkflow.Unavailable(route),Does.Contain("Publish"));
                Assert.Throws<InvalidOperationException>(()=>RacingRouteEventWorkflow.Create(route,"Assets/DoNotCreate.asset"));
                Assert.That(AssetDatabase.LoadMainAssetAtPath("Assets/DoNotCreate.asset"),Is.Null);
            }
            finally{UnityEngine.Object.DestroyImmediate(route);}
        }

        [Test]
        public void VehicleFrameworkIsRegisteredAsWorkspaceModule()
        {
            var module = RacingModuleRegistry.Find("vehicle-framework");
            Assert.That(module, Is.Not.Null);
            Assert.That(module.Group, Is.EqualTo(RacingToolGroup.Vehicles));
            Assert.That(module.SourceTypes, Does.Contain(typeof(VehicleProfileDraft)));
            using (var view = module.CreateView()) Assert.That(view.Root, Is.Not.Null);
        }

        [Test]
        public void SharedViewsReleaseWithoutEditorWindowControllers()
        {
            using(var route=new RaceRouteView())
            {
                Assert.That(route.Root,Is.Not.Null);
                Assert.That(typeof(EditorWindow).IsAssignableFrom(route.GetType()),Is.False);
                route.Dispose();
            }
            Assert.That(RaceRouteWindow.Active,Is.Null);
            using(var placement=new EventPlacementView())Assert.That(placement.Root,Is.Not.Null);
        }
    }
}

