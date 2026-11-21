using System;
using System.Collections.Generic;
using System.Globalization;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Steady (Quickdraw 1-3): less wobble (a Quick main's every shot, a Charge main's charged shots: SteadyRule.WobbleTier),
    // through one game deviance modifier per tier on Arrow.FlightModifiers (the game multiplies them into every flight tick, ArrowFlightSettings.GetDeviance), and faster snaps (never Lines),
    // through the speed argument of Arrow.Shoot, so the server's flight message and the velocity the spectral arrows launch
    // from both carry it. Decided per arrow in a prefix on Shooter.Shoot on every machine: the server from its own resolution
    // (which already accepts the shooter's claimed snap), the shooter from its own, a spectator from the relayed claim.
    // The rules are SteadyRule (Charge.cs, unit-tested).
    internal static class Steady {
        const int PendingCap = 64;
        const float FallbackMax = 99f;

        static MelonPreferences_Entry<float> wobble1 = null!, wobble2 = null!, wobble3 = null!, speed1 = null!, speed2 = null!, speed3 = null!;

        internal static SteadyValues Values => wobble1 == null ? SteadyValues.Default
            : new SteadyValues(wobble1.Value, wobble2.Value, wobble3.Value, speed1.Value, speed2.Value, speed3.Value);

        sealed class Pending {
            public int Tier;
            public float SnapSpeed, Wobble;
            public bool WobbleOnly; // a Charge main's charged shot: the wobble part alone (its Quick complement)
            public string Kind = "", Name = "?", Side = "";
        }

        static readonly Dictionary<uint, Pending> pending = new Dictionary<uint, Pending>(); // arrow id -> decided at Shooter.Shoot
        static readonly ArrowFlightSettingsDevianceMultiplierModifier?[] modifiers = new ArrowFlightSettingsDevianceMultiplierModifier?[4]; // [tier], 1..3
        static readonly HashSet<string> livePatches = new HashSet<string>();
        static AccessTools.FieldRef<ArrowFlightSettingsDevianceMultiplierModifier, float>? multiplierRef;
        static bool warnedModifier, warnedMax;

        static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0, live1, live2;
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Steady: patch on {patch} is live");
        }

        internal static void Init() {
            SteadyValues d = SteadyValues.Default;
            var cat = Cfg.Category("Steady");
            const string wobble = "Quickdraw {0}: the arrow's wobble in flight is multiplied by this on every shot of a Quickdraw main, and on a Charge Shot main's charged shots (1 = the game's; never for a multi-arrow main).";
            const string speed = "Quickdraw {0}: snaps (not Quickdraw 4 lines, not charged shots) leave this much faster (0.10 = 10%), capped just under the game's 100 m/s network limit.";
            wobble1 = Cfg.Shared(cat.CreateEntry("Wobble1", d.Wobble1, description: string.Format(wobble, 1)));
            wobble2 = Cfg.Shared(cat.CreateEntry("Wobble2", d.Wobble2, description: string.Format(wobble, 2)));
            wobble3 = Cfg.Shared(cat.CreateEntry("Wobble3", d.Wobble3, description: string.Format(wobble, 3)));
            speed1 = Cfg.Shared(cat.CreateEntry("SnapSpeed1", d.Speed1, description: string.Format(speed, 1)));
            speed2 = Cfg.Shared(cat.CreateEntry("SnapSpeed2", d.Speed2, description: string.Format(speed, 2)));
            speed3 = Cfg.Shared(cat.CreateEntry("SnapSpeed3", d.Speed3, description: string.Format(speed, 3)));
            Cfg.Reloaded += RefreshModifiers;
        }

        // ---------------------------------------------------------------- the deviance modifiers (one shared instance per tier)

        static bool SetMultiplier(ArrowFlightSettingsDevianceMultiplierModifier m, float value) {
            try {
                multiplierRef ??= AccessTools.FieldRefAccess<ArrowFlightSettingsDevianceMultiplierModifier, float>("devianceMultiplier");
                multiplierRef(m) = value;
                return true;
            }
            catch (Exception e) {
                if (!warnedModifier) { warnedModifier = true; MelonLogger.Error($"[Archery] Steady: cannot set the wobble multiplier; shots keep the game's wobble: {e.Message}"); }
                return false;
            }
        }

        static ArrowFlightSettingsDevianceMultiplierModifier? ModifierFor(int tier) {
            if (tier < 1 || tier > 3) return null;
            ArrowFlightSettingsDevianceMultiplierModifier? m = modifiers[tier];
            if (m != null) return m;
            m = ScriptableObject.CreateInstance<ArrowFlightSettingsDevianceMultiplierModifier>();
            m.name = $"QuickshotRing Steady {tier}";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!SetMultiplier(m, SteadyRule.Wobble(tier, Values))) { UnityEngine.Object.Destroy(m); return null; }
            modifiers[tier] = m;
            return m;
        }

        static readonly int T_RefreshModifiers = Timing.Section("Steady.RefreshModifiers");
        static void RefreshModifiers() {
            using var _t = Timing.Begin(T_RefreshModifiers);
            SteadyValues v = Values;
            for (int t = 1; t <= 3; t++) {
                ArrowFlightSettingsDevianceMultiplierModifier? m = modifiers[t];
                if (m != null) SetMultiplier(m, SteadyRule.Wobble(t, v));
            }
        }

        static bool IsOurs(ArrowFlightSettingsModifier m) {
            if (m == null) return false;
            for (int t = 1; t <= 3; t++) if (ReferenceEquals(m, modifiers[t])) return true;
            return false;
        }

        static int RemoveOurs(Arrow arrow) => arrow.FlightModifiers.RemoveAll(IsOurs);

        static float NetworkMax() {
            try { return PhysicsSerializationSettings.VelocityMax - 1f; }
            catch (Exception e) {
                if (!warnedMax) { warnedMax = true; MelonLogger.Warning($"[Archery] Steady: cannot read the network speed limit, using {F(FallbackMax, "0")} m/s: {e.Message}"); }
                return FallbackMax;
            }
        }

        // ---------------------------------------------------------------- Step 1: decide per arrow at Shooter.Shoot

        static PlayerController? ArcherOf(BowString bow, Pickup pickup) {
            PlayerController pc = bow.Pickup != null ? bow.Pickup.LastPlayerInteractor : null!;
            if (pc == null && pickup != null) pc = pickup.LastPlayerInteractor;
            return pc != null ? pc : null;
        }

        static string KindText(Resolution r) => r.Ultimate == UltimateUse.Line ? "Line" : r.Kind.ToString();

        [HarmonyPatch(typeof(Shooter), nameof(Shooter.Shoot))]
        static class Decide {
            static readonly int T = Timing.Section("patch Shooter.Shoot / Steady.Decide");
            static void Prefix(Shooter __instance, Pickup pickup, bool isFromNetwork) {
                using var _t = Timing.Begin(T);
                try {
                    if (pickup == null || pickup.Entity == null) return;
                    Live(ref live0, "Shooter.Shoot");
                    uint id = pickup.Entity.Identifier;
                    pending.Remove(id);
                    BowString? bow = Volley.BowForShooter(__instance);
                    if (bow == null) return;
                    PlayerController? pc = ArcherOf(bow, pickup);
                    Player? who = pc != null ? pc.NetworkPlayer : null;
                    string name = Builds.NameOf(who);
                    SteadyValues v = Values;
                    Resolution r;
                    Build build;
                    string side;

                    if (NetworkSceneManager.IsServer) {
                        build = Builds.ServerBuild(who);
                        r = Draws.ResolveServer(bow, who, id); // includes the shooter's claimed snap (the echo fix)
                        side = "server";
                    }
                    else if (!isFromNetwork) {
                        build = Builds.LocalBuild();
                        r = Draws.ResolveLocal(bow);
                        side = "own";
                    }
                    else {
                        build = Builds.ClientBuild(name);
                        side = "seen";
                        // A Line is relayed as Snap: SeenUltimate reads it back from the shooter's build so it is never sped up.
                        if (Claims.TryRelay(id, out ShotClaim? c) && c != null)
                            r = new Resolution(c.Kind, ClaimRules.SeenUltimate(c.Kind, build), ReleasePlan.From(c.Kind, c.Stage), 0);
                        else
                            r = new Resolution(ShotKind.Early, UltimateUse.None, default, 0); // no relay: wobble only, no faster snap
                    }

                    // The wobble part follows the main path (a Charge main's charged shots take it as the Quick complement,
                    // a Multi main never); the speed part only ever moves snaps, which only a Quick main has.
                    int tier = SteadyRule.WobbleTier(build, r.Kind);
                    bool chargeMain = build.Main == MainPath.Charge && r.Kind == ShotKind.Charged;
                    if (tier <= 0 && build.Quick > 0)
                        { if (Logs.Debug) MelonLogger.Msg($"[Archery] Steady: arrow {id} '{name}' {KindText(r)}: no wobble change (main path {build.Main}, Quickdraw {build.Quick}) ({side})"); }
                    if (pending.Count >= PendingCap) pending.Clear();
                    pending[id] = new Pending {
                        Tier = tier,
                        SnapSpeed = SteadyRule.SnapSpeed(build.Quick, r.Kind, r.Ultimate, v),
                        Wobble = SteadyRule.Wobble(tier, v),
                        Kind = KindText(r) + (chargeMain ? " (Charge main)" : ""), Name = name, Side = side,
                        WobbleOnly = chargeMain,
                    };
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Steady: deciding the shot failed: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- Step 2: apply in Arrow.Shoot

        // Arrow.Shoot(Vector3, float, bool): `strength` is the launch speed in m/s (Shooter.Shoot). There is a one-argument
        // overload (the game's extra arrows), which is not patched.
        [HarmonyPatch(typeof(Arrow), nameof(Arrow.Shoot), new[] { typeof(Vector3), typeof(float), typeof(bool) })]
        static class Apply {
            static readonly int T = Timing.Section("patch Arrow.Shoot / Steady.Apply");
            static void Prefix(Arrow __instance, ref float strength) {
                using var _t = Timing.Begin(T);
                try {
                    if (__instance == null) return;
                    Live(ref live1, "Arrow.Shoot");
                    RemoveOurs(__instance);
                    NetworkEntity entity = __instance.Entity;
                    if (entity == null || !pending.TryGetValue(entity.Identifier, out Pending p)) return;
                    pending.Remove(entity.Identifier);
                    if (p.Tier <= 0 && !(p.SnapSpeed > 1f)) return;

                    ArrowFlightSettingsDevianceMultiplierModifier? m = ModifierFor(p.Tier);
                    if (m != null) __instance.FlightModifiers.Add(m);
                    if (p.WobbleOnly) {
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Steady: arrow {entity.Identifier} '{p.Name}' tier {p.Tier} {p.Kind}: wobble x{F(m != null ? p.Wobble : 1f, "0.00")} ({p.Side})");
                        return;
                    }

                    float before = strength, max = NetworkMax();
                    bool capped = false;
                    if (p.SnapSpeed > 1f) {
                        if (strength < max) strength = SteadyRule.Boost(strength, p.SnapSpeed, max);
                        capped = before * p.SnapSpeed > max;
                    }
                    string cap = capped ? $" (capped at {F(max, "0")})" : "";
                    if (Logs.Debug) MelonLogger.Msg($"[Archery] Steady: arrow {entity.Identifier} '{p.Name}' tier {p.Tier} {p.Kind}: speed {F(before, "0.0")} -> {F(strength, "0.0")} m/s{cap}, " +
                                    $"wobble x{F(m != null ? p.Wobble : 1f, "0.00")} ({p.Side})");
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Steady: applying the shot failed: {e.Message}"); }
            }
        }

        // A docked arrow (nocked again, put in a quiver) loses this mod's modifiers; the next shot adds its own.
        [HarmonyPatch(typeof(Pickup), "OnDocked")]
        static class Docked {
            static readonly int T = Timing.Section("patch Pickup.OnDocked / Steady.Docked");
            static void Postfix(Pickup __instance) {
                using var _t = Timing.Begin(T);
                try {
                    if (__instance == null) return;
                    Arrow arrow = __instance.GetComponent<Arrow>();
                    if (arrow == null || arrow.FlightModifiers.Count == 0) return;
                    Live(ref live2, "Pickup.OnDocked");
                    RemoveOurs(arrow);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Steady: dock cleanup failed: {e.Message}"); }
            }
        }
    }
}
