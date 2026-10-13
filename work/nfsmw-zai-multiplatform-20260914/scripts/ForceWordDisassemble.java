import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Instruction;

public class ForceWordDisassemble extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) throw new IllegalArgumentException("expected start/end");
        Address start = toAddr(args[0]);
        Address end = toAddr(args[1]);
        currentProgram.getListing().clearCodeUnits(start, end, false, monitor);
        println("RANGE " + start + ".." + end);
        for (Address a = start; a.compareTo(end) <= 0; a = a.add(4)) {
            Address wordEnd = a.add(3);
            DisassembleCommand command = new DisassembleCommand(a, new AddressSet(a, wordEnd), false);
            command.applyTo(currentProgram, monitor);
            Instruction instruction = currentProgram.getListing().getInstructionAt(a);
            println(a + "  " + (instruction == null ? "<none>" : instruction.toString()));
        }
    }
}
