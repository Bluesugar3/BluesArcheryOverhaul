using System;
using Alta.Serialization;

namespace BluesArcheryOverhaul {
    /// <summary>The game's message stream seen through the mod's bit interfaces (Build.cs, Wire.cs).
    /// Writing goes through <c>Stream.SerializeBits</c> on whatever writing stream the game hands us; reading
    /// only makes sense on a <c>StreamReader</c>, and any other stream reads as finished.</summary>
    internal sealed class StreamBits : IBitWriter, IBitReader {
        readonly Stream stream;

        internal StreamBits(Stream stream) { this.stream = stream; }

        public void Write(uint value, int bits) => stream.SerializeBits(ref value, bits);

        public uint Read(int bits) {
            uint value = 0;
            stream.SerializeBits(ref value, bits);
            return value;
        }

#if QUEST
        // Quest (IL2CPP): a game object must be cast with TryCast (a C# cast of the proxy gives null), and the Quest StreamReader
        // has no IsFinished or TotalByteCount: its BitReader has the numbers they are made of (BitsRead, TotalBits).
        public bool Finished {
            get { var r = stream.TryCast<StreamReader>()?.reader; return r == null || r.BitsRead >= r.TotalBits - 7; }
        }

        public int BitsLeft {
            get { var r = stream.TryCast<StreamReader>()?.reader; return r == null ? 0 : Math.Max(0, (int)r.TotalBits - (int)r.BitsRead); }
        }
#else
        public bool Finished => (stream as StreamReader)?.IsFinished ?? true;

        // Clamped: the game's reader keeps counting past the end of the message.
        public int BitsLeft => stream is StreamReader r ? Math.Max(0, (int)(r.TotalByteCount * 8) - (int)r.BitIndex) : 0;
#endif
    }
}
