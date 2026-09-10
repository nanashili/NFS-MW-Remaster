using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class GameFlowStorageTests
    {
        [Test]
        public void AtomicSaveKeepsPreviousVersionAndPresenceRejectsTraversal()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nfs-flow-storage-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Storage test");
            try
            {
                var storage = root.AddComponent<JsonCareerProfileStorage>(); storage.SetDirectory(directory);
                Assert.That(storage.TryExists("alias", out bool exists, out _), Is.True); Assert.That(exists, Is.False);
                string first = JsonUtility.ToJson(CareerProfileData.Create("alias", "first"));
                string second = JsonUtility.ToJson(CareerProfileData.Create("alias", "second"));
                Assert.That(storage.TrySave("alias", first, out string failure), Is.True, failure);
                Assert.That(storage.TrySave("alias", second, out failure), Is.True, failure);
                Assert.That(storage.TryLoad("alias", out string value, out _), Is.True); Assert.That(value, Is.EqualTo(second));
                Assert.That(storage.TryCreate("alias", first, out _), Is.False);
                Assert.That(storage.TryLoad("alias", out value, out _), Is.True); Assert.That(value, Is.EqualTo(second));
                Assert.That(storage.TryExists("alias", out exists, out _), Is.True); Assert.That(exists, Is.True);
                Assert.That(Directory.GetFiles(directory, "*.pending-*"), Is.Empty);
                string[] backups = Directory.GetFiles(directory, "*.save", SearchOption.AllDirectories);
                Assert.That(backups.Length, Is.EqualTo(2));
                Assert.That(storage.TryExists("../outside", out _, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
