using System;
using System.Collections.Generic;
using System.Text;

// Unity-free, compiled by BluesArcheryOverhaul.Tests (tested in JoinTests.cs), so only System.* in here.
// The join gate (2026-11-20, design docs/superpowers/specs/2026-11-20-quest-port-join-gate-design.md): the mod replaces the archery
// skill tree, so a game that does not run a matching copy of it must not join. The server greets a joining game (Here), the game's
// copy of the mod answers (Hello), and JoinRule decides. This file holds the two messages' layout, the wire number and the rule;
// JoinGate.cs is the part that touches the game.
//   Both messages carry NUMBERS ONLY: nothing a joining game sends is ever a string, so nothing it sends can end up in a log line
//   or a message as text of its choosing.
namespace BluesArcheryOverhaul {
    /// <summary>[Join] Check: what the server does with a joining game that has no matching copy of the mod.</summary>
    internal enum JoinCheck { Refuse, Log, Off }

    /// <summary>What the joining game says it runs on (2 bits on the wire; 2 is not used and reads as Unknown).</summary>
    internal enum JoinPlatform { Pc = 0, Quest = 1, Unknown = 3 }

    /// <summary>The mod's version as three numbers, each held to 0..1023 (10 bits on the wire).</summary>
    internal struct ModVersion {
        public int Major, Minor, Patch;
        public const int MaxPart = 1023;

        /// <summary>"0.19.1" gives 0, 19, 1. Each part is its leading digits ("3-test" is 3); a missing or unreadable part is 0.</summary>
        public static ModVersion Parse(string? text) {
            var v = new ModVersion();
            if (string.IsNullOrEmpty(text)) return v;
            string[] parts = text!.Split('.');
            v.Major = Part(parts, 0); v.Minor = Part(parts, 1); v.Patch = Part(parts, 2);
            return v;
        }

        static int Part(string[] parts, int i) {
            if (i >= parts.Length) return 0;
            int n = 0;
            foreach (char c in parts[i].Trim()) {
                if (c < '0' || c > '9') break;
                n = n * 10 + (c - '0');
                if (n > MaxPart) return MaxPart; // also keeps a very long run of digits from overflowing
            }
            return n;
        }

        internal static int Hold(int part) => part < 0 ? 0 : part > MaxPart ? MaxPart : part;

        public override string ToString() => Major + "." + Minor + "." + Patch;
    }

    /// <summary>Server to the joining game: "this server runs the mod, at this wire number and version".</summary>
    internal struct JoinHere { public int Wire; public ModVersion Version; }

    /// <summary>The joining game's answer: what it runs, its shared-settings checksum (Cfg.SharedChecksum) and its platform.</summary>
    internal struct JoinHello { public int Wire; public ModVersion Version; public uint Check; public JoinPlatform Platform; }

    internal static class JoinWire {
        /// <summary>THE wire number. Raise it on every build friends need: a change to the build block, the shot claim, the set of
        /// [shared] settings, or shooter-side behaviour that must match (the judgement tools/stage-build.sh asks for with
        /// "friends: yes"). A game with another number is refused.</summary>
        public const int Version = 4;

        /// <summary>The game MessageType value both messages use. The game writes a message type in 5 bits
        /// (OutgoingPacketManager.StartSerialize), so only 0..31 exist.</summary>
        public const int Message = 27;

        /// <summary>The values the game's MessageType enum does not use, less 18 (SyncLib's).</summary>
        public static readonly int[] FreeMessages = { 3, 5, 8, 14, 16, 19, 27, 31 };

        /// <summary>The join-result integer of a refusal: one the game's ClientJoinResult does not define, so the joining game shows
        /// the server's text (after its own "Unknown connection error 60") and does not retry.</summary>
        public const int RefusedResult = 60;

        public const uint KindHere = 1, KindHello = 2;
        const int KindBits = 8, WireBits = 16, PartBits = 10, CheckBits = 32, PlatformBits = 2;
        public const int HereBits = KindBits + WireBits + 3 * PartBits;                    // 54
        public const int HelloBits = HereBits + CheckBits + PlatformBits;                  // 88
        const int MaxWire = (1 << WireBits) - 1;

