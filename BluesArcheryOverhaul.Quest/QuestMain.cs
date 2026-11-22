using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BluesArcheryOverhaul.Main), "Blue's Archery Overhaul", BluesArcheryOverhaul.Main.Version, "Blue")]
[assembly: MelonGame("Alta", "Att_Quest")]
[assembly: HarmonyDontPatchAll]

namespace BluesArcheryOverhaul {
    // Quest only, and only while the port is in phases (docs/superpowers/plans/2026-11-22-quest-port.md): the mod's entry on Quest
    // until Main.cs itself joins the Quest compile (task 5). It starts, runs and stops exactly the modules compiled so far, in
    // Main.cs's own order. `Version` comes from Main.cs at build time (QuestVersion.g.cs, made by the project file), so the Quest
    // build always carries the PC build's version and wire number.
    public partial class Main : MelonMod {
        /// <summary>This mod's name up to 0.18.3 (Cfg.CarryOverOldName). A headset never had the old one; the copy finds nothing.</summary>
        internal const string OldName = "QuickshotRing";

        float nextSlowPoll;

        public override void OnInitializeMelon() {
            LoggerInstance.Msg($"[Archery] Blue's Archery Overhaul v{Info.Version} (Quest; wire {JoinWire.Version})");
            Cfg.CarryOverOldName();
            QuestTrace.Step("start: registering the later orbs' skill class");
            QuestSkills.Register();   // before anything makes a later orb's skill
            QuestTrace.Step("start: skill class registered");
            Logs.Init();
            TimingHost.Init();
            Tree.Init();          // task 2
            Builds.Init();
            Paths.Init();
            JoinGate.Init();
            ServerSettings.Init();
            Cfg.Ready();
            TimingHost.Refresh();
            VolleySkill.TryRegister("start");   // retried on every scene and at the tree load until it works
            QuestPatches.ApplyAll(HarmonyInstance);
            SelfCheck("start-up");
            Cfg.Reloaded += () => SelfCheck("settings reload");
        }

        /// <summary>SelfCheck.cs reads every holder of the whole mod; until those files are in, this reads the ones that are.</summary>
        static void SelfCheck(string when) {
            int sets = 0;
            try {
                object sink;
                sink = JoinGate.Values; sets++;
                sink = Paths.Values; sets++;
                sink = Tree.Stages; sets++;
                sink = Tree.ChargeDamageTable; sets++;
                MelonLogger.Msg($"[Archery] Self-check (Quest): {sets} value sets evaluated ({when})");
            } catch (Exception e) {
                MelonLogger.Error($"[Archery] Self-check (Quest) FAILED after {sets} value sets ({when}): {e}");
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) {
            if (!VolleySkill.Registered) VolleySkill.TryRegister($"scene {sceneName}");
        }

        public override void OnUpdate() {
            TimingHost.Frame();
            try { ServerSettings.Tick(); }
            catch (Exception e) { MelonLogger.Error($"[Archery] Settings: tick failed: {e.Message}"); }
            try { Tree.ClientLoad(); }   // a client loads the archery tree once it has joined (once a second until it is in)
            catch (Exception e) { MelonLogger.Error($"[Archery] Tree: client load failed: {e.Message}"); }
            if (Time.unscaledTime >= nextSlowPoll) {
                nextSlowPoll = Time.unscaledTime + 2f;
                Cfg.Poll();
                Tree.PollIcons();
            }
        }

        public override void OnApplicationQuit() {
            ServerSettings.Quit();
            TimingHost.Shutdown();
        }
    }
}
