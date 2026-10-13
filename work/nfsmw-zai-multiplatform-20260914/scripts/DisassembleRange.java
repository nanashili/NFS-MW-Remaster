// Disassemble and print an explicit address range.
// Usage: -postScript DisassembleRange.java <start> <end-inclusive>

import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;

public class DisassembleRange extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) {
            throw new IllegalArgumentException("expected start and end addresses");
        }

        Address start = toAddr(args[0]);
        Address end = toAddr(args[1]);
        AddressSet range = new AddressSet(start, end);
        DisassembleCommand command = new DisassembleCommand(start, range, true);
        if (!command.applyTo(currentProgram, monitor)) {
            throw new IllegalStateException("disassembly failed: " + command.getStatusMsg());
        }

        println("RANGE " + start + ".." + end);
        InstructionIterator instructions = currentProgram.getListing().getInstructions(range, true);
        while (instructions.hasNext()) {
            Instruction instruction = instructions.next();
            println(instruction.getAddress() + "  " + instruction);
        }
    }
}
