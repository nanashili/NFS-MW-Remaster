// Decompile one function selected by address from an already analyzed program.
// Usage: -postScript DecompileFunction.java <address>

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DecompileFunction extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("expected one function address");
        }

        Address address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(args[0]);
        Function function = currentProgram.getFunctionManager().getFunctionAt(address);
        if (function == null) {
            function = currentProgram.getFunctionManager().getFunctionContaining(address);
        }
        if (function == null) {
            throw new IllegalArgumentException("no function at " + address);
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        DecompileResults results = decompiler.decompileFunction(function, 120, monitor);
        if (!results.decompileCompleted() || results.getDecompiledFunction() == null) {
            throw new IllegalStateException(results.getErrorMessage());
        }

        println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
        println(results.getDecompiledFunction().getC());
        decompiler.dispose();
    }
}
