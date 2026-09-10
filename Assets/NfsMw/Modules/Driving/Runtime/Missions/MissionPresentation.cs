using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public sealed class MissionObjectiveView
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Parent { get; internal set; }
        public string Marker { get; internal set; }
        public ObjectiveState State { get; internal set; }
        public double Current { get; internal set; }
        public double Required { get; internal set; }
        public double Remaining { get; internal set; }
        public double Normalized => Math.Min(1, Required <= 0 ? 0 : Current / Required);
        public bool Optional { get; internal set; }
    }
    public static class MissionPresentation
    {
        // UI sampling only; never used by gameplay evaluation or high-frequency dispatch.
        public static IReadOnlyList<MissionObjectiveView> Read(MissionRuntime runtime)
        {
            var views = new List<MissionObjectiveView>();
            foreach (var definition in runtime.Definition.objectives)
            {
                var state = runtime.Objective(definition.id);
                views.Add(new MissionObjectiveView { Id = definition.id, Title = definition.title, Parent = definition.parent,
                    Marker = state.state == ObjectiveState.Active ? definition.marker : "", State = state.state,
                    Current = definition.kind == "timer" || definition.kind == "deadline" ? state.elapsed : state.progress,
                    Required = definition.kind == "sequence" ? definition.sequence.Length : definition.kind == "hold" || definition.kind == "timer" ? definition.duration : definition.required,
                    Remaining = Math.Max(0, definition.duration - state.elapsed), Optional = definition.optional });
            }
            return views.AsReadOnly();
        }
    }
    [Serializable] public sealed class MissionRecordedStep
    {
        public long step;
        public double gameSeconds, realSeconds;
        public MissionEvent[] events = Array.Empty<MissionEvent>();
    }
    public static class MissionReplay
    {
        public static MissionSnapshot Run(MissionGraph graph, IReadOnlyList<MissionRecordedStep> recording, ICareerFacts career = null, IMissionActions actions = null)
        {
            if (recording == null || recording.Count > 100000) throw new ArgumentException("Invalid recording length.");
            using var runtime = new MissionRuntime(graph, career, actions);
            foreach (var step in recording)
            {
                if (step == null) throw new ArgumentException("Null recorded step.");
                runtime.Step(step.step, step.gameSeconds, step.realSeconds, step.events);
            }
            return runtime.Capture();
        }
    }
}
