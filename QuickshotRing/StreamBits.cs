using System;
using Alta.Serialization;

namespace QuickshotRing {
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

        public bool Finished => (stream as StreamReader)?.IsFinished ?? true;

        // Clamped: the game's reader keeps counting past the end of the message.
        public int BitsLeft => stream is StreamReader r ? Math.Max(0, (int)(r.TotalByteCount * 8) - (int)r.BitIndex) : 0;
    }
}
