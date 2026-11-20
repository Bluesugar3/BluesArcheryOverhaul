# Skyfall: where to pick up (updated 2026-11-19)

## Done
- Brainstorm, concept art and checks:
  - the 48 ideas, the game-feel kit and its checks, the art toolkit: `QuickshotRing\Art\admin_skill\`;
  - the pictures: `QuickshotRing\Art\drafts\admin_skill\`.
- **Design, approved by the owner on 2026-11-19:** `docs\superpowers\specs\2026-11-19-skyfall-admin-skill-design.md`. The approval includes the one invisible-message exception to the no-new-traffic rule.
- Fact checks behind the design: the other files in this folder.
- Code maps for the plan: 2 of 5 finished, in `plan-map\`:
  - `tests-and-rules.md`
  - `tools-and-docs.md`

## Not done
- **The remaining code maps:** claim-and-seam, server-systems, client-looks. They were stopped at the pause, with nothing half-written.
  - In the same session, re-run the map workflow with its `resumeFromRunId`.
  - In a new session, run the five-reader map again: the two finished readers can be skipped. The script was `skyfall-plan-map-wf_9ac3507e-07f.js` under this session's `workflows\scripts` folder.
- **The implementation plan,** with the superpowers:writing-plans skill. Save it to `docs\superpowers\plans\2026-11-19-skyfall.md`, self-review it, then ask the owner to choose an execution method. Recommend subagent-driven: Opus builds, and reviews run on Opus max until Fable's limit lifts on 2026-11-20 1pm PT.
- **No code is written and nothing is installed.**

## Before any Skyfall build is installed
- The owner said "Restore my pack first". The game's `Mods` folder held another 57-mod pack (RevampedSkills, no QuickshotRing), and `Modpacks\My mods` was gone.
- Do not run the auto-installer until the owner has switched back.
- The plan must also make `tools/install-when-closed.sh` refuse when a target folder is missing, never leaving a half install. That requirement is in the design.

## Update, later on 2026-11-19
- **All 5 code maps are done** (`plan-map/`).
- **The plan is written and reviewed:** `docs/superpowers/plans/2026-11-19-skyfall.md`, 14 tasks.
  - Three checkers reviewed it and found 46 issues; an editor fixed them.
  - The controller's rulings are in the plan.
- **Next:** the owner reviews the plan and picks an execution method (subagent-driven recommended). Then build Task 1 first.
- **Still true:** no code is written and nothing is installed, and Task 14 waits for the owner to restore their pack.
