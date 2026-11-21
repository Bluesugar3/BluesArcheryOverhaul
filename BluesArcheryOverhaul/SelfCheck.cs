using System;
using System.Collections.Generic;
using MelonLoader;

namespace BluesArcheryOverhaul {
    // fixF (final review C-1): Paths.OrbNumbers and Shatter.Blast read each other, so the first held bow string would have overflowed
    // the stack on the server and every client; the tests never compile those files and no 0.15.0 build had been started. This reads
    // every settings-derived value holder and the shrine tablet's text for a few builds once, at the end of start-up (after
    // Cfg.Ready, so after any reset or migration) and after each settings reload, so a cycle or an exception shows at start-up on
    // both sides instead of on the first draw. It only reads: the one cache it fills (Paths.OrbNumbers) is the one every reader
    // fills on its first read, and it is dropped first so it is built from the settings just loaded.
    //
    // Who reads whom (fixF, proven by reading; "A -> B" = A's value is built by reading B; leaves read only their own entries):
    //   Paths.Numbers      -> Paths.Values, Weight.Values, Tree.QuickMultiplier, Tree.ChargeDamageAt (ChargeDamageTable), Tree.Stages,
    //                         Volley.Times, Tree.HolySeconds, Volley.InnerShare/OuterShare, Paths.OrbNumbers
    //   Paths.OrbNumbers   -> Quickdraw4.PierceShares, Shatter.MetresSetting (was Shatter.Blast: the cycle)
    //   Shatter.Blast      -> Paths.OrbNumbers;  Shatter.PelletsFor -> Tree.OrbOn, Paths.OrbNumbers
    //   Paths.StagesFor    -> Tree.Stages, Paths.Values;  Paths.VolleyTimesFor -> Volley.Times, Paths.Values
    //   Paths.HolySecondsFor -> Tree.HolySeconds, Paths.Values;  Paths.SharedStage/VolleyFactor -> Paths.Values
    //   Tree.SharedVolleySeconds -> Tree.TwoShotSeconds, Paths.SharedVolleyFactor
    //   Volley.Times       -> Tree.TwoShotSeconds/ThreeShotSeconds/RippleSeconds/RackSeconds, Tree.Stages
    //   Ricochet.JumpMetres/JumpShare/Spread, Targets (off-path share) -> Paths.OrbNumbers (0.16.0: SnapDraw.OpensAt and Mark.PinSeconds/MaxPins went with their orbs)
    //   Weight.StaggerUnit -> HolyArrow.BlastCripple;  Draws.DipGrace -> Volley.RelaxGrace (client);  Pvp.JumpsLandOnPlayers -> Pvp.ModHitsLand
    //   G3: the hold stage (VolleyHold.Stage) -> Paths.VolleyTimesFor, Paths.Values (VolleyStageSeconds); Targets.IsDummy -> Targets.DummyNames
    //   G4: GoldHold (the hold stages' look) -> its own [GoldHold] entries, Volley.Colour/ChargedColour, ChargeFx.ThumpSound/ThumpStrength (leaves)
    //   H2: SeekLook (the homing looks: the lock's cage, the bow's green and lean, the arrival) -> its own [SeekLook] and [SeekingVolley]
    //       look entries, Volley.OtherArcherBrightness, GoldStages (leaves); LockCage / SeekBurst / SeekReveal read SeekLook only
    //   H1: Paths.OrbNumbers -> Seek.Values (a leaf: its own [SeekingVolley] entries only, cached per load); the homing launch and step
    //       (Volley.Launch, VolleySim) -> Seek.Values, Paths.OrbNumbers (SeekerShare); the tablet's Seeking Volley line -> OrbValues.Homing
    //   H3: Seek.Values gained BloomMaxMetres, SideMaxMetres, LetGoSeconds, PlaceMaxMetres, LeadMaxSeconds (still a leaf: its own
    //       entries only); Seek.Open/TargetsNow/PlaceReal -> Seek.Values; the tree layout (Tree.ApplyLayout) -> [TreeLayout] entries and
    //       OrbPlaces.Default (a constant table); Tree, Preview -> no new holder. The self-check below evaluates Seek.Values as before.
    //   H5: Seek.Values gained ChainLocks and ChainMaxLifeSeconds (still a leaf: its own entries only; the chain's flight in VolleySim and
    //       Seek.Homes/ClaimLocks read Seek.Values.FlightSeconds). Evaluated by the Seek.Values line below as before.
    //   Skyfall.Values (the admin skill) -> its own [Skyfall] entries only (a leaf, cached per load); Skyfall.Check -> Skyfall.Values, SkyfallPattern (Unity-free)
    //   Skyfall looks: SkyfallSigil, SkyfallHold, SkyfallSky (Task 10), SkyfallLook (Task 11), SkyfallNail (Task 12) -> their own look entries in [Skyfall] and Skyfall.Values (a leaf); SkyfallCurves and SkyDial are Unity-free functions
    //   JoinGate.Values (the join gate) -> its own [Join] entries only (a leaf, read again on every use); the join step -> JoinGate.Values, Cfg.SharedChecksum
    //   Leaves: Paths.Values, Weight.Values, Steady.Values, Tree.Stages/ChargeDamageTable/HolySeconds/*Seconds, Volley.InnerShare/
    //           OuterShare/RelaxGrace, Quickdraw4.PierceShares, Shatter.MetresSetting, Targets.DummyNames, HolyArrow.BlastCripple, Seek.Values,
    //           Burst's and Claims' numbers, Skyfall.Values.
    // Every edge points from a holder toward the leaves, so there is no cycle.
    internal static class SelfCheck {
        static readonly int T_Run = Timing.Section("SelfCheck.Run");
        internal static void Run(string when) {
            using var _t = Timing.Begin(T_Run, when);
            int sets = 0, texts = 0;
            try {
                Paths.ForgetCachedNumbers();
                object sink;
                sink = Seek.Values; sets++; // H1: before OrbNumbers, which reads it
                sink = Paths.Values; sets++;
                sink = Paths.OrbNumbers; sets++;
                sink = Shatter.Blast; sets++;
                sink = Shatter.MetresSetting; sets++;
                sink = Weight.Values; sets++;
                sink = Weight.StaggerUnit; sets++;
                sink = Steady.Values; sets++;
                sink = Tree.Stages; sets++;
                sink = Tree.ChargeDamageTable; sets++;
                sink = Volley.Times; sets++;
                sink = Quickdraw4.PierceShares; sets++;
                sink = Paths.SharedStageFactor; sets++;
                sink = Paths.SharedVolleyFactor; sets++;
                sink = Tree.SharedVolleySeconds; sets++;

                sink = Ricochet.JumpMetres; sets++;
                sink = Ricochet.JumpShare; sets++;
                sink = Ricochet.Spread; sets++;
                sink = Draws.DipGrace; sets++;
                sink = Pvp.JumpsLandOnPlayers; sets++;
                sink = Targets.DummyNames; sets++;
                sink = GoldHold.Check(); sets++; // G4: every ring's look at every hold stage with the settings just loaded (its own entries only)
                sink = SeekLook.Check(); sets++; // H2: the lock cage's poses and the homing looks' numbers (its own entries only; nothing on the server)
                sink = JoinGate.Values; sets++; // the join gate: its own [Join] entries only (a leaf)
                sink = Skyfall.Values; sets++; // Skyfall: its own [Skyfall] entries only (a leaf, cached per load)
                sink = Skyfall.Check(); sets++; // Skyfall: every SkyfallValues field has its entry; the pattern and its end with the settings just loaded
                sink = SkyfallSigil.Check() + SkyfallHold.Check() + SkyfallSky.Check() + SkyfallLook.Check() + SkyfallNail.Check(); sets++; // Skyfall looks (clients; 0 on the server): spins, unfold, hold rings, the dial's numbers, the storm's fall and scorch curves, the World-Nail's slide, bands and roll
                FactNumbers numbers = Paths.Numbers(); sets++;

                foreach (KeyValuePair<MainPath, Orb[]> c in Cases()) {
                    Build b = Build.From(c.Value).With(c.Key);
                    sink = Paths.StagesFor(b); sink = Paths.VolleyTimesFor(b); sink = Paths.HolySecondsFor(b);
                    sink = Shatter.PelletsFor(b); sink = Shatter.HalfFor(b); sink = Targets.Wanted(b);
                    sink = VolleyHold.Stage(b, 30f, Paths.VolleyTimesFor(b), Paths.Values.VolleyStageSeconds); // G3: the hold stage's numbers
                    sink = Seek.Applies(b); sink = LockRule.Dwell(30f, b.Quick, Seek.Values); // H1
                    sets += 9;
                    if (ApplicationManager.IsHeadless) continue; // G2: the tablet is a client's; the server reads every value set above
                    var owned = new List<Orb>(c.Value);
                    texts += Count(TabletText.ForBuild(owned, c.Key, numbers, ""));
                    texts += Count(TabletText.ForOther("Self-check", true, b, numbers, ""));
                    foreach (Orb o in new[] { Orb.Shatter, Orb.Ricochet, Orb.AutoDock, Orb.SeekingVolley })
                        texts += Count(TabletText.ForOrb(o, owned.Contains(o), "", owned, c.Key, numbers, "", ""));
                }
                GC.KeepAlive(sink);
                if (when == "start-up" || Logs.Summary) MelonLogger.Msg($"[Archery] Self-check: {sets} value sets and {texts} tablet texts evaluated ({when})");
            }
            catch (Exception e) {
                // a StackOverflowException may end the process instead of landing here; the line above is then missing from the log
                MelonLogger.Error($"[Archery] Self-check FAILED ({when}) after {sets} value sets and {texts} tablet texts: {e}");
            }
        }

