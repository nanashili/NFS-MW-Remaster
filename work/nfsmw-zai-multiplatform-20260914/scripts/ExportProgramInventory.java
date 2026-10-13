//@category NFS

import java.io.FileWriter;
import java.io.PrintWriter;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;

public class ExportProgramInventory extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("usage: ExportProgramInventory.java <output.tsv>");
        }
        int count = 0;
        try (PrintWriter out = new PrintWriter(new FileWriter(args[0], false))) {
            out.println("address\tsize\tname");
            FunctionIterator functions = currentProgram.getFunctionManager().getFunctions(true);
            while (functions.hasNext()) {
                Function function = functions.next();
                out.printf("%s\t%d\t%s%n", function.getEntryPoint(), function.getBody().getNumAddresses(), function.getName());
                count++;
            }
        }
        println("Exported " + count + " functions from " + currentProgram.getName());
    }
}
