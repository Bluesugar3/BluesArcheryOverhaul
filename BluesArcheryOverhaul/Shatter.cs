using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Alta.Impact;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Progression.Skills;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    // Shatter (a Charge side orb off Charge Shot 3 since batch 4b; Charge main only). Play-test 7 (the user: "a giant shotgun is
    // coming out behind ... a good chunk of pellets and ... a really good chunk of damage"; chosen: about double the arrow's hit on a
    // creature right behind, 6 m, a wide cone): the splinter rays (7 shatters, 21 splinters, 0 struck) became a blast. Rules in
    // Spectral.cs (ShatterRule, ShatterBlast, SplinterPattern, tested).
    //   - When: a Charge main's charged real arrow (stage reached above 0, never a Holy Arrow beam) on its first creature hit
    //     (Weight's real-arrow hook or the clients' embed fallback), along the hit's direction (Weight.HitDirection). One shatter
    //     per release (Targets.Release.Shattered), within [Shatter] RealArrowSeconds of the release.
    //   - The blast (server): every creature whose body reaches into a ConeDegrees (60, full width) cone behind the hit along the
    //     arrow's direction, within Metres (10; 6 until G3), takes BlastShare (x2) + PerMultiOrb (0.15) per multi-arrow orb of the charged
    //     arrow's hit (its power x the stage's [Charge] StageNDamage), times a distance falloff (full within FullMetres 3, EdgeShare
    //     0.25 at Metres) and an angle falloff (full on the axis, EdgeShare at the cone's edge). Never the struck creature; a player
    //     only with the server's PvP on and [PvP] BlastHitsPlayers (fixF, default off), never the shooter (Hits.Apply, Pvp.cs). One batched hit each, no cripple of its own,
    //     under Hits.Dealing: it cannot jump (Ricochet) or shatter again. No target list is needed: the server finds the bodies in
    //     a bounded overlap sphere round the hit (grown buffer, as Targets.cs).
    //   - Clients draw what they can see (no new message): the same cone over the creatures loaded on this machine, from the real
    //     arrow's hit hook or the embed fallback. Fix batch C: a burst out of the struck creature's back (the game's shatter
    //     effects and sound, a light, a ring of shards), [Shatter] Pellets (24) bright pellets through the cone (one onto each
    //     creature this machine's own cone test says was caught, with the game's orange hit effect there; the rest on a fan
    //     seeded from the arrow's id, stopping at the first solid thing or their reach), and one thump in the shooter's bow hand
    //     (BlastLook). If it cannot be built: one log line, and shatters show nothing until the settings are reloaded (the
    //     plain look before it, its fallback, was removed in 0.16.0).
    //   - Try-out: while the orb is held, a networked thing that is not a creature counts as struck; practised on the charged
    //     release (Volley.Launch).
    //   Fix batch D:
    //   - Strength (fixA-review I-4): BlastShare x the damage the hit REALLY dealt to the struck creature (Weight's hook passes
    //     the hit's HealthDamage; unknown: power x stage x [Shatter] AssumedEffectiveness 0.65); a body counts at most
    //     [Shatter] MaxBodyMetres 0.75 m wide. A real hit of 2.34 at stage 3 gives a creature right behind 4.68.
    //   - Off the main path (the user: "make ... shatter ... work even though its not your main path", "Half strength"): any
    //     owner. The trigger is the real arrow's first creature hit of any release (a Quickdraw 4 line: its first creature, along
    //     the line, ServerLineHit), once per release, within RealArrowSeconds; the blast is x [Complements] SideOrbOffPathShare,
    //     no multi-arrow bonus (ShatterRule.MultiOrbs), and creatures the same release already damaged are skipped. Clients draw
    //     it for off-path owners too (the release record's ShatterReal); their pellets may still land on a skipped creature.
    internal static class Shatter {
        // fixF (final review M-5): one cap per kind of line, so off-path owners' shots (once a second) cannot use up the charged
        // shots' lines; past its first lines an off-path shatter is logged every OffPathEvery-th time.
        const int LogsPerSession = 64, OffPathLogs = 16, OffPathEvery = 25, OverlapCap = 4096;
        static MelonPreferences_Entry<float> cone = null!, metres = null!, fullMetres = null!, edgeShare = null!, maxBody = null!, realArrowSeconds = null!, assumed = null!;
        static Collider[] overlap = new Collider[128];
        static bool fullWarned;
        static readonly List<HealthObject> healths = new List<HealthObject>();
        static readonly List<SplinterCandidate> candidates = new List<SplinterCandidate>();
#if QUEST
        static readonly HashSet<HealthObject> seenHealth = new HashSet<HealthObject>(Il.ByPointer<HealthObject>());
#else
        static readonly HashSet<HealthObject> seenHealth = new HashSet<HealthObject>();
#endif
        static int logs, offLogs, offSeen;

        internal static void Init() {
            var cat = Cfg.Category("Shatter"); // BlastShare, PerMultiOrb and Pellets are created by Paths (the tablet's numbers)
            BlastValues d = BlastValues.Default;
            cone = Cfg.Shared(cat.CreateEntry("ConeDegrees", d.ConeDegrees, description: "Shatter: the blast fills a cone this wide (degrees, its full width) behind the struck creature, along the arrow's direction."));
            metres = Cfg.Shared(cat.CreateEntry("Metres", d.Metres, description: "Shatter: the blast reaches this many metres from the hit."));
            fullMetres = Cfg.Shared(cat.CreateEntry("FullMetres", d.FullMetres, description: "Shatter: within this many metres the blast deals its full share; beyond it, less down to EdgeShare at Metres."));
            edgeShare = Cfg.Shared(cat.CreateEntry("EdgeShare", d.EdgeShare, description: "Shatter: the share of the blast left at Metres and at the cone's edge (0-1)."));
            maxBody = Cfg.Shared(cat.CreateEntry("MaxBodyMetres", ShatterBlast.DefaultMaxBody, description: "Shatter: a creature's body counts as at most this wide (radius, metres) when the blast's cone and reach are measured to it."));
            assumed = Cfg.Shared(cat.CreateEntry("AssumedEffectiveness", ShatterBlast.DefaultAssumedEffectiveness, description: "Shatter: the blast is a share of the damage the shattering hit really dealt; when that is not known, the hit is taken as the arrow's power x the charge stage's damage x this."));
            realArrowSeconds = Cfg.Shared(cat.CreateEntry("RealArrowSeconds", 10f, description: "Shatter: the charged arrow shatters only within this many seconds of the release (its flight; an arrow picked up and stabbed into a creature later does not shatter)."));
            Look.Init(cat);
        }

        /// <summary>[Shatter] Metres itself, read from the entry and nothing else (fixF C-1: Paths.OrbNumbers reads this, never
        /// Blast, which reads Paths.OrbNumbers).</summary>
        internal static float MetresSetting => metres != null ? metres.Value : BlastValues.Default.Metres;

        /// <summary>The blast's live numbers (shared settings).</summary>
        internal static BlastValues Blast {
            get {
                if (cone == null) return BlastValues.Default;
                OrbValues o = Paths.OrbNumbers;
                return new BlastValues(metres.Value, Mathf.Clamp(cone.Value, 0f, 170f), o.ShatterBlast, fullMetres.Value, Mathf.Clamp01(edgeShare.Value), o.ShatterPerOrb);
            }
        }

        static float MaxBody => maxBody != null ? Mathf.Max(0.05f, maxBody.Value) : ShatterBlast.DefaultMaxBody;
        static float Assumed => assumed != null ? Mathf.Max(0f, assumed.Value) : ShatterBlast.DefaultAssumedEffectiveness;

        /// <summary>The release is still in its flight (b4b-review M-5, as Ricochet's real-arrow jump).</summary>
        static bool InFlight(Targets.Release rec) => Time.time - rec.At <= (realArrowSeconds != null ? Mathf.Max(0f, realArrowSeconds.Value) : 10f);

        /// <summary>The orb is on ([Tree] ShatterOrb) and this build is a Charge main that owns it.</summary>
        internal static ShatterHalf HalfFor(Build b) => Tree.OrbOn(Orb.Shatter) ? ShatterRule.Half(b) : ShatterHalf.None;
        internal static bool Applies(Build b) => HalfFor(b) != ShatterHalf.None;

        /// <summary>The pellet streaks this build's blast draws (0 when the orb is off or it gets no half).</summary>
        internal static int PelletsFor(Build b) => Applies(b) ? ShatterRule.Pellets(b, Paths.OrbNumbers.ShatterPellets) : 0;

        static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        static void Log(string line, bool offPath) {
            if (offPath) {
                offSeen++;
                if (offLogs < OffPathLogs) {
                    if (++offLogs == OffPathLogs) line += Inv($" (off-path shatters from now on: one line in every {OffPathEvery})");
                    MelonLogger.Msg(line);
                }
                else if (offSeen % OffPathEvery == 0) MelonLogger.Msg(line + Inv($" (off-path shatter number {offSeen} this session)"));
                return;
            }
            if (logs >= LogsPerSession) return;
            if (++logs == LogsPerSession) line += " (no more Shatter lines for charged shots this session)";
            MelonLogger.Msg(line);
        }

        // ---------------------------------------------------------------- what was struck, and who is in the blast

        /// <summary>A health target's body: the centre and a radius from its colliders' bounds (capped by MaxBodyMetres).</summary>
        static Vector3 Body(HealthObject h, out float radius) {
            Transform root = Targets.RootOf(h);
            float cap = MaxBody;
            if (!Targets.BoundsOf(root, out Bounds b)) { radius = Mathf.Min(0.5f, cap); return root.position + Vector3.up * 0.5f; }
            radius = ShatterBlast.Radius(b.extents.x, b.extents.y, b.extents.z, cap); // fixA-review I-4: capped at 0.75 m by default
            return b.center;
        }

        /// <summary>Whether a hit on <paramref name="h"/> (or, with no health, on the detector's thing) shatters, and its id.</summary>
        static bool StruckBy(ImpactDetector? detector, HealthObject? h, bool tryingOut, out uint id, out string name) {
            id = 0u; name = h != null ? h.name : detector != null ? detector.name : "?";
#if QUEST
            bool player = h.Is<PlayerHealth>();
#else
            bool player = h is PlayerHealth;
#endif
            bool creature = Targets.IsCreature(h);
            // the try-out target counts only as a networked thing (never the ground or a static world object)
            bool thing = tryingOut && (h != null || (detector != null && detector.GetComponentInParent<NetworkEntity>() != null));
            if (!ShatterRule.Struck(creature, player, thing)) return false;
            if (h != null) id = Targets.IdOf(h);
            return true;
        }

        /// <summary>Every living creature (and, with <paramref name="players"/>, every player but <paramref name="shooter"/>) whose body
        /// is within the blast's reach of <paramref name="point"/> on this machine, into healths/candidates; the struck one is left out.
        /// A bounded overlap sphere whose buffer grows as Targets.cs's does.</summary>
        static readonly int T_Gather = Timing.Section("Shatter.Gather (overlap)");
        static void Gather(uint struck, HealthObject? struckHealth, Vector3 point, bool players, PlayerController? shooter) {
            using var _t = Timing.Begin(T_Gather, struck);
            healths.Clear(); candidates.Clear(); seenHealth.Clear();
            float reach = Mathf.Max(0f, Blast.Metres) + MaxBody;
            if (!(reach > 0f)) return;
            int n = Targets.Overlap(point, reach, ref overlap, OverlapCap, ref fullWarned, "Shatter: round a blast");
            for (int i = 0; i < n; i++) {
                Collider c = overlap[i];
                if (c == null) continue;
                ImpactDetector det = c.GetComponentInParent<ImpactDetector>();
                HealthObject h = det != null ? det.HealthTarget : null!;
                if (h == null || h == struckHealth || !seenHealth.Add(h) || !(h.Health > 0f)) continue;
#if QUEST
                bool player = h.Is<PlayerHealth>();
#else
                bool player = h is PlayerHealth;
#endif
#if QUEST
                if (player ? !players || (shooter != null && h.Cast<PlayerHealth>().Player == shooter) : !Targets.IsCreature(h)) continue;
#else
                if (player ? !players || (shooter != null && ((PlayerHealth)h).Player == shooter) : !Targets.IsCreature(h)) continue;
#endif
                uint id = Targets.IdOf(h);
                if (id == 0u) id = (uint)h.GetInstanceID();
                if (id == struck) continue;
                Vector3 centre = Body(h, out float r);
                healths.Add(h); candidates.Add(new SplinterCandidate(id, centre.x, centre.y, centre.z, r, player));
            }
        }

        // ---------------------------------------------------------------- server: the blast that counts

        /// <summary>Server, Weight's real-arrow hook (after Ricochet): the charged arrow's first creature hit.</summary>
        static readonly int T_ServerRealHit = Timing.Section("Shatter.ServerRealHit");
        internal static void ServerRealHit(uint arrowId, Arrow arrow, ImpactDetector detector, HealthObject? h, ImpactHitWithTool hit) {
            using var _t = Timing.Begin(T_ServerRealHit, arrowId);
            try {
                Targets.Release? rec = Targets.Of(arrowId);
                if (rec == null || rec.Shattered || !rec.ShatterReal || !InFlight(rec)) return;
                if (!StruckBy(detector, h, Shrine.Trying(rec.Shooter, Orb.Shatter), out uint id, out string name)) return;
                if (!ShatterRule.MayShatter(HitSource.RealArrow)) return; // G1-review M-3: the release's one shatter is spent only when it may start
                rec.Shattered = true;
                PlayerController? shooter = rec.Shooter != null ? rec.Shooter.PlayerController : null;
                Vector3 dir = Weight.HitDirection(hit, arrow, shooter, out _);
                ImpactTool tool = arrow.ImpactTool != null ? arrow.ImpactTool : hit.Tool;
                ServerShatter(rec, id, h, name, hit.Point, dir, tool, shooter, h != null ? hit.HealthDamage : float.NaN, HitSource.RealArrow);
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shatter: arrow {arrowId} real-arrow shatter failed: {e.Message}"); }
        }

        /// <summary>Server, fix batch D (off the main path): a Quickdraw 4 line's first creature shatters along the line's direction,
        /// from what the line dealt it (<paramref name="dealt"/>, Hits.LastApplyDamage). Creatures the line pierced are skipped
        /// (the release damaged them: ShatterRule.SkipsDamaged). Nothing without the orb; once per release.</summary>
        static readonly int T_ServerLineHit = Timing.Section("Shatter.ServerLineHit");
        internal static void ServerLineHit(uint arrowId, HealthObject h, Vector3 point, Vector3 direction, ImpactTool tool, float dealt, PlayerController? shooter) {
            using var _t = Timing.Begin(T_ServerLineHit, arrowId);
            try {
                Targets.Release? rec = Targets.Of(arrowId);
                if (rec == null || rec.Shattered || !rec.ShatterReal || !InFlight(rec)) return;
                if (!StruckBy(null, h, false, out uint id, out string name)) return;
                if (!ShatterRule.MayShatter(HitSource.RealArrow)) return; // G1-review M-3, as above
                rec.Shattered = true;
                ServerShatter(rec, id, h, name, point, direction, tool, shooter, dealt, HitSource.RealArrow); // the line is the real arrow's shot
            }
            catch (Exception e) { MelonLogger.Warning($"[Archery] Shatter: arrow {arrowId} line shatter failed: {e.Message}"); }
        }

        // fixA-review I-4 (the controller's ruling): the blast is BlastShare x the damage the shattering hit really dealt to the struck
        // creature (else the arrow's power x the stage's multiplier x [Shatter] AssumedEffectiveness), with the distance and angle
        // falloffs and a Charge main's per-multi-arrow-orb bonus; off the main path (fix batch D) x [Complements] SideOrbOffPathShare,
        // no multi-arrow bonus, and creatures the release already damaged are skipped. Hits.Apply deals a multiple of the arrow's
        // power, so the health wanted is divided by the power. <paramref name="source"/>: what dealt the shattering hit; only the hits
        // ShatterRule.MayShatter allows start a blast (never a jump's or a blast's own).
        static readonly int T_ServerShatter = Timing.Section("hit Shatter.ServerShatter (blast)");
        static void ServerShatter(Targets.Release rec, uint struckId, HealthObject? struckHealth, string struckName, Vector3 point, Vector3 dir, ImpactTool tool, PlayerController? shooter, float dealt, HitSource source) {
            using var _t = Timing.Begin(T_ServerShatter, struckId);
            if (!ShatterRule.MayShatter(source)) return;

            Vector3 axis = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
            bool off = rec.Shatter == ShatterHalf.OffPath;
            int tier = Draws.ReleaseOf(rec.Id)?.ChargedTier ?? 0;
            float stage = off ? 1f : Tree.ChargeDamageAt(tier);
            float power = Hits.Power(tool);
            float hitDamage = ShatterBlast.HitDamage(dealt, power, stage, Assumed);
            bool known = !float.IsNaN(dealt) && !float.IsInfinity(dealt) && dealt > 0f;
            BlastValues v = Blast;
            // G2: the line is built only at [Log] Level Debug (its throttle counters in Log count logged lines only)
            StringBuilder? line = !Logs.Debug ? null : new StringBuilder(Inv($"[Archery] Shatter: arrow {rec.Id} {(off ? "the arrow (off the main path)" : "the charged arrow")} shattered on '{struckName}' ({struckId}) at {Targets.P(point)}: blast {v.Metres:0.#} m, {v.ConeDegrees:0} degrees, up to x{ShatterBlast.Peak(v, rec.MultiOrbs) * rec.ShatterShare:0.##} of the hit's {hitDamage:0.##} damage ({(known ? "dealt" : Inv($"not known: power {power:0.##} x stage x{stage:0.##} x {Assumed:0.##}"))}{(off ? "" : Inv($", stage {tier}"))})"));
            if (axis == Vector3.zero || tool == null || !(power > 0f)) {
                if (line != null) { line.Append(axis == Vector3.zero ? ", no direction" : tool == null ? ", no arrow tool" : ", the arrow has no power"); Log(line.ToString(), off); }
                return;
            }
            Gather(struckId, struckHealth, point, Pvp.BlastLandsOnPlayers, shooter); // fixF I-2: players only with [PvP] BlastHitsPlayers
            int struck = 0, skipped = 0;
            using var _tel = HitScope.As(DealtBy.Shatter, rec.Id); // balance telemetry: the blast's hits
            for (int i = 0; i < candidates.Count; i++) {
                SplinterCandidate c = candidates[i];
                float share = ShatterBlast.Share(struckId, point.x, point.y, point.z, axis.x, axis.y, axis.z, c, v, rec.MultiOrbs);
                if (!(share > 0f)) continue;
                if (ShatterRule.SkipsDamaged(rec.Shatter, rec.Damaged.Contains(c.Id))) { skipped++; continue; }
                HealthObject h = healths[i];
                float multiplier = ShatterBlast.Damage(share, hitDamage, rec.ShatterShare) / power;
                Vector3 at = new Vector3(c.X, c.Y, c.Z);
                Vector3 normal = (point - at).sqrMagnitude > 1e-6f ? (point - at).normalized : -axis;
                bool hitOne = false; string how = "no detector";
                foreach (object o in h.ImpactDetectors) {
#if QUEST
                    if (!(o as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).Is(out ImpactDetector det)) continue;
#else
                    if (!(o is ImpactDetector det) || det == null) continue;
#endif
                    // cripple 0 (b3-review I-3): the charged arrow's stagger is Weight's ledger's; the blast adds none of its own
                    if (Hits.Apply(tool, det, at, normal, multiplier, out how, 0f)) { hitOne = true; break; }
                }
                if (hitOne) { struck++; Targets.Damaged(rec.Id, h); }
                line?.Append(Inv($"; '{h.name}' ({c.Id}) {(at - point).magnitude:0.0} m share x{share:0.##} = {multiplier * power:0.##} damage (x{multiplier:0.##} of the arrow), no cripple: {(hitOne ? how : "not dealt (" + how + ")")}"));
            }
            if (line != null) {
                line.Append(Inv($"; {struck} hit of {candidates.Count} within reach{(skipped > 0 ? Inv($", {skipped} skipped (this release already damaged them)") : "")}"));
                Log(line.ToString(), off);
            }
        }

        // ---------------------------------------------------------------- clients: what this machine can see

        /// <summary>The local player is trying Shatter out in the shrine (their own releases only).</summary>
        static bool LocalTrying(Targets.Release rec) {
            if (!rec.Local) return false;
            SkillOrb? held = ShrineView.Held;
            ProgressionSlot? slot = Tree.SlotOf(Orb.Shatter);
            return held != null && held.Slot != null && slot != null && held.Slot.Hash == slot.Hash;
        }

        /// <summary>Clients, Weight's real-arrow hook or its embed fallback: the charged arrow's shatter, looks only.</summary>
        static readonly int T_ClientRealHit = Timing.Section("Shatter.ClientRealHit");
        internal static void ClientRealHit(uint arrowId, Arrow? arrow, ImpactDetector? detector, HealthObject? h, Vector3 point, Vector3 fallbackDir, ImpactHitWithTool? hit) {
            using var _t = Timing.Begin(T_ClientRealHit, arrowId);
            if (ApplicationManager.IsHeadless || NetworkSceneManager.IsServer) return;
            try {
                Targets.Release? rec = Targets.Of(arrowId);
                if (rec == null || rec.Shattered || !rec.ShatterReal || !InFlight(rec)) return;
                if (!StruckBy(detector, h, LocalTrying(rec), out uint id, out string name)) return;
                rec.Shattered = true;
                Vector3 dir = fallbackDir;
                if (hit != null && arrow != null) dir = Weight.HitDirection(hit, arrow, rec.Shooter != null ? rec.Shooter.PlayerController : null, out _);
                ClientShatter(rec, id, h, detector, name, point, dir);
            }
            catch (Exception e) { Look.Failed(e.Message); }
        }

        /// <summary>The struck thing's own root: the blast's pellets start behind it and never stop on it.</summary>
        static Transform? RootOf(HealthObject? h, ImpactDetector? detector) {
            if (h != null) return Targets.RootOf(h);

            if (detector == null) return null;
            NetworkEntity de = detector.GetComponentInParent<NetworkEntity>();
            if (de == null) return detector.transform;
            return de.PrefabRoot != null ? de.PrefabRoot.transform : de.transform;
        }

        static readonly int T_ClientShatter = Timing.Section("look start Shatter.ClientShatter");
        static void ClientShatter(Targets.Release rec, uint struckId, HealthObject? struckHealth, ImpactDetector? detector, string struckName, Vector3 point, Vector3 dir) {
            using var _t = Timing.Begin(T_ClientShatter, struckId);
            int n = Mathf.Max(0, rec.Splinters);
            Vector3 axis = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
            if (axis == Vector3.zero) return;
            BlastValues v = Blast;
            Gather(struckId, struckHealth, point, false, null);
            float alpha = rec.Local ? 1f : Mathf.Clamp01(Volley.OtherArcherBrightness);
            int onto = 0;
            string look = "looks off";
            if (Look.On) {
                try { onto = BlastLook(rec, struckId, struckHealth, detector, point, axis, v, n, alpha); look = "the blast"; }
                catch (Exception e) { Look.Failed(e.Message); look = "no look (it failed)"; }
            }
            Log(Inv($"[Archery] Shatter: arrow {rec.Id} seen shattering on '{struckName}' ({struckId}) at {Targets.P(point)} ({(rec.Local ? "own" : "seen")}): {look}, {n} pellets, {onto} onto a creature in the blast ({candidates.Count} within reach)"), rec.Shatter == ShatterHalf.OffPath);
        }

        // Fix batch C (play-test 7, the user: "it kind of explodes out particles and small shattered pieces out the back, hitting
        // enemies behind it ... a giant shotgun is coming out behind of it"; "not really having too much feedback"). What this
        // machine draws, from its own view of the hit and of the creatures round it (no message says what the server struck):
        //   - the burst, out of the struck creature's back (where the arrow's line leaves its body, ShatterBlast.ExitDistance):
        //     the game's shatter effects, a short orange light, a ring of shards and a spray of sparks thrown on along the
        //     arrow's direction, the game's shatter sound, and one thump in the shooter's own bow hand (Look.Burst);
        //   - the pellets: [Shatter] Pellets streaks through the server's own cone (same apex at the hit, same axis, same width,
        //     same reach), all at one speed (the whole reach in StreakSeconds). One goes onto each creature this machine believes
        //     was caught (the server's cone test, ShatterBlast.Share, on this machine's positions) and ends in the game's orange
        //     hit burst on its near side; the rest fan through the cone on directions that are the release's own
        //     (SplinterPattern.FanDirection, seeded from the arrow's id: every machine draws the same fan) and stop with a small
        //     spark at whatever solid thing is first on their way (never the struck creature), else at their reach.
        // Returns how many creatures it drew a pellet onto.
        static readonly int T_BlastLook = Timing.Section("look Shatter.BlastLook");
        static int BlastLook(Targets.Release rec, uint struckId, HealthObject? struckHealth, ImpactDetector? detector, Vector3 point, Vector3 axis, BlastValues v, int n, float alpha) {
            using var _t = Timing.Begin(T_BlastLook, struckId);
            Transform? root = RootOf(struckHealth, detector);
            float exit = 0f;
            if (struckHealth != null) {
                Vector3 body = Body(struckHealth, out float radius);
                exit = ShatterBlast.ExitDistance(point.x, point.y, point.z, axis.x, axis.y, axis.z, body.x, body.y, body.z, radius);
                exit = BackOf(root, point, axis, 2f * radius, exit);
            }
            Vector3 back = point + axis * exit;
            Look.Burst(rec, point, back, axis, alpha);
            float reach = Mathf.Max(0.1f, v.Metres);
            int onto = 0;
            for (int i = 0; i < candidates.Count && onto < n; i++) {
                SplinterCandidate k = candidates[i];
                if (!(ShatterBlast.Share(struckId, point.x, point.y, point.z, axis.x, axis.y, axis.z, k, v, rec.MultiOrbs) > 0f)) continue;
                onto++;
                var centre = new Vector3(k.X, k.Y, k.Z);
                Vector3 to = centre - back;
                float d = to.magnitude;
                Vector3 end = d > k.Radius + 0.05f ? centre - to / d * k.Radius : centre; // about its near side
                // its real near side when this pellet's own line meets it first (through a wall the server's blast still counts)
                if (d > 0.05f && Hits.Cast(back, to / d, d, out RaycastHit on, root) && on.transform != null && on.transform.IsChildOf(RootOf(healths[i], null)!)) end = on.point;
                Look.Pellet(back, end, alpha, true, true);
            }
            int rest = n - onto;
            for (int i = 0; i < rest; i++) {
                SplinterPattern.FanDirection(axis.x, axis.y, axis.z, rec.Id, i, rest, v.ConeDegrees, out double dx, out double dy, out double dz, out float share, Look.MinReach);
                var d = new Vector3((float)dx, (float)dy, (float)dz);
                float far = reach * share;
                if (d.sqrMagnitude < 0.5f || far <= exit + 0.05f) continue;
                Vector3 from = point + d * exit;
                bool stopped = Hits.Cast(from, d, far - exit, out RaycastHit hit, root);
                Look.Pellet(from, stopped ? hit.point : point + d * far, alpha, false, stopped);
            }
            return onto;
        }

        static readonly List<RaycastHit> backHits = new List<RaycastHit>();

        /// <summary>Where the arrow's line leaves the struck creature: metres from the hit to the last of its own colliders on the
        /// line (looked for from behind it, within <paramref name="span"/>); <paramref name="fallback"/> (the body ball's far
        /// side, ShatterBlast.ExitDistance) when the line meets none of them.</summary>
        static float BackOf(Transform? root, Vector3 point, Vector3 axis, float span, float fallback) {
            if (root == null || !(span > 0f)) return fallback;
            float from = span + 0.1f;
            Hits.CastAll(point + axis * from, -axis, from, backHits);
            foreach (RaycastHit h in backHits)
                if (h.collider != null && h.transform != null && h.transform.IsChildOf(root)) return Mathf.Clamp(from - h.distance, 0f, span);
            return fallback;
        }

        // ---------------------------------------------------------------- the looks

        // Everything a shatter shows is the game's own (its shatter effects and sound, its orange hit effect, its spark material
        // through the mod's shared spark emitter) plus flat vertex-coloured lines for the pellets. Pellets are pooled: one
        // GameObject with one LineRenderer each, reused; per frame only positions and colours are set (no allocation).
        // G4 (play-test 8: the blast reaches 10 m, was 6; the user: "a little hard to see"), the shipped numbers re-read at the
        // new reach. Every pellet flies the whole reach in StreakSeconds: at 0.2 s that was 50 m/s, a 0.7 m streak moving 0.6 m
        // a frame at 80 frames a second (thin dashes, gone in 0.15 to 0.2 s). Now 0.28 s: about 36 m/s (it was 30 at 6 m), a
        // fanned pellet is in the air 0.21 to 0.28 s (it ends between 7.5 and 10 m) and moves 0.45 m a frame under a 1.1 m
        // streak 5.5 cm wide at its head, so it reads as one solid streak. The burst at the body grows with the reach: the
        // game's two effects at 1.3 size, 24 sparks at 10 m/s, 18 shards at 11 m/s and 3.5 times a spark's size (a ring that
        // reaches about 3 to 4 m, as it reached about 2.5 of 6 m), a light of 4 reaching 8 m for 0.2 s. A caught creature's
        // burst is played by its own pellet when that pellet arrives (0.08 s at 3 m, 0.28 s at 10 m); the server's damage
        // lands at once, so on the far ones the health drops up to a quarter second before the pellet shows it.
        internal static class Look {
            const int PoolCap = 96;
            static MelonPreferences_Entry<bool> looks = null!;
            static MelonPreferences_Entry<string> effect = null!, backEffect = null!, caughtEffect = null!, sound = null!, chargeColour = null!, headColour = null!;
            static MelonPreferences_Entry<float> effectScale = null!, backEffectScale = null!, caughtEffectScale = null!, volume = null!, streakSeconds = null!, streakLength = null!,
                streakWidth = null!, flashBrightness = null!, flashRange = null!, flashSeconds = null!, shardDegrees = null!, shardSpeed = null!, shardSize = null!,
                haptic = null!, hapticSeconds = null!, minReach = null!, offPathScale = null!, sparkSpeed = null!;
            static MelonPreferences_Entry<int> sparks = null!, shards = null!, endSparks = null!, caughtSparks = null!;
            static readonly List<Splinter> pool = new List<Splinter>();
            static readonly HashSet<string> missing = new HashSet<string>();
            static bool failed;
            static readonly Color DefaultCharge = new Color(1f, 0.604f, 0.18f);   // #FF9A2E, the heavy arrows' orange-gold
            static readonly Color DefaultHead = new Color(1f, 0.886f, 0.651f);    // #FFE2A6, the same orange near white

            internal static void Init(MelonPreferences_Category cat) {
                looks = cat.CreateEntry("Looks", true, description: "Looks of a shatter. Off changes nothing it does.");
                effect = cat.CreateEntry("Effect", "Charged Axe Shatter Effect", description: "The game's pooled effect where the blast leaves the struck creature's back (names separated by ';', the first one found is used; empty = none). See loaded_assets.txt.");
                effectScale = cat.CreateEntry("EffectScale", 1.3f, description: "Size of that effect, times its own.");
                backEffect = cat.CreateEntry("BackEffect", "Axe Shatter Damage Effect", description: "A second pooled effect at the same place, pointing along the arrow: the game's spray of specks (empty = none).");
                backEffectScale = cat.CreateEntry("BackEffectScale", 1.3f, description: "Size of BackEffect, times its own.");
                sound = cat.CreateEntry("Sound", "Axe Charge Shatter Sounds;Glass Break", description: "Sound at the shattering hit (names separated by ';', the first one found is used; empty = none).");
                volume = cat.CreateEntry("Volume", 1f, description: "Volume of that sound (0 to 1).");
                sparks = cat.CreateEntry("Sparks", 24, description: "Sparks thrown along the arrow's direction out of the struck creature's back. 0 = none.");
                sparkSpeed = cat.CreateEntry("SparkSpeed", 10f, description: "Speed of those sparks in metres a second (each between half of this and all of it; a spark lives about a third of a second).");
                flashBrightness = cat.CreateEntry("FlashBrightness", 4f, description: "Brightness of the short orange light at the burst. 0 = none.");
                flashRange = cat.CreateEntry("FlashRange", 8f, description: "Metres that light reaches.");
                flashSeconds = cat.CreateEntry("FlashSeconds", 0.2f, description: "Seconds that light takes to go out.");
                shards = cat.CreateEntry("Shards", 18, description: "Shards (large sparks) thrown in a ring round the arrow's direction out of the struck creature's back. 0 = none.");
                shardDegrees = cat.CreateEntry("ShardDegrees", 40f, description: "Degrees between the arrow's direction and each shard: the ring's width (the pellets' cone is half of [Shatter] ConeDegrees).");
                shardSpeed = cat.CreateEntry("ShardSpeed", 11f, description: "Speed of the shards in metres a second (each between half of this and all of it).");
                shardSize = cat.CreateEntry("ShardSize", 3.5f, description: "Size of a shard, times a spark's.");
                streakSeconds = cat.CreateEntry("StreakSeconds", 0.28f, description: "Seconds a pellet takes to fly the blast's whole reach ([Shatter] Metres); every pellet flies at that speed (0.28 s over 10 m = about 36 metres a second).");
                streakLength = cat.CreateEntry("StreakLength", 1.1f, description: "Length in metres of each pellet's trail.");
                streakWidth = cat.CreateEntry("StreakWidth", 0.055f, description: "Width in metres of each pellet at its head; it narrows to nothing at the end of its trail.");
                minReach = cat.CreateEntry("PelletMinReach", SplinterPattern.MinReach, description: "A fanned pellet that meets nothing flies between this share and all of [Shatter] Metres (each its own, so the fan's end is ragged). 1 = every one the whole way.");
                chargeColour = cat.CreateEntry("ChargeColour", "#FF9A2E", description: "Colour of the pellets' trails, the shards, the sparks and the light (a Charge Shot archer's shatter).");
                headColour = cat.CreateEntry("HeadColour", "#FFE2A6", description: "Colour of each pellet's bright head.");
                endSparks = cat.CreateEntry("EndSparks", 4, description: "Sparks where each pellet stops. 0 = none.");
                caughtEffect = cat.CreateEntry("CaughtEffect", "Combat Hit", description: "The game's pooled effect on each creature the blast caught, as your game sees it: its orange hit effect (empty = none).");
                caughtEffectScale = cat.CreateEntry("CaughtEffectScale", 0.8f, description: "Size of CaughtEffect, times its own.");
                caughtSparks = cat.CreateEntry("CaughtSparks", 8, description: "Sparks off each creature the blast caught. 0 = none.");
                haptic = cat.CreateEntry("Haptic", 0.8f, description: "Strength (0 to 1) of the one thump in your bow hand when your own arrow shatters. 0 = none.");
                hapticSeconds = cat.CreateEntry("HapticSeconds", 0.12f, description: "Length of that thump in seconds.");
                offPathScale = cat.CreateEntry("OffPathLookScale", 0.6f, description: "Looks only: a shatter off the main path (half strength) shows its burst at this size, its light and sound at this share, and no thump in the bow hand. 1 = the same size and volume as a Charge Shot archer's (still no thump).");
                Cfg.Reloaded += () => { missing.Clear(); failed = false; }; // a changed setting may be what makes it buildable: try again
            }

            internal static Color ChargeColour => chargeColour != null ? Cfg.Colour(chargeColour, DefaultCharge) : DefaultCharge;
            static Color HeadColour => headColour != null ? Cfg.Colour(headColour, DefaultHead) : DefaultHead;
            /// <summary>[Shatter] PelletMinReach: the least share of the reach a fanned pellet flies.</summary>
            internal static float MinReach => minReach != null ? Mathf.Clamp01(minReach.Value) : SplinterPattern.MinReach;
            /// <summary>Shatters show something at all ([Shatter] Looks, and the look has not failed since the settings were last read).</summary>
            internal static bool On => !failed && looks != null && looks.Value;
            /// <summary>The look's size for this release: [Shatter] OffPathLookScale off the main path, else 1 (fixF, final review M-8).</summary>
            internal static float ScaleFor(Targets.Release rec) =>
                rec.Shatter == ShatterHalf.OffPath ? (offPathScale != null ? Mathf.Clamp01(offPathScale.Value) : 0.6f) : 1f;

            /// <summary>The look failed: one log line, and shatters show nothing until the settings are reloaded (damage is unaffected).</summary>
            internal static void Failed(string why) {
                if (failed) return;
                failed = true;
                MelonLogger.Warning($"[Archery] Shatter: the blast's look could not be built ({why}); shatters show nothing until the settings are reloaded");
            }

            static readonly int T_Spawn = Timing.Section("look Shatter.Spawn (game effect)");
            static void Spawn(string names, Vector3 at, Quaternion rot, float k) {
                using var _t = Timing.Begin(T_Spawn, names);
                if (string.IsNullOrWhiteSpace(names) || !(k > 0f)) return;
                PooledObjectDefinition? def = Fx.FindEffect(names);
                if (def != null) Fx.GetScaled(def, at, rot, k);
                else if (missing.Add(names)) MelonLogger.Warning($"[Archery] Shatter: effect '{names}' not found; the blast shows without it"); // G5 (M-5): every level
            }

            /// <summary>The burst out of the struck creature's back at <paramref name="back"/>, along <paramref name="axis"/>: the
            /// game's two shatter effects, a short light, sparks, a ring of shards (turned by an angle of the release's own), the
            /// sound at the hit, and the thump in the shooter's own bow hand. Throws when something cannot be built.</summary>
            static readonly int T_Burst = Timing.Section("look Shatter.Burst");
            internal static void Burst(Targets.Release rec, Vector3 point, Vector3 back, Vector3 axis, float alpha) {
                using var _t = Timing.Begin(T_Burst);
                Color c = ChargeColour;
                var spark = new Color(c.r, c.g, c.b, alpha);
                Quaternion rot = Quaternion.LookRotation(axis);
                float lookScale = ScaleFor(rec);   // off the main path: smaller, quieter, no thump
                bool full = rec.Shatter != ShatterHalf.OffPath; // never a thump off the main path
                Spawn(effect.Value, back, rot, effectScale.Value * lookScale);
                Spawn(backEffect.Value, back, rot, backEffectScale.Value * lookScale);
                if (flashBrightness.Value > 0f && flashSeconds.Value > 0f)
                    Fx.Flash(back, c, flashBrightness.Value * alpha * lookScale, Mathf.Max(0.1f, flashRange.Value * lookScale), flashSeconds.Value);
                VolleyFx.Sparks.Emit(back, axis, Mathf.Max(0, sparks.Value), spark, 25f, Mathf.Max(0f, sparkSpeed.Value));
                int n = Mathf.Clamp(shards.Value, 0, 64);
                if (n > 0) {
                    Fx.Basis(axis, out Vector3 u, out Vector3 w);
                    float a = Mathf.Clamp(shardDegrees.Value, 0f, 85f) * Mathf.Deg2Rad, turn = LookSeed.Unit(rec.Id, 0, 21) * 2f * Mathf.PI;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a), speed = Mathf.Max(0f, shardSpeed.Value), size = Mathf.Max(0.1f, shardSize.Value);
                    for (int k = 0; k < n; k++) {
                        float phi = turn + k * (2f * Mathf.PI / n);
                        VolleyFx.Sparks.Emit(back, ca * axis + sa * (Mathf.Cos(phi) * u + Mathf.Sin(phi) * w), 1, spark, 4f, speed, size);
                    }
                }
                VolleyFx.Play(sound.Value, point, 0f, Mathf.Clamp01(volume.Value) * lookScale);
                if (full && rec.Local && rec.Bow != null && haptic.Value > 0f) {
                    Controller? hand = Volley.BowHand(rec.Bow);
                    if (hand != null) hand.TriggerHaptic(Mathf.Clamp01(haptic.Value), Mathf.Clamp(hapticSeconds.Value, 0.01f, 0.5f));
                }
            }

            /// <summary>One pellet of the blast from <paramref name="from"/> to <paramref name="to"/>: a bright head with a short
            /// trail, at the blast's one speed. Where it stops: a small spark (thrown back when <paramref name="stopped"/> by
            /// something solid, on along its way at its reach), and for one that <paramref name="caught"/> a creature the game's
            /// orange hit effect. Throws when it cannot be built.</summary>
            internal static void Pellet(Vector3 from, Vector3 to, float alpha, bool caught, bool stopped) {
                Splinter? s = Take();
                if (s == null) return; // every pooled pellet is flying: this one is not drawn
                Color tail = ChargeColour, head = HeadColour;
                float speed = Mathf.Max(0.1f, Blast.Metres) / Mathf.Max(0.02f, streakSeconds.Value);
                s.Fly(from, to, new Color(head.r, head.g, head.b, alpha), new Color(tail.r, tail.g, tail.b, alpha), Mathf.Max(0.001f, streakWidth.Value),
                      (to - from).magnitude / speed, Mathf.Max(0.02f, streakLength.Value), Mathf.Max(0, endSparks.Value), caught, stopped);
            }

            /// <summary>A pellet has stopped (from its own Update).</summary>
            static void Landed(Vector3 at, Vector3 dir, float alpha, int count, bool caught, bool stopped) {
                try {
                    Color c = ChargeColour;
                    var spark = new Color(c.r, c.g, c.b, alpha);
                    Vector3 away = stopped ? -dir : dir;
                    VolleyFx.Sparks.Emit(at, away, count, spark, stopped ? 70f : 30f, 2f, 1.3f);
                    if (!caught) return;
                    Spawn(caughtEffect.Value, at, Quaternion.LookRotation(away), caughtEffectScale.Value);
                    VolleyFx.Sparks.Emit(at, away, Mathf.Max(0, caughtSparks.Value), spark, 70f, 3f, 1.5f);
                }
                catch (Exception e) {
                    // G1-review M-1: one pellet's end (it has already flown) never switches the whole look off; logged once a session
                    if (!landedWarned) { landedWarned = true; MelonLogger.Warning($"[Archery] Shatter: a pellet's landing spark failed ({e.Message}); that pellet's end is skipped (said once)"); }
                }
            }

            static bool landedWarned;

            static Splinter? Take() {
                foreach (Splinter p in pool) if (p != null && !p.Busy) return p;
                if (pool.Count >= PoolCap) { pool.RemoveAll(p => p == null); if (pool.Count >= PoolCap) return null; }
                if (Fx.LineMaterial() == null) throw new InvalidOperationException("no line material");
                var go = new GameObject("ArcheryShatterPellet");
                UnityEngine.Object.DontDestroyOnLoad(go);
                Splinter s = go.AddComponent<Splinter>();
                s.Build();
                pool.Add(s);
                return s;
            }

            // One pooled pellet: a line whose head travels from where it starts to where it stops: a bright head narrowing into a
            // trail that fades to nothing behind it, then its landing (Landed).
            sealed class Splinter : MonoBehaviour {
#if QUEST
                public Splinter(System.IntPtr pointer) : base(pointer) { }   // Quest: a component of the mod is made by the game from its pointer
#endif
                static readonly AnimationCurve Comet = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                LineRenderer line = null!;
                Vector3 from, to, dir;
                Color head, tail;
                float born, life, length, distance;
                int sparks;
                bool caught, stopped;
                internal bool Busy => gameObject.activeSelf;

                internal void Build() {
                    line = Fx.NewLine(gameObject, 2, 0.01f, false);
                    line.widthCurve = Comet;
                    line.numCapVertices = 3;
                    gameObject.SetActive(false);
                }

                internal void Fly(Vector3 a, Vector3 b, Color headColour, Color tailColour, float width, float seconds, float len, int endSparks,
                                  bool isCaught, bool isStopped) {
                    from = a; to = b; head = headColour; tail = tailColour; life = Mathf.Max(0.02f, seconds); length = len; born = Time.time;
                    sparks = endSparks; caught = isCaught; stopped = isStopped;
                    distance = (b - a).magnitude;
                    dir = distance > 1e-5f ? (b - a) / distance : Vector3.up;
                    line.widthMultiplier = width;
                    gameObject.SetActive(true);
                    Place(0f);
                }

                // point 0 is the head, point 1 the end of its trail
                void Place(float t) {
                    float at = distance * Mathf.Clamp01(t);
                    line.SetPosition(0, from + dir * at);
                    line.SetPosition(1, from + dir * Mathf.Max(0f, at - length));
                    line.startColor = head;
                    line.endColor = new Color(tail.r, tail.g, tail.b, 0f);
                }

                static readonly int TUpdate = Timing.Section("Update Shatter.Splinter");
                void Update() {
                    using var _t = Timing.Begin(TUpdate);
                    float t = (Time.time - born) / life;
                    if (t < 1f) { Place(t); return; }
                    gameObject.SetActive(false);
                    Landed(to, dir, head.a, sparks, caught, stopped);

                }
            }
        }
    }
}
