using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) {
        RockportReplacementTrees.CreateLibrary();
        RockportReplacementTrees.CreatePrefabs(250);
        result.Log("Updated independent tree library and generated first replacement prefab batch.");
    }
}
