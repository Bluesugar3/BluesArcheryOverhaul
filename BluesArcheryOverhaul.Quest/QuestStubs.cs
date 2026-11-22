using System.Linq;

// Quest only, and only while the port is in phases (docs/superpowers/plans/2026-11-22-quest-port.md): stand-ins for members of
// files not yet in the Quest compile, each with the value the PC member has when its feature is absent. A stand-in is deleted in
// the task that adds its file; one left here after that is a duplicate-type compile error, so none can outlive its file.
namespace BluesArcheryOverhaul {
    // Each stand-in returns what the PC member returns when its own file has not set up its settings (the holders' own fallbacks),
    // and the looks do nothing: the server's settings for these modules reach a Quest game only once their files are in.

    // task 3 adds Volley.cs, Weight.cs, Quickdraw4.cs, Draws.cs, Hits.cs
    internal static class Volley {
        internal static VolleyTimes Times => VolleyTimes.Default;
        internal static float InnerShare => VolleyMath.InnerShare;
        internal static float OuterShare => VolleyMath.OuterShare;
        internal static BowString? BowOf(NetworkEntity entity) => null;
        internal static void ServerVolleyChanged(BowString bow, bool charged, Player? owner) { }
        internal static void ClientVolleyStatus(BowString bow, ToolBasedSkillStatus status, string owner, bool local) { }
        internal static void ClientChargeStatus(BowString bow, ToolBasedSkillStatus status, string owner, bool local) { }
    }
    internal static class Weight {
        internal static WeightValues Values => WeightValues.Default;
    }
    internal static class Quickdraw4 {
        internal static float[] PierceShares => (float[])LinePierce.DefaultShares.Clone();
    }
    internal static class Draws {
        internal static bool InputActive(BowDrawSkill? skill, BowString bow) => false;
    }
    internal static class Hits {
        internal static ImpactTool[] Tools(ImpactTool tool) => tool.GetComponentsInChildren<ImpactTool>(true).ToArray();
    }
    internal static class QuiverGather {
        internal static bool RevampedSkillsLoaded => false;   // no RevampedSkills on Quest
    }

    // task 4 adds Seek.cs, Shatter.cs, Ricochet.cs, Shrine.cs
    internal static class Seek {
        internal static HomingValues Values => HomingValues.Default;
    }
    internal static class Shatter {
        internal static BlastValues Blast => BlastValues.Default;
        internal static float MetresSetting => BlastValues.Default.Metres;
    }
    internal static class Ricochet {
        internal static int Bounces => JumpRule.Bounces(2);
    }
    internal static class Shrine {
        internal static void LogTree(ProfessionSkillTree tree) { }
    }

    // task 5 adds ChargeFx.cs, SnapWeight.cs
    internal static class ChargeFx {
        internal static bool FillShownFor(NetworkEntity entity) => false;
        internal static void GlowHidden(NetworkEntity entity) { }
        internal static void NoteArcher(BowString bow, string owner, bool local) { }
        internal static bool OffPath(NetworkEntity? entity, MainPath line, out MainPath main, out string archer) { main = MainPath.None; archer = ""; return false; }
        internal static void Status(BowString bow, ToolBasedSkillStatus status, string owner, bool local) { }
    }
    internal static class SnapWeight {
        internal static void Status(BowString bow, ToolBasedSkillStatus status, string owner, bool local, uint arrowId) { }
    }

    // task 6 adds ShrineView.cs
    internal static class ShrineView {
        internal static bool Open => false;
        internal static SkillOrb? Held => null;
    }
}
