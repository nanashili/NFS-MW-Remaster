// Headless Ghidra post-script for the authorized nfsmw PC sample.
// The range is the zBWare split from config/SPEED_EXE_1_3/splits.json.
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardOpenOption;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

public class ExportZbware extends GhidraScript {
    private static final long RANGE_START = 0x0045c929L;
    private static final long RANGE_END = 0x004664a0L;

    @Override
    public void run() throws Exception {
        Path output = getScriptArgs().length == 0
            ? Paths.get("zbware-ghidra-decomp.txt")
            : Paths.get(getScriptArgs()[0]);
        Files.createDirectories(output.toAbsolutePath().getParent());

        FunctionManager manager = currentProgram.getFunctionManager();
        List<Function> functions = new ArrayList<>();
        FunctionIterator iterator = manager.getFunctions(true);
        while (iterator.hasNext()) {
            Function function = iterator.next();
            long address = function.getEntryPoint().getOffset();
            if (address >= RANGE_START && address < RANGE_END) {
                functions.add(function);
            }
        }
        functions.sort(Comparator.comparingLong(f -> f.getEntryPoint().getOffset()));

        DecompInterface decompiler = new DecompInterface();
        DecompileOptions options = new DecompileOptions();
        decompiler.setOptions(options);
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(false);
        if (!decompiler.openProgram(currentProgram)) {
            throw new IllegalStateException("Unable to open program in decompiler: " +
                decompiler.getLastMessage());
        }

        List<String> lines = new ArrayList<>();
        lines.add("# Ghidra 12 zBWare headless decompilation");
        lines.add("# sample=" + currentProgram.getExecutablePath());
        lines.add(String.format("# range=0x%08x..0x%08x (end exclusive)", RANGE_START, RANGE_END));
        lines.add("# functions=" + functions.size());
        lines.add("");

        int index = 0;
        for (Function function : functions) {
            monitor.setMessage("Decompiling zBWare " + (++index) + "/" + functions.size() +
                " " + function.getName());
            if (monitor.isCancelled()) {
                break;
            }
            Address entry = function.getEntryPoint();
            Address bodyMin = function.getBody().getMinAddress();
            Address bodyMax = function.getBody().getMaxAddress();
            lines.add(String.format("===== %s @ %s body=%s..%s =====",
                function.getName(), entry, bodyMin, bodyMax));
            DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
            if (result != null && result.decompileCompleted() &&
                result.getDecompiledFunction() != null) {
                lines.add(result.getDecompiledFunction().getC());
            } else {
                String error = result == null ? "null result" : result.getErrorMessage();
                lines.add("[DECOMPILATION_FAILED] " + error);
            }
            lines.add("");
        }

        Files.write(output, lines, StandardCharsets.UTF_8,
            StandardOpenOption.CREATE, StandardOpenOption.TRUNCATE_EXISTING,
            StandardOpenOption.WRITE);
        println("Exported " + functions.size() + " zBWare functions to " + output);
    }
}
