using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Serialization;
using HarmonyLib;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // The join gate (2026-11-20; rules and wire layout in JoinRules.cs). The SECOND owner-approved exception to "No new network
    // traffic" (the first is Skyfall's signal, SkyfallLink.cs). The mod replaces the archery skill tree, so the owner wants a game
    // without a matching copy of it turned away at the door with a reason it can read.
    //   What is sent: Here (server -> the joining game: wire number, mod version) and Hello (that game -> server: wire number, mod
    //     version, shared-settings checksum, platform), both on message type JoinWire.Message, numbers only.
    //   When: once per join, inside the game's own join sequence. Never during play. With [Join] Check = off, nothing at all.
    //   To whom: the joining game only.
    //   The refusal itself is the game's own MessageType.RequestJoinResponse.
    //
    // How it sits in the game (read in _decomp, 2026-11-20):
    //   - ServerHandler's public constructor sets Current and creates connectionSteps before it returns, so a non-null Current seen
    //     from OnUpdate can take a step (the private one, a client's dummy local server, has no step list: Tick skips it). UserPrerequisites runs every step for each joining connection (after the server's "allowed, doing
    //     prerequisites" reply); a step that throws, or a connection dropped during a step, fails the prerequisites, and the game
    //     then sends its own "Failed join prerequisites." refusal (with a join result the joining game retries 24 times) and drops
    //     the connection. So a refusal here sends ours, flushes, waits 2 s and drops the connection itself, as the game's
    //     PlayerDenied does: the game's own text can then not be sent. Throwing from the step is only the fallback.
    //   - ConfirmJoinMessage is internal: its layout is written by hand (bool allowed, bool doing prerequisites, the join-result
    //     integer, then for a refusal the error text as an ASCII string).
    //   - A join result the game does not define (JoinWire.RefusedResult) is shown as "Unknown connection error 60, please contact
    //     support. Error: <our text>" and is not retried. That wrapper is the game's (JoinResult.ErrorMessage).
    //   - A game without the mod that receives Here logs "Unsupported message type" and carries on (MessageProcessor.HandleMessage).
    //   - TavernLib (the launcher's plugin, on servers and clients) replaces the join request's handler with its own account checks
    //     and then calls the game's CheckApproved, so the steps still run. It also widens the message type from the game's 5 bits to 8
    //     (MessageExpanderPatch); JoinWire.Message fits both.
    //   Message numbers seen in the other mods installed here (2026-11-20): 18 SyncLib, 32 TavernLib (roles), 9 TavernLib (the
    //     game's own RequestJoin). ArmorRestored uses 137. RevampedSkills, attbullshit2 and BluesInGameStringTool set no handler.
    //
    // A fault in the gate never refuses anyone: only a decided refusal fails the join step. The step keeps to Connection calls and
    // the log: no Unity object, no Timing section (review 2026-11-20: its continuations do run on the main thread here, since the
    // game's receive runs in Update and TavernLib's filter finishes synchronously on the pass path; the rule stays anyway).
    // A Hello is kept only while its connection's step waits (JoinHellos.Expect/Drop): the handler is on every connection from
    // ConnectionCreated, before any account check.
    internal static class JoinGate {
        static MelonPreferences_Entry<string> check = null!, settingsMismatch = null!;
        static MelonPreferences_Entry<float> answerSeconds = null!;
        static readonly JoinHellos hellos = new JoinHellos();          // server; every use under lock (hellos)
        static readonly HashSet<string> warnedTexts = new HashSet<string>();
        static ServerHandler? installedOn;
        /// <summary>The join step is on a real server's join sequence (server-set settings send only then).</summary>
        internal static bool OnServer { get; private set; }
        static Connection? greetedBy;                                   // client: the server connection whose Here was logged

        const MessageType Message = (MessageType)JoinWire.Message;

        internal struct Settings { public JoinCheck Check; public float Seconds; public bool SettingsRefuse; }

        internal static void Init() {
            var cat = Cfg.Category("Join");
            check = cat.CreateEntry("Check", "refuse", description: "Server. What happens when a joining game has no matching copy of this mod: refuse, log (let in, log what would be refused) or off (no check, nothing sent).");
            answerSeconds = cat.CreateEntry("AnswerSeconds", 5f, description: "Server. How long a joining game has to answer the check (1 to 60).");
            settingsMismatch = cat.CreateEntry("SettingsMismatch", "log", description: "Server. The server sends its settings to every joining game, which uses them while it is on this server. A game that could not take them (another build of the mod with other built-in settings, no answer, or orb switches that need a restart): log or refuse.");
        }

        /// <summary>The [Join] settings as they are now (a leaf: its own entries only). An unknown text is warned once.</summary>
        internal static Settings Values {
            get {
                var s = new Settings();
                s.Check = JoinRule.ParseCheck(check.Value, out bool known);
                if (!known) WarnOnce("Check", check.Value, "refuse, log or off", "refuse");
                s.SettingsRefuse = JoinRule.ParseSettingsRefuse(settingsMismatch.Value, out known);
                if (!known) WarnOnce("SettingsMismatch", settingsMismatch.Value, "log or refuse", "log");
                s.Seconds = JoinRule.AnswerSeconds(answerSeconds.Value);
                return s;
            }
        }

        static void WarnOnce(string key, string? text, string valid, string used) {
            string shown = JoinRule.Ascii(text, 40);
            lock (warnedTexts) if (!warnedTexts.Add(key + "=" + shown)) return;
            MelonLogger.Warning($"[Archery] Join: [Join] {key} = \"{shown}\" is not {valid}: using {used}");
        }

        static ModVersion OwnVersion => ModVersion.Parse(Main.Version);

        // ---------------------------------------------------------------- server

        /// <summary>Every frame from Main.OnUpdate, before the headless return: adds the join step to the server's handler once (and
        /// again if the game makes a new one). One reference comparison per frame otherwise; nothing on a client.</summary>
        internal static void Tick() {
            ServerHandler current = ServerHandler.Current;
#if QUEST
            if (current == null || Il.Same(current, installedOn)) return;
#else
            if (current == null || ReferenceEquals(current, installedOn)) return;
#endif
            installedOn = current;
            if (current.IsDummyLocalServer) return;                     // a client's stand-in server: nobody joins it, and it has no step list
#if QUEST
            return;   // a headset is never the server (and its build has no AddConnectionStep)
#else
            current.AddConnectionStep(Step);
#endif
            OnServer = true;
            MelonLogger.Msg($"[Archery] Join: check installed on message {JoinWire.Message} ({Values.Check.ToString().ToLowerInvariant()})");
        }

#if !QUEST // the server's half: never on a headset
        [HarmonyPatch(typeof(ServerHandler), "ConnectionCreated")]
        static class ServerListen {
            static readonly int T = Timing.Section("patch ServerHandler.ConnectionCreated / JoinGate.ServerListen");
            static void Postfix(Connection connection) {
                using var _t = Timing.Begin(T);
                try { connection?.SetHandler(Message, ServerRead); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Join: could not listen for a joining game's answer: {e.Message}"); }
            }
        }
#endif

        static readonly int TServerRead = Timing.Section("JoinGate.ServerRead (a joining game's answer)");
        static void ServerRead(Connection connection, Stream stream) {
            using var _t = Timing.Begin(TServerRead);
            try {
                if (connection == null) return;
                // the message is copied off first, so its kind can be looked at: Hello, or (server-set settings) a SettingsAck
                BitTape tape = BitTape.From(new StreamBits(stream));
                if (tape.Peek(8) == JoinWire.KindSettingsAck) {
                    if (JoinWire.ReadSettingsAck(tape, out JoinSettingsAck ack) == WireRead.Ok) ServerSettings.ServerAck(connection, ack);
                    else if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a damaged settings answer: ignored");
                    return;
                }
                if (JoinWire.ReadHello(tape, out JoinHello hello) != WireRead.Ok) {
                    if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a message on the join number was not a Hello: ignored");
                    return;
                }
                bool kept;
                lock (hellos) kept = hellos.Put(connection.Identifier, hello);          // kept only while that connection's step waits
                if (!kept && Logs.Debug) MelonLogger.Msg("[Archery] Join: a Hello no join step was waiting for: ignored");
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: reading a joining game's answer failed: {e.Message}"); }
        }

        static async Task Step(Connection connection, Action<string> progress) {
            JoinVerdict verdict = default;
            string reason = "", name = "";
            int id = 0;
            bool expecting = false, sendSettings = false;
            Settings s = default;
            try {
                s = Values;
                if (s.Check == JoinCheck.Off) return;                                   // nothing sent
                id = connection.Identifier;
                lock (hellos) expecting = hellos.Expect(id);                            // only now is this connection's Hello kept
                if (!expecting) {
                    MelonLogger.Error($"[Archery] Join: {JoinHellos.Max} joins are being checked at once, so this one is let in unchecked");
                    return;
                }
                var here = new JoinHere { Wire = JoinWire.Version, Version = OwnVersion };
                connection.Send(null, Message, (c, stream) => JoinWire.WriteHere(new StreamBits(stream), here));

                JoinHello hello = default;
                bool answered = false;
                int waited = 0, limit = (int)(s.Seconds * 1000f);
                while (!connection.IsDisposed) {
                    lock (hellos) answered = hellos.Take(id, out hello);
                    if (answered || waited >= limit) break;
                    await Task.Delay(100);
                    waited += 100;
                }
                if (connection.IsDisposed) return;                                      // they left: no line, no refusal

                JoinHello? got = answered ? hello : (JoinHello?)null;
                // Server-set settings: the Hello's checksum is the game's own file, which the server's settings replace next, so it no
                // longer refuses here ("settings same|DIFFERENT" in the line is that file); [Join] SettingsMismatch judges the game's
                // answer to the server's settings instead (ServerSettings.AtJoin).
                verdict = JoinRule.Decide(got, JoinWire.Version, OwnVersion, Cfg.SharedChecksum, s.Check, settingsRefuse: false);
                sendSettings = answered && hello.Wire == JoinWire.Version && !verdict.Refuse;
                name = JoinRule.Ascii(connection.UserInfo?.Username ?? connection.PlayerName, 40);
                string line = JoinRule.Line(name, got, verdict, s.Check, waited);
                if (verdict.Problem != null) MelonLogger.Warning(answered ? line : $"{line} (no answer in {waited / 1000f:0.#} s)");
                else if (Logs.Summary) MelonLogger.Msg(line);
                reason = verdict.Problem ?? "";
            } catch (Exception e) {
                MelonLogger.Error($"[Archery] Join: the check itself failed, so the player is let in: {e}");
                return;                                                                 // a fault never refuses
            } finally {
                if (expecting) lock (hellos) hellos.Drop(id);                           // a late Hello is no longer kept
            }
            if (sendSettings) {
                // server-set settings: sent and answered in this step; a fault lets the player in (AtJoin never throws)
                string? refusal = await ServerSettings.AtJoin(connection, name, s.Seconds, s.SettingsRefuse);
                if (refusal != null) { verdict.Refuse = true; reason = refusal; }
                else if (connection.IsDisposed) return;
            }
            if (!verdict.Refuse) return;

            // A decided refusal, the way the game refuses (ServerPlayerConnectionHandlerOld.PlayerDenied): the text, a flush, a pause,
            // then the connection is dropped here. The game's step loop sees the dropped connection and fails the join; its own
            // "Failed join prerequisites." cannot be sent on it, so the joining game only ever gets ours. (That one carries a join
            // result the joining game retries 24 times; ours, JoinWire.RefusedResult, is not retried.)
            try {
                string text = JoinRule.Ascii(reason, 160);
                bool sent = connection.Send(null, MessageType.RequestJoinResponse, (c, stream) => {
                    bool allowed = false, prerequisites = false;
                    int result = JoinWire.RefusedResult;
                    stream.SerializeBool(ref allowed);
                    stream.SerializeBool(ref prerequisites);
                    stream.SerializeInteger(ref result);
                    stream.SerializeString(ref text);
                });
                if (!sent) MelonLogger.Warning("[Archery] Join: the refusal text could not be sent (the connection is closing); the player is dropped without it");
                connection.FlushPacketManagers();
                await Task.Delay(2000);
                if (!connection.IsDisposed) connection.Socket.DestroyConnection(connection, isFlushing: true);
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: refusing went wrong ({e.Message}); the game's own refusal follows"); }
            if (!connection.IsDisposed) throw new InvalidOperationException(JoinRule.ModName + " refused the join: " + reason);   // the fallback
        }

        // ---------------------------------------------------------------- client

        [HarmonyPatch(typeof(JoinServerPipeline), "ConnectionApprovedByServer")]
        static class ClientListen {
            static readonly int T = Timing.Section("patch JoinServerPipeline.ConnectionApprovedByServer / JoinGate.ClientListen");
            static void Postfix(Connection connection) {
                using var _t = Timing.Begin(T);
                try { connection?.SetHandler(Message, ClientRead); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Join: could not listen for the server's greeting: {e.Message}"); }
            }
        }

        static readonly int TClientRead = Timing.Section("JoinGate.ClientRead (the server's greeting)");
        static void ClientRead(Connection connection, Stream stream) {
            using var _t = Timing.Begin(TClientRead);
            try {
                if (connection == null) return;
                // the message is copied off first, so its kind can be looked at: Here, or (server-set settings) a part of the server's settings
                BitTape tape = BitTape.From(new StreamBits(stream));
                if (tape.Peek(8) == JoinWire.KindSettings) {
                    if (JoinWire.ReadSettings(tape, out JoinSettings part) == WireRead.Ok) ServerSettings.ClientPart(connection, part);
                    else if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a damaged part of the server's settings: ignored");
                    return;
                }
                if (JoinWire.ReadHere(tape, out JoinHere here) != WireRead.Ok) {
                    if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a message on the join number was not a Here: ignored");
                    return;
                }
                ModVersion own = OwnVersion;
#if QUEST
                if (!Il.Same(greetedBy, connection)) {
#else
                if (!ReferenceEquals(greetedBy, connection)) {
#endif
                    greetedBy = connection;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Join: this server runs {JoinRule.ModName} {here.Version} (wire {here.Wire})");
                    if (here.Wire > JoinWire.Version)
                        MelonLogger.Warning($"[Archery] Join: Your {JoinRule.ModName} {own} is too old for this server's {here.Version}. Update it, then join again.");
                    else if (here.Wire < JoinWire.Version)
                        MelonLogger.Warning($"[Archery] Join: Your {JoinRule.ModName} {own} is newer than this server's {here.Version}. The server needs an update first.");
                }
#if QUEST
                var hello = new JoinHello { Wire = JoinWire.Version, Version = own, Check = Cfg.SharedChecksum, Platform = JoinPlatform.Quest };
#else
                var hello = new JoinHello { Wire = JoinWire.Version, Version = own, Check = Cfg.SharedChecksum, Platform = JoinPlatform.Pc };
#endif
                connection.Send(null, Message, (c, s) => JoinWire.WriteHello(new StreamBits(s), hello));
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: answering the server's greeting failed: {e.Message}"); }
        }
    }
}
