using System;
using System.Collections.Generic;
using System.Linq;
using ATT.Saving;
using Alta.Chunks;
using Alta.Crafting;
using Alta.Inventory;
using Alta.Networking;
using Alta.Utilities;
using MelonLoader;
using UnityEngine;

namespace QuickshotRing {
    // The range's shelves (TestRange.cs has the overview). Every item is spawned unchunked with SaveMode.Ignore and
    // Pickup.BaseSaveMode = Ignore and docked at once (one entity stands for the whole stack, as SpawnHelper.SpawnDocked does, but
    // a refused dock destroys the item instead of dropping it into the world). A crafted arrow is assembled here: a wooden shaft
    // ([TestRange] ShaftPrefab) and a head, the head forged and set to its metal through the game's own spawn arguments
    // (ISpawnArguments.CheckArguments {"forged", metal}, as the console's spawn command does), then JointSlot.TryConnect. This is the
    // one part never run before: if a kind cannot be assembled or stocked, that slot stays EMPTY, one line names the kind and why,
    // and it is not retried until Refill or a rebuild; every other slot, the creatures and every button work regardless.
    //
    // H4 (range review I-2, I-3): range stock. Every item the range spawns (MakeItem, MakeCrafted: NeverSaved) is remembered in
    // `stock`, and so is the copy a stacked range dock makes of one taken off it: the game's IDockable.Undock runs CreateNewImage (the
    // stack's next item, spawned from the dock's serialized data and docked, PickupDock.DockedPickup) BEFORE it invokes ItemUndocked,
    // so the handler (Taken) sees the taken item and the dock's new DockedPickup; when the taken one is stock, the new one is too.
    // Every range dock (each dock of a range shelf, used or not, and a range quiver's inner dock) carries the game's own DockFilter
    // (RangeOnly): it takes range stock only, so a player's own item never sits on a range dock. Only stock is made never-saved,
    // tracked as a loose arrow or destroyed when the range is removed; anything else found on a dock is put down beside it (UndockAll,
    // its own saving). The game clears DockFilter while it docks a stack's new copy (CreateNewImage) and checks no dock for a
    // quantity-0 dock (load, image), so the filter never refuses the shelf's own next item. Server only, like everything here.
    internal static partial class TestRange {
        static MelonPreferences_Entry<float> refillEvery = null!;

        static void InitShelf(MelonPreferences_Category cat) {
            arrowShelf = cat.CreateEntry("ArrowShelf", RangeDefaults.ArrowShelf, description: "Server only. The arrow shelf's slots in order, ';' between slots: a prefab name and a count ('Arrow x90'), or an arrowhead ('Spike Fancy', 'Spike Tall', 'Spike Short', 'Small Bone Spike', 'Explosive Spike', 'Healing Pod', 'Crystal Shard Blue') with an optional '/Metal' (Copper, Iron, Silver, Mythril, Evinon Steel Alloy, ...) assembled on a wooden shaft. Counts 1 to 90. A slot whose kind cannot be made stays empty and is named in the log.");
            gearShelf = cat.CreateEntry("GearShelf", RangeDefaults.GearShelf, description: "Server only. The bow and quiver shelf's slots in order: 'Bow', 'Metal Bow', 'Quiver+Arrow x90' (a quiver filled with 90 of what follows the '+'). Empty = no second shelf.");
            refillEvery = cat.CreateEntry("RefillSeconds", 2f, description: "Server only, live. Every this many seconds each shelf slot below its count is topped up (never a slot holding something else).");
            shaftPrefab = cat.CreateEntry("ShaftPrefab", 29772, description: "Server only. The prefab hash of the wooden arrow shaft crafted arrows are built on ('Arrow Shaft Wooden').");
            refillSeconds = refillEvery;
        }

        sealed class Slot {
            public PickupDock Dock = null!;
            public ShelfEntry Want = null!;
            public string Shelf = "";
            public int Index;
            public string Holds = "";    // what it was last stocked with ("" = nothing yet)
            public string Failed = "";   // why its kind could not be made or stocked: not retried until Refill or a rebuild
            public bool WarnedWrong;
            public PickupDockInteractionHandler? OnUndock;
            public string Name => $"{Shelf} slot {Index + 1} '{Want}'";
        }

