"""R2: the play-test range's plan at the archery shrine, drawn from the range's own planner.

Reads the game's terrain heightmap from the private exported project (numbers only; nothing is copied out), writes the planner's
input to a temp folder, runs the planner (`dotnet run --project QuickshotRing.Tests -c Release -- --plan in out [lanes]`, the same
RangeBuild.cs code the server runs, on the heightmap instead of the live world) and draws docs/range-r2-plan.png.

    python -I tools/range_r2_plan.py [--lanes "<[TestRange] Lanes text>"] [--out docs/range-r2-plan.png] [--no-run]

The live build reads the real colliders (trees, the shrine, saved objects, other players) that the heightmap does not have, so the
game's plan can differ in detail: the server log's `TEST RANGE:` lines are the truth.
"""
import argparse, math, os, subprocess, sys, tempfile
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TERRAIN = r"C:\ATTUnity\ExportedProject\Assets\TerrainData\MasterTerrainData.asset"
ORIGIN = [-834.32, 214.09, 387.00, -21.0]        # the first real build (server log 2026-11-18 11:54): where Blue_XD stood and faced
TREE = (-833.4, 381.2)                            # the archery tree (from the tablet cradle (-834.61, 381.56) and tree-layout.md)
TRYOUT = (-833.4, 375.5)                          # the try-out area, 4.5-7 m behind the tree
REGION = (-905, 352, 131, 101)                    # x0, z0, nx, nz (1 m)
# static colliders above the shelf (chunk 17-19 x 37-39 collider prefabs, measured): name, min xyz, max xyz
ROCKS = [
    ("Boulder", (-826.84, 212.39, 371.25), (-824.02, 214.39, 374.07)),
    ("Shrine stones", (-832.05, 213.48, 373.64), (-828.59, 220.17, 376.27)),
    ("Boulder", (-834.17, 212.14, 374.28), (-831.44, 214.25, 376.99)),
    ("Boulder", (-829.87, 211.99, 374.51), (-827.02, 214.42, 377.61)),
    ("Boulder", (-836.21, 212.19, 375.40), (-833.59, 214.19, 378.02)),
    ("Boulder", (-826.46, 212.32, 378.34), (-824.46, 214.32, 380.34)),
    ("Boulder", (-841.86, 211.37, 380.89), (-838.86, 214.37, 383.89)),
    ("Shrine bench", (-834.87, 213.77, 381.30), (-832.09, 215.06, 383.11)),
    ("Boulder", (-830.39, 211.39, 385.26), (-827.39, 214.39, 388.26)),
    ("Boulder", (-892.25, 212.92, 387.74), (-889.25, 215.92, 390.74)),
    ("Boulder", (-870.64, 211.53, 392.14), (-867.64, 214.53, 395.14)),
    ("Boulder", (-877.45, 211.39, 402.16), (-874.45, 214.39, 405.16)),
]
# ground the server itself measured at the first build (server log, 11:54): name, x, y, z
LOGGED = [("shelf", -832.82, 214.09, 387.57), ("shelf", -835.81, 214.09, 386.43), ("dummy", -837.19, 213.72, 394.47), ("dummy", -839.34, 214.09, 400.07),
          ("Turabada", -857.12, 214.35, 394.74), ("Turabada", -861.49, 214.67, 393.77), ("Gotera (on a rock)", -849.74, 219.03, 413.21)]
LANE_COLOURS = {"Dummies": "#8b5a2b", "Pack": "#c0392b", "Goteras": "#2e86c1", "Homing": "#27ae60", "Long": "#16a085", "Runners": "#d35400"}


def heights():
    b = open(TERRAIN, "rb").read()
    return np.frombuffer(b, dtype="<i2", count=4097 * 4097, offset=9720).reshape(4097, 4097).astype(np.float32) * (1000.0 / 32766.0)


def write_input(path, H):
    x0, z0, nx, nz = REGION
    with open(path, "w") as f:
        f.write("origin %f %f %f %f\n" % tuple(ORIGIN))
        f.write("grid %d %d %d %d\n" % (x0, z0, nx, nz))
        for j in range(nz):
            f.write(" ".join("%.3f" % H[z0 + j + 2048, x0 + i + 2048] for i in range(nx)) + "\n")
        for name, mn, mx in ROCKS:
            f.write("rock %f %f %f %f %f %f %s (layer 13)\n" % (*mn, *mx, name))
        f.write("keep %f %f 3.5 the archery tree\n" % TREE)
        f.write("keep %f %f 8 the try-out area\n" % TRYOUT)


