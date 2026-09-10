using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) { RockportReplacementTrees.CreateLibrary(); result.Log("Created independent tree model and texture library."); }
}
