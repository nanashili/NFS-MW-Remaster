// Dump the decoded instruction listing for a function selected by address.
// Usage: -postScript DumpFunctionListing.java <address>

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;

public class DumpFunctionListing extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("expected one function address");
        }

        Address address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(args[0]);
        Function function = currentProgram.getFunctionManager().getFunctionContaining(address);
        if (function == null) {
            throw new IllegalArgumentException("no function at " + address);
        }

        println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
        InstructionIterator instructions = currentProgram.getListing().getInstructions(function.getBody(), true);
        while (instructions.hasNext()) {
            Instruction instruction = instructions.next();
            println(instruction.getAddress() + "  " + instruction);
        }
    }
}
