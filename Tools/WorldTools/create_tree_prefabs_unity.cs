using NfsMwRemaster.Driving.Editor;
// Compatibility entry point: all tree generation now uses independent models.
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) {
        RockportReplacementTrees.CreateLibrary();
        RockportReplacementTrees.CreatePrefabs(250);
        result.Log("Created independent replacement tree assets; source tree meshes are not used.");
    }
}