        // docks inside items the range made (a quiver's), with the handler that keeps what leaves them out of the save. The handler and
        // the range-only filter stay on a quiver's inner dock for as long as that quiver exists (it is range stock, carried or not);
        // this list only serves Remove (a quiver still on a range shelf is emptied there).
        static readonly List<KeyValuePair<PickupDock, PickupDockInteractionHandler>> innerDocks = new List<KeyValuePair<PickupDock, PickupDockInteractionHandler>>();
        static float nextRefill;

        // ---------------------------------------------------------------- range stock and the never-saved trap

        static readonly HashSet<NetworkEntity> stock = new HashSet<NetworkEntity>();
        static readonly Predicate<NetworkEntity> StockGone = e => e == null || e.IsBeingDestroyed;

        static bool IsStock(Pickup? p) => p != null && p.Entity != null && stock.Contains(p.Entity);

        /// <summary>Never in the world save: the entity now, and (for an item) again whenever a dock or a hand lets it go. The entity
        /// is range stock from now on.</summary>
        static void NeverSaved(NetworkEntity? e) {
            if (e == null) return;
            stock.Add(e);
            if (e.Prefab != null) e.Prefab.SaveMode = SaveMode.Ignore;
            Pickup p = e.CommonPickup;
            if (p != null) p.BaseSaveMode = SaveMode.Ignore;
        }

        static readonly int T_Taken = Timing.Section("event TestRange.Taken (range dock undock)");
        /// <summary>An item left a range dock (a hand took it, or the dock was emptied). Range stock only (RangeStock.OnTaken): the game set
        /// it back to its BaseSaveMode, so it is set to Ignore again here, an arrow is remembered as a loose range arrow, and the dock's next
        /// copy of the stack (CreateNewImage ran just before this) is range stock too. A player's own item is never touched.</summary>
        static void Taken(Pickup? p, PickupDock? dock) {
            using var _t = Timing.Begin(T_Taken);
            if (p == null || p.Entity == null || p.Entity.IsBeingDestroyed) return;
            Pickup? image = dock != null && dock.HasDockedPickup ? dock.DockedPickup : null;
            if (!RangeStock.OnTaken(IsStock(p), image != null && image.Entity != null && image != p, out bool imageIsStock)) return;
            NeverSaved(p.Entity);
            if (imageIsStock && image != null && image.Entity != null) stock.Add(image.Entity);
            if (p.GetComponent<Arrow>() != null && enabled.Value) Track(p.Entity);
        }

        static PickupDockInteractionHandler Handler() => (interactor, pickup, dock) => { try { Taken(pickup, dock); } catch { } };

