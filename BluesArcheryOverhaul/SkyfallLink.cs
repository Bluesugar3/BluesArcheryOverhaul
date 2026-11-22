using System;
using System.Collections.Generic;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Skyfall's one message: the ONE owner-approved exception to the "No new network traffic" rule. CLAUDE.md says of it: "The
    // exception (`SkyfallLink.cs`) is one message to a Skyfall holder's game." ... "It is never sent per shot and never goes to other
    // players." ... "Any further exception needs the owner's approval."
    //   What it holds: the game's own player text message (PlayerCommunicationManager.SendMessageToPlayer, a PlayerTextMessage shown
    //     for 0 s) carrying only the tag text SkyfallSignal.Format: QSR_SKYFALL_V1|<holds 0/1>|<festival 0/1>|<shared-settings check>.
    //     No name and nothing else. The text is built when it is sent (Skyfall.Holds, Skyfall.Festival and Cfg.SharedShort as they are
    //     then), so a queued message is never stale; one queue entry per account (SkyfallOutbox).
    //   When: the console's `skyfall give` and `skyfall take` (to that player, if online), and once each time a holder joins (a new
    //     Player entity id for the account, polled once a second: SkyfallSeen). There is no festival command any more (Task 12b), so
    //     the festival field is always 0 and QueueHolders (every online holder) has no caller.
    //     Nothing else: never per shot, not when the server's settings reload. A message waits until the player's PlayerController and
    //     the game's PlayerCommunicationManager exist (the game drops a message to a player without a controller) and is tried again
    //     every frame while the player is online; after SkyfallOutbox.GiveUpSeconds it is warned once and still waits. A player who
    //     goes offline is dropped from the queue; their next join is a rejoin.
    //   To whom: that holder only (or the player just taken from, who is told "does not hold"). Never anyone else.
    //   On the client: a postfix on PlayerTextMessage.Serialize latches it (Skyfall.LocalHolds, LocalFestival and
    //     LocalTakenAt on a take) and a prefix on PlayerMessageDisplay.Display swallows every QSR_SKYFALL_ text, so nothing ever shows
    //     on screen. The latch is dropped when this game leaves the server (Player.Current changes away from a player).
    internal static class SkyfallLink {
        static readonly SkyfallOutbox outbox = new SkyfallOutbox();
        static readonly SkyfallSeen seen = new SkyfallSeen();
        static readonly HashSet<int> warned = new HashSet<int>();   // accounts already warned about a long wait; an account leaves it at Done
        static readonly HashSet<int> failed = new HashSet<int>();   // accounts whose send threw (warned once); an account leaves it at Done
        static float nextPoll;
        const float PollSeconds = 1f;
        static readonly List<KeyValuePair<int, uint>> polled = new List<KeyValuePair<int, uint>>();   // reused per poll
        static readonly List<int> sending = new List<int>();                                          // reused per flush

        /// <summary>Server: this account's game is to be told (the text is built when it is sent). Account 0 is ignored.</summary>
        internal static void Queue(int account) => outbox.Want(account, Time.unscaledTime);

        /// <summary>Server: queues every online holder (festival on/off). Returns how many.</summary>
        internal static int QueueHolders() {
            int n = 0;
            foreach (IPlayer ip in Player.AllPlayers) {
#if QUEST
                if (!ip.Is(out Player p) || p.UserInfo == null) continue;
                int account = p.UserInfo.Identifier;
#else
                if (!(ip is Player p) || p == null || p.UserInfo == null) continue;
                int account = p.UserInfo.Identifier;
#endif
                if (!Skyfall.Holds(account)) continue;
                Queue(account);
                n++;
            }
            return n;
        }

        /// <summary>Every frame from Main.OnUpdate, before the headless return. Server: the rejoin poll and the queued sends. Client:
        /// the latch is dropped when this game leaves the server.</summary>
        internal static void Tick() {
            if (NetworkSceneManager.IsServer) ServerTick();
            else ClientTick();
        }

        // ---------------------------------------------------------------- server: the send queue

        static void ServerTick() {
            _ = Skyfall.Grants;   // the first tick reads the grant file
            float now = Time.unscaledTime;
            if (now >= nextPoll) {
                nextPoll = now + PollSeconds;
                polled.Clear();
                foreach (IPlayer ip in Player.AllPlayers) {
#if QUEST
                    if (!ip.Is(out Player p) || p.UserInfo == null) continue;   // still joining: seen at a later poll
#else
                    if (!(ip is Player p) || p == null || p.UserInfo == null) continue;   // still joining: seen at a later poll
#endif
                    polled.Add(new KeyValuePair<int, uint>(p.UserInfo.Identifier, p.Identifier));
                }
                foreach (int account in seen.Changed(polled))
                    if (Skyfall.Holds(account)) outbox.Want(account, now);   // a holder's join or rejoin; nobody else
            }
            if (outbox.Waiting.Count == 0) return;
            sending.Clear();
            sending.AddRange(outbox.Waiting);   // a copy: Send removes what it sends
            foreach (int account in sending) {
                try { Send(account, now); }
                catch (Exception e) {   // kept waiting and tried again next frame (only going offline drops it)
                    if (failed.Add(account)) MelonLogger.Warning($"[Archery] Skyfall: sending account {account}'s signal failed; tried again each frame while they are online: {e.Message}");
                }
            }
        }

        static readonly int TSend = Timing.Section("SkyfallLink.Send (one signal)");
        static void Send(int account, float now) {
            using var _t = Timing.Begin(TSend);
            Player? p = OnlinePlayer(account);
            if (p == null) {   // the only way an entry is dropped unsent
                Done(account);
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Skyfall: account {account} went offline before its signal was sent; dropped (their next join is a rejoin)");
                return;
            }
            PlayerCommunicationManager? messages = PlayerCommunicationManager.Instance;
            if (p.PlayerController == null || messages == null) {   // the game drops a message to a player without a controller: wait
                if (outbox.Expired(account, now) && warned.Add(account))
                    MelonLogger.Warning($"[Archery] Skyfall: account {account}'s game is still waiting for the signal after {SkyfallOutbox.GiveUpSeconds:0} s ({(p.PlayerController == null ? "no body yet" : "the game's message manager is missing")}); it is sent as soon as it can be");
                return;
            }
            bool holds = Skyfall.Holds(account), festival = Skyfall.Festival;
#if QUEST
            messages.SendMessageToPlayer(p.Cast<IPlayer>(), SkyfallSignal.Format(holds, festival, Cfg.SharedShort), 0f);   // 0 s, as CustomIngots
#else
            messages.SendMessageToPlayer(p, SkyfallSignal.Format(holds, festival, Cfg.SharedShort), 0f);   // 0 s, as CustomIngots
#endif
            Done(account);
            if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: told '{Builds.NameOf(p)}''s game quietly: {(holds ? "holds Skyfall" : "does not hold Skyfall")}, festival mode {(festival ? "on" : "off")}");
        }

        static void Done(int account) {
            outbox.Done(account);
            warned.Remove(account);
            failed.Remove(account);
        }

        // The online player of this account. A loop of our own, not Player.SafeGetPlayer: that reads every player's UserInfo.Identifier
        // and throws while one has none yet (the server adds a Player to AllPlayers before InitializePlayerOnServer sets its UserInfo).
        static Player? OnlinePlayer(int account) {
            foreach (IPlayer ip in Player.AllPlayers)
#if QUEST
                if (ip.Is(out Player p) && p.UserInfo != null && p.UserInfo.Identifier == account) return p;
#else
                if (ip is Player p && p != null && p.UserInfo != null && p.UserInfo.Identifier == account) return p;
#endif
            return null;
        }

        // ---------------------------------------------------------------- client: leaving drops the latch

        static IPlayer? lastPlayer;

        // Not on the first player being set: the message can be read in the frame before this tick sees the new player.
        static void ClientTick() {
#if QUEST
            IPlayer? now = Il2Cpp.Player.Current;
#else
            IPlayer? now = global::Player.Current;
#endif
#if QUEST
            if (now.Is(out UnityEngine.Object o) && o == null) now = null;   // a destroyed player counts as none
#else
            if (now is UnityEngine.Object o && o == null) now = null;   // a destroyed player counts as none
#endif
#if QUEST
            if (!Il.Same(now, lastPlayer) && lastPlayer != null) {
#else
            if (!ReferenceEquals(now, lastPlayer) && lastPlayer != null) {
#endif
                bool held = Skyfall.LocalHolds;
                Skyfall.LocalHolds = Skyfall.LocalFestival = false;
                Skyfall.LocalTakenAt = -1f;
                warnedCheck = -1;   // the next server's check is compared afresh
                if (held && Logs.Summary) MelonLogger.Msg("[Archery] Skyfall: left the server; this game holds Skyfall again only when a server says so");
            }
            lastPlayer = now;
        }

        // ---------------------------------------------------------------- client: the latch and the swallow

        // The client handler (PlayerCommunicationManager.HandlePlayerMessage) serializes the message first, then shows it: so this
        // reads the text whether or not this game has a body yet. As Claims.Bits for the ShootArguments struct.
        [HarmonyPatch(typeof(PlayerTextMessage), nameof(PlayerTextMessage.Serialize))]
        static class Latch {
            static readonly int T = Timing.Section("patch PlayerTextMessage.Serialize / SkyfallLink.Latch");
#if QUEST
            static void Postfix(ref PlayerTextMessage __instance, Il2CppAlta.Serialization.Stream stream) {
#else
            static void Postfix(ref PlayerTextMessage __instance, Alta.Serialization.Stream stream) {
#endif
                using var _t = Timing.Begin(T);
                if (stream == null || !stream.IsReading) return;
                try { Read(__instance.Message); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: reading a player message failed: {e.Message}"); }
            }
        }

        static bool warnedUnreadable;
        static int warnedCheck = -1;   // the server check last warned about (-1: none)

        static void Read(string? text) {
            if (!SkyfallSignal.Hidden(text)) return;   // not ours: one prefix test
            if (!SkyfallSignal.TryParse(text, out bool holds, out bool festival, out byte check)) {
                if (!warnedUnreadable) {
                    warnedUnreadable = true;
                    MelonLogger.Warning($"[Archery] Skyfall: the server sent a Skyfall message this version cannot read ('{text}'); is the server's Blue's Archery Overhaul newer? Nothing changes here");
                }
                return;
            }
            bool wasHolds = Skyfall.LocalHolds, wasFestival = Skyfall.LocalFestival;
            Skyfall.LocalHolds = holds;
            Skyfall.LocalFestival = festival;
            if (wasHolds && !holds) Skyfall.LocalTakenAt = Time.time;   // taken: the caster's storm fizzles (Task 11 watches this)
            if ((holds != wasHolds || festival != wasFestival) && Logs.Summary)
                MelonLogger.Msg($"[Archery] Skyfall: this game {(holds ? "holds" : "no longer holds")} Skyfall (festival mode {(festival ? "on" : "off")})");
            // the whole byte (SkyfallSignal.Differs), where the bow statuses' build block carries only 6 bits (Builds.cs)
            if (SkyfallSignal.Differs(check, Cfg.SharedShort)) {
                if (warnedCheck != check) {
                    warnedCheck = check;
                    MelonLogger.Error($"[Archery] Skyfall: SHARED SETTINGS DIFFER FROM THE SERVER (server {check:x2}, this client {Cfg.SharedShort:x2}). This game could not take the server's settings (read the '[Archery] Settings:' lines above): until then this game's storms can be drawn differently from everyone else's.");
                }
            }
            else {
                warnedCheck = -1;   // once fixed, a later difference is warned again
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: shared settings match the server (cfg {check:x2})");
            }
        }

        // The 3-argument overload HandlePlayerMessage calls. Returning false also leaves the popup already showing alone (the overload
        // it would call kills it: ForceNextKillCurrent). Any version's tag is swallowed, so a newer server's message never shows here.
        [HarmonyPatch(typeof(PlayerMessageDisplay), nameof(PlayerMessageDisplay.Display), new[] { typeof(string), typeof(float), typeof(DisplayMessageType) })]
        static class Swallow {
            static readonly int T = Timing.Section("patch PlayerMessageDisplay.Display / SkyfallLink.Swallow");
            static bool Prefix(string? message) {
                using var _t = Timing.Begin(T);
                try { return !SkyfallSignal.Hidden(message); }
                catch { return true; }
            }
        }
    }
}
