using System;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Quest only: work that cost the headset whole frames when it ran during a shot, done ahead while the player is idle after joining,
    // one item per frame (each costs 4-30 ms there). Measured on the headset on 2026-11-25 ([Timing] report): adding one of the mod's
    // own components to a new object costs about 4 ms (ghost arrows, bolts, lights: 60-90 ms frames as a volley was drawn), and the
    // first use of each effect or sound name scanned every loaded asset (20-30 ms each). The kept objects are used again for the rest
    // of the game run (Volley.Ghost.Recycle, Fx's bolts and lights), so after this nothing of the kind is made during play unless a
    // burst needs more than are kept.
    internal static class QuestStock {
        const float StartAfterSeconds = 5f;
        const int Ghosts = 16, HeavyGhosts = 8, Bolts = 24, Lights = 8;
        static float startAt = -1f;
        static bool done;
        static int items;
        static float began;

        static readonly int T = Timing.Section("Main.OnUpdate / QuestStock (warm-up, one item)");

        /// <summary>Main.OnUpdate on a game (never the server): one item per frame from a few seconds after the tree is ready.</summary>
        internal static void Tick() {
            if (done || !Tree.Ready) return;
            if (startAt < 0f) { startAt = Time.unscaledTime + StartAfterSeconds; return; }
            if (Time.unscaledTime < startAt) return;
            using var _t = Timing.Begin(T);
            if (items == 0) began = Time.unscaledTime;
            try {
                if (Step()) { items++; return; }
                done = true;
                MelonLogger.Msg($"[Archery] Quest: warm-up done ({items} items over {Time.unscaledTime - began:0.0} s: the game's effects and sounds indexed, {Volley.Ghost.Spare} ghost arrows, {Fx.StockBolts} bolts and {Fx.StockLights} lights kept)");
            }
            catch (Exception e) {
                done = true;
                MelonLogger.Warning($"[Archery] Quest: warm-up stopped after {items} items ({e.Message}); the rest is made when first used");
            }
        }

        // one item; false when there is nothing left to do
        static bool Step() {
            if (!Fx.EffectsIndexed) { Fx.IndexEffects(); return true; }
            if (!Fx.SoundsIndexed) { Fx.IndexSounds(); return true; }
            if (Volley.Ghost.Spare < HeavyGhosts) { Volley.Ghost.StockShell(heavyReady: true); return true; }
            if (Volley.Ghost.Spare < Ghosts) { Volley.Ghost.StockShell(heavyReady: false); return true; }
            if (Fx.StockBolts < Bolts) { Fx.StockBolt(); return true; }
            if (Fx.StockLights < Lights) { Fx.StockLight(); return true; }
            return false;
        }
    }
}