def run(inp, out, lanes, fixed=False):
    cmd = ["dotnet", "run", "--project", os.path.join(ROOT, "QuickshotRing.Tests"), "-c", "Release", "--", "--plan", inp, out, lanes or "-"]
    if fixed: cmd.append("fixed")
    subprocess.run(cmd, check=True)


def read(out):
    plan = {"rays": {}, "faces": [], "pieces": [], "skips": [], "notes": [], "lanes": []}
    for line in open(out, encoding="utf-8"):
        line = line.rstrip("\n")
        k, _, rest = line.partition(" ")
        if k == "yaw":
            p = rest.split(" ")
            plan.update(yaw=float(p[0]), best=int(p[2]), runner=int(p[4]), construction=int(p[6]), caphit=int(p[8]))
        elif k == "ray":
            i, w = rest.split(" "); plan["rays"][int(i)] = float(w)
        elif k == "face":
            head, _, desc = rest.partition(" | "); p = head.split(" ")
            plan["faces"].append(dict(yaw=float(p[1]), usable=float(p[2]), walk=float(p[3]), ground=int(p[4]), pads=int(p[5]), stacks=int(p[6]), blocked=int(p[7]), desc=desc))
        elif k == "piece":
            head, label, kind, note = (rest.split(" | ") + ["", "", ""])[:4]; p = head.split(" ")
            plan["pieces"].append(dict(role=p[0], lane=p[1], x=float(p[2]), y=float(p[3]), z=float(p[4]), yaw=float(p[5]), host=int(p[6]), last=p[7] == "1", free=p[8] == "1",
                                       cx=float(p[9]), cz=float(p[10]), hx=float(p[11]), hz=float(p[12]), byaw=float(p[13]), miny=float(p[14]), maxy=float(p[15]), label=label, kind=kind, note=note))
        elif k == "skip": plan["skips"].append(rest)
        elif k == "note": plan["notes"].append(rest)
        elif k == "lane": plan["lanes"].append(rest)
    return plan


def corners(p):
    a = math.radians(p["byaw"]); c, s = math.cos(a), math.sin(a)
    pts = []
    for lx, lz in ((-p["hx"], -p["hz"]), (p["hx"], -p["hz"]), (p["hx"], p["hz"]), (-p["hx"], p["hz"])):
        pts.append((p["cx"] + lx * c + lz * s, p["cz"] - lx * s + lz * c))
    return pts