        static int Count(List<string> lines) => lines != null && lines.Count > 0 ? 1 : throw new InvalidOperationException("a tablet text came out empty");

        /// <summary>No main path, each main path, each with and without the new orbs (Auto Dock, Ricochet, Shatter).</summary>
        static IEnumerable<KeyValuePair<MainPath, Orb[]>> Cases() {
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.None, new Orb[0]);
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.None, new[] { Orb.Quick1, Orb.Volley, Orb.Charge1, Orb.Shatter, Orb.Ricochet, Orb.AutoDock });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Quick, new[] { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4 });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Quick, new[] { Orb.Quick1, Orb.Quick2, Orb.Quick3, Orb.Quickdraw4, Orb.AutoDock, Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Shatter });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Multi, new[] { Orb.Volley, Orb.FullVolley, Orb.Burst });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Multi, new[] { Orb.Volley, Orb.FullVolley, Orb.Burst, Orb.Ricochet, Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Shatter });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Multi, new[] { Orb.Volley, Orb.FullVolley, Orb.Burst, Orb.SeekingVolley, Orb.Ricochet, Orb.Quick1, Orb.Quick2 }); // H1
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Charge, new[] { Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.HolyArrow });
            yield return new KeyValuePair<MainPath, Orb[]>(MainPath.Charge, new[] { Orb.Charge1, Orb.Charge2, Orb.Charge3, Orb.Shatter, Orb.Volley, Orb.FullVolley, Orb.Burst, Orb.Ricochet });
        }
    }
}
