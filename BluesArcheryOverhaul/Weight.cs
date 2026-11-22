using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Alta.Impact;
using Alta.Networking;
using Alta.StatSystem;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Weight: what a charged release carries into the real arrow's first creature hit. On the server: one
    // cripple hit per creature per release (the real arrow's stagger and its ghosts' summed in the ledger) and the interrupt.
    // Charge Shot 3's pierce was removed in batch 4b (the user: "Charge Shot 3 loses pierce"; Quickdraw 4's line pierces
    // instead, Quickdraw4.cs); an old [Weight] PierceRange key left in a settings file is read by nothing. Clients keep the carry
    // per arrow for the looks. The rules are in Charge.cs (unit-tested).
    internal static class Weight {
        const int ArmedCap = 64, CreatureCap = 256;
        const float ArmedSeconds = 10f, AfterInterruptSeconds = 0.3f;

        static MelonPreferences_Entry<float> charge1 = null!, charge2 = null!, charge3 = null!, ghostFactor = null!,
            batchSeconds = null!, maxWaitSeconds = null!, interruptCooldown = null!, knockDownImmunity = null!;
        static MelonPreferences_Entry<bool> interrupt = null!, interruptWyrms = null!;

        internal static WeightValues Values => charge1 == null ? WeightValues.Default
            : new WeightValues(charge1.Value, charge2.Value, charge3.Value, ghostFactor.Value);

        /// <summary>The cripple one stagger of 1.0 deals: the centre of a Holy Arrow blast.</summary>
        internal static float StaggerUnit => HolyArrow.BlastCripple;

        /// <summary>[Weight] InterruptCooldown: seconds before the same creature can be interrupted again (the server's rule;
        /// SnapWeight spaces its interrupt cue by it).</summary>
        internal static float InterruptCooldown => interruptCooldown != null ? Mathf.Max(0f, interruptCooldown.Value) : 3f;

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        internal static void Init() {
            WeightValues d = WeightValues.Default;
            var cat = Cfg.Category("Weight");
            const string stagger = "Added to a creature's cripple pool by a charged hit at Charge {0}. 1.0 = the centre of a Holy Arrow blast ([HolyArrow] BlastCripple).";
            charge1 = Cfg.Shared(cat.CreateEntry("Charge1Stagger", d.Charge1, description: string.Format(stagger, 1)));
            charge2 = Cfg.Shared(cat.CreateEntry("Charge2Stagger", d.Charge2, description: string.Format(stagger, 2)));
            charge3 = Cfg.Shared(cat.CreateEntry("Charge3Stagger", d.Charge3, description: string.Format(stagger, 3)));
            ghostFactor = Cfg.Shared(cat.CreateEntry("GhostStaggerFactor", d.GhostFactor, description: "Ghosts of a charged shot stagger by the charge's stagger x their share x this."));
            batchSeconds = cat.CreateEntry("BatchSeconds", 0.2f, description: "Server. A release with no spectral arrows out deals its one cripple hit per creature this long after its first stagger lands.");
            maxWaitSeconds = cat.CreateEntry("MaxWaitSeconds", 2f, description: "Server. A release with spectral arrows out deals its stagger when its last spectral arrow has landed, or this long after its first stagger, whichever comes first.");
            interrupt = cat.CreateEntry("Interrupt", true, description: "Server. Charged hits (and a Quickdraw main's quick shots with enough Charge Shot orbs, [Complements] SnapInterruptFrom) cancel the creature's current action, only when the game marks it cancellable. Off = Weight is stagger only.");
            interruptWyrms = cat.CreateEntry("InterruptWyrms", false, description: "Server. Wyrms are left out until tested: cancelling a dig or an emerge can leave one visible while flagged as burrowed.");
            interruptCooldown = cat.CreateEntry("InterruptCooldown", 3f, description: "Server. Seconds before the same creature can be interrupted again, whoever shot it.");
            knockDownImmunity = cat.CreateEntry("KnockDownImmunity", 4f, description: "Server. Seconds after the game knocks a creature down in which Weight neither staggers nor interrupts it.");
        }

        // ---------------------------------------------------------------- armed arrows (every machine)

        sealed class Armed {
            public WeightCarry Carry;
            public PlayerController? Shooter;
            public float At;
            public bool Local, Done; // Done: the real arrow has had its first creature (or terrain) hit
            public Embeddable? Embed;              // clients: the embed fallback's subscription (Task 11)
            public Action<Embeddable>? OnEmbed;
        }

        static void Unwatch(Armed a) {
            Embeddable? e = a.Embed;
            Action<Embeddable>? h = a.OnEmbed;
            a.Embed = null; a.OnEmbed = null;
            if (e == null || h == null) return;
            try { e.Embedded -= h; } catch { }
        }

        static void Drop(uint arrowId) {
            if (armed.TryGetValue(arrowId, out Armed a)) Unwatch(a);
            armed.Remove(arrowId);
        }

        static readonly Dictionary<uint, Armed> armed = new Dictionary<uint, Armed>();
        static readonly List<uint> armedOrder = new List<uint>();

        static void Store(uint arrowId, Armed a) {
            if (arrowId == 0u) return;
            float now = Time.time;
            for (int i = 0; i < armedOrder.Count; i++) {
                uint k = armedOrder[i];
                bool stale = !armed.TryGetValue(k, out Armed old) || now - old.At > ArmedSeconds || k == arrowId;
                if (!stale) continue;
                Drop(k); armedOrder.RemoveAt(i--);
            }
            while (armedOrder.Count >= ArmedCap) { Drop(armedOrder[0]); armedOrder.RemoveAt(0); }
            armed[arrowId] = a;
            armedOrder.Add(arrowId);
        }

        static Armed? Find(uint arrowId) =>
            armed.TryGetValue(arrowId, out Armed a) && Time.time - a.At <= ArmedSeconds ? a : null;

        /// <summary>Server, at release (Draws.Release): remember what this arrow carries.</summary>
        static readonly int T_Arm = Timing.Section("Weight.Arm");
        internal static void Arm(uint arrowId, Resolution r, Build build, Player? shooter, BowString? bow = null) {
            using var _t = Timing.Begin(T_Arm, arrowId);
            WeightCarry carry = WeightRules.ForRelease(r, build, Values, Paths.Values);
            if (!carry.Any) { Drop(arrowId); return; }
            Store(arrowId, new Armed { Carry = carry, Shooter = shooter != null ? shooter.PlayerController : null, At = Time.time });
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Weight: arrow {arrowId} carries {carry}");
        }

        /// <summary>Clients, at release: the same carry, for the looks.</summary>
        static readonly int T_ArmClient = Timing.Section("Weight.ArmClient");
        internal static void ArmClient(uint arrowId, Resolution r, Build build, bool local) {
            using var _t = Timing.Begin(T_ArmClient, arrowId);
            WeightCarry carry = WeightRules.ForRelease(r, build, Values, Paths.Values);
            // fix batch D: an owner of Ricochet or Shatter on any main path keeps the embed fallback too, so its arc and blast show
            // when no client OnToolHit arrives (the looks' "who shows it"; nothing for anyone else)
            bool sideOrbs = r.Ultimate != UltimateUse.Beam && (Ricochet.Applies(build) || Shatter.Applies(build));
            if (!carry.Any && !sideOrbs) { Drop(arrowId); return; }
            Store(arrowId, new Armed { Carry = carry, At = Time.time, Local = local });
        }

        /// <summary>Clients, from ChargeFx.Released right after ArmClient: whether clients get OnToolHit for creatures at all
        /// is unknown (Target disables its object on clients), so the armed arrow's embed is watched too. When it embeds in a
        /// non-player creature and no client OnToolHit was seen for it, the hit looks run from there.</summary>
        static readonly int T_WatchEmbed = Timing.Section("Weight.WatchEmbed");
        internal static void WatchEmbed(NetworkEntity? arrowEntity) {
            using var _t = Timing.Begin(T_WatchEmbed);
            if (arrowEntity == null || NetworkSceneManager.IsServer) return;
            uint id = arrowEntity.Identifier;
            Armed? a = Find(id);
            if (a == null || a.Embed != null) return;
            Arrow arrow = arrowEntity.GetComponent<Arrow>();
            Embeddable? e = arrow != null ? Traverse.Create(arrow).Field("embeddable").GetValue<Embeddable>() : null;
            if (e == null) e = arrowEntity.GetComponent<Embeddable>();
            if (e == null) return;
            Armed me = a;
            a.Embed = e;
            a.OnEmbed = emb => Embedded(id, me, emb);
            e.Embedded += a.OnEmbed;
        }

        static readonly int T_Embedded = Timing.Section("Weight.Embedded");
        static void Embedded(uint id, Armed a, Embeddable emb) {
            using var _t = Timing.Begin(T_Embedded, id);
            try {
                bool current = Find(id) == a;
                Unwatch(a); // once: the next embed of this arrow belongs to a later release
                if (!current || a.Done || emb == null) return;
                NetworkEntity other = emb.EmbeddedEntity;
                NetworkEntity root = other != null ? other.PrefabRoot : null!;
                if (root == null || root.GetComponent<PlayerController>() != null) return;
                HealthObject h = root.GetComponentInChildren<HealthObject>(true);
                if (h == null || h is PlayerHealth || !h.HasCrippleStat) return; // creatures only, as the Balance Ring
                a.Done = true;
                Arrow arrow = emb.GetComponent<Arrow>();
                Transform tipT = arrow != null && arrow.ArrowTip != null ? arrow.ArrowTip.transform : emb.transform;
                Vector3 p = tipT.position, fwd = (arrow != null ? arrow.transform : emb.transform).forward;
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Hit (embed fallback): arrow {id} in '{h.name}' at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})"));
                ChargeFx.Hit(id, null, p, -fwd, h);
                Targets.RealHit(id, h); // Ricochet: struck by its release, and its jump drawn (nothing without the orb)
                Ricochet.ClientStruck(id, h, true, p);
                Shatter.ClientRealHit(id, arrow, null, h, p, fwd, null); // the Charge half's splinters (nothing without the orb)
                SnapWeight.Hit(id, p, -fwd, h); // a Quick main's weighted quick shot (nothing for any other arrow)
                if (a.Local) Balance.Hit(h, Draws.ReleaseOf(id)?.ChargedTier ?? 0);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Hit (embed fallback): arrow {id} failed: {e.Message}"); }
        }

        static bool IsLocalArrow(Arrow arrow) {
            Pickup p = arrow.Entity != null ? arrow.Entity.CommonPickup : null!;
            PlayerController pc = p != null ? p.LastPlayerInteractor : null!;
            return pc != null && pc.IsLocalPlayer;
        }

        /// <summary>The arrow a damage tool belongs to (an arrow has several tools: its own and its parts').</summary>
        internal static Arrow? ArrowOf(ImpactTool tool) {
            if (tool == null) return null;
            Arrow a = tool.GetComponentInParent<Arrow>();
            if (a != null) return a;
            Rigidbody rb = tool.Rigidbody;
            if (rb != null) { a = rb.GetComponent<Arrow>(); if (a != null) return a; }
            return null;
        }

        // ---------------------------------------------------------------- creatures (server)

        sealed class Creature {
            public HealthObject Health = null!;
            public string Name = "?";
            public float LastKnockDown = float.NaN, LastInterrupt = float.NaN, Seen;
            public bool FullSinceKnockDown;
            public StatHandler? OnPool;
            public Stat? Pool;
#if QUEST
            public Il2CppAlta.Intelligence.Agent? Agent;
#else
            public Alta.Intelligence.Agent? Agent;
#endif
            public bool AgentSearched;
            public float Spiritual = float.NaN;
        }

        static readonly Dictionary<int, Creature> creatures = new Dictionary<int, Creature>();
        static readonly FieldInfo? typeMultipliersField = AccessTools.Field(typeof(HealthObject), "impactTypeMultipliers");

        static int KeyOf(HealthObject h) => h.GetInstanceID();

        static Creature CreatureOf(HealthObject h) {
            int key = KeyOf(h);
            if (creatures.TryGetValue(key, out Creature c) && c.Health != null) { c.Seen = Time.time; return c; }
            if (c != null) Forget(key, c); // a destroyed object whose id came round again
            if (creatures.Count >= CreatureCap) {
                var drop = new List<int>();
                foreach (KeyValuePair<int, Creature> kv in creatures) if (kv.Value.Health == null) drop.Add(kv.Key);
                foreach (int k in drop) Forget(k, creatures[k]);
                while (creatures.Count >= CreatureCap) {
                    int oldest = 0; float seen = float.MaxValue;
                    foreach (KeyValuePair<int, Creature> kv in creatures) if (kv.Value.Seen < seen) { seen = kv.Value.Seen; oldest = kv.Key; }
                    Forget(oldest, creatures[oldest]);
                }
            }
            c = new Creature { Health = h, Name = h.name, Seen = Time.time };
            if (NetworkSceneManager.IsServer && h.HasCrippleStat && h.CrippleStat != null) {
                Creature me = c;
                c.Pool = h.CrippleStat;
                c.OnPool = s => { if (s != null && s.Value >= s.Maximum - 0.0001f) me.FullSinceKnockDown = true; };
                c.Pool.Changed += c.OnPool;
            }
            creatures[key] = c;
            return c;
        }

        static void Forget(int key, Creature c) {
            try { if (c.Pool != null && c.OnPool != null) c.Pool.Changed -= c.OnPool; } catch { }
            creatures.Remove(key);
        }

        static float SpiritualOf(Creature c) {
            if (!float.IsNaN(c.Spiritual)) return c.Spiritual;
            float m = 1f;
            try {
                if (typeMultipliersField?.GetValue(c.Health) is Dictionary<ImpactType, float> map && map.TryGetValue(ImpactType.Spiritual, out float v)) m = v;
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Weight: reading '{c.Name}' impact type multipliers failed: {e.Message}"); }
            return c.Spiritual = m;
        }

        static string Pool(HealthObject h, out float value, out float max) {
            value = float.NaN; max = float.NaN;
            if (h == null || !h.HasCrippleStat || h.CrippleStat == null) return "-";
            value = h.CrippleStat.Value; max = h.CrippleStat.Maximum;
            return Inv($"{value:0.00}");
        }

        // ---------------------------------------------------------------- the knock-down probes (server)

        static readonly Type[] KnockDownTypes = {
#if QUEST
            typeof(Il2CppAlta.Intelligence.Turabada), typeof(Il2CppAlta.Intelligence.Gotera), typeof(Il2CppAlta.Intelligence.GreaterGotera),
            typeof(Il2CppAlta.Intelligence.Babu), typeof(Il2CppAlta.Intelligence.Dragonfly),
#else
            typeof(Alta.Intelligence.Turabada), typeof(Alta.Intelligence.Gotera), typeof(Alta.Intelligence.GreaterGotera),
            typeof(Alta.Intelligence.Babu), typeof(Alta.Intelligence.Dragonfly),
#endif
        };

        [HarmonyPatch]
        static class KnockDown {
            static IEnumerable<MethodBase> TargetMethods() {
                foreach (Type t in KnockDownTypes) {
                    MethodInfo? m = AccessTools.Method(t, "Stun", new[] { typeof(float) });
                    if (m != null) yield return m;
                }
            }

            static readonly int T = Timing.Section("patch Agent.Stun (5 creature types) / Weight.KnockDown");
#if QUEST
            static void Postfix(Il2CppAlta.Intelligence.Agent __instance, float strength) {
#else
            static void Postfix(Alta.Intelligence.Agent __instance, float strength) {
#endif
                using var _t = Timing.Begin(T);
                try {
                    if (!NetworkSceneManager.IsServer || __instance == null) return;
                    HealthObject h = __instance.HealthObject;
                    if (h == null) return;
                    Creature c = CreatureOf(h);
                    float now = Time.time;
                    string previous = float.IsNaN(c.LastKnockDown) ? "never" : Inv($"{now - c.LastKnockDown:0.0} s ago");
                    string full = float.IsNaN(c.LastKnockDown) ? "-" : c.FullSinceKnockDown ? "yes" : "no";
                    Pool(h, out float value, out float max);
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: '{c.Name}' ({__instance.GetType().Name}) knocked down by the game (Stun {strength:0.00}); pool {value:0.00} of {max:0.00}; previous knock-down {previous}; pool full again since: {full}"));
                    c.LastKnockDown = now;
                    c.FullSinceKnockDown = false;
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Weight: knock-down probe failed: {e.Message}"); }
            }
        }

        /// <summary>Once, after every mod has patched: which of the five knock-downs carry this mod's probe.</summary>
        internal static void LogPatches() {
            try {
                var found = new List<string>();
                foreach (Type t in KnockDownTypes) {
                    MethodInfo? m = AccessTools.Method(t, "Stun", new[] { typeof(float) });
                    if (m == null) continue;
                    Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    foreach (Patch p in info.Postfixes)
                        if (p.PatchMethod != null && p.PatchMethod.DeclaringType == typeof(KnockDown)) { found.Add(t.Name); break; }
                }
                MelonLogger.Msg($"[Archery] Weight: knock-down patches live on {string.Join(", ", found.ToArray())} ({found.Count} of 5)");
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Weight: listing the knock-down patches failed: {e.Message}"); }
        }

        // ---------------------------------------------------------------- the real arrow's hit (server and clients)

        struct HitState { public float Before; public bool Passes; }

        static bool loggedTool;
        static readonly HashSet<string> livePatches = new HashSet<string>();

        // G2: one flag per patch (no string hashed per frame); the set still says each patch once.
        static bool live0;
        static void Live(ref bool said, string patch) {
            if (said) return;
            said = true;
            if (livePatches.Add(patch) && Logs.Summary) MelonLogger.Msg($"[Archery] Weight: patch on {patch} is live");
        }

        [HarmonyPatch(typeof(ImpactDetector), nameof(ImpactDetector.OnToolHit))]
        static class RealHit {
            static readonly int TPrefix = Timing.Section("patch ImpactDetector.OnToolHit / Weight.RealHit prefix");
            static readonly int TPostfix = Timing.Section("patch ImpactDetector.OnToolHit / Weight.RealHit postfix");
            static void Prefix(ImpactDetector __instance, ImpactHitWithTool impactWithTool, Dictionary<ImpactTool, float> ___hitCooldowns, out HitState __state) {
                using var _t = Timing.Begin(TPrefix);
                __state = new HitState { Before = float.NaN, Passes = false };
                try {
                    if (Hits.Dealing || impactWithTool == null || impactWithTool.Tool == null) return;
                    __state.Passes = ___hitCooldowns == null || !___hitCooldowns.TryGetValue(impactWithTool.Tool, out float until) || Time.realtimeSinceStartup >= until;
                    HealthObject h = __instance.HealthTarget;
                    if (h != null && h.HasCrippleStat && h.CrippleStat != null) __state.Before = h.CrippleStat.Value;
                }
                catch { __state.Passes = false; }
            }

            static void Postfix(ImpactDetector __instance, ImpactHitWithTool impactWithTool, HitState __state) {
                using var _t = Timing.Begin(TPostfix);
                try {
                    if (Hits.Dealing || !__state.Passes || impactWithTool == null) return;
                    HealthObject h = __instance.HealthTarget;
                    if (h is PlayerHealth) return;
                    Arrow? arrow = ArrowOf(impactWithTool.Tool);
                    if (arrow == null || arrow.Entity == null) return;
                    uint id = arrow.Entity.Identifier;
                    if (h == null) {
                        // Shatter (batch 4): in a try-out the try-out target counts as struck, health or not (nothing otherwise)
                        if (NetworkSceneManager.IsServer) Shatter.ServerRealHit(id, arrow, __instance, null, impactWithTool);
                        else Shatter.ClientRealHit(id, arrow, __instance, null, impactWithTool.Point, arrow.transform.forward, impactWithTool);
                        // a terrain or object hit: the arrow's first creature hit is past, whatever it hits next
                        Armed? stopped = Find(id);
                        if (stopped != null && !stopped.Done) {
                            stopped.Done = true;
                            if (!NetworkSceneManager.IsServer) {
                                ChargeFx.Hit(id, impactWithTool, impactWithTool.Point, impactWithTool.Normal, null);
                                SnapWeight.Hit(id, impactWithTool.Point, impactWithTool.Normal, null); // no creature: only ends its wait
                            }
                        }
                        return;
                    }
                    Live(ref live0, "ImpactDetector.OnToolHit");
                    bool server = NetworkSceneManager.IsServer;
                    if (!loggedTool) {
                        loggedTool = true;
                        ImpactTool tool = impactWithTool.Tool;
                        string entity = tool.Entity != null ? tool.Entity.Identifier.ToString(CultureInfo.InvariantCulture) : "none";
                        if (Logs.Debug) MelonLogger.Msg($"[Archery] Hit: tool '{tool.name}' entity {entity} -> arrow {id}");
                    }
                    Vector3 p = impactWithTool.Point;
                    if (server) {
                        Resolution? r = Draws.ReleaseOf(id);
                        string kind = r.HasValue ? r.Value.Kind.ToString() : "?";
                        Pool(h, out float after, out float max);
                        if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Hit: arrow {id} on '{h.name}' {impactWithTool.HealthDamage:0.##} damage at {impactWithTool.RelativeVelocity.magnitude:0.0} m/s at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00}) ({kind}); pool {__state.Before:0.00} -> {after:0.00} of {max:0.00}"));
                    }
                    // Balance Ring: every creature hit of the local player's own real arrow, charged or not (looks only)
                    if (!server && IsLocalArrow(arrow)) Balance.Hit(h, Draws.ReleaseOf(id)?.ChargedTier ?? 0);
                    // Ricochet: the real arrow's creature is struck by its release, and its hit jumps (nothing without the orb)
                    Targets.RealHit(id, h);
                    if (server) Ricochet.ServerRealHit(id, h, impactWithTool);
                    else Ricochet.ClientStruck(id, h, true, p);
                    // Shatter (batch 4b, a Charge side orb): a Charge main's charged arrow throws splinters from its first creature hit,
                    // along the hit's direction (nothing without the orb; once per release)
                    if (server) Shatter.ServerRealHit(id, arrow, __instance, h, impactWithTool);
                    else Shatter.ClientRealHit(id, arrow, __instance, h, p, arrow.transform.forward, impactWithTool);
                    Armed? a = Find(id);
                    if (a == null || a.Done) return;
                    a.Done = true; // each armed arrow acts on its first creature only
                    if (!server) {
                        if (a.Local) { if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Hit (local): arrow {id} on '{h.name}' at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})")); }
                        ChargeFx.Hit(id, impactWithTool, p, impactWithTool.Normal, h);
                        SnapWeight.Hit(id, p, impactWithTool.Normal, h); // a Quick main's weighted quick shot (nothing for any other arrow)
                        return;
                    }
                    if (!h.HasCrippleStat) return; // G3: a combat dummy (Targets.IsDummy) is never staggered or interrupted: it has no pool
                    Creature c = CreatureOf(h);
                    if (a.Carry.Stagger > 0f) ledger.Add(id, KeyOf(h), a.Carry.Stagger, Time.time, a.Carry.WaitsForSpectral(Hits.IsOpen(id)));
                    if (a.Carry.Interrupt) TryInterrupt(c);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] Weight: real arrow hit failed: {e}"); }
            }
        }

        // ---------------------------------------------------------------- the hit's direction (Shatter's splinters)

        static bool directionLogged;

        static bool Usable(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z)
            && v.sqrMagnitude > 1e-6f;

        /// <summary>Which way the real arrow was going when it hit (kept from Charge Shot 3's removed pierce for Shatter): the hit's
        /// relative velocity, checked against the shooter's head, else the hit's direction, else the arrow's forward.</summary>
        static readonly int T_HitDirection = Timing.Section("Weight.HitDirection");
        internal static Vector3 HitDirection(ImpactHitWithTool hit, Arrow arrow, PlayerController? shooter, out string source) {
            using var _t = Timing.Begin(T_HitDirection);
            Vector3 dir = hit.RelativeVelocity;
            source = "RelativeVelocity";
            if (!Usable(dir)) {
                dir = hit.Direction;
                source = "Direction";
                if (!Usable(dir)) { source = "arrow forward"; dir = arrow.transform.forward; }
                dir = dir.normalized;
            }
            else {
                dir = dir.normalized;
                // Unity's relativeVelocity sign is the detector's view: checked against the shooter, not assumed.
                // From the archer's head (b4b-review M-6): the controller's own transform is the play-space origin, metres off at
                // room scale, so at point-blank range it could give the wrong sign.
                Vector3? from = shooter != null ? (shooter.Head != null ? shooter.Head.position : shooter.transform.position) : (Vector3?)null;
                if (from.HasValue && Vector3.Dot(dir, hit.Point - from.Value) < 0f) {
                    dir = -dir;
                    source = "RelativeVelocity (flipped)";
                }
            }
            if (!directionLogged) { directionLogged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] Weight: the real arrow's hit direction from {source}"); }
            return dir;
        }

        // ---------------------------------------------------------------- stagger (server)

        static readonly WeightLedger ledger = new WeightLedger();
        static readonly List<WeightDue> due = new List<WeightDue>(); // Tick's list (only Tick fills it; Deal never reaches Tick)
        static bool tickLogged;

        /// <summary>Server, from Hits.FlushSpectral after a dealt spectral hit on a creature: the ghosts' stagger joins the release's
        /// entry. A charged shot's ghosts stagger by their summed <paramref name="shares"/>; a Multi main's gold ghosts add a flat
        /// StaggerPerGhost for each of the <paramref name="ghosts"/> ghost arrows among the hit (pellets add nothing).</summary>
        static readonly int T_AddSpectral = Timing.Section("Weight.AddSpectral");
        internal static void AddSpectral(uint release, HealthObject target, float shares, int ghosts) {
            using var _t = Timing.Begin(T_AddSpectral, release);
            if (target == null || target is PlayerHealth || !target.HasCrippleStat || !(shares > 0f)) return; // G3: never a combat dummy
            Armed? a = Find(release);
            if (a == null) return;
            float stagger = (a.Carry.GhostStaggerPerShare > 0f ? shares * a.Carry.GhostStaggerPerShare : 0f)
                          + (a.Carry.StaggerPerGhost > 0f && ghosts > 0 ? ghosts * a.Carry.StaggerPerGhost : 0f);
            if (!(stagger > 0f)) return;
            CreatureOf(target);
            ledger.Add(release, KeyOf(target), stagger, Time.time, true);
            if (a.Carry.StaggerPerGhost > 0f && ghosts > 0)
                { if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: arrow {release} gold ghosts: {ghosts} on '{target.name}' add stagger {ghosts * a.Carry.StaggerPerGhost:0.###}")); }
        }

        /// <summary>Server, from Hits.Close: the release's last spectral arrow has ended, so its stagger is dealt now.</summary>
        internal static void ReleaseClosed(uint release) {
            if (ledger.Pending == 0) return;
            foreach (WeightDue d in ledger.TakeRelease(release)) Deal(d);
        }

        static readonly int T_Deal = Timing.Section("hit Weight.Deal (cripple)");
        static void Deal(WeightDue d) {
            using var _t = Timing.Begin(T_Deal);
            try {
                if (!creatures.TryGetValue(d.Creature, out Creature c) || c.Health == null) return; // gone
                HealthObject h = c.Health;
                if (h is PlayerHealth) return; // never: players are not staggered
                float now = Time.time;
                if (WeightRules.Immune(now, c.LastKnockDown, knockDownImmunity.Value)) {
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: arrow {d.Release} '{c.Name}' stagger {d.Stagger:0.00} skipped: knocked down by the game {now - c.LastKnockDown:0.0} s ago"));
                    return;
                }
                float m = SpiritualOf(c);
                if (!(m > 0f)) {
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: arrow {d.Release} '{c.Name}' stagger {d.Stagger:0.00} skipped: this creature takes no Spiritual cripple (Spiritual x{m:0.##})"));
                    return;
                }
                float cripple = WeightRules.Cripple(d.Stagger, StaggerUnit);
                PlayerController? shooter = Find(d.Release)?.Shooter;
                Pool(h, out float before, out float _);
                var hit = new ImpactHit(0f, cripple, 1f, 1f, null, ImpactType.Spiritual, h.transform.position, Vector3.zero, Vector3.zero, null, null, shooter);
                using (HitScope.As(DealtBy.Weight, d.Release)) h.DetectImpact(hit); // balance telemetry
                Pool(h, out float after, out float max);
                if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: arrow {d.Release} '{c.Name}' stagger {d.Stagger:0.00} in {d.Parts} part(s) -> cripple {cripple:0.000} (Spiritual x{m:0.##}); pool {before:0.00} -> {after:0.00} of {max:0.00}"));
            }
            catch (Exception e) { MelonLogger.Error($"[Archery] Weight: dealing arrow {d.Release}'s stagger failed: {e}"); }
        }

        /// <summary>Every frame (Main.OnUpdate, which the headless server also runs up to here): deal what is due.</summary>
        internal static void Tick() {
            if (!NetworkSceneManager.IsServer) return;
            if (!tickLogged) { tickLogged = true; if (Logs.Summary) MelonLogger.Msg("[Archery] Weight: tick is live (server)"); }
            float now = Time.time;
            if (ledger.Pending > 0) {
                ledger.TakeDue(now, batchSeconds.Value, maxWaitSeconds.Value, due); // G2: one kept list, not a new one per frame
                foreach (WeightDue d in due) Deal(d);
                due.Clear();
            }
            for (int i = 0; i < afterInterrupt.Count; i++) {
                if (now < afterInterrupt[i].At) continue;
                Creature c = afterInterrupt[i].Creature;
                afterInterrupt.RemoveAt(i--);
                string now2 = "none";
                if (c.Agent != null) {
#if QUEST
                    Il2CppAlta.Intelligence.IActiveAction act = c.Agent.ActiveAction;
#else
                    Alta.Intelligence.IActiveAction act = c.Agent.ActiveAction;
#endif
                    if (act != null && act.Action != null) now2 = act.Action.Name;
                }
                if (Logs.Debug) MelonLogger.Msg($"[Archery] Weight: after interrupt '{c.Name}' now '{now2}'");
            }
        }

        // ---------------------------------------------------------------- the interrupt (server)

        struct Pending { public float At; public Creature Creature; }
        static readonly List<Pending> afterInterrupt = new List<Pending>();

        /// <summary>Server, from Quickdraw4's line after Hits.Apply struck something with the main line: a Line is a snap, so it
        /// carries the Quick main's snap weight (Charge Shot orbs): its stagger joins the same batched cripple hit as any other
        /// release's, and it interrupts from [Complements] SnapInterruptFrom orbs. Needle lines never call this. Batch 4b: the
        /// line pierces; a creature after the first (<paramref name="first"/> false) is staggered by its <paramref name="share"/>
        /// of the snap's stagger and never interrupted.</summary>
        static readonly int T_OnLineHit = Timing.Section("Weight.OnLineHit");
        internal static void OnLineHit(uint arrowId, HealthObject? h, Player? shooter, float share = 1f, bool first = true) {
            using var _t = Timing.Begin(T_OnLineHit, arrowId);
            try {
                Armed? a = Find(arrowId);
                if (a == null) return;
                if (first) a.Done = true; // the line is the arrow's hit; the placed arrow acts on nothing else
                if (!(a.Carry.Stagger > 0f || a.Carry.Interrupt)) return;
                if (h == null || h is PlayerHealth || !h.HasCrippleStat) return; // G3: never a combat dummy (no stagger pool)
                Creature c = CreatureOf(h);
                float stagger = a.Carry.Stagger * (first ? 1f : Mathf.Max(0f, share));
                if (stagger > 0f) {
                    ledger.Add(arrowId, KeyOf(h), stagger, Time.time, a.Carry.WaitsForSpectral(Hits.IsOpen(arrowId)));
                    if (Logs.Debug) MelonLogger.Msg(Inv($"[Archery] Weight: line arrow {arrowId} staggers '{c.Name}' by {stagger:0.00}{(first ? "" : Inv($" (pierced, x{share:0.##})"))} (batched)"));
                }
                if (first && a.Carry.Interrupt) TryInterrupt(c);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Weight: line weight failed: {e}"); }
        }

#if QUEST
        static Il2CppAlta.Intelligence.Agent? AgentOf(Creature c) {
#else
        static Alta.Intelligence.Agent? AgentOf(Creature c) {
#endif
            if (c.AgentSearched) return c.Agent;
            c.AgentSearched = true;
            NetworkEntity e = c.Health.Entity;
            NetworkEntity root = e != null ? e.PrefabRoot : null!;
            if (root == null) return null;
#if QUEST
            foreach (Il2CppAlta.Intelligence.Agent agent in root.GetComponentsInChildren<Il2CppAlta.Intelligence.Agent>(true))
#else
            foreach (Alta.Intelligence.Agent agent in root.GetComponentsInChildren<Alta.Intelligence.Agent>(true))
#endif
                if (agent != null && agent.HealthObject == c.Health) { c.Agent = agent; break; }
            return c.Agent;
        }

        static readonly int T_TryInterrupt = Timing.Section("Weight.TryInterrupt");
        static void TryInterrupt(Creature c) {
            using var _t = Timing.Begin(T_TryInterrupt);
            HealthObject h = c.Health;
            if (h == null || h is PlayerHealth) return;
            float now = Time.time;
#if QUEST
            Il2CppAlta.Intelligence.Agent? agent = AgentOf(c);
            Il2CppAlta.Intelligence.IActiveAction? active = agent != null ? agent.ActiveAction : null;
            Il2CppAlta.Intelligence.IAction? action = active?.Action;
#else
            Alta.Intelligence.Agent? agent = AgentOf(c);
            Alta.Intelligence.IActiveAction? active = agent != null ? agent.ActiveAction : null;
            Alta.Intelligence.IAction? action = active?.Action;
#endif
            bool cancellable = action != null && action.GeneralSettings != null && action.GeneralSettings.IsCancellable;
#if QUEST
            bool wyrm = agent is Il2CppAlta.Intelligence.Wyrm;
#else
            bool wyrm = agent is Alta.Intelligence.Wyrm;
#endif
            string reason;
            if (!interrupt.Value) reason = "skipped (off)";
            else if (agent == null) reason = "skipped (no Agent found)";
            else if (action == null) reason = "skipped (no action)";
            else if (!cancellable) reason = "skipped (not cancellable)";
            else if (wyrm && !interruptWyrms.Value) reason = "skipped (Wyrm)";
            else if (WeightRules.Immune(now, c.LastKnockDown, knockDownImmunity.Value)) reason = Inv($"skipped (knocked down {now - c.LastKnockDown:0.0} s ago)");
            else if (!float.IsNaN(c.LastInterrupt) && now - c.LastInterrupt < interruptCooldown.Value)
                reason = Inv($"skipped (cooldown, {interruptCooldown.Value - (now - c.LastInterrupt):0.0} s left)");
            else reason = "cancelled";

            bool may = WeightRules.MayInterrupt(now, c.LastInterrupt, interruptCooldown.Value, c.LastKnockDown, knockDownImmunity.Value,
                                                cancellable, wyrm, interruptWyrms.Value, interrupt.Value) && agent != null && action != null;
            if (may != (reason == "cancelled")) reason += " (rule disagrees)"; // never expected; would show a mismatch with the tested rule
            if (may) {
                try {
                    agent!.CancelCurrentAction();
                    c.LastInterrupt = now;
                    afterInterrupt.Add(new Pending { At = now + AfterInterruptSeconds, Creature = c });
                    if (afterInterrupt.Count > 64) afterInterrupt.RemoveAt(0);
                }
                catch (Exception e) { reason = $"failed ({e.Message})"; }
            }
            if (Logs.Debug) MelonLogger.Msg($"[Archery] Weight: interrupt '{c.Name}' action '{(action != null ? action.Name : "none")}' cancellable {(cancellable ? "yes" : "no")}: {reason}");
        }
    }
}
