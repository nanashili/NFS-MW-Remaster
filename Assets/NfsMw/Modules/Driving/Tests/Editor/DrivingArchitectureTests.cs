using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class DrivingArchitectureTests
    {
        [Test]
        public void SimulationContractsRemainUnityFree()
        {
            string asmdef = ReadProjectFile("Assets", "Driving", "Runtime", "Contracts", "NfsMwRemaster.Driving.Contracts.asmdef");

            Assert.That(asmdef, Does.Contain("\"references\": []"));
            Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"));
        }

        [Test]
        public void DrivingRuntimeDoesNotDependOnHdrp()
        {
            string asmdef = ReadProjectFile("Assets", "Driving", "Runtime", "NfsMwRemaster.Driving.asmdef");

            Assert.That(asmdef, Does.Not.Contain("HighDefinition"));
            Assert.That(asmdef, Does.Contain("NfsMwRemaster.Driving.Contracts"));
        }

        [Test]
        public void HdrpDependencyBelongsToRenderingAdapter()
        {
            string asmdef = ReadProjectFile("Assets", "Driving", "Runtime", "Rendering", "Runtime", "NfsMwRemaster.Driving.Rendering.Runtime.asmdef");

            Assert.That(asmdef, Does.Contain("Unity.RenderPipelines.HighDefinition.Runtime"));
            Assert.That(asmdef, Does.Contain("NfsMwRemaster.Driving"));
        }

        [Test]
        public void GameFlowCoreIsUnityFreeAndHasOnlyDeliberateConsumers()
        {
            JObject gameFlow = ReadAssemblyDefinition(
                "Assets", "Driving", "Runtime", "GameFlow", "Core",
                "NfsMwRemaster.Driving.GameFlow.asmdef");
            JObject gameFlowTests = ReadAssemblyDefinition(
                "Assets", "Driving", "Tests", "GameFlow",
                "NfsMwRemaster.Driving.GameFlow.Tests.asmdef");

            Assert.That((bool)gameFlow["noEngineReferences"], Is.True);
            Assert.That((JArray)gameFlow["references"], Is.Empty);
            Assert.That(
                ReadProjectFile("Assets", "Driving", "Runtime", "GameFlow", "Core", "GameFlow.cs"),
                Does.Not.Contain("UnityEngine"));
            JArray gameFlowTestReferences = (JArray)gameFlowTests["references"];
            Assert.That(gameFlowTestReferences.Count, Is.EqualTo(1));
            Assert.That(
                (string)gameFlowTestReferences[0],
                Is.EqualTo("NfsMwRemaster.Driving.GameFlow"));

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string drivingRoot = Path.Combine(projectRoot, "Assets", "Driving");
            var directConsumers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(
                drivingRoot,
                "*.asmdef",
                SearchOption.AllDirectories))
            {
                JObject definition = JObject.Parse(File.ReadAllText(path));
                foreach (JToken reference in (JArray)definition["references"] ?? new JArray())
                {
                    if ((string)reference == "NfsMwRemaster.Driving.GameFlow")
                    {
                        directConsumers.Add((string)definition["name"]);
                    }
                }
            }

            Assert.That(directConsumers, Is.EquivalentTo(new[]
            {
                "NfsMwRemaster.Driving",
                "NfsMwRemaster.Driving.Editor",
                "NfsMwRemaster.Driving.GameFlow.Tests",
                "NfsMwRemaster.Driving.Tests"
            }));
        }

        [Test]
        public void DrivingAssemblyGraphRemainsAcyclic()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string drivingRoot = Path.Combine(projectRoot, "Assets", "Driving");
            var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(
                drivingRoot,
                "*.asmdef",
                SearchOption.AllDirectories))
            {
                JObject definition = JObject.Parse(File.ReadAllText(path));
                string name = (string)definition["name"];
                JArray references = (JArray)definition["references"] ?? new JArray();
                string[] names = new string[references.Count];
                for (int i = 0; i < references.Count; i++) names[i] = (string)references[i];
                graph.Add(name, names);
            }

            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string assembly in graph.Keys)
                Visit(assembly, graph, states, new List<string>());
        }

        private static void Visit(
            string assembly,
            IReadOnlyDictionary<string, string[]> graph,
            IDictionary<string, int> states,
            List<string> path)
        {
            states.TryGetValue(assembly, out int state);
            if (state == 2) return;
            if (state == 1)
            {
                path.Add(assembly);
                Assert.Fail("Assembly cycle: " + string.Join(" -> ", path));
            }

            states[assembly] = 1;
            path.Add(assembly);
            foreach (string dependency in graph[assembly])
                if (graph.ContainsKey(dependency)) Visit(dependency, graph, states, path);
            path.RemoveAt(path.Count - 1);
            states[assembly] = 2;
        }

        private static JObject ReadAssemblyDefinition(params string[] relativePath)
        {
            return JObject.Parse(ReadProjectFile(relativePath));
        }

        private static string ReadProjectFile(params string[] relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = projectRoot;
            for (int i = 0; i < relativePath.Length; i++) path = Path.Combine(path, relativePath[i]);
            return File.ReadAllText(path);
        }
    }
}
