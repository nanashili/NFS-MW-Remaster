using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedWarehouseTests
    {
        private const string Root = "Assets/NfsMw/Content/Frontend/UI/Data/";
        [Test]
        public void ProductionBootReferencesThePublishedWarehouseAndVehicle()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(Root + "GameFlowSettings.asset");
            Assert.That(settings.RequireTitleConfirmation, Is.True);
            var showroom = settings.Showroom;
            Assert.That(showroom, Is.Not.Null);
            Assert.That(showroom.environmentSource, Does.EndWith("FrontendWarehouseRemake/Warehouse.gltf"));
            Assert.That(showroom.environmentParts, Is.Not.Empty);
            Assert.That(showroom.parts, Is.Not.Empty);
            foreach (var part in showroom.environmentParts)
            {
                Assert.That(part.mesh, Is.Not.Null);
                Assert.That(part.materials, Has.None.Null);
            }
        }

        [Test]
        public void SettingsUseWheelOrCourtyardShotsAndBackRestoresTheOptionsShot()
        {
            var showroom = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(Root + "GameFlowSettings.asset").Showroom;
            var navigation = new MostWantedFrontendNavigation();
            var main = showroom.ShotFor(navigation.Current.Page);
            navigation.Push(MostWantedFrontendPage.Options);
            var options = showroom.ShotFor(navigation.Current.Page);
            navigation.Select(1, 7);
            navigation.Push(MostWantedFrontendPage.Video);
            var video = showroom.ShotFor(navigation.Current.Page);
            var wheel = showroom.ShotFor(MostWantedFrontendPage.Audio);
            Assert.That(Vector3.Distance(main.position, options.position), Is.GreaterThan(5));
            Assert.That(Vector3.Distance(wheel.position, video.position), Is.GreaterThan(1));
            Assert.That(wheel.fieldOfView, Is.LessThan(video.fieldOfView));
            Assert.That(showroom.ShotFor(MostWantedFrontendPage.Controls).position, Is.EqualTo(video.position));
            Assert.That(showroom.ShotFor(MostWantedFrontendPage.Gameplay).position, Is.EqualTo(wheel.position));
            Assert.That(navigation.Back(), Is.True);
            Assert.That(navigation.Current.Selection, Is.EqualTo(1));
            Assert.That(showroom.ShotFor(navigation.Current.Page).position, Is.EqualTo(options.position));
        }

        [Test]
        public void FrontToRearCameraTravelArcsAroundTheVehicle()
        {
            var showroom = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(Root + "GameFlowSettings.asset").Showroom;
            var front = showroom.ShotFor(MostWantedFrontendPage.MainMenu);
            var rear = showroom.ShotFor(MostWantedFrontendPage.Options);
            float minimumRadius = Mathf.Min(new Vector2(front.position.x,front.position.z).magnitude,
                new Vector2(rear.position.x,rear.position.z).magnitude);
            for(int sample=0;sample<=20;sample++)
            {
                var shot=MostWantedShowroom.InterpolateShot(front,rear,sample/20f);
                Assert.That(new Vector2(shot.position.x,shot.position.z).magnitude,Is.GreaterThanOrEqualTo(minimumRadius-.001f));
                Assert.That(Vector3.Distance(shot.position,shot.target),Is.GreaterThan(1));
            }
            Assert.That(Vector3.Distance(MostWantedShowroom.InterpolateShot(front,rear,1).position,rear.position),Is.LessThan(.001f));
        }

        [Test]
        public void ReversingAnInterruptedCameraMoveStartsAtTheVisiblePose()
        {
            var showroom = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(Root + "GameFlowSettings.asset").Showroom;
            var main=showroom.ShotFor(MostWantedFrontendPage.MainMenu);
            var midway=MostWantedShowroom.InterpolateShot(main,showroom.ShotFor(MostWantedFrontendPage.Career),.4f);
            var reversed=MostWantedShowroom.InterpolateShot(midway,main,0);
            Assert.That(Vector3.Distance(reversed.position,midway.position),Is.LessThan(.001f));
            Assert.That(reversed.target,Is.EqualTo(midway.target));
            Assert.That(reversed.fieldOfView,Is.EqualTo(midway.fieldOfView));
        }

        [Test]
        public void ProductionGlassDoesNotModifyTheDrivingMaterial()
        {
            var glass = AssetDatabase.LoadAssetAtPath<Material>(Root + "ShowroomGlass.mat");
            Assert.That(glass, Is.Not.Null);
            Assert.That(glass.GetFloat("_SurfaceType"), Is.EqualTo(1));
            Assert.That(glass.GetColor("_BaseColor").a, Is.LessThan(1));
        }
    }
}
