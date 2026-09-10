using System;
using System.Collections;
using NfsMwRemaster.Driving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Maps.Tests
{
    public sealed class MapRuntimeTests
    {
        private GameObject go;
        private MapPublication publication;
        private RoadNetworkAsset roads;
        private MapStyle style;
        private Keyboard keyboard;
        private WorldMapController controller;
        [SetUp] public void Setup()
        {
            keyboard=InputSystem.AddDevice<Keyboard>();go=new GameObject("Map test player");
            roads=ScriptableObject.CreateInstance<RoadNetworkAsset>();roads.Initialize(RoadId.New(),"test",Array.Empty<RoadBakedLane>(),Array.Empty<RoadBakedChunk>());
            publication=ScriptableObject.CreateInstance<MapPublication>();publication.Initialize(Guid.NewGuid().ToString("N"),"map","frame",roads,new MapFrame(),128,Array.Empty<MapTileEntry>(),Array.Empty<MapLandmark>(),new MapPoint(-100,-100),new MapPoint(100,100));
            style=ScriptableObject.CreateInstance<MapStyle>();controller=go.AddComponent<WorldMapController>();controller.map=publication;controller.style=style;controller.player=go.transform;
        }
        [TearDown] public void Teardown(){Object.DestroyImmediate(go);Object.DestroyImmediate(publication);Object.DestroyImmediate(roads);Object.DestroyImmediate(style);InputSystem.RemoveDevice(keyboard);}
        [UnityTest] public IEnumerator RealKeyboardCannotDriveWhenMapCapturesInput()
        {
            var input=go.AddComponent<PlayerVehicleInput>();yield return null;yield return null;
            keyboard.MakeCurrent();InputSystem.Update();InputState.Change(keyboard,new KeyboardState(Key.W),InputState.currentUpdateType);
            Assert.IsTrue(keyboard.wKey.isPressed,"Injected W state must be active.");Assert.IsFalse(MapInputFocus.Captured,"Map is initially closed; frame "+Time.frameCount+", open "+controller.IsOpen);
            typeof(PlayerVehicleInput).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(input,null);
            Assert.That(input.Current.Throttle,Is.GreaterThan(.9f));controller.SetOpen(true);Assert.That(input.Current.Throttle,Is.Zero);
            yield return null;Assert.That(input.Current.Throttle,Is.Zero);controller.SetOpen(false);Assert.That(input.Current.Throttle,Is.Zero);
            yield return null;yield return null;keyboard.MakeCurrent();InputSystem.Update();InputState.Change(keyboard,new KeyboardState(Key.W),InputState.currentUpdateType);
            Assert.IsTrue(keyboard.wKey.isPressed,"Injected W state must be active.");Assert.IsFalse(MapInputFocus.Captured,"Map is initially closed; frame "+Time.frameCount+", open "+controller.IsOpen);
            typeof(PlayerVehicleInput).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(input,null);Assert.That(input.Current.Throttle,Is.GreaterThan(.9f));
        }
        [UnityTest] public IEnumerator OriginShiftFocusRetainsLogicalPosition()
        {
            go.transform.position=new Vector3(100,0,200);yield return null;controller.FocusPlayer();var before=controller.View.center;
            go.transform.position=new Vector3(-900,0,-1800);controller.worldShift=new LogicalOrigin{x=1000,z=2000};controller.FocusPlayer();Assert.That((before-controller.View.center).Square,Is.LessThan(1e-10));yield return null;
        }
        [UnityTest] public IEnumerator DisableReleasesFocusAndInvalidatesPendingRoute()
        {
            yield return null;controller.SetOpen(true);long generation=controller.Navigation.Begin();controller.enabled=false;yield return null;
            Assert.IsFalse(MapInputFocus.Captured);Assert.IsFalse(controller.IsOpen);Assert.IsFalse(controller.Navigation.Publish(generation,new[]{Vector3.zero,Vector3.one}));
        }
    }
}