        static void WriteHead(IBitWriter w, uint kind, int wire, ModVersion v) {
            w.Write(kind, KindBits);
            w.Write((uint)(wire < 0 ? 0 : wire > MaxWire ? MaxWire : wire), WireBits);
            w.Write((uint)ModVersion.Hold(v.Major), PartBits);
            w.Write((uint)ModVersion.Hold(v.Minor), PartBits);
            w.Write((uint)ModVersion.Hold(v.Patch), PartBits);
        }

        static bool ReadHead(IBitReader r, uint kind, int bits, out int wire, out ModVersion v) {
            wire = 0; v = new ModVersion();
            if (r.BitsLeft < bits) return false;
            if (r.Read(KindBits) != kind) return false;
            wire = (int)r.Read(WireBits);
            v.Major = (int)r.Read(PartBits); v.Minor = (int)r.Read(PartBits); v.Patch = (int)r.Read(PartBits);
            return true;
        }

        public static void WriteHere(IBitWriter w, JoinHere h) => WriteHead(w, KindHere, h.Wire, h.Version);

        public static WireRead ReadHere(IBitReader r, out JoinHere h) {
            h = new JoinHere();
            return ReadHead(r, KindHere, HereBits, out h.Wire, out h.Version) ? WireRead.Ok : WireRead.Malformed;
        }

        public static void WriteHello(IBitWriter w, JoinHello h) {
            WriteHead(w, KindHello, h.Wire, h.Version);
            w.Write(h.Check, CheckBits);
            w.Write((uint)h.Platform & 3u, PlatformBits);
        }

        public static WireRead ReadHello(IBitReader r, out JoinHello h) {
            h = new JoinHello();
            if (!ReadHead(r, KindHello, HelloBits, out h.Wire, out h.Version)) return WireRead.Malformed;
            h.Check = r.Read(CheckBits);
            uint p = r.Read(PlatformBits);
            h.Platform = p == 0 ? JoinPlatform.Pc : p == 1 ? JoinPlatform.Quest : JoinPlatform.Unknown;
            return WireRead.Ok;
        }
    }

    /// <summary>What the server decided about one join. <c>Problem</c> is the text for the player and the log (null: nothing wrong);
    /// <c>Refuse</c> only when there is a problem and [Join] Check is refuse.</summary>
    internal struct JoinVerdict { public string? Problem; public bool Refuse; public bool SettingsDiffer; }

    internal static class JoinRule {
        public const string ModName = "Blue's Archery Overhaul";
        public const string CfgName = "BluesArcheryOverhaul.cfg";

        /// <summary>refuse, log or off (any case, spaces ignored). Anything else is refuse, the shipped default, with known false.</summary>
        public static JoinCheck ParseCheck(string? text, out bool known) {
            known = true;
            switch ((text ?? "").Trim().ToLowerInvariant()) {
                case "refuse": return JoinCheck.Refuse;
                case "log": return JoinCheck.Log;
                case "off": return JoinCheck.Off;
                default: known = false; return JoinCheck.Refuse;
            }
        }

        /// <summary>[Join] SettingsMismatch: refuse is true, log is false. Anything else is log, the shipped default, with known false.</summary>
        public static bool ParseSettingsRefuse(string? text, out bool known) {
            string t = (text ?? "").Trim().ToLowerInvariant();
            known = t == "refuse" || t == "log";
            return t == "refuse";
        }

        /// <summary>[Join] AnswerSeconds held to 1..60; a broken number is 5.</summary>
        public static float AnswerSeconds(float setting) =>
            float.IsNaN(setting) || float.IsInfinity(setting) ? 5f : setting < 1f ? 1f : setting > 60f ? 60f : setting;

