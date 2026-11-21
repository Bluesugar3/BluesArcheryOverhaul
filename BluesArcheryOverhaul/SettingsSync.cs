using System;
using System.Collections.Generic;
using System.Text;

// Unity-free, compiled by BluesArcheryOverhaul.Tests (tested in SettingsSyncTests.cs), so only System.* in here.
// Server-set settings (2026-11-20, design docs/superpowers/specs/2026-11-20-server-settings-design.md): while a game is on a server,
// every setting of the mod comes from the server. This file holds the rules: which entries the server sets, what it sends (only what
// differs from the mod's built-in values), the byte layout of a send, its parts, what a game must set, a game's receipt of a send in
// parts, and the hold of the player's own values. ServerSettings.cs is the part that touches the game; the two join kinds that carry it
// are in JoinRules.cs.
namespace BluesArcheryOverhaul {
    /// <summary>One setting as text: "Section.Name", its value now and its built-in default (SettingsChecksum.Format).</summary>
    internal struct SettingEntry { public string Key, Text, Default; }

    internal static class SettingsSync {
        /// <summary>The per-machine sections: never sent, never set by a server.</summary>
        internal static readonly string[] LocalSections = { "Log", "Timing", "Telemetry", "Join", "Settings" };
        internal const int MaxParts = 255, MaxPartBytes = 900, MaxEntries = 4000;
        const int MaxVarintBytes = 5;

