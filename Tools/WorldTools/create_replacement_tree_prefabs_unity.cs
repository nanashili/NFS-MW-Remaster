using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) { RockportReplacementTrees.CreatePrefabs(250); result.Log("Generated a bounded batch of new Terrain tree prefabs."); }
}
