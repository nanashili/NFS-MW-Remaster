using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        RockportOceanPreview.Run();
        result.Log("Started animated ocean captures with temporary Play Mode lighting.");
    }
}
