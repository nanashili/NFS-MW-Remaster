using System;
using System.IO;

namespace NfsMwRemaster.Driving
{
    /// <summary>Bounded main-thread signal graph. Audio playback is supplied by the vehicle voice adapter.</summary>
    public sealed class AemsEvaluator
    {
        public sealed class Player
        {
            public int Sample, Generation, Control;
            public float Gain = 1, Pitch = 1, TimeScale = 1, ElapsedMilliseconds, RemainingMilliseconds;
            public bool Active, Completed;
        }
        public readonly Player[] Players;
        public readonly ClassOutput[] Outputs;
        public sealed class ClassOutput { public bool Active; public int InterfaceKey; public int[] Parameters; }
        private readonly AemsProgram program;
        private readonly byte[] state;
        private uint random = 0x61c88647;
        private float period;
        public bool Finished { get; private set; }
        public AemsEvaluator(AemsProgram program)
        {
            this.program = program ?? throw new ArgumentNullException(nameof(program));
            if (program.schema != 1 || program.initialState == null || program.initialState.Length > 262144 || program.instructions == null || program.instructions.Length > 16384
                || program.players == null || program.players.Length > 32 || program.tables == null || program.tables.Length > 4194304)
                throw new InvalidDataException("Unsupported AEMS program.");
            state = (byte[])program.initialState.Clone(); Players = new Player[program.players.Length];
            for (int i = 0; i < Players.Length; i++) Players[i] = new Player();
            if (program.outputs == null || program.outputs.Length > 16) throw new InvalidDataException("Invalid AEMS class output budget.");
            Outputs = new ClassOutput[program.outputs.Length];
            for (int i = 0; i < Outputs.Length; i++) Outputs[i] = new ClassOutput { InterfaceKey = program.outputs[i].interfaceKey, Parameters = new int[B(program.outputs[i].address + 13)] };
        }
        public void SetParameter(int index, int value)
        { if (index < 0 || index >= program.parameterCount) throw new ArgumentOutOfRangeException(nameof(index)); W(program.parameterOffset + index * 4, value); }
        /// <summary>Restart the recovered graph without reusing a completed audio-player generation.</summary>
        public void Reset()
        {
            Array.Copy(program.initialState, state, state.Length); Finished = false; period = 0; random = 0x61c88647;
            foreach (var player in Players)
            {
                player.Generation = player.Generation == int.MaxValue ? 1 : player.Generation + 1;
                player.Sample = player.Control = 0; player.Active = player.Completed = false;
                player.Gain = player.Pitch = player.TimeScale = 1; player.ElapsedMilliseconds = player.RemainingMilliseconds = 0;
            }
            foreach (var output in Outputs) { output.Active = false; Array.Clear(output.Parameters, 0, output.Parameters.Length); }
        }
        public int GetParameter(int index)
        { if (index < 0 || index >= program.parameterCount) throw new ArgumentOutOfRangeException(nameof(index)); return R(program.parameterOffset + index * 4); }
        public void Step(float milliseconds)
        {
            if (Finished) return;
            if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds) || milliseconds <= 0 || milliseconds > 100) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            period = milliseconds; int value = 0;
            foreach (var instruction in program.instructions)
            {
                switch (instruction.kind)
                {
                    case 0: value = R(instruction.address); break;
                    case 1: W(instruction.address, value); break;
                    case 2: value = Evaluate(instruction.operation, instruction.address); break;
                    case 3: B(instruction.address, instruction.operation); break;
                    default: throw new InvalidDataException("Unknown AEMS instruction.");
                }
            }
        }
        private int Evaluate(int operation, int p)
        {
            unchecked
            {
                switch (operation)
                {
                    case 0: { int v = R(p + 16); W(p + 16, 0); return v; }
                    case 1: return R(p + 20);
                    case 2: return R(p + 24);
                    case 3: { int v = R(p); W(p, 0); return v; }
                    case 4: if (R(p + 12) != 0) Finished = true; return 0;
                    case 6:
                        if (R(p + 20) >= R(p) && R(p + 20) <= R(p + 4)) return R(p + 20);
                        if (R(p + 16) > 0) { int v = R(p + 8) + (sbyte)B(p + 12); W(p + 8, v > R(p + 4) ? R(p) : v < R(p) ? R(p + 4) : v); }
                        return R(p + 8);
                    case 7: if (R(p + 12) != 0) W(p + 8, R(p) + Random(R(p + 4))); return R(p + 8);
                    case 8: return Shuffle(p);
                    case 10:
                        if (R(p + 20) >= R(p) && R(p + 20) <= R(p + 4))
                        { if (B(p + 16) == 0) { B(p + 16, 1); B(p + 17, 1); return 1; } }
                        else if (R(p + 20) >= R(p + 8) && R(p + 20) <= R(p + 12)) B(p + 16, 0);
                        B(p + 17, 0); return 0;
                    case 11:
                        if (R(p + 8) != 0) F(p, 0);
                        if (F(p) >= 0) { if (F(p) >= R(p + 12)) { F(p, -1); B(p + 4, 1); return 1; } F(p, F(p) + period); }
                        B(p + 4, 0); return 0;
                    case 12:
                        for (int i = 0; i < B(p + 2); i++) if (R(p + U16(p) + i * 4) != 0) { W(p + 4, R(p + 8 + i * 4)); break; }
                        return R(p + 4);
                    case 13: for (int i = 0; i < B(p); i++) if (R(p + 4 + i * 4) != 0) return 1; return 0;
                    case 14: return Envelope(p);
                    case 15: return Table(p);
                    case 16: return Delay(p);
                    case 17: { int n = R(p + 4); return n <= 0 || n > B(p) ? 0 : R(p + 4 + n * 4); }
                    case 19: case 20: case 21: case 22: case 30: return Fold(operation, p);
                    case 23: return R(p) - R(p + 4);
                    case 24: return R(p) * R(p + 4);
                    case 25: return R(p + 4) == 0 ? 0 : (int)((long)R(p) / R(p + 4));
                    case 26: return R(p + 4) == 0 ? 0 : (int)((long)R(p) % R(p + 4));
                    case 27: return UpdatePlayer(p);
                    case 28: return Oscillator(p);
                    case 29: return Ramp(p);
                    case 31: return Math.Max(R(p), R(p + 4) - R(p + 8));
                    case 32: return Math.Min(R(p), R(p + 4) * R(p + 8));
                    case 33: return Math.Min(R(p), R(p + 4));
                    case 34: return Math.Max(R(p), R(p + 4));
                    case 35: return Round((float)R(p + 4) * R(p + 8) * F(p));
                    case 36: return R(p) + R(p + 4);
                    case 37: { int v = B(p + 25); B(p + 25, 0); return v; }
                    case 38: return ControlOutput(p);
                    default: throw new InvalidDataException("Unsupported AEMS node " + operation + ".");
                }
            }
        }
        private int Fold(int operation, int p)
        {
            int count = B(p), at = p + (operation == 21 || operation == 30 ? 8 : 4);
            if (count == 0) throw new InvalidDataException("Empty AEMS arithmetic node.");
            if (operation == 21) { float value = R(at); for (int i = 1; i < count; i++) value *= R(at + i * 4); return Round(value * F(p + 4)); }
            int result = R(at);
            for (int i = 1; i < count; i++) { int v = R(at + i * 4); result = operation == 19 ? Math.Min(result, v) : operation == 20 ? Math.Max(result, v) : unchecked(result + v); }
            return operation == 30 ? Math.Min(result, R(p + 4)) : result;
        }
        private int Table(int p)
        {
            int input = R(p + 12);
            if (input == R(p + 4)) return R(p + 8);
            W(p + 4, input); int table = R(p), size = ReadByte(program.tables, table), count = ReadU16(program.tables, table + 2);
            if (count < 1 || count > 32768 || size != 1 && size != 2 && size != 4) throw new InvalidDataException("Invalid AEMS lookup table.");
            float resolution = BitConverter.Int32BitsToSingle(Read(program.tables, table + 12));
            float position = (Math.Max(Read(program.tables, table + 4), Math.Min(Read(program.tables, table + 8), input)) - (long)Read(program.tables, table + 4)) * resolution;
            if (float.IsNaN(position) || float.IsInfinity(position) || position < 0 || position > count - 1 + 0.01f) throw new InvalidDataException("AEMS table index outside range.");
            int a = Math.Min(count - 1, (int)position), b = Math.Min(count - 1, a + 1);
            int first = TableEntry(table + 16 + a * size, size);
            int result = resolution == 1 ? first : Round(first + (position - a) * ((float)TableEntry(table + 16 + b * size, size) - first));
            W(p + 8, result); return result;
        }
        private int TableEntry(int at, int size) => size == 1 ? (sbyte)ReadByte(program.tables, at) : size == 2 ? (short)ReadU16(program.tables, at) : Read(program.tables, at);
        private int Shuffle(int p)
        {
            if (R(p + U16(p)) == 0) return R(p + 12);
            int range = U16(p + 10), index = U16(p + 8), size = B(p + 2), avoid = (sbyte)B(p + 3);
            if (size != 1 && size != 2 || range < 1 || index >= range) throw new InvalidDataException("Invalid AEMS shuffle.");
            int swap = index + Random(range - index - avoid), a = p + 16 + swap * size, b = p + 16 + index * size;
            int value = size == 1 ? B(a) : U16(a), previous = size == 1 ? B(b) : U16(b);
            if (size == 1) { B(a, previous); B(b, value); } else { U16(a, previous); U16(b, value); }
            index++; B(p + 3, index >= range ? 1 : 0); U16(p + 8, index >= range ? 0 : index);
            W(p + 12, unchecked(value + R(p + 4))); return R(p + 12);
        }
        private int Delay(int p)
        {
            int inputs = p + U16(p), count = U16(p + 2), output = U16(p + 6), input = U16(p + 4);
            if (count < 1 || count > 32768) throw new InvalidDataException("Invalid AEMS delay line.");
            if (R(inputs + 4) != R(p + 8)) { W(p + 8, R(inputs + 4)); input = output + Math.Min(count - 1, Round(Math.Max(0, R(inputs + 4)) / period)); }
            input %= count; output %= count; W(p + 12 + input * 4, R(inputs)); int value = R(p + 12 + output * 4);
            U16(p + 4, input + 1); U16(p + 6, output + 1); return value;
        }
        private int Envelope(int p)
        {
            int control = R(p + U16(p)), previous = B(p + 2), segment = B(p + 3), count = B(p + 16), release = (short)U16(p + 18);
            if (control == 1 && previous == 0) { F(p + 12, F(p + 20)); B(p + 3, 0); EnvelopeSegment(p); }
            else if (control == 3 && previous != 3 && segment < release) { B(p + 3, release); EnvelopeSegment(p); }
            else if ((control == 1 || control == 3) && segment < count)
            {
                F(p + 4, F(p + 4) - period);
                if (F(p + 4) <= 0) { F(p + 12, F(p + 28 + segment * 8)); B(p + 3, ++segment); if (segment < count) EnvelopeSegment(p); else F(p + 12, 0); }
                else F(p + 12, F(p + 12) + F(p + 8));
            }
            else if (control != 2) F(p + 12, 0);
            B(p + 2, control); return Round(F(p + 12));
        }
        private void EnvelopeSegment(int p)
        {
            int n = B(p + 3); if (n >= B(p + 16)) throw new InvalidDataException("Invalid AEMS envelope segment.");
            float duration = F(p + 24 + n * 8); F(p + 4, duration);
            F(p + 8, duration > 0 ? (F(p + 28 + n * 8) - F(p + 12)) * period / duration : 0);
        }
        private int Oscillator(int p)
        {
            if (R(p + 8) <= 0) return 0;
            float phase = F(p + 4); phase -= (float)Math.Floor(phase);
            float wave = B(p) == 0 ? (float)Math.Sin(phase * 2 * Math.PI) : B(p) == 1 ? (phase >= .5f ? 1 : 0) : B(p) == 2 ? phase : 2 * Math.Min(phase, 1 - phase);
            F(p + 4, phase + period / R(p + 8)); return Round(wave * R(p + 12));
        }
        private int Ramp(int p)
        {
            int target = R(p + 24), duration = R(p + 16); float current = F(p);
            if (target == current) return target;
            if (target != R(p + 8) || duration != R(p + 12))
            {
                W(p + 8, target); W(p + 12, duration);
                if (duration <= 0) { F(p, target); return target; }
                F(p + 4, (target - current) * period / duration / 4096f);
            }
            float delta = F(p + 4); current += delta * R(p + 20); current = delta >= 0 ? Math.Min(current, target) : Math.Max(current, target);
            F(p, current); return Round(current);
        }
        private int UpdatePlayer(int p)
        {
            int index = Array.IndexOf(program.players, p); if (index < 0) throw new InvalidDataException("Undeclared AEMS player.");
            var player = Players[index]; int control = Math.Max(0, Math.Min(2, R(p + 24)));
            if (player.Completed) { player.Active = false; player.Completed = false; B(p + 16, 255); }
            if (control != B(p + 12))
            {
                if (control == 0) { player.Active = false; B(p + 16, 255); }
                else if (control == 1 && !player.Active && !(B(p + 14) == 1 && B(p + 15) == 2))
                {
                    int table = R(p + 4), count = Read(program.tables, table); if (count < 1 || count > 4096) throw new InvalidDataException("Invalid AEMS sample group.");
                    int slot = Math.Max(0, Math.Min(count - 1, R(p + 20))), entry = table + 4 + slot * 12;
                    if (ReadByte(program.tables, entry) != 0) throw new InvalidDataException("AEMS streaming player is unsupported.");
                    player.Sample = Read(program.tables, entry + 4); player.Active = player.Sample != 0; player.Generation++;
                    player.ElapsedMilliseconds = player.RemainingMilliseconds = 0; B(p + 16, player.Active ? 0 : 255);
                }
                B(p + 13, B(p + 12)); B(p + 12, control);
            }
            player.Control = control; player.Gain = 1; player.Pitch = 1; player.TimeScale = 1;
            int inputs = B(p + 14); if (inputs > 12) throw new InvalidDataException("Invalid AEMS player input count.");
            for (int i = 0; i < inputs; i++)
            {
                int at = p + 28 + i * 12, value = R(at + 8);
                switch (B(at))
                { case 0: player.Pitch = Math.Max(0, Math.Min(65535, value)) / 4096f; break;
                  case 1: player.TimeScale = Math.Max(2048, Math.Min(8192, value)) / 4096f; break;
                  case 2: player.Gain = Math.Max(0, Math.Min(32767, value)) / 32767f; break; }
            }
            if (B(p + 15) != 0) { W(p + 28 + inputs * 12, player.Active ? Round(player.RemainingMilliseconds) : 0); W(p + 32 + inputs * 12, player.Active ? Round(player.ElapsedMilliseconds) : 0); }
            return player.Active ? control : 0;
        }
        private int ControlOutput(int p)
        {
            int index = -1; for (int i = 0; i < program.outputs.Length; i++) if (program.outputs[i].address == p) { index = i; break; }
            if (index < 0) throw new InvalidDataException("Undeclared AEMS class output.");
            var output = Outputs[index]; int count = output.Parameters.Length, inputs = p + 16 + (B(p + 12) != 0 ? count * 8 : 0);
            if (R(inputs + 4) != 0) output.Active = false; else if (R(inputs) != 0) output.Active = true;
            if (output.Active) for (int i = 0; i < count; i++) { int value = R(inputs + 8 + i * 4); output.Parameters[i] = B(p + 12) == 0 ? value : Math.Max(R(p + 16 + i * 8), Math.Min(R(p + 20 + i * 8), value)); }
            return output.Active ? 1 : 0;
        }
        private int Random(int count)
        { if (count <= 0) throw new InvalidDataException("Invalid AEMS random range."); random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (int)(random % (uint)count); }
        private static int Round(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite AEMS arithmetic."); return value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)Math.Round(value); }
        private int R(int p) => Read(state, p);
        private void W(int p, int v) { Bounds(state, p, 4); unchecked { state[p] = (byte)v; state[p + 1] = (byte)(v >> 8); state[p + 2] = (byte)(v >> 16); state[p + 3] = (byte)(v >> 24); } }
        private int B(int p) => ReadByte(state, p);
        private void B(int p, int value) { Bounds(state, p, 1); state[p] = unchecked((byte)value); }
        private int U16(int p) => ReadU16(state, p);
        private void U16(int p, int value) { Bounds(state, p, 2); state[p] = unchecked((byte)value); state[p + 1] = unchecked((byte)(value >> 8)); }
        private float F(int p) => BitConverter.Int32BitsToSingle(R(p));
        private void F(int p, float value) => W(p, BitConverter.SingleToInt32Bits(value));
        private static int Read(byte[] bytes, int p) { Bounds(bytes, p, 4); return BitConverter.ToInt32(bytes, p); }
        private static int ReadByte(byte[] bytes, int p) { Bounds(bytes, p, 1); return bytes[p]; }
        private static int ReadU16(byte[] bytes, int p) { Bounds(bytes, p, 2); return BitConverter.ToUInt16(bytes, p); }
        private static void Bounds(byte[] bytes, int p, int n)
        { if (p < 0 || p > bytes.Length - n) throw new InvalidDataException("AEMS graph accessed data outside its bounds."); }
    }
}
