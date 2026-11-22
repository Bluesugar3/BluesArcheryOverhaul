// Quest only, and only while the port is in phases (docs/superpowers/plans/2026-11-22-quest-port.md): stand-ins for members of
// files not yet in the Quest compile, each with the value the PC member has when its feature is absent. A stand-in is deleted in
// the task that adds its file; one left here after that is a duplicate-type compile error, so none can outlive its file.
namespace BluesArcheryOverhaul {
    /// <summary>Tree.cs joins in task 2. Until then this game run builds no tree, so no switch can have changed since.</summary>
    internal static class Tree {
        internal static bool SwitchesChangedSinceBuild => false;
    }
}
