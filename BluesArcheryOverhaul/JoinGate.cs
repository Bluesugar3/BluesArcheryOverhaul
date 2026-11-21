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
    //     prerequisites" reply); a step that throws fails the prerequisites, and the game then sends its own "Failed join
    //     prerequisites." refusal and drops the connection. The joining game keeps the FIRST refusal it gets, so ours goes out first.
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
    // A fault in the gate never refuses anyone: only a decided refusal fails the join step. The step's code after an await is not
    // promised to run on the main thread, so it touches no Unity object and no Timing section.
    internal static class JoinGate {
        static MelonPreferences_Entry<string> check = null!, settingsMismatch = null!;
        static MelonPreferences_Entry<float> answerSeconds = null!;
        static readonly JoinHellos hellos = new JoinHellos();          // server; every use under lock (hellos)
        static readonly HashSet<string> warnedTexts = new HashSet<string>();
        static ServerHandler? installedOn;
        static Connection? greetedBy;                                   // client: the server connection whose Here was logged

        const MessageType Message = (MessageType)JoinWire.Message;

        internal struct Settings { public JoinCheck Check; public float Seconds; public bool SettingsRefuse; }

        internal static void Init() {
            var cat = Cfg.Category("Join");
            check = cat.CreateEntry("Check", "refuse", description: "Server. What happens when a joining game has no matching copy of this mod: refuse, log (let in, log what would be refused) or off (no check, nothing sent).");
            answerSeconds = cat.CreateEntry("AnswerSeconds", 5f, description: "Server. How long a joining game has to answer the check (1 to 60).");
            settingsMismatch = cat.CreateEntry("SettingsMismatch", "log", description: "Server. A joining game whose [shared] settings differ: log or refuse.");
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
            if (current == null || ReferenceEquals(current, installedOn)) return;
            installedOn = current;
            if (current.IsDummyLocalServer) return;                     // a client's stand-in server: nobody joins it, and it has no step list
            current.AddConnectionStep(Step);
            MelonLogger.Msg($"[Archery] Join: check installed on message {JoinWire.Message} ({Values.Check.ToString().ToLowerInvariant()})");
        }

        [HarmonyPatch(typeof(ServerHandler), "ConnectionCreated")]
        static class ServerListen {
            static readonly int T = Timing.Section("patch ServerHandler.ConnectionCreated / JoinGate.ServerListen");
            static void Postfix(Connection connection) {
                using var _t = Timing.Begin(T);
                try { connection?.SetHandler(Message, ServerRead); }
                catch (Exception e) { MelonLogger.Error($"[Archery] Join: could not listen for a joining game's answer: {e.Message}"); }
            }
        }

        static readonly int TServerRead = Timing.Section("JoinGate.ServerRead (a joining game's answer)");
        static void ServerRead(Connection connection, Stream stream) {
            using var _t = Timing.Begin(TServerRead);
            try {
                if (connection == null || JoinWire.ReadHello(new StreamBits(stream), out JoinHello hello) != WireRead.Ok) {
                    if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a message on the join number was not a Hello: ignored");
                    return;
                }
                lock (hellos) hellos.Put(connection.Identifier, hello);
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: reading a joining game's answer failed: {e.Message}"); }
        }

        static async Task Step(Connection connection, Action<string> progress) {
            JoinVerdict verdict = default;
            string reason = "";
            try {
                Settings s = Values;
                if (s.Check == JoinCheck.Off) return;                                   // nothing sent
                var here = new JoinHere { Wire = JoinWire.Version, Version = OwnVersion };
                connection.Send(null, Message, (c, stream) => JoinWire.WriteHere(new StreamBits(stream), here));

                JoinHello hello = default;
                bool answered = false;
                int waited = 0, limit = (int)(s.Seconds * 1000f);
                while (!connection.IsDisposed) {
                    lock (hellos) answered = hellos.Take(connection.Identifier, out hello);
                    if (answered || waited >= limit) break;
                    await Task.Delay(100);
                    waited += 100;
                }
                if (connection.IsDisposed) { lock (hellos) hellos.Drop(connection.Identifier); return; }   // they left: no line, no refusal

                JoinHello? got = answered ? hello : (JoinHello?)null;
                verdict = JoinRule.Decide(got, JoinWire.Version, OwnVersion, Cfg.SharedChecksum, s.Check, s.SettingsRefuse);
                string name = JoinRule.Ascii(connection.UserInfo?.Username ?? connection.PlayerName, 40);
                string line = JoinRule.Line(name, got, verdict, s.Check);
                if (verdict.Problem != null) MelonLogger.Warning(answered ? line : $"{line} (no answer in {waited / 1000f:0.#} s)");
                else if (Logs.Summary) MelonLogger.Msg(line);
                reason = verdict.Problem ?? "";
            } catch (Exception e) {
                MelonLogger.Error($"[Archery] Join: the check itself failed, so the player is let in: {e}");
                return;                                                                 // a fault never refuses
            }
            if (!verdict.Refuse) return;

            // A decided refusal. Ours must reach the joining game before the game's own "Failed join prerequisites.".
            try {
                string text = JoinRule.Ascii(reason, 160);
                connection.Send(null, MessageType.RequestJoinResponse, (c, stream) => {
                    bool allowed = false, prerequisites = false;
                    int result = JoinWire.RefusedResult;
                    stream.SerializeBool(ref allowed);
                    stream.SerializeBool(ref prerequisites);
                    stream.SerializeInteger(ref result);
                    stream.SerializeString(ref text);
                });
                connection.FlushPacketManagers();
                await Task.Delay(500);
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: could not send the refusal text: {e.Message}"); }
            throw new InvalidOperationException(JoinRule.ModName + " refused the join: " + reason);
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
                if (connection == null || JoinWire.ReadHere(new StreamBits(stream), out JoinHere here) != WireRead.Ok) {
                    if (Logs.Debug) MelonLogger.Msg("[Archery] Join: a message on the join number was not a Here: ignored");
                    return;
                }
                ModVersion own = OwnVersion;
                if (!ReferenceEquals(greetedBy, connection)) {
                    greetedBy = connection;
                    if (Logs.Summary) MelonLogger.Msg($"[Archery] Join: this server runs {JoinRule.ModName} {here.Version} (wire {here.Wire})");
                    if (here.Wire > JoinWire.Version)
                        MelonLogger.Warning($"[Archery] Join: Your {JoinRule.ModName} {own} is too old for this server's {here.Version}. Update it, then join again.");
                    else if (here.Wire < JoinWire.Version)
                        MelonLogger.Warning($"[Archery] Join: Your {JoinRule.ModName} {own} is newer than this server's {here.Version}. The server needs an update first.");
                }
                var hello = new JoinHello { Wire = JoinWire.Version, Version = own, Check = Cfg.SharedChecksum, Platform = JoinPlatform.Pc };
                connection.Send(null, Message, (c, s) => JoinWire.WriteHello(new StreamBits(s), hello));
            } catch (Exception e) { MelonLogger.Error($"[Archery] Join: answering the server's greeting failed: {e.Message}"); }
        }
    }
}
