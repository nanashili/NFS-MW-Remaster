using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NfsMwRemaster.Driving.AudioAnalysis
{
    /// <summary>Recognizes the bounded, straight-line AEMS compiler template. Never executes x86.</summary>
    public static class BlackBoxAemsCompiler
    {
        public const string Version = "aems-pc-dataflow/1";
        public static AemsProgram[] Compile(byte[] bank)
        {
            if (bank == null || bank.Length > 32 * 1024 * 1024 || U32(bank, 0) != 0x434b4241 || bank[8] != 0)
                throw new InvalidDataException("AEMS routing requires a PC ABKC bank.");
            int count = U16(bank, 10), module = I32(bank, 28), staticEnd = I32(bank, 32);
            if (count < 1 || count > 64 || staticEnd < 92) throw new InvalidDataException("Invalid AEMS module budget.");
            var tables = Slice(bank, 0, staticEnd); var result = new List<AemsProgram>();
            for (int i = 0; i < count; i++)
            {
                Bounds(bank, module, 60);
                int players = bank[module + 36], controllers = bank[module + 39];
                if (players > 32 || controllers > 16) throw new InvalidDataException("Unsupported AEMS player/controller budget.");
                int code = I32(bank, module + 40), data = I32(bank, module + 44), size = I32(bank, module + 48);
                if (size < 24 || size > 256 * 1024 || code >= data || data + (long)size > staticEnd) throw new InvalidDataException("Invalid AEMS data bounds.");
                var p = new AemsProgram { moduleId = I32(bank, module), initialState = Slice(bank, data, size), tables = tables,
                    players = Enumerable.Range(0, players).Select(n => I32(bank, module + 60 + n * 4)).ToArray() };
                var identity = Interface(bank, module + 4); p.interfaceName = identity.Item1; p.interfaceKey = identity.Item2;
                p.outputs = Enumerable.Range(0, controllers).Select(n =>
                {
                    int address = I32(bank, module + 60 + (players + n) * 4);
                    Bounds(p.initialState, address, 16); var id = Interface(bank, data + address);
                    return new AemsClassOutput { address = address, interfaceName = id.Item1, interfaceKey = id.Item2 };
                }).ToArray();
                var operations = new List<AemsInstruction>(); int at = code, state = 24, pushed = 0;
                Expect(bank, ref at, 0x56, 0x8b, 0x74, 0x24, 0x08);
                bool ended = false;
                while (at < data && operations.Count < 16384)
                {
                    if (Matches(bank, at, 0x8b, 0x06, 0x03, 0x46, 0x04)) { operations.Add(new AemsInstruction(2, state, 36)); at += 5; continue; }
                    if (Matches(bank, at, 0x8b, 0x06, 0x2b, 0x46, 0x04)) { operations.Add(new AemsInstruction(2, state, 23)); at += 5; continue; }
                    if (Matches(bank, at, 0x8b, 0x06, 0x0f, 0xaf, 0x46, 0x04)) { operations.Add(new AemsInstruction(2, state, 24)); at += 6; continue; }
                    if (Matches(bank, at, 0x8b, 0x06, 0x8b, 0x4e, 0x04, 0x3b, 0xc1, 0x7c, 0x02, 0x8b, 0xc1)) { operations.Add(new AemsInstruction(2, state, 33)); at += 11; continue; }
                    if (Matches(bank, at, 0x8b, 0x06, 0x8b, 0x4e, 0x04, 0x3b, 0xc1, 0x7f, 0x02, 0x8b, 0xc1)) { operations.Add(new AemsInstruction(2, state, 34)); at += 11; continue; }
                    int opcode = bank[at++];
                    if (opcode == 0x5e && pushed == 0) { Expect(bank, ref at, 0xc3); ended = true; break; }
                    if (opcode == 0x56) { pushed++; continue; }
                    if (opcode == 0xe8)
                    {
                        int fn = I32(bank, at); at += 4;
                        if (fn < 0 || fn > 39) throw new InvalidDataException("Unknown AEMS node.");
                        Bounds(p.initialState, state, 4); operations.Add(new AemsInstruction(2, state, fn));
                        if (fn == 1) { p.parameterOffset = state + 20; p.parameterCount = p.initialState[state + 16]; Bounds(p.initialState, p.parameterOffset, p.parameterCount * 4); }
                        continue;
                    }
                    if (opcode == 0x8b || opcode == 0x89)
                    {
                        int mod = bank[at++], displacement;
                        if (mod == 0x46) displacement = unchecked((sbyte)bank[at++]);
                        else if (mod == 0x86) { displacement = I32(bank, at); at += 4; }
                        else if (mod == 0x06) displacement = 0;
                        else throw new InvalidDataException("Unsupported AEMS data move " + mod.ToString("X") + " at " + (at - 2).ToString("X"));
                        int address = checked(state + displacement); Bounds(p.initialState, address, 4);
                        operations.Add(new AemsInstruction(opcode == 0x8b ? 0 : 1, address)); continue;
                    }
                    if (opcode == 0xc6)
                    {
                        Expect(bank, ref at, 0x46); int address = state + unchecked((sbyte)bank[at++]);
                        int value = bank[at++]; Bounds(p.initialState, address, 1);
                        operations.Add(new AemsInstruction(3, address, value)); continue;
                    }
                    if (opcode == 0x83 || opcode == 0x81)
                    {
                        int register = bank[at++]; int amount = opcode == 0x83 ? unchecked((sbyte)bank[at++]) : I32(bank, at);
                        if (opcode == 0x81) at += 4;
                        if (register == 0xc6) { state = checked(state + amount); if (state < 24 || state > size) throw new InvalidDataException("AEMS state cursor outside data."); continue; }
                        if (register == 0xc4 && amount == pushed * 4) { pushed = 0; continue; }
                    }
                    throw new InvalidDataException("Unrecognized AEMS compiler instruction " + opcode.ToString("X") + " at " + (at - 1).ToString("X"));
                }
                if (!ended) throw new InvalidDataException("Incomplete AEMS program.");
                foreach (int player in p.players) Bounds(p.initialState, player, 28);
                p.instructions = operations.ToArray(); result.Add(p); module += 60 + 4 * (players + controllers);
            }
            if (U32(bank, staticEnd) == 0x41303153)
            {
                // S10A has a uint16 sample index at +0 and a stream offset at +8.
                // Normalize RAM entries to the portable patch representation (zero remains silence).
                var visited = new HashSet<int>();
                foreach (var program in result) foreach (int player in program.players)
                {
                    int table = I32(program.initialState, player + 4); if (!visited.Add(table)) continue;
                    int entries = I32(tables, table); if (entries < 1 || entries > 4096) throw new InvalidDataException("Invalid S10A sample group.");
                    Bounds(tables, table + 4, entries * 12);
                    for (int entry = table + 4; entry < table + 4 + entries * 12; entry += 12)
                    {
                        int index = U16(tables, entry);
                        if (index != 65535 && I32(tables, entry + 8) != -1) throw new InvalidDataException("S10A streaming requires an external stream and is unsupported.");
                        tables[entry] = 0; tables[entry + 1] = tables[entry + 2]; tables[entry + 2] = tables[entry + 3] = 0;
                        byte[] target = BitConverter.GetBytes(index == 65535 ? 0 : index + 1); Array.Copy(target, 0, tables, entry + 4, 4);
                    }
                }
            }
            else if (U32(bank, staticEnd) != 0x6c4b4e42) throw new InvalidDataException("Unknown AEMS embedded sample bank.");
            return result.ToArray();
        }
        private static void Expect(byte[] data, ref int at, params byte[] bytes)
        { foreach (byte b in bytes) { Bounds(data, at, 1); if (data[at++] != b) throw new InvalidDataException("Unknown AEMS compiler template at " + (at - 1).ToString("X") + " expected " + b.ToString("X") + " got " + data[at - 1].ToString("X")); } }
        private static bool Matches(byte[] data, int at, params byte[] bytes)
        { if (at < 0 || at > data.Length - bytes.Length) return false; for (int i = 0; i < bytes.Length; i++) if (data[at + i] != bytes[i]) return false; return true; }
        private static void Bounds(byte[] data, int p, int count)
        { if (data == null || p < 0 || count < 0 || p > data.Length - count) throw new InvalidDataException("AEMS offset outside source."); }
        private static uint U32(byte[] data, int p) { Bounds(data, p, 4); return BitConverter.ToUInt32(data, p); }
        private static int I32(byte[] data, int p) => unchecked((int)U32(data, p));
        private static int U16(byte[] data, int p) { Bounds(data, p, 2); return BitConverter.ToUInt16(data, p); }
        private static byte[] Slice(byte[] data, int p, int count) { Bounds(data, p, count); var result = new byte[count]; Array.Copy(data, p, result, 0, count); return result; }
        private static Tuple<string, int> Interface(byte[] bank, int handle)
        {
            int table = I32(bank, 56), count = I32(bank, table); if (count < 1 || count > 4096) throw new InvalidDataException("Invalid AEMS interface table.");
            Bounds(bank, table + 4, count * 12);
            for (int i = 0; i < count; i++)
            {
                int at = table + 4 + i * 12; if (I32(bank, at) != handle) continue;
                if (bank[at + 8] != 1) throw new InvalidDataException("AEMS module requires a class interface.");
                int identity = I32(bank, at + 4), start = identity + 4, end = start; Bounds(bank, identity, 5);
                while (end < bank.Length && end - start <= 128 && bank[end] != 0) end++;
                if (end >= bank.Length || end - start > 128) throw new InvalidDataException("Invalid AEMS interface name.");
                return Tuple.Create(Encoding.ASCII.GetString(bank, start, end - start), I32(bank, identity));
            }
            throw new InvalidDataException("Missing AEMS class interface identity.");
        }
    }
}
