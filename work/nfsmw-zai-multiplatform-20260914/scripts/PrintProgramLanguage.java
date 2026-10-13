import ghidra.app.script.GhidraScript;

public class PrintProgramLanguage extends GhidraScript {
    @Override
    public void run() throws Exception {
        println("LANGUAGE " + currentProgram.getLanguageID());
        println("COMPILER " + currentProgram.getCompilerSpec().getCompilerSpecID());
    }
}
