using System;

namespace NfsMwRemaster.Driving
{
    /// <summary>Portable data flow recovered from a PC AEMS bank. Contains no executable game code.</summary>
    [Serializable] public sealed class AemsProgram
    {
        public int schema = 1, moduleId, parameterOffset, parameterCount;
        public string interfaceName = "";
        public int interfaceKey;
        public byte[] initialState = Array.Empty<byte>(), tables = Array.Empty<byte>();
        public AemsInstruction[] instructions = Array.Empty<AemsInstruction>();
        public int[] players = Array.Empty<int>();
        public AemsClassOutput[] outputs = Array.Empty<AemsClassOutput>();
    }
    [Serializable] public sealed class AemsClassOutput
    {
        public int address, interfaceKey;
        public string interfaceName = "";
    }
    [Serializable] public struct AemsInstruction
    {
        // Load/store an integer accumulator, or evaluate a whitelisted AEMS node.
        public int kind, address, operation;
        public AemsInstruction(int kind, int address, int operation = 0)
        { this.kind = kind; this.address = address; this.operation = operation; }
    }
}