        /// <summary>A "Section.Name" key the server sets: it has a section and the section is not a local one (exact, case-sensitive,
        /// as MelonLoader's category identifiers).</summary>
        internal static bool ServerSet(string? key) {
            if (string.IsNullOrEmpty(key)) return false;
            int dot = key!.IndexOf('.');
            if (dot <= 0) return false;
            string section = key.Substring(0, dot);
            foreach (string s in LocalSections) if (string.Equals(s, section, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>What the server sends: every server-set entry whose value differs from its built-in default, in the given order.</summary>
        internal static List<KeyValuePair<string, string>> Changed(IEnumerable<SettingEntry> all) {
            var list = new List<KeyValuePair<string, string>>();
            foreach (SettingEntry e in all)
                if (ServerSet(e.Key) && !string.Equals(e.Text ?? "", e.Default ?? "", StringComparison.Ordinal))
                    list.Add(new KeyValuePair<string, string>(e.Key, e.Text ?? ""));
            return list;
        }

        /// <summary>The check over every server-set entry's key and text (SettingsChecksum.Compute: order does not matter).</summary>
        internal static uint Check(IEnumerable<SettingEntry> all) {
            var list = new List<KeyValuePair<string, string>>();
            foreach (SettingEntry e in all) if (ServerSet(e.Key)) list.Add(new KeyValuePair<string, string>(e.Key, e.Text ?? ""));
            return SettingsChecksum.Compute(list);
        }

        // ---------------------------------------------------------------- bytes: a varint count, then per entry a varint length and
        // the UTF-8 key, a varint length and the UTF-8 value. Nothing else.

        internal static byte[] Pack(List<KeyValuePair<string, string>> changed) {
            var bytes = new List<byte>();
            Varint(bytes, changed.Count);
            foreach (KeyValuePair<string, string> kv in changed) {
                Bytes(bytes, Encoding.UTF8.GetBytes(kv.Key ?? ""));
                Bytes(bytes, Encoding.UTF8.GetBytes(kv.Value ?? ""));
            }
            return bytes.ToArray();
        }

        static void Bytes(List<byte> to, byte[] b) { Varint(to, b.Length); to.AddRange(b); }

        static void Varint(List<byte> to, int n) {
            uint v = (uint)n;
            while (v >= 0x80) { to.Add((byte)(v | 0x80)); v >>= 7; }
            to.Add((byte)v);
        }

        /// <summary>False (and an empty list) for anything that is not exactly one whole send: cut short, a length past the end, a
        /// runaway number, bytes left over, more than <see cref="MaxEntries"/> entries. Never throws.</summary>
        internal static bool TryUnpack(byte[] bytes, out List<KeyValuePair<string, string>> changed) {
            changed = new List<KeyValuePair<string, string>>();
            if (bytes == null) return false;
            try {
                int at = 0;
                if (!ReadVarint(bytes, ref at, out int count) || count > MaxEntries) return false;
                var list = new List<KeyValuePair<string, string>>(Math.Min(count, 256));
                for (int i = 0; i < count; i++) {
                    if (!ReadText(bytes, ref at, out string key) || !ReadText(bytes, ref at, out string value)) return false;
                    list.Add(new KeyValuePair<string, string>(key, value));
                }
                if (at != bytes.Length) return false;
                changed = list;
                return true;
            }
            catch (Exception) { changed = new List<KeyValuePair<string, string>>(); return false; }
        }

        static bool ReadText(byte[] b, ref int at, out string text) {
            text = "";
            if (!ReadVarint(b, ref at, out int n) || n > b.Length - at) return false;
            text = Encoding.UTF8.GetString(b, at, n);
            at += n;
            return true;
        }

        static bool ReadVarint(byte[] b, ref int at, out int n) {
            n = 0;
            uint v = 0;
            for (int i = 0; i < MaxVarintBytes; i++) {
                if (at >= b.Length) return false;
                byte x = b[at++];
                v |= (uint)(x & 0x7F) << (7 * i);
                if ((x & 0x80) == 0) {
                    if (v > int.MaxValue) return false;
                    n = (int)v;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The packed bytes in parts of at most <paramref name="partBytes"/> (held to 1..MaxPartBytes); always at least one
        /// part, even for no bytes. The caller refuses to send more than <see cref="MaxParts"/>.</summary>
        internal static List<byte[]> Split(byte[] packed, int partBytes = MaxPartBytes) {
            if (partBytes < 1) partBytes = 1;
            if (partBytes > MaxPartBytes) partBytes = MaxPartBytes;
            var parts = new List<byte[]>();
            packed ??= new byte[0];
            for (int at = 0; at < packed.Length; at += partBytes) {
                int n = Math.Min(partBytes, packed.Length - at);
                var p = new byte[n];
                Buffer.BlockCopy(packed, at, p, 0, n);
                parts.Add(p);
            }
            if (parts.Count == 0) parts.Add(new byte[0]);
            return parts;
        }

        /// <summary>What a game must set: for every server-set entry of its own, the server's text when sent, else the entry's own
        /// built-in default (never the player's file). A sent key that is not one of the game's server-set entries is counted in
        /// <paramref name="unknownKeys"/> and not targeted. A key sent twice: the last one counts.</summary>
        internal static List<KeyValuePair<string, string>> Targets(IEnumerable<SettingEntry> mine, List<KeyValuePair<string, string>> changed, out int unknownKeys) {
            unknownKeys = 0;
            var own = new Dictionary<string, SettingEntry>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (SettingEntry e in mine) {
                if (!ServerSet(e.Key) || own.ContainsKey(e.Key)) continue;
                own[e.Key] = e;
                order.Add(e.Key);
            }
            var sent = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in changed) {
                if (kv.Key == null || !own.ContainsKey(kv.Key)) { unknownKeys++; continue; }
                sent[kv.Key] = kv.Value ?? "";
            }
            var targets = new List<KeyValuePair<string, string>>(order.Count);
            foreach (string k in order)
                targets.Add(new KeyValuePair<string, string>(k, sent.TryGetValue(k, out string v) ? v : own[k].Default ?? ""));
            return targets;
        }

        /// <summary>Serial-number order on 16 bits: true when <paramref name="a"/> comes after <paramref name="b"/> (0 after 65535).</summary>
        internal static bool Newer(int a, int b) {
            int d = (a - b) & 0xFFFF;
            return d != 0 && d < 0x8000;
        }
    }

    /// <summary>One game's receipt of the server's sends on one connection, in parts. A newer sequence replaces an older one whole (so
    /// parts of two sends are never mixed); a part of an older or of the last finished sequence is ignored; a part that disagrees with
    /// the send in hand (another part count or check), or is out of bounds, is refused.</summary>
    internal sealed class SettingsInbox {
        int sequence = -1, parts, have, finished = -1;
        uint check;
        byte[]?[] got = new byte[0][];

        /// <summary>True once the send is complete: <paramref name="whole"/> is its parts joined in order.</summary>
        internal bool Add(int sequence, int part, int parts, uint check, byte[] payload, out byte[] whole, out uint wholeCheck) {
            whole = new byte[0]; wholeCheck = 0;
            if (payload == null || payload.Length > SettingsSync.MaxPartBytes) return false;
            if (parts < 1 || parts > SettingsSync.MaxParts || part < 0 || part >= parts) return false;
            sequence &= 0xFFFF;
            if (finished >= 0 && (sequence == finished || SettingsSync.Newer(finished, sequence))) return false;   // late or repeated
            if (this.sequence < 0 || (sequence != this.sequence && SettingsSync.Newer(sequence, this.sequence))) {
                this.sequence = sequence; this.parts = parts; this.check = check; have = 0;
                got = new byte[parts][];
            }
            else if (sequence != this.sequence) return false;                                                      // older than in hand
            else if (parts != this.parts || check != this.check) return false;                                     // damage
            if (got[part] != null) return false;                                                                   // duplicate
            got[part] = payload;
            if (++have < this.parts) return false;
            int total = 0;
            foreach (byte[]? p in got) total += p!.Length;
            whole = new byte[total];
            int at = 0;
            foreach (byte[]? p in got) { Buffer.BlockCopy(p!, 0, whole, at, p!.Length); at += p.Length; }
            wholeCheck = this.check;
            finished = this.sequence;
            this.sequence = -1; got = new byte[0][]; have = 0;
            return true;
        }
    }

    /// <summary>The player's own values (texts by key, server-set entries only) while a server's are held.</summary>
    internal sealed class SettingsHold {
        readonly Dictionary<string, string> own = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> order = new List<string>();

        internal bool Held { get; private set; }

        /// <summary>Remembers the text of every server-set entry; only the first time (until <see cref="Release"/>).</summary>
        internal void Take(IEnumerable<SettingEntry> mine) {
            if (Held) return;
            own.Clear(); order.Clear();
            foreach (SettingEntry e in mine) Put(e);
            Held = true;
        }

        /// <summary>The player's file was reloaded while held: these values are now theirs (keys not in it keep what was remembered).</summary>
        internal void FileChanged(IEnumerable<SettingEntry> reloaded) {
            if (!Held) return;
            foreach (SettingEntry e in reloaded) Put(e);
        }

        void Put(SettingEntry e) {
            if (!SettingsSync.ServerSet(e.Key)) return;
            if (!own.ContainsKey(e.Key)) order.Add(e.Key);
            own[e.Key] = e.Text ?? "";
        }

        /// <summary>Their own texts to put back, in the order first seen; no longer held. Empty when nothing was held.</summary>
        internal List<KeyValuePair<string, string>> Release() {
            var list = new List<KeyValuePair<string, string>>();
            if (!Held) return list;
            foreach (string k in order) list.Add(new KeyValuePair<string, string>(k, own[k]));
            own.Clear(); order.Clear();
            Held = false;
            return list;
        }

        /// <summary>The remembered texts, without letting go (put in before a reload of the player's file, so a key it lacks keeps
        /// the player's value).</summary>
        internal List<KeyValuePair<string, string>> Own() {
            var list = new List<KeyValuePair<string, string>>();
            if (Held) foreach (string k in order) list.Add(new KeyValuePair<string, string>(k, own[k]));
            return list;
        }
    }

    /// <summary>One whole message copied off its reader (at most <see cref="MaxBits"/>), so the kind can be looked at before the
    /// message's own reader reads it from the start: Here, Hello and the two settings kinds share message 27. Bits are kept most
    /// significant first, as the game's BitReader reads them (decompiled: its scratch shifts left and a read takes the top bits), so
    /// a value read in one size from the tape is the value read in that size from the message.</summary>
    internal sealed class BitTape : IBitReader {
        /// <summary>The game's receive buffer is 2048 words (MessageProcessor): no message is longer.</summary>
        internal const int MaxBits = 2048 * 32;
        readonly uint[] words;
        readonly int count;
        int pos;

        BitTape(uint[] words, int count) { this.words = words; this.count = count; }

        internal static BitTape From(IBitReader r) {
            int n = Math.Min(r.BitsLeft, MaxBits);
            var w = new uint[(n + 31) / 32];
            int left = n, i = 0;
            while (left > 0) {
                int take = Math.Min(32, left);
                uint v = r.Read(take);
                w[i++] = take == 32 ? v : v << (32 - take);   // msb first, as the bits were written
                left -= take;
            }
            return new BitTape(w, n);
        }

        bool Bit(int at) => at < count && ((words[at >> 5] >> (31 - (at & 31))) & 1u) != 0;

        public uint Read(int bits) {
            uint v = 0;
            for (int i = 0; i < bits; i++, pos++) v = (v << 1) | (Bit(pos) ? 1u : 0u);
            return v;
        }

        /// <summary>The next bits, without moving on.</summary>
        internal uint Peek(int bits) {
            int keep = pos;
            uint v = Read(bits);
            pos = keep;
            return v;
        }

        public bool Finished => count - pos < 8;
        public int BitsLeft => count - pos > 0 ? count - pos : 0;
    }
}
