using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Server-set settings (2026-11-20; design docs/superpowers/specs/2026-11-20-server-settings-design.md, plan
    // docs/superpowers/plans/2026-11-20-server-settings.md; rules, layout and tests in SettingsSync.cs and JoinRules.cs). While a game
    // is on a server, every setting of the mod comes from the server: the server sends what it changed from the built-in defaults in the
    // join gate's own step (message 27, kinds Settings and SettingsAck) and again whenever its settings are reloaded; the game holds them
    // in memory, ignores the player's own file while connected and puts the player's own values back on leaving and at quit. The
    // player's file is never written by this feature. The owner approved these two kinds with the design (the join gate's exception
    // to "No new network traffic").
    //
    // What was read (2026-11-20):
    //   - Message size (_decomp OutgoingPacketManager.Send, Socket's constructor; Alta.NetworkTransport.Runtime.dll KcpTransport, the
    //     only ITransport): a message may be at most OutgoingPacketManager.PacketSize = 1200 - 100 = 1100 bytes, less 2 (its length)
    //     and 1 (the transport's offset); a larger one is dropped with "Message too large". A full part is 1 byte of message type
    //     (TavernLib's 8 bits) + 54 + 74 bits of head + 900 bytes = 918 bytes: 179 bytes to spare, so SettingsSync.MaxPartBytes
    //     stays 900. A reliable channel whose failed-packet queue reaches 64 forces a disconnect, so the parts are paced: at most
    //     PartsPerFrame a frame, from Tick, on the game's thread.
    //   - Reaching connected games after the join: ServerHandler has no list of approved connections we can use cheaply, so the
    //     server keeps its own table of the connections that took its settings at the join (`synced`, by Connection.Identifier),
    //     drops a connection once Connection.IsDisposed, and sends to those on every reload. A game that joined with [Join] Check
    //     = off, without the mod, or with another wire number is never in it.
    //   - Leaving (client): the hold belongs to the server Connection the settings came on (the one JoinGate listens on from
    //     JoinServerPipeline.ConnectionApprovedByServer); Socket.DestroyConnection disposes it, so a held game checks
    //     Connection.IsDisposed once a second (and a new server's settings move the hold to that connection).
    //   - Quit order (MelonLoader 0.7.3 and its Mono support module, decompiled): Unity's OnApplicationQuit -> mods' OnApplicationQuit;
    //     then the support component's OnDestroy -> MelonEvents.OnApplicationDefiniteQuit -> Core.Quit, which destroys the file
    //     watchers and runs MelonPreferences.Save() (every category's values into the file). Closing the console window takes
    //     WindowsUnhandledQuit's path: OnApplicationDefiniteQuit then Core.Quit, WITHOUT OnApplicationQuit. So Quit runs from both
    //     Main.OnApplicationQuit and an OnApplicationDefiniteQuit subscription at the lowest priority number (it runs first); both are
    //     before the save. And while the server's values are held, the file's internal ShouldSave is off (File.Save writes nothing),
    //     so not even another mod's MelonPreferences.Save(), or a quit that skips both, can write them into the player's file.
    //   - The file watcher (MelonLoader.Preferences.IO.Watcher): a FileSystemWatcher whose event runs on a thread-pool thread and calls
    //     MelonPreferences.LoadFileAndRefreshCategories, which sets every entry from the file with none of the mod's handlers. So the
    //     first hold stops it (Cfg.StopFileWatcher; it is not started again in this game run: the mod's own 2-second poll reloads the
    //     file anyway, Cfg.Poll), and a load from any other thread still seen (OnPreferencesLoaded) has the server's values put back
    //     at the next frame.
    //   - Start-only entries: the [Tree] <Orb>Orb switches (read when this game run's tree is built, Tree.AddNewOrbs). Every other
    //     entry is read live or through a reload handler (Cfg.Changed runs them).
    //
    // A fault in this feature never refuses or drops anyone: only a decided mismatch under [Join] SettingsMismatch = refuse refuses, at
    // the join, through the join gate's own refusal. After the join a mismatch is only logged.
    internal static class ServerSettings {
#if QUEST
        const Il2CppAlta.Networking.MessageType Message = (Il2CppAlta.Networking.MessageType)JoinWire.Message;
#else
        const Alta.Networking.MessageType Message = (Alta.Networking.MessageType)JoinWire.Message;
#endif
        const int PartsPerFrame = 8;
        static int mainThread;
        static readonly object gate = new object();

        /// <summary>Tick's one flag: anything at all to do (server: parts to send; client: settings to apply, a stray load, or a hold
        /// whose connection is checked once a second). <c>busy</c>: work this frame.</summary>
        static volatile bool active, busy;

        static void Wake() { busy = true; active = true; }

        internal static void Init() {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            try { MelonPreferences.OnPreferencesLoaded.Subscribe(Loaded); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Settings: cannot see MelonLoader's file loads ({e.Message})"); }
            try { MelonEvents.OnApplicationDefiniteQuit.Subscribe(Quit, int.MinValue); }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Settings: cannot see the quit ({e.Message}); the file is still not saved while a server's settings are held"); }
        }

        static ModVersion OwnVersion => ModVersion.Parse(Main.Version);

        static readonly int TTick = Timing.Section("ServerSettings.Tick (parts to send, settings to apply, leaving)");

        /// <summary>Every frame from Main.OnUpdate, before the headless return. One flag test when there is nothing to do.</summary>
        internal static void Tick() {
            if (!active) return;
            if (!busy && Time.unscaledTime < nextLeaveCheck) return;   // held, nothing due this frame
            using var _t = Timing.Begin(TTick);
            try { ServerTick(); } catch (Exception e) { MelonLogger.Error($"[Archery] Settings: sending the server's settings failed: {e.Message}"); }
            try { ClientTick(); } catch (Exception e) { MelonLogger.Error($"[Archery] Settings: holding the server's settings failed: {e.Message}"); }
            lock (gate) {
                busy = outbox.Count > 0 || pending != null || strayLoad;
                active = busy || hold.Held;
            }
        }

        // ================================================================ server

        sealed class Send { public int Sequence; public uint Check; public int Changed; public List<byte[]> Parts = new List<byte[]>(); public bool TooBig; }
        sealed class Job { public Connection Connection = null!; public Send Send = null!; public int Next; }

        static int sequence = 1;
        static Send? current;
        static readonly Dictionary<int, Connection> synced = new Dictionary<int, Connection>();   // took the settings at the join
        static readonly HashSet<int> waiting = new HashSet<int>();                              // a join step waits for its ack
        static readonly Dictionary<int, JoinSettingsAck> acks = new Dictionary<int, JoinSettingsAck>();
        static readonly Dictionary<int, int> warnedSequence = new Dictionary<int, int>();
        static readonly List<Job> outbox = new List<Job>();

        /// <summary>The send of the server's settings as they are now, built once per reload (under the lock).</summary>
        static Send Current() {
            if (current != null) return current;
            List<SettingEntry> entries = Cfg.Entries();
            List<KeyValuePair<string, string>> changed = SettingsSync.Changed(entries);
            var s = new Send { Sequence = sequence, Check = SettingsSync.Check(entries), Changed = changed.Count };
            s.Parts = SettingsSync.Split(SettingsSync.Pack(changed));
            s.TooBig = changed.Count > SettingsSync.MaxEntries || s.Parts.Count > SettingsSync.MaxParts;
            current = s;
            return s;
        }

        static void Enqueue(Connection c, Send s) {
            foreach (Job j in outbox) if (ReferenceEquals(j.Connection, c)) { j.Send = s; j.Next = 0; return; }   // the newer send, whole
            outbox.Add(new Job { Connection = c, Send = s });
        }

        /// <summary>The join step, after the Hello passed: sends the server's settings and waits [Join] AnswerSeconds for the game's
        /// answer. Returns the refusal text, or null to let the player in. Never throws (a fault lets them in). Touches no Unity
        /// object and no Timing section (it runs after awaits).</summary>
        internal static async Task<string?> AtJoin(Connection connection, string name, float seconds, bool settingsRefuse) {
            int id = 0;
            bool expecting = false;
            try {
                id = connection.Identifier;
                Send send;
                lock (gate) {
                    Prune();
                    send = Current();
                    if (!send.TooBig) {
                        waiting.Add(id); expecting = true;
                        acks.Remove(id);
                        synced[id] = connection;
                        Enqueue(connection, send);
                        Wake();
                    }
                }
                if (send.TooBig) {
                    MelonLogger.Error($"[Archery] Join: the server's settings are too large to send ({send.Changed} changed, {send.Parts.Count} parts; at most {SettingsSync.MaxEntries} and {SettingsSync.MaxParts}): {name} is let in with their own");
                    return null;
                }
                JoinSettingsAck ack = default;
                bool answered = false;
                uint check;
                int sent, waited = 0, limit = (int)(seconds * 1000f);
                while (true) {
                    lock (gate) {
                        Send now = Current();
                        check = now.Check; sent = now.Changed;
                        if (acks.TryGetValue(id, out JoinSettingsAck a) && a.Sequence == now.Sequence) { ack = a; answered = true; }
                    }
                    if (answered || waited >= limit || connection.IsDisposed) break;
                    await Task.Delay(100);
                    waited += 100;
                }
                if (connection.IsDisposed) { lock (gate) synced.Remove(id); return null; }   // they left: no line, no refusal
                JoinSettingsVerdict verdict = JoinRule.SettingsDecision(check, answered ? ack : (JoinSettingsAck?)null, settingsRefuse, out string text);
                string skipped = answered && ack.Skipped > 0 ? $", {ack.Skipped} value(s) it could not take" : "";
                switch (verdict) {
                    case JoinSettingsVerdict.Pass:
                        if (Logs.Summary) MelonLogger.Msg($"[Archery] Join: {name} took the server's settings ({sent} differ from the built-in ones, check {check:x8}, answered in {waited} ms{skipped})");
                        return null;
                    case JoinSettingsVerdict.Log:
                        MelonLogger.Warning($"[Archery] Join: {name} would be refused (log only): {text} (server check {check:x8}, the game's {(answered ? ack.Check.ToString("x8") : "none")}{skipped})");
                        return null;
                    default:
                        lock (gate) synced.Remove(id);
                        MelonLogger.Warning($"[Archery] Join: {name} refused: {text} (server check {check:x8}, the game's {(answered ? ack.Check.ToString("x8") : "none")}{skipped})");
                        return text;
                }
            }
            catch (Exception e) {
                MelonLogger.Error($"[Archery] Join: sending the server's settings failed, so the player is let in: {e}");
                return null;
            }
            finally {
                if (expecting) lock (gate) { waiting.Remove(id); acks.Remove(id); }
            }
        }

        /// <summary>A game's answer, from JoinGate's server handler (the game's thread). Kept for a waiting join step; after the join,
        /// compared and a mismatch logged once per send (never a kick).</summary>
        internal static void ServerAck(Connection connection, JoinSettingsAck ack) {
            int id = connection.Identifier;
            string? warn = null;
            lock (gate) {
                if (waiting.Contains(id)) { acks[id] = ack; return; }
                if (!synced.ContainsKey(id) || current == null || ack.Sequence != current.Sequence) {
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Settings: an answer to an older or unknown send from connection {id}: ignored");
                    return;
                }
                JoinSettingsVerdict v = JoinRule.SettingsDecision(current.Check, ack, false, out string text);
                if (v == JoinSettingsVerdict.Pass) { warnedSequence.Remove(id); if (Logs.Debug) MelonLogger.Msg($"[Archery] Settings: connection {id} took send {ack.Sequence}"); return; }
                if (warnedSequence.TryGetValue(id, out int w) && w == ack.Sequence) return;
                warnedSequence[id] = ack.Sequence;
                warn = $"[Archery] Settings: {JoinRule.Ascii(connection.UserInfo?.Username ?? connection.PlayerName, 40)} did not take the server's new settings: {text} (server check {current.Check:x8}, the game's {ack.Check:x8}; not kicked: only a join is refused)";
            }
            MelonLogger.Warning(warn);
        }

        /// <summary>From Cfg.Poll after the server's own file was reloaded (any machine; only a server with games that took its
        /// settings sends). The newer send replaces any part still waiting. Never throws.</summary>
        internal static void ServerReloaded() {
            try {
                if (!JoinGate.OnServer) return;
                int games = 0;
                Send s;
                lock (gate) {
                    sequence = (sequence + 1) & 0xFFFF;
                    current = null;
                    s = Current();
                    if (synced.Count == 0) return;
                    if (JoinGate.Values.Check == JoinCheck.Off) return;   // the switch means: say nothing
                    if (s.TooBig) { games = -1; }
                    else {
                        Prune();
                        foreach (KeyValuePair<int, Connection> kv in synced) { Enqueue(kv.Value, s); games++; }
                        if (games > 0) Wake();
                    }
                }
                if (games < 0) MelonLogger.Error($"[Archery] Settings: the server's settings are too large to send ({s.Changed} changed, {s.Parts.Count} parts): the games keep the last ones they took");
                else if (games > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] Settings: the server's settings changed: sending them to {games} game(s) ({s.Changed} differ from the built-in ones, check {s.Check:x8})");
            }
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: could not send the server's new settings: {e.Message}"); }
        }

        /// <summary>Under the lock: connections that are gone leave the table.</summary>
        static void Prune() {
            List<int>? gone = null;
            foreach (KeyValuePair<int, Connection> kv in synced)
                if (kv.Value == null || kv.Value.IsDisposed) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (int id in gone) { synced.Remove(id); warnedSequence.Remove(id); }
        }

        static bool warnedSendFailed;

        static void ServerTick() {
            int budget = PartsPerFrame;
            lock (gate) {
                for (int i = 0; i < outbox.Count && budget > 0;) {
                    Job j = outbox[i];
                    Connection c = j.Connection;
                    if (c == null || c.IsDisposed) { outbox.RemoveAt(i); if (c != null) synced.Remove(c.Identifier); continue; }
                    if (current != null && j.Send.Sequence != current.Sequence && !current.TooBig) { j.Send = current; j.Next = 0; }
                    var part = new JoinSettings { Wire = JoinWire.Version, Version = OwnVersion, Sequence = j.Send.Sequence, Part = j.Next, Parts = j.Send.Parts.Count, Check = j.Send.Check, Payload = j.Send.Parts[j.Next] };
                    bool ok = c.Send(null, Message, (cc, stream) => JoinWire.WriteSettings(new StreamBits(stream), part));
                    budget--;
                    if (!ok) {
                        if (!warnedSendFailed) { warnedSendFailed = true; MelonLogger.Warning($"[Archery] Settings: a part of the server's settings could not be sent to connection {c.Identifier} (closing?): that game keeps what it had"); }
                        outbox.RemoveAt(i);
                        continue;
                    }
                    if (++j.Next >= j.Send.Parts.Count) { outbox.RemoveAt(i); continue; }
                    i++;
                }
            }
        }

        // ================================================================ client

        sealed class Pending { public Connection Connection = null!; public int Sequence; public uint Check; public List<KeyValuePair<string, string>> Changed = null!; }

        static readonly SettingsHold hold = new SettingsHold();
        static SettingsInbox inbox = new SettingsInbox();
        static Connection? inboxConnection, heldConnection;
        static Pending? pending;
        static List<KeyValuePair<string, string>>? targets;
        static volatile bool strayLoad;
        static bool saveStopped, watcherStopped, warnedNoSaveStop, toldEdit;
        static float nextLeaveCheck;

        /// <summary>This game holds a server's settings now (its own file is not used).</summary>
        internal static bool Held => hold.Held;

        /// <summary>A part of the server's settings, from JoinGate's client handler (the game's thread). A whole send waits for Tick.</summary>
        internal static void ClientPart(Connection connection, JoinSettings part) {
            if (part.Wire != JoinWire.Version) {
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Settings: a part from wire {part.Wire} (this game: {JoinWire.Version}): ignored");
                return;
            }
            if (!ReferenceEquals(connection, inboxConnection)) { inboxConnection = connection; inbox = new SettingsInbox(); }
            if (!inbox.Add(part.Sequence, part.Part, part.Parts, part.Check, part.Payload, out byte[] whole, out uint check)) return;
            if (!SettingsSync.TryUnpack(whole, out List<KeyValuePair<string, string>> changed)) {
                MelonLogger.Warning($"[Archery] Settings: the server's settings arrived damaged ({whole.Length} bytes, send {part.Sequence}): this game keeps what it has");
                return;
            }
            lock (gate) { pending = new Pending { Connection = connection, Sequence = part.Sequence, Check = check, Changed = changed }; Wake(); }
        }

        static void ClientTick() {
            Pending? p;
            lock (gate) { p = pending; pending = null; }
            if (p != null) Apply(p);
            if (strayLoad) {
                strayLoad = false;
                if (hold.Held && targets != null) {
                    Cfg.SetTexts(targets);
                    Cfg.Changed("a load of the file by MelonLoader");
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Settings: MelonLoader read {Cfg.FileName}; the server's settings are back in use (your file is used again when you leave this server)");
                }
            }
            if (hold.Held && Time.unscaledTime >= nextLeaveCheck) {
                nextLeaveCheck = Time.unscaledTime + 1f;
                if (heldConnection == null || heldConnection.IsDisposed) Release("left the server", true);
            }
        }

        static void Apply(Pending p) {
            if (p.Connection.IsDisposed) return;
            List<SettingEntry> mine = Cfg.Entries();
            bool first = !hold.Held;
            if (first) {
                hold.Take(mine);
                toldEdit = false;
                saveStopped = Cfg.SetFileSaving(false);
                if (!saveStopped && !warnedNoSaveStop) { warnedNoSaveStop = true; MelonLogger.Warning($"[Archery] Settings: cannot stop MelonLoader saving {Cfg.FileName} while the server's settings are held; your own values are put back before it saves at quit"); }
                if (!watcherStopped) { watcherStopped = true; Cfg.StopFileWatcher(); }
            }
            heldConnection = p.Connection;
            targets = SettingsSync.Targets(mine, p.Changed, out int unknown);
            var skippedKeys = new List<string>();
            int skipped = Cfg.SetTexts(targets, skippedKeys) + unknown;
            Cfg.Changed("the server's settings");
            uint check = SettingsSync.Check(Cfg.Entries());
            bool restart = Tree.SwitchesChangedSinceBuild;
            var ack = new JoinSettingsAck { Wire = JoinWire.Version, Version = OwnVersion, Sequence = p.Sequence, Check = check, RestartNeeded = restart, Skipped = skipped };
            bool sent = p.Connection.Send(null, Message, (c, s) => JoinWire.WriteSettingsAck(new StreamBits(s), ack));
            if (Logs.Summary) MelonLogger.Msg(first
                ? $"[Archery] Settings: this server sets the mod's settings ({p.Changed.Count} differ from the built-in ones; your own file is not used on this server)"
                : $"[Archery] Settings: the server changed its settings ({p.Changed.Count} differ from the built-in ones)");
            if (check != p.Check) MelonLogger.Warning($"[Archery] Settings: after taking the server's settings this game's check is {check:x8}, the server's {p.Check:x8}: this copy of the mod has other built-in settings than the server's. Install the server's copy of the mod.");
            if (skipped > 0) MelonLogger.Warning($"[Archery] Settings: {skipped} of the server's values could not be taken ({unknown} unknown to this copy of the mod{(skippedKeys.Count > 0 ? ": " + string.Join(", ", skippedKeys.GetRange(0, Math.Min(5, skippedKeys.Count)).ToArray()) : "")})");
            if (restart) MelonLogger.Warning("[Archery] Settings: the server's settings switch orbs on or off ([Tree] ...Orb), which this game reads only when it starts: restart the game, then join again");
            if (!sent) MelonLogger.Warning("[Archery] Settings: the answer to the server could not be sent (the connection is closing)");
        }

        /// <summary>From Cfg.Poll, while held, just before the file is read: the player's own values go into the entries, so a key the
        /// file lacks keeps theirs.</summary>
        internal static void BeforeFileLoad() {
            try { Cfg.SetTexts(hold.Own()); }
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: before reading your file: {e.Message}"); }
        }

        /// <summary>From Cfg.Poll, while held, right after the file was read (before any handler): what was read is the player's (when
        /// the read worked), and the server's values go back in at once.</summary>
        internal static void AfterFileLoad(bool readOk) {
            try {
                if (readOk) hold.FileChanged(Cfg.Entries());
                if (targets != null) Cfg.SetTexts(targets);
                if (readOk && !toldEdit && Logs.Summary) { toldEdit = true; MelonLogger.Msg($"[Archery] Settings: your edit of {Cfg.FileName} is kept for when you leave this server; the server's settings stay in use (said once per server)"); }
            }
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: after reading your file: {e.Message}"); }
        }

        /// <summary>MelonLoader read a settings file: a load of ours from another thread (its watcher) put the player's values in.</summary>
        static void Loaded(string path) {
            if (Thread.CurrentThread.ManagedThreadId == mainThread || !hold.Held) return;   // the mod's own poll handles its own loads
            try { if (!string.Equals(System.IO.Path.GetFileName(path), Cfg.FileName, StringComparison.OrdinalIgnoreCase)) return; } catch { return; }
            strayLoad = true;
            Wake();
        }

        static void Release(string why, bool runHandlers) {
            List<KeyValuePair<string, string>> own = hold.Release();
            targets = null;
            heldConnection = null;
            int skipped = Cfg.SetTexts(own);
            // MelonLoader's saves come back on only when every own value is back: else its quit save would write a server's value
            if (saveStopped && skipped == 0) saveStopped = !Cfg.SetFileSaving(true);
            else if (saveStopped) MelonLogger.Warning($"[Archery] Settings: {skipped} of your own values could not be put back, so MelonLoader does not save {Cfg.FileName} in this game run (the file keeps your values)");
            if (runHandlers) Cfg.Changed("your own settings");
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Settings: {why}: your own settings from {Cfg.FileName} are back{(skipped > 0 ? $" ({skipped} could not be put back)" : "")}");
        }

        static readonly int TQuit = Timing.Section("ServerSettings.Quit (your own settings back before the quit save)");

        /// <summary>First thing at quit (Main.OnApplicationQuit, and MelonLoader's OnApplicationDefiniteQuit before its save): the
        /// player's own values back, so MelonLoader's save writes theirs. Never throws.</summary>
        internal static void Quit() {
            using var _t = Timing.Begin(TQuit);
            try { if (hold.Held) Release("the game is closing", false); }
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: putting your own settings back at quit failed ({e.Message}); {Cfg.FileName} is not saved this time"); }
        }
    }
}