        /// <summary><paramref name="hello"/> null: the joining game did not answer. The wire number is judged before the settings.</summary>
        public static JoinVerdict Decide(JoinHello? hello, int serverWire, ModVersion serverVersion, uint serverCheck,
                                         JoinCheck check, bool settingsRefuse) {
            var v = new JoinVerdict();
            if (hello == null) {
                v.Problem = $"This server needs the mod {ModName} ({serverVersion}). Install it, then join again.";
            } else {
                JoinHello h = hello.Value;
                v.SettingsDiffer = h.Check != serverCheck;
                if (h.Wire < serverWire) v.Problem = $"Your {ModName} {h.Version} is too old for this server's {serverVersion}. Update it, then join again.";
                else if (h.Wire > serverWire) v.Problem = $"Your {ModName} {h.Version} is newer than this server's {serverVersion}. The server needs an update first.";
                else if (v.SettingsDiffer && settingsRefuse) v.Problem = $"Your {ModName} settings differ from this server's. Ask the server owner for its {CfgName}, then join again.";
            }
            v.Refuse = v.Problem != null && check == JoinCheck.Refuse;
            return v;
        }

        /// <summary>The server's one log line for a join. <paramref name="name"/> must already be bounded (Ascii).
        /// <paramref name="answeredMs"/>: how long the answer took, shown on a pass so log mode tells how near a slow game is to
        /// [Join] AnswerSeconds before refuse is switched on.</summary>
        public static string Line(string name, JoinHello? hello, JoinVerdict verdict, JoinCheck check, int answeredMs) {
            if (verdict.Problem != null)
                return $"[Archery] Join: {name} {(verdict.Refuse ? "refused" : "would be refused (log only)")}: {verdict.Problem}";
            if (hello == null) return $"[Archery] Join: {name} passed";
            JoinHello h = hello.Value;
            string platform = h.Platform == JoinPlatform.Pc ? "PC" : h.Platform == JoinPlatform.Quest ? "Quest" : "?";
            return $"[Archery] Join: {name} passed (client {h.Version}, wire {h.Wire}, {platform}, settings {(verdict.SettingsDiffer ? "DIFFERENT" : "same")}, answered in {answeredMs} ms)";
        }

        /// <summary>Text held to printable ASCII (anything else becomes '?') and to <paramref name="max"/> characters: the refusal
        /// goes out as an ASCII string, and a player's name goes into a log line.</summary>
        public static string Ascii(string? text, int max) {
            if (string.IsNullOrEmpty(text) || max <= 0) return "";
            var sb = new StringBuilder(Math.Min(text!.Length, max));
            foreach (char c in text) {
                if (sb.Length >= max) break;
                sb.Append(c >= ' ' && c <= '~' ? c : '?');
            }
            return sb.ToString();
        }
    }

    /// <summary>The server's Hellos waiting for their join step, by connection. An answer is kept only while that connection's step
    /// waits for it (<see cref="Expect"/> ... <see cref="Drop"/>): the handler is set on every connection before the game's account
    /// check, so anything else could fill the table. At most <see cref="Max"/> steps wait at once; one more is told no (and its
    /// player is let in: a fault never refuses). Not thread-safe: its one user (JoinGate) locks it.</summary>
    internal sealed class JoinHellos {
        public const int Max = 256;
        readonly HashSet<int> expected = new HashSet<int>();
        readonly Dictionary<int, JoinHello> waiting = new Dictionary<int, JoinHello>();

        public int Count => waiting.Count;
        public int Expected => expected.Count;

        /// <summary>This connection's step starts waiting. False when Max steps wait already.</summary>
        public bool Expect(int connection) => expected.Contains(connection) || (expected.Count < Max && expected.Add(connection));

        /// <summary>False (ignored) unless that connection's step is waiting.</summary>
        public bool Put(int connection, JoinHello hello) {
            if (!expected.Contains(connection)) return false;
            waiting[connection] = hello;
            return true;
        }

        public bool Take(int connection, out JoinHello hello) {
            if (!waiting.TryGetValue(connection, out hello)) return false;
            waiting.Remove(connection);
            return true;
        }

        /// <summary>The step is over: nothing more is kept for this connection.</summary>
        public void Drop(int connection) { expected.Remove(connection); waiting.Remove(connection); }
    }
}
