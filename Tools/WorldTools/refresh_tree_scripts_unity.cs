using UnityEditor;
using UnityEditor.Compilation;
internal class CommandScript : IRunCommand {
    public void Execute(ExecutionResult result) {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        CompilationPipeline.RequestScriptCompilation();
        result.Log("Requested import and compilation of updated tree scripts.");
    }
}