        static readonly int T_RangeOnly = Timing.Section("event TestRange.RangeOnly (range dock filter)");
        static bool refusedLogged;
        /// <summary>The game's DockFilter on every range dock: range stock only (RangeStock.DockAccepts). Server only.</summary>
        static void RangeOnly(Interactor interactor, Pickup pickup, ref bool result) {
            using var _t = Timing.Begin(T_RangeOnly);
            if (!result) return;
            try {
                if (RangeStock.DockAccepts(IsStock(pickup))) return;
                result = false;
                if (!refusedLogged) { refusedLogged = true; if (Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: '{(pickup != null ? pickup.name : "?")}' is not the range's, so a range shelf or quiver does not take it (said once)"); }
            }
            catch { }
        }
        static readonly DockFilter RangeOnlyFilter = RangeOnly;

        // ---------------------------------------------------------------- making items

        static readonly Dictionary<string, PhysicalMaterial?> materials = new Dictionary<string, PhysicalMaterial?>(StringComparer.OrdinalIgnoreCase);
        static readonly int T_Material = Timing.Section("asset TestRange.Material (FindObjectsOfTypeAll PhysicalMaterial)");
        static PhysicalMaterial? Material(string name) {
            if (materials.TryGetValue(name, out PhysicalMaterial? m)) return m;
            using var _t = Timing.Begin(T_Material, name);
            m = Resources.FindObjectsOfTypeAll<PhysicalMaterial>().FirstOrDefault(x => x != null && string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
            materials[name] = m;
            return m;
        }

        static void Destroy(NetworkEntity? e) {
            try { if (e != null && !e.IsBeingDestroyed) e.SceneDestroy(); } catch { }
        }

        /// <summary>One item of this entry, unchunked and never saved, or null with the reason.</summary>
        static NetworkEntity? MakeItem(ShelfEntry e, out string why) {
            why = "";
            if (e.Crafted) return MakeCrafted(e, out why);
            uint hash = Resolve(e.Name, RangeKinds.ItemHash(e.Name));
            if (hash == 0u || PrefabOf(hash, e.Name) == null) { why = $"no prefab named '{e.Name}' in this game"; return null; }
            NetworkEntity ent = SpawnHelper.SpawnUnchunked(hash, SpawnData.DontGenerate);
            if (ent == null) { why = $"the game spawned nothing for prefab {hash}"; return null; }
            NeverSaved(ent);
            if (ent.CommonPickup == null) { Destroy(ent); why = $"'{e.Name}' is not an item that can sit in a dock"; return null; }
            if (e.Inner != null) {
                // H4 (range I-5): a throw while filling destroys what this attempt spawned and fails the slot like any other reason
                string? inner;
                try { inner = FillContainer(ent, e.Inner); }
                catch (Exception ex) { inner = $"filling it threw {ex.GetType().Name}: {ex.Message}"; }
                if (inner != null) { Destroy(ent); why = $"its contents: {inner}"; return null; }
            }
            return ent;
        }

        static readonly int T_Crafted = Timing.Section("TestRange.MakeCrafted (shaft + head)");
        /// <summary>A crafted arrow: a wooden shaft with this head joined, in this metal. Null with the reason; nothing is left behind.</summary>
        static NetworkEntity? MakeCrafted(ShelfEntry e, out string why) {
            using var _t = Timing.Begin(T_Crafted, e.Name);
            why = "";
            uint headHash = RangeKinds.HeadHash(e.Name), shaftHash = (uint)Mathf.Max(0, shaftPrefab.Value);
            if (PrefabOf(shaftHash, "Arrow Shaft Wooden") == null) { why = $"no shaft prefab {shaftHash} ([TestRange] ShaftPrefab)"; return null; }
            if (headHash == 0u || PrefabOf(headHash, e.Name) == null) { why = $"no head prefab for '{e.Name}'"; return null; }
            NetworkEntity? shaft = null, head = null;
            try {
                shaft = SpawnHelper.SpawnUnchunked(shaftHash, SpawnData.DontGenerate);
                head = SpawnHelper.SpawnUnchunked(headHash, SpawnData.DontGenerate);
                NeverSaved(shaft); NeverSaved(head);
                if (shaft == null || head == null) { why = "the game spawned nothing for the shaft or the head"; }
                else {
                    string[] args = e.Material.Length > 0 ? new[] { "forged", e.Material } : new[] { "forged" };
                    foreach (ISpawnArguments a in head.GetComponentsInChildren<ISpawnArguments>(true)) a.CheckArguments(args);
                    PhysicalMaterialPart? mp = head.GetComponentInChildren<PhysicalMaterialPart>(true);
                    if (e.Material.Length > 0) {
                        string got = mp != null && mp.PhysicalMaterial != null ? mp.PhysicalMaterial.name : "none";
                        if (!string.Equals(got, e.Material, StringComparison.OrdinalIgnoreCase)) {
                            // the spawn argument's name lookup missed: the material asset by its own name
                            PhysicalMaterial? m = Material(e.Material);
                            if (mp != null && m != null) { mp.SetMaterial(m); got = mp.PhysicalMaterial != null ? mp.PhysicalMaterial.name : "none"; }
                            if (!string.Equals(got, e.Material, StringComparison.OrdinalIgnoreCase))
                                why = mp == null ? "the head has no material part" : $"the head would not take the metal '{e.Material}' (it is {got}; known metals are the game's material names)";
                        }
                    }
                    if (why.Length == 0) {
                        CraftingPart? part = head.GetComponentsInChildren<CraftingPart>(true).FirstOrDefault(cp => cp != null && cp.Insert != null);
                        if (part == null) why = "the head has no insert to join";
                        else {
                            JointSlot? slot = shaft.GetComponentsInChildren<JointSlot>(true).FirstOrDefault(js => js != null && !js.IsUsed && js.Supports(part.Insert.Type));
                            if (slot == null) why = $"the shaft has no free slot for this head's insert ('{(part.Insert.Type != null ? part.Insert.Type.name : "?")}')";
                            else if (!slot.TryConnect(part, false)) why = "the shaft's slot refused the head (JointSlot.TryConnect)";
                            else return shaft; // the head is now the shaft's child (SaveMode Child, chunked with it)
                        }
                    }
                }
            }
            catch (Exception ex) { why = $"assembling threw {ex.GetType().Name}: {ex.Message}"; }
            Destroy(head); Destroy(shaft);
            return null;
        }

        /// <summary>A container item (a quiver) filled with this entry; null when it worked, else why not.</summary>
        static string? FillContainer(NetworkEntity container, ShelfEntry inner) {
            PickupDock dock = container.GetComponentInChildren<PickupDock>(true);
            if (dock == null) return "it has no dock";
            NetworkEntity? item = MakeItem(inner, out string why);
            if (item == null) return why;
            PickupDock.AllowedResult r;
            try { r = item.CommonPickup.DockInto(null, dock, inner.Count, true); }
            catch (Exception e) { Destroy(item); return $"docking {inner} threw {e.GetType().Name}: {e.Message}"; } // H4 (range I-5)
            if (r != PickupDock.AllowedResult.Ok) { Destroy(item); return $"it refused {inner} ({r})"; }
            PickupDockInteractionHandler h = Handler();
            dock.ItemUndocked += h;
            dock.DockFilter += RangeOnlyFilter; // H4 (range I-2): a range quiver takes range stock only, wherever it is carried
            innerDocks.Add(new KeyValuePair<PickupDock, PickupDockInteractionHandler>(dock, h));
            return null;
        }

        // ---------------------------------------------------------------- shelves and slots

        static readonly HashSet<uint> shelfLogged = new HashSet<uint>();

        /// <summary>The arrow shelf (right) and the gear shelf (left) where the planner put them (R2: RangeBuild.cs, fitted to the ground
        /// and tested for overlap), stocked. A refusal when the arrow shelf cannot stand.</summary>
        static string? PlaceShelves(RangeState r, List<Placed> planned, List<string> log) {
            foreach (bool right in new[] { true, false }) {
                string label = right ? "arrow shelf" : "gear shelf";
                List<ShelfEntry> entries = ShelfEntry.ParseList(right ? arrowShelf.Value : gearShelf.Value, out List<string> errors);
                foreach (string e in errors) log.Add($"{label}: skipped {e}");
                if (entries.Count == 0) {
                    if (right) return "[TestRange] ArrowShelf names no item that can be stocked";
                    log.Add($"{label}: none ([TestRange] GearShelf is empty)");
                    continue;
                }
                Placed? at = planned.FirstOrDefault(p => p.Label.Contains(right ? "arrow" : "gear"));
                if (at == null) {
                    if (right) return "the arrow shelf has nowhere to stand one step to your right. Stand on open, level ground and press Build again";
                    log.Add($"{label}: skipped (no place for it: see 'left out' below)");
                    continue;
                }
                var pos = new Vector3(at.X, at.Y, at.Z);
                uint hash = (uint)Mathf.Max(0, (right ? arrowShelfPrefab : gearShelfPrefab).Value);
                NetworkPrefab? prefab = PrefabOf(hash, label);
                LocationChunk chunk = LocationChunkHelper.GetBestContaining(pos);
                if (prefab == null || chunk == null) {
                    string w = prefab == null ? $"no prefab {hash}" : "no chunk at that spot";
                    if (right) return $"the arrow shelf: {w}";
                    log.Add($"{label}: skipped ({w})");
                    continue;
                }
                NetworkEntity shelf = SpawnHelper.Spawn(prefab, SpawnData.DontGenerate, chunk, pos, Quaternion.Euler(0f, at.Yaw + shelfYawOffset.Value, 0f),
                    e => { if (e != null && e.Prefab != null) e.Prefab.SaveMode = SaveMode.Ignore; });
                if (shelf == null) { if (right) return "the arrow shelf: the game spawned nothing"; log.Add($"{label}: the spawn failed"); continue; }
                r.Furniture.Add(shelf);
                LogShelfOnce(shelf, hash, at.Y);
                PickupDock[] docks = shelf.GetComponentsInChildren<PickupDock>(true);
                log.Add($"{label} '{shelf.name}' at {Targets.P(pos)}{(at.Note.Length > 0 ? $" ({at.Note})" : "")}, {docks.Length} docks");
                foreach (PickupDock d in docks) if (d != null) d.DockFilter += RangeOnlyFilter; // H4 (range I-2): every dock, used or not
                for (int i = 0; i < entries.Count; i++) {
                    if (i >= docks.Length) { log.Add($"{label} slot {i + 1} '{entries[i]}': skipped (the shelf has only {docks.Length} docks)"); continue; }
                    var slot = new Slot { Dock = docks[i], Want = entries[i], Shelf = label, Index = i };
                    slot.OnUndock = Handler();
                    slot.Dock.ItemUndocked += slot.OnUndock;
                    r.Slots.Add(slot);
                    if (slot.Dock.QuantityStored > 0) { slot.Failed = "the dock already held something"; continue; }
                    string? fail;
                    try { fail = Stock(slot, entries[i].Count); }
                    catch (Exception e) { fail = $"stocking threw {e.GetType().Name}: {e.Message}"; } // one slot, not the whole build
                    if (fail != null) slot.Failed = fail;
                }
            }
            return null;
        }

        /// <summary>Once per shelf prefab: its shape in its own space and the ground it stands on. R2: measured from its colliders' own
        /// definitions (TestRange.Measure), not Collider.bounds: the first build's "footprint 330 x 202 m" came from bounds read in the
        /// spawn's own frame, before the physics transforms synced (and from inactive or disabled colliders), so they spanned the
        /// entity's place before the spawn moved it.</summary>
        static void LogShelfOnce(NetworkEntity shelf, uint hash, float groundY) {
            if (!shelfLogged.Add(hash) || !Logs.Summary) return;
            try {
                PieceShape s = Measure(shelf.gameObject, out string from);
                MelonLogger.Msg($"[Archery] TEST RANGE: shelf '{shelf.name}' ({hash}) on the ground at {groundY:0.00}: {s} ({from})");
            }
            catch (Exception e) { MelonLogger.Msg($"[Archery] TEST RANGE: shelf shape unreadable: {e.Message}"); }
        }

        static readonly int T_Stock = Timing.Section("TestRange.Stock (one slot)");
        /// <summary>Adds <paramref name="count"/> of the slot's kind to its dock; null when it worked, else why (nothing is left behind).</summary>
        static string? Stock(Slot s, int count) {
            using var _t = Timing.Begin(T_Stock, s.Want.Name);
            NetworkEntity? item = MakeItem(s.Want, out string why);
            if (item == null) return why;
            int before = s.Dock.QuantityStored;
            PickupDock.AllowedResult r;
            try { r = item.CommonPickup.DockInto(null, s.Dock, count, true); }
            catch (Exception e) { Destroy(item); return $"docking threw {e.GetType().Name}: {e.Message}"; }
            if (r != PickupDock.AllowedResult.Ok) { Destroy(item); return $"the shelf refused it ({r})"; }
            int now = s.Dock.QuantityStored;
            s.Holds = Describe(s.Dock);
            if (now <= before) return $"the dock took the item but holds {now} (was {before}; asked for {count} more)"; // never refilled again every tick
            if (now - before != count && Logs.Summary) MelonLogger.Msg($"[Archery] TEST RANGE: {s.Name}: asked for {count} more, the dock went from {before} to {now}");
            return null;
        }

        /// <summary>What a dock holds, for the log: "30 x 'Arrow Shaft Wooden' + 'Spike Fancy' (Mythril)".</summary>
        static string Describe(PickupDock d) {
            if (d == null || !d.HasDockedPickup) return "";
            Pickup p = d.DockedPickup;
            string text = $"{d.QuantityStored} x '{(p.Entity != null ? p.Entity.name : p.name)}'";
            HeadOf(p, out uint head, out string metal, out string headName);
            if (head != 0u) text += $" + '{headName}' ({metal})";
            return text;
        }

        /// <summary>The head joined to a crafted shaft (its prefab hash and material), 0 when there is none.</summary>
        static void HeadOf(Pickup p, out uint hash, out string material, out string name) {
            hash = 0u; material = ""; name = "";
            foreach (JointSlot js in p.GetComponentsInChildren<JointSlot>(true)) {
                CraftingPart part = js != null ? js.ConnectedPart : null!;
                if (part == null || part.Entity == null || part.Entity.Prefab == null) continue;
                hash = part.Entity.Prefab.Hash;
                name = part.Entity.Prefab.name;
                PhysicalMaterialPart mp = part.GetComponentInChildren<PhysicalMaterialPart>(true);
                material = mp != null && mp.PhysicalMaterial != null ? mp.PhysicalMaterial.name : "";
                return;
            }
        }

        /// <summary>The slot's dock holds its own kind (the same prefab; for a crafted arrow the same head in the same metal).</summary>
        static bool Matches(Slot s, Pickup docked) {
            if (docked == null || docked.Entity == null || docked.Entity.Prefab == null) return false;
            ShelfEntry w = s.Want;
            uint hash = docked.Entity.Prefab.Hash;
            if (!w.Crafted) return hash == Resolve(w.Name, RangeKinds.ItemHash(w.Name));
            if (hash != (uint)shaftPrefab.Value) return false;
            HeadOf(docked, out uint head, out string metal, out _);
            return head == RangeKinds.HeadHash(w.Name) && (w.Material.Length == 0 || string.Equals(metal, w.Material, StringComparison.OrdinalIgnoreCase));
        }

        static void LogSlots(RangeState r) {
            if (!Logs.Summary) return;
            foreach (Slot s in r.Slots)
                MelonLogger.Msg($"[Archery] TEST RANGE: {s.Name}: {(s.Failed.Length > 0 ? $"EMPTY ({s.Failed})" : s.Holds.Length > 0 ? $"holds {s.Holds}" : "empty")}");
        }

        static readonly int T_RefillPass = Timing.Section("TestRange.RefillPass");
        /// <summary>Tops every slot up (RefillRule); a slot whose kind failed is skipped unless <paramref name="retry"/>. Returns
        /// (slots topped up, slots still failing with why).</summary>
        static int RefillPass(bool retry, List<string> failed) {
            using var _t = Timing.Begin(T_RefillPass);
            if (range == null) return 0;
            int topped = 0;
            foreach (Slot s in range.Slots) {
                if (s.Dock == null) continue;
                if (s.Failed.Length > 0 && !retry) continue;
                bool had = s.Failed.Length > 0;
                s.Failed = "";
                // H4 (range I-5): a slot that throws is marked failed (one line) and not retried until Refill, like a crafted kind
                try {
                    int stored = s.Dock.HasDockedPickup ? s.Dock.QuantityStored : 0;
                    bool matches = stored > 0 && Matches(s, s.Dock.DockedPickup);
                    int room = stored > 0 ? Math.Max(0, s.Dock.MaxQuantityCurrentlyPossible - stored) : ShelfEntry.MaxCount;
                    switch (RefillRule.Need(stored, s.Want.Count, matches, room, out int add)) {
                        case RefillKind.Add:
                            string? why = Stock(s, add);
                            if (why == null) { topped++; break; }
                            s.Failed = why;
                            failed.Add($"{s.Name}: {why}");
                            MelonLogger.Warning($"[Archery] TEST RANGE: {s.Name} could not be stocked and stays as it is until Refill: {why}");
                            break;
                        case RefillKind.WrongKind:
                            if (!s.WarnedWrong) { s.WarnedWrong = true; MelonLogger.Warning($"[Archery] TEST RANGE: {s.Name} holds another range item ({Describe(s.Dock)}); left alone until it is taken"); }
                            break;
                        default:
                            if (had && retry && stored == 0) failed.Add($"{s.Name}: empty");
                            break;
                    }
                    if (stored == 0 || matches) s.WarnedWrong = false; // warned again if something else turns up later
                }
                catch (Exception e) {
                    s.Failed = $"stocking threw {e.GetType().Name}: {e.Message}";
                    failed.Add($"{s.Name}: {s.Failed}");
                    MelonLogger.Warning($"[Archery] TEST RANGE: {s.Name} could not be stocked and stays as it is until Refill: {s.Failed}");
                }
            }
            if (topped > 0 && Logs.Debug) MelonLogger.Msg($"[Archery] TEST RANGE: refilled {topped} shelf slot(s)");
            return topped;
        }

        static void Refill(Captured c) {
            if (range == null) { Refuse(c, "no test range stands (press Build first)"); return; }
            var failed = new List<string>();
            int topped = RefillPass(true, failed);
            nextRefill = Time.time + Mathf.Max(0.5f, refillEvery.Value);
            LogSlots(range);
            Status(c, true, failed.Count == 0 ? $"{topped} slot(s) topped up; every slot is full" : $"{topped} slot(s) topped up; {failed.Count} still empty: {string.Join("; ", failed.ToArray())}", failed.Count > 0);
        }

        /// <summary>Empties every dock of the range's shelves (H4, range I-3: used or not) and every range container still on them: range
        /// stock is destroyed (the game's own DestroyAmountToEmpty), anything else is put down beside the shelf with its own saving
        /// (RangeStock.OnRemove). Handlers off first. A range quiver a player carries keeps its contents (and its handler and filter).</summary>
        static int EmptySlots(RangeState r) {
            int n = 0;
            var shelfDocks = new HashSet<PickupDock>();
            foreach (NetworkEntity f in r.Furniture) {
                try { if (f != null) foreach (PickupDock d in f.GetComponentsInChildren<PickupDock>(true)) if (d != null) shelfDocks.Add(d); }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: reading a shelf's docks failed: {e.Message}"); }
            }
            foreach (KeyValuePair<PickupDock, PickupDockInteractionHandler> kv in innerDocks) {
                try {
                    if (kv.Key == null) continue;
                    // a container still on a range shelf is emptied first; one a player carries keeps its contents
                    Pickup holder = kv.Key.GetComponentInParent<Pickup>();
                    if (holder == null || holder.Dock == null || !shelfDocks.Contains(holder.Dock)) continue;
                    kv.Key.ItemUndocked -= kv.Value;
                    n += Empty(kv.Key);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: emptying a quiver failed: {e.Message}"); }
            }
            innerDocks.Clear();
            foreach (Slot s in r.Slots) {
                try { if (s.Dock != null && s.OnUndock != null) s.Dock.ItemUndocked -= s.OnUndock; }
                catch { }
            }
            foreach (PickupDock d in shelfDocks) {
                try {
                    d.DockFilter -= RangeOnlyFilter;
                    n += Empty(d);
                }
                catch (Exception e) { MelonLogger.Warning($"[Archery] TEST RANGE: emptying a shelf dock failed: {e.Message}"); }
            }
            return n;
        }

        /// <summary>One dock on Remove: range stock destroyed, anything else put down beside it. How many range items went.</summary>
        static int Empty(PickupDock d) {
            int stored = d.QuantityStored;
            Pickup? docked = d.HasDockedPickup ? d.DockedPickup : null;
            switch (RangeStock.OnRemove(stored, IsStock(docked))) {
                case RemoveAction.Destroy: d.DestroyAmountToEmpty(); return stored;
                case RemoveAction.DropBeside:
                    MelonLogger.Warning($"[Archery] TEST RANGE: '{(docked != null ? docked.name : "?")}' x{stored} on a range shelf is not the range's: put down beside it, not destroyed");
                    d.UndockAll();
                    return 0;
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- loose range arrows

        // Since: when it was first SEEN loose (H4, range minor 1: LooseCap.Stamp; NaN while held or docked); TakenAt: when it left the shelf
        sealed class Loose { public NetworkEntity Entity = null!; public float Since = float.NaN, TakenAt; }
        static readonly List<Loose> loose = new List<Loose>();
        const int TrackMax = 4000;

        static void Track(NetworkEntity e) {
            foreach (Loose l in loose) if (ReferenceEquals(l.Entity, e)) { l.TakenAt = Time.time; l.Since = float.NaN; return; }
            loose.Add(new Loose { Entity = e, TakenAt = Time.time });
            if (loose.Count > TrackMax) loose.RemoveAt(0); // forgotten, not destroyed: the oldest are long in someone's quiver
            work = true;
        }

        static bool Gone(Loose l) => l.Entity == null || l.Entity.IsBeingDestroyed;
        static readonly Predicate<Loose> GoneTest = Gone;

        static readonly int T_TickShelf = Timing.Section("TestRange.TickShelf (refill, loose arrows)");
        static void TickShelf(float now) {
            if (now < nextRefill) return;
            using var _t = Timing.Begin(T_TickShelf);
            nextRefill = now + Mathf.Max(0.5f, refillEvery.Value);
            loose.RemoveAll(GoneTest);
            stock.RemoveWhere(StockGone);
            if (!enabled.Value) { loose.Clear(); return; } // H4 (range minor 3): switched off, nothing is touched
            foreach (Loose l in loose) l.Since = LooseCap.Stamp(IsLoose(l), l.Since, now);
            Pouches(now);
            if (range != null) RefillPass(false, new List<string>());
            LooseTick();
        }

        /// <summary>A stack taken off a range dock in one go goes into a new pouch the game spawns (PickupDock.UndockQuantity), which
        /// the game would save, contents and all, once it lies loose. A pouch holding range stock taken in the last 30 s gets
        /// BaseSaveMode and SaveMode Ignore while it lies loose (RangeStock.PouchNeverSaved; a pouch in a player's bag is theirs).
        /// H4 (range minor 13): the pouch's dock sits on an embedded child entity ('Storage', its own hash), so the pouch is that
        /// entity's PrefabRoot (hash 38942), not PickupDock.Entity (which never matched).</summary>
        static void Pouches(float now) {
            uint pouchHash = RangeKinds.ItemHash("Pouch");
            foreach (Loose l in loose) {
                if (Gone(l)) continue;
                Pickup p = l.Entity.CommonPickup;
                PickupDock d = p != null ? p.Dock : null!;
                NetworkEntity pouch = d != null && d.Entity != null ? d.Entity.PrefabRoot : null!;
                if (pouch == null || pouch.Prefab == null || pouch.Prefab.Hash != pouchHash) continue;
                Pickup pp = pouch.CommonPickup;
                if (pp == null || !RangeStock.PouchNeverSaved(pp.IsDocked, IsStock(p), now - l.TakenAt, 30f)) continue;
                pp.BaseSaveMode = ATT.Saving.SaveMode.Ignore;
                if (pouch.Prefab.SaveMode != ATT.Saving.SaveMode.Ignore) pouch.Prefab.SaveMode = ATT.Saving.SaveMode.Ignore;
            }
        }
    }
}