def draw(plan, H, png):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Polygon, Circle
    from matplotlib.colors import LightSource
    x0, z0, nx, nz = REGION
    sub = H[z0 + 2048:z0 + nz + 2048, x0 + 2048:x0 + nx + 2048]
    gz, gx = np.gradient(sub)
    slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    ox, oy, oz, pyaw = ORIGIN
    fig = plt.figure(figsize=(21, 12.5))
    ax = fig.add_axes([0.035, 0.05, 0.64, 0.87])
    ext = [x0 - 0.5, x0 + nx - 0.5, z0 - 0.5, z0 + nz - 0.5]
    shade = LightSource(azdeg=315, altdeg=40).shade(sub, cmap=plt.get_cmap("gist_earth"), vert_exag=2, blend_mode="soft", vmin=180, vmax=300)
    ax.imshow(shade, origin="lower", extent=ext, alpha=0.85)
    steep = np.ma.masked_where(slope <= 35, slope)
    ax.imshow(steep, origin="lower", extent=ext, cmap="Greys", alpha=0.35, vmin=0, vmax=90)
    X, Z = np.meshgrid(np.arange(x0, x0 + nx), np.arange(z0, z0 + nz))
    cs = ax.contour(X, Z, sub, levels=np.arange(186, 330, 2), colors="k", linewidths=0.35, alpha=0.6)
    ax.clabel(cs, levels=np.arange(190, 330, 10), fmt="%d m", fontsize=7)
    # the fan: walkable metres per bearing
    for i, w in plan["rays"].items():
        a = math.radians(i * 15)
        ax.plot([ox, ox + w * math.sin(a)], [oz, oz + w * math.cos(a)], color="#f1c40f", lw=1.0, alpha=0.75, zorder=3)
        if w > 0:
            ax.text(ox + (w + 2) * math.sin(a), oz + (w + 2) * math.cos(a), "%d" % w, color="#7d6608", fontsize=6.5, ha="center", va="center", zorder=3)
    # keep-clear zone
    for (cx, cz), r, what in ((TREE, 3.5, "tree"), (TRYOUT, 8, "try-out")):
        ax.add_patch(Circle((cx, cz), r, facecolor="#9b59b6", alpha=0.18, edgecolor="#6c3483", hatch="///", lw=1.2, zorder=4))
        ax.text(cx, cz, "keep clear:\n" + what, color="#4a235a", fontsize=7, ha="center", va="center", zorder=5)
    for name, mn, mx in ROCKS:
        ax.add_patch(Circle(((mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2), (mx[0] - mn[0]) / 2, facecolor="#7f8c8d", edgecolor="#2c3e50", alpha=0.6, zorder=4))
    # lanes: shooter to each spot
    for p in plan["pieces"]:
        if p["role"] == "Creature":
            ax.plot([ox, p["x"]], [oz, p["z"]], color=LANE_COLOURS.get(p["lane"], "k"), lw=0.6, alpha=0.45, ls="--", zorder=5)
    style = {"Shelf": ("#a0522d", "#4e2a0e"), "Pad": ("#deb887", "#8b5a2b"), "Tall": ("#cd853f", "#5d3a1a"), "Frame": ("#cd853f", "#5d3a1a"),
             "Wall": ("#922b21", "#3b0f0b"), "Lamp": ("#f7dc6f", "#b7950b")}
    for p in plan["pieces"]:
        if p["role"] in style:
            fc, ec = style[p["role"]]
            ax.add_patch(Polygon(corners(p), closed=True, facecolor=fc, edgecolor=ec, lw=1.2 if p["role"] != "Lamp" else 0.8, zorder=6 if p["role"] != "Lamp" else 8))
            if p["role"] == "Lamp":
                ax.add_patch(Circle((p["cx"], p["cz"]), 0.9, facecolor="#f9e79f", alpha=0.35, edgecolor="none", zorder=7))
            if p["role"] in ("Pad", "Tall"):
                lift = "stack, deck %+.1f m" % (p["maxy"] - oy) if p["role"] == "Tall" else "pad, top %.1f m" % (p["y"] + 1.006)
                ax.text(p["cx"] + 1.3, p["cz"] - 1.3, lift, fontsize=6.5, color="#5d3a1a", zorder=9)
    for p in plan["pieces"]:
        if p["role"] != "Creature": continue
        col = LANE_COLOURS.get(p["lane"], "k")
        marker = "s" if "dummy" in p["kind"] else ("^" if p["kind"].startswith("Gotera") else "o")
        ax.plot(p["x"], p["z"], marker=marker, ms=9, color=col, mec="k", mew=0.8, zorder=10)
        if p["free"]:
            ax.add_patch(Circle((p["x"], p["z"]), 8, facecolor="none", edgecolor=col, ls=":", lw=0.8, zorder=5))
        moved = " (moved)" if "moved" in p["note"] else ""
        ax.text(p["x"] + 0.9, p["z"] + 0.7, "%s %s\n%.1f m%s" % (p["lane"], p["label"].split(" ")[1], p["y"], moved), fontsize=6.3, color="#1b2631", zorder=11)
    for name, lx, ly, lz in LOGGED:
        ax.plot(lx, lz, marker="+", ms=8, color="#000000", zorder=12)
        ax.text(lx - 0.6, lz - 1.6, "log %.2f" % ly, fontsize=6, color="#000000", ha="right", zorder=12)
    # shooter and facings
    ax.plot(ox, oz, marker="*", ms=20, color="#f4d03f", mec="k", zorder=12)
    def arrow(yaw, col, ls, lab):
        a = math.radians(yaw)
        ax.annotate("", xy=(ox + 9 * math.sin(a), oz + 9 * math.cos(a)), xytext=(ox, oz), arrowprops=dict(arrowstyle="-|>", color=col, lw=2.2, ls=ls), zorder=13)
        ax.text(ox + 10.5 * math.sin(a), oz + 10.5 * math.cos(a), lab, color=col, fontsize=8, fontweight="bold", ha="center", zorder=13)
    arrow(pyaw, "#7f8c8d", "--", "you faced")
    if plan["runner"] >= 0:
        arrow(plan["faces"][plan["runner"]]["yaw"], "#34495e", ":", "runner-up")
    arrow(plan["yaw"], "#000000", "-", "AutoFace")
    ax.set_xlim(-899, -819); ax.set_ylim(369, 427); ax.set_aspect("equal")   # the shelf and the range on it
    ax.set_xlabel("world x (m), west <-> east"); ax.set_ylabel("world z (m), south <-> north")
    ax.grid(alpha=0.15)
    cnt = {}
    for p in plan["pieces"]: cnt[p["role"]] = cnt.get(p["role"], 0) + 1
    ax.set_title("R2 range at the archery shrine (Archers Refuge), planned by the range's own rules on the game's terrain\n"
                 "facing %.0f degrees (AutoFace); %d construction pieces: %d walls, %d lamps, %d pads, %d stack pieces; %d creatures/dummies; 2 shelves"
                 % (((plan["yaw"] + 180) % 360) - 180, plan["construction"], cnt.get("Wall", 0), cnt.get("Lamp", 0), cnt.get("Pad", 0), cnt.get("Tall", 0) + cnt.get("Frame", 0), cnt.get("Creature", 0)), fontsize=11)
    # right: AutoFace scores
    bx = fig.add_axes([0.71, 0.58, 0.26, 0.34], projection="polar")
    bx.set_theta_zero_location("N"); bx.set_theta_direction(-1)
    faces = plan["faces"]
    if faces:
        th = [math.radians(f["yaw"]) for f in faces]; sc = [max(0, f["usable"]) for f in faces]
        colours = ["#000000" if i == plan["best"] else ("#34495e" if i == plan["runner"] else "#95a5a6") for i in range(len(faces))]
        bx.bar(th, sc, width=math.radians(12), color=colours, alpha=0.85)
        ray_th = [math.radians(i * 15) for i in sorted(plan["rays"])]; ray_w = [plan["rays"][i] for i in sorted(plan["rays"])]
        bx.plot(ray_th + ray_th[:1], ray_w + ray_w[:1], color="#f1c40f", lw=1.5)
    bx.set_title("AutoFace score per facing (bars; black = chosen, dark = runner-up)\nyellow: walkable metres per direction (the way out)", fontsize=9)
    tx = fig.add_axes([0.69, 0.03, 0.31, 0.5]); tx.axis("off")
    lines = []
    if faces:
        b, r = faces[plan["best"]], faces[plan["runner"]] if plan["runner"] >= 0 else None
        lines.append("Chosen: " + b["desc"])
        if r: lines.append("Runner-up: " + r["desc"])
    lines += [""] + ["Lane " + l for l in plan["lanes"]]
    if plan["notes"]: lines += [""] + plan["notes"]
    if plan["skips"]: lines += ["", "Left out / skipped:"] + plan["skips"][:12]
    lines += ["", "Legend: brown squares pads (top level with the high side's ground), dark red bars backstop walls",
              "(Temp Dynamic Wall), yellow lamps (Outdoor Light Fixture Dim), grey discs boulders, purple keep-clear,",
              "grey wash: slopes over 35 degrees, labels: spot and the height a creature stands at (m), dotted rings: a moving",
              "lane's 8 m leash, '+ log': ground the server measured at the first build (the exported terrain matches it to",
              "1-4 cm). The live build also sees trees, the shrine, saved objects and players, which this plan does not."]
    import textwrap
    wrapped = []
    for l in lines: wrapped += textwrap.wrap(l, 88) or [""]
    tx.text(0, 1, "\n".join(wrapped), va="top", fontsize=7.2, family="monospace")
    fig.savefig(png, dpi=110)
    print("wrote", png)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--lanes", default=None)
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "range-r2-plan.png"))
    ap.add_argument("--keep-files", action="store_true")
    ap.add_argument("--face", type=float, default=None, help="build facing this way (AutoFace off), to compare")
    ap.add_argument("--yaw", type=float, default=None, help="the way you face when you press Build (AutoFace on); default the first build's -21")
    a = ap.parse_args()
    H = heights()
    d = tempfile.mkdtemp(prefix="range_r2_")
    inp, out = os.path.join(d, "plan_in.txt"), os.path.join(d, "plan_out.txt")
    if a.face is not None: ORIGIN[3] = a.face
    elif a.yaw is not None: ORIGIN[3] = a.yaw
    write_input(inp, H)
    run(inp, out, a.lanes, a.face is not None)
    plan = read(out)
    for l in plan["lanes"]: print("lane", l)
    for s in plan["skips"]: print("skip", s)
    for n in plan["notes"]: print("note", n)
    if plan["best"] >= 0:
        print("chosen", plan["faces"][plan["best"]]["desc"])
        if plan["runner"] >= 0: print("runner", plan["faces"][plan["runner"]]["desc"])
    draw(plan, H, a.out)
    if a.keep_files: print("files in", d)
