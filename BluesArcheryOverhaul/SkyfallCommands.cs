using System;
using System.Collections.Generic;
using Alta.Console;
using Alta.Networking.Scripts.Player;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // The `skyfall` console module on the dedicated server (the game's console, e.g. the TavernLauncher Server window): `skyfall` (who
    // holds it), `skyfall give <player>` / `give all`, `skyfall take <player>` (offline holders too). Skyfall is given and taken ONLY
    // here. Registered from Main.OnInitializeMelon on the dedicated server only ([ServerOnly] would hide it anywhere else). A command only
    // changes the grants and queues the quiet signal: every send happens in SkyfallLink.Tick. None ever throws: a failure is warned with
    // its stack and answered with SkyfallConsole.Failed. The words and the take rule are SkyfallConsole's (SkyfallRules.cs, tested).
    // `take` never stops a storm already falling. There is no festival command (the owner dropped festival mode, 2026-11-19): nothing
    // sets Skyfall.Festival, so every storm is cast in normal mode; the festival plumbing elsewhere stays, dormant.
    [PlayOnly, ServerOnly, Module("skyfall", "Skyfall, the admin sky-rain skill: who holds it, give, take")]
    internal static class SkyfallCommands {
        // Task 14 sets this true for the 0.18.0 play-test build; while false, other sessions' interim builds cannot hand out a
        // half-built Skyfall
        internal const bool Ready = true;

        internal static void Register() {
            try {
                CommandService.CommandCollection.ProcessModule(typeof(SkyfallCommands), typeof(SkyfallCommands).Assembly);
                MelonLogger.Msg("[Archery] Skyfall: console commands added (skyfall, skyfall give, skyfall take)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Skyfall: the skyfall console commands could not be added: {e.Message}"); }
        }

        static readonly int TStatus = Timing.Section("command SkyfallCommands.Status"),
            TGive = Timing.Section("command SkyfallCommands.Give"),
            TTake = Timing.Section("command SkyfallCommands.Take");

        // A parameter with a default makes the word optional, so a bare `skyfall give` gets the usage text, not "Command not found".

        [Command(null, "Who holds Skyfall")]
        [Alias("status")]
        static string Status() {
            using var _t = Timing.Begin(TStatus);
            try {
                var online = new HashSet<int>();
                foreach (var o in Online()) online.Add(o.account);
                return SkyfallConsole.Status(Skyfall.Grants.All, online);
            }
            catch (Exception e) { return Failed(e); }
        }

        [Command("give", "Gives Skyfall to an online player: skyfall give <player>, or skyfall give all")]
        static string Give(string player = "") {
            using var _t = Timing.Begin(TGive);
            try {
                string q = (player ?? "").Trim();
                if (q.Length == 0) return SkyfallConsole.GiveUsage;
                // Not ready: give <player> and give all change nothing. Asked through a call, so the const leaves no unreachable code.
                string? notFinished = SkyfallConsole.NotFinished(Ready);
                if (notFinished != null) return notFinished;
                var online = Online();
                if (string.Equals(q, "all", StringComparison.OrdinalIgnoreCase)) {
                    var given = new List<string>();
                    var already = new List<string>();
                    foreach (var o in online) {
                        if (o.account == 0) continue;   // still joining
                        if (Skyfall.Holds(o.account)) already.Add(o.name);
                        else if (Skyfall.Grants.Give(o.account, o.name)) given.Add(o.name);
                        else continue;   // a name the table refuses (blank or on two lines): not given, not told
                        SkyfallLink.Queue(o.account);
                    }
                    string? err = given.Count > 0 ? Skyfall.SaveGrants() : null;
                    if (given.Count > 0 && Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: given to {given.Count} player(s) from the console (give all): {string.Join(", ", given)}");
                    return SkyfallConsole.GivenAll(given, already, err);
                }
                var names = new List<string>(online.Count);
                foreach (var o in online) names.Add(o.name);
                string? name = SkyfallNames.Match(names, q, out string refusal);
                if (name == null) return SkyfallConsole.NotGiven(refusal);
                // The match found again in the same list (as Player.GetPlayer(name) would, without its throw while a joining player
                // has no UserInfo).
                int account = 0;
                foreach (var o in online) if (o.name == name) { account = o.account; break; }
                if (account <= 0) return SkyfallConsole.NoAccount(name);
                bool held = Skyfall.Holds(account);
                bool changed = Skyfall.Grants.Give(account, name);   // a holder under an older name is renamed
                string? saveError = changed ? Skyfall.SaveGrants() : null;
                SkyfallLink.Queue(account);   // told again even when already held
                if (changed && Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: given to '{name}' ({account}) from the console");
                return SkyfallConsole.Given(name, account, held, saveError);
            }
            catch (Exception e) { return Failed(e); }
        }

        [Command("take", "Takes Skyfall from a player, online or not: skyfall take <player>")]
        static string Take(string player = "") {
            using var _t = Timing.Begin(TTake);
            try {
                string q = (player ?? "").Trim();
                if (q.Length == 0) return SkyfallConsole.TakeUsage;
                var online = Online();
                var pairs = new List<KeyValuePair<int, string>>(online.Count);
                foreach (var o in online) pairs.Add(new KeyValuePair<int, string>(o.account, o.name));
                int? target = SkyfallConsole.TakeTarget(Skyfall.Grants, q, pairs, out string refusal);
                if (!target.HasValue) return refusal;
                int account = target.Value;
                string name = q;
                foreach (var kv in Skyfall.Grants.All) if (kv.Key == account) { name = kv.Value; break; }   // the saved name, before taking
                Skyfall.Grants.Take(account);
                string? saveError = Skyfall.SaveGrants();
                bool isOnline = false;
                foreach (var o in online) if (o.account == account) { isOnline = true; break; }
                if (isOnline) SkyfallLink.Queue(account);   // told "does not hold"; a storm already falling keeps falling
                if (Logs.Summary) MelonLogger.Msg($"[Archery] Skyfall: taken from '{name}' ({account}) from the console");
                return SkyfallConsole.Taken(name, account, isOnline, saveError);
            }
            catch (Exception e) { return Failed(e); }
        }

        static string Failed(Exception e) {
            MelonLogger.Warning($"[Archery] Skyfall: console command failed: {e}");
            return SkyfallConsole.Failed(e.Message);
        }

        // The players online now with a user record, sorted by name: (account (0 while still joining), name as Builds.NameOf).
        static List<(int account, string name)> Online() {
            var l = new List<(int account, string name)>();
            foreach (IPlayer ip in Player.AllPlayers) {
                if (!(ip is Player p) || p == null || p.UserInfo == null) continue;
                l.Add((p.UserInfo.Identifier, Builds.NameOf(p)));
            }
            l.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
            return l;
        }
    }
}
