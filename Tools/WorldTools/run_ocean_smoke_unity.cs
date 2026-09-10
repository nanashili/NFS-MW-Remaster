using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        RockportOceanSmoke.Run();
        result.Log("Started temporary Play Mode ocean and collision fixtures; result is written to ocean-playmode-verification.json.");
    }
}
