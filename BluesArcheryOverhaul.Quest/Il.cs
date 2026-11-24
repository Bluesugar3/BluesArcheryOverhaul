using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppAlta.Networking;
using Il2CppInterop.Runtime.InteropTypes;
using Il2Cpp2 = Il2CppSystem.Collections.Generic;
using Stream = Il2CppAlta.Serialization.Stream;

// Quest (IL2CPP) only: the platform layer. The shared source files keep their PC lines; where a PC line cannot work on Quest, this
// file either makes it compile and work unchanged (the extension methods below: C# only looks for an extension method when no
// instance method fits, so on PC these are never seen) or gives the `#if QUEST` alternative of the line something to call.
// What each form is, and that it worked on the owner's headset: docs/superpowers/specs/2026-11-22-quest-spike-report.md.
namespace BluesArcheryOverhaul {
    internal static class Il {
        // Every delegate handed to the game is held here: the game keeps only a native pointer to it, so the garbage collector
        // could take the managed side while the game still calls it.
        static readonly Dictionary<(IntPtr, int), SerializeConnectionMethod> handlers = new Dictionary<(IntPtr, int), SerializeConnectionMethod>();
        static readonly Queue<object> recent = new Queue<object>();
        const int RecentMax = 512;
        static readonly List<object> forever = new List<object>();

        /// <summary>Keeps <paramref name="o"/> alive for the rest of the game run.</summary>
        internal static T Keep<T>(T o) where T : class { lock (forever) forever.Add(o); return o; }

        /// <summary>Keeps <paramref name="o"/> alive for a while: for a delegate the game calls at once (a message's serializer is run
        /// inside Send) but may hold a moment longer.</summary>
        static T KeepAWhile<T>(T o) where T : class {
            lock (recent) {
                recent.Enqueue(o);
                while (recent.Count > RecentMax) recent.Dequeue();
            }
            return o;
        }

        /// <summary>The PC line <c>connection.SetHandler(type, Method)</c> compiles to this on Quest: the game's delegate type is made
        /// from the managed one (its implicit operator) and held for as long as the connection may call it.</summary>
        internal static void SetHandler(this Connection connection, MessageType type, Action<Connection, Stream> handler) {
            var d = (SerializeConnectionMethod)handler;
            lock (handlers) handlers[(connection.Pointer, (int)type)] = d;
            connection.SetHandler(type, d);
        }

        /// <summary>The PC line <c>connection.Send(channel, type, (c, s) => ...)</c> compiles to this on Quest.</summary>
        internal static bool Send(this Connection connection, ConnectionChannel? channel, MessageType type, Action<Connection, Stream> write) {
            var d = KeepAWhile((SerializeConnectionMethod)write);
            return connection.Send(channel!, type, d);
        }

        // ---------------------------------------------------------------- casts
        // A C# cast or `is` of a game object compiles on Quest and gives null / false: the proxy object's managed type is the type the
        // value was declared as, not the object's real type. These ask the game.

        /// <summary>`o is T` for a game object.</summary>
        internal static bool Is<T>(this Il2CppObjectBase? o) where T : Il2CppObjectBase => o != null && o.TryCast<T>() != null;

        /// <summary>`o as T` for a game object.</summary>
        internal static T? As<T>(this Il2CppObjectBase? o) where T : Il2CppObjectBase => o?.TryCast<T>();

        /// <summary>`o is T t` for a game object.</summary>
        internal static bool Is<T>(this Il2CppObjectBase? o, out T t) where T : Il2CppObjectBase {
            t = o?.TryCast<T>()!;
            return t != null;
        }

        // ---------------------------------------------------------------- the game's own collections
        // The shared lines use LINQ on game lists; on Quest those are Il2CppSystem lists, which System.Linq does not know. These have
        // LINQ's names, so the shared lines compile unchanged and read the game's list in place. Nothing here copies a game list
        // except ToList, whose PC form also makes a new list.

        /// <summary>The items of a game list, in order (a managed sequence over the list itself).</summary>
        internal static IEnumerable<T> Each<T>(this Il2Cpp2.List<T> list) {
            for (int i = 0; i < list.Count; i++) yield return list[i];
        }

        /// <summary>The items of any game sequence (its enumerator, through the game's own MoveNext).</summary>
        internal static IEnumerable<T> Each<T>(this Il2Cpp2.IEnumerable<T> items) {
            Il2Cpp2.IEnumerator<T> e = items.GetEnumerator();
            var move = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (move.MoveNext()) yield return e.Current;
        }

        internal static bool Any<T>(this Il2Cpp2.List<T> list, Func<T, bool> test) => list.Each().Any(test);
        internal static IEnumerable<T> Where<T>(this Il2Cpp2.List<T> list, Func<T, bool> test) => list.Each().Where(test);
        internal static IEnumerable<R> Select<T, R>(this Il2Cpp2.List<T> list, Func<T, R> map) => list.Each().Select(map);
        internal static T? FirstOrDefault<T>(this Il2Cpp2.List<T> list, Func<T, bool> test) => list.Each().FirstOrDefault(test);
        internal static List<T> ToList<T>(this Il2Cpp2.List<T> list) => list.Each().ToList();
        internal static bool Contains<T>(this Il2Cpp2.IEnumerable<T> items, T value) => items.Each().Any(x => value is Il2CppObjectBase ? Same(x, value) : EqualityComparer<T>.Default.Equals(x, value));

        // A managed list of game objects: List.Contains / Remove compare managed wrappers, and the game hands out a new wrapper per
        // read, so they never match. These compare the game objects themselves.
        internal static int IndexOfSame<T>(List<T> list, T item) where T : class { for (int i = 0; i < list.Count; i++) if (Same(list[i], item)) return i; return -1; }
        internal static bool Has<T>(List<T> list, T item) where T : class => IndexOfSame(list, item) >= 0;
        internal static bool Drop<T>(List<T> list, T item) where T : class { int i = IndexOfSame(list, item); if (i < 0) return false; list.RemoveAt(i); return true; }

        /// <summary>A managed sequence as a game sequence (a new game list behind the game's IEnumerable).</summary>
        internal static Il2Cpp2.IEnumerable<T> Game<T>(IEnumerable<T> items) {
            var made = new Il2Cpp2.List<T>();
            foreach (T t in items) made.Add(t);
            return made.Cast<Il2Cpp2.IEnumerable<T>>();
        }

        // The PC lines hand a managed array or list where the Quest game takes its own IEnumerable, or ask Unity to fill a list.
        internal static void SetupRenderers(this MovingObjectsBetweenDocks g, IEnumerable<UnityEngine.MeshRenderer> r) => g.SetupRenderers(Game(r));
        internal static void InitializeEffect(this ToolBasedEffectBase e, IEnumerable<UnityEngine.MeshRenderer> r) => e.InitializeEffect(Game(r));
        internal static void GetComponentsInChildren<T>(this UnityEngine.Component c, bool includeInactive, List<T> into) where T : UnityEngine.Component {
            into.Clear();
            into.AddRange(c.GetComponentsInChildren<T>(includeInactive));
        }

        /// <summary>A new game list with the same items (the PC line's <c>new List&lt;T&gt;(list)</c>).</summary>
        internal static Il2Cpp2.List<T> Copy<T>(Il2Cpp2.List<T> list) {
            var made = new Il2Cpp2.List<T>();
            for (int i = 0; i < list.Count; i++) made.Add(list[i]);
            return made;
        }

        // ---------------------------------------------------------------- identity
        // On Quest the interop hands back a NEW managed object each time a game object is read (its object cache is off: the
        // garbage-collector hook it needs is not found in this game build), so ReferenceEquals, == on a plain game object, and a
        // Dictionary or HashSet keyed by game objects all fail silently. These compare the game's own object address.

        /// <summary>The PC line's ReferenceEquals(a, b): the same game object (by address), or the same managed object.</summary>
        internal static bool Same(object? a, object? b) {
            if (a is Il2CppObjectBase x && b is Il2CppObjectBase y) return x.Pointer == y.Pointer;
            return ReferenceEquals(a, b);
        }

        /// <summary>A comparer for a Dictionary or HashSet keyed by game objects.</summary>
        internal static IEqualityComparer<T> ByPointer<T>() where T : class => PointerComparer<T>.Instance;

        // Physics queries that fill a caller's buffer. On Quest a managed array handed to Unity is COPIED in, so the results never
        // come back: the count is right and every entry is empty. And the mod making a game-side array for Unity to fill ended the
        // game process the moment the array was made (2026-11-22, no message; the cause is not known). So these ask the game for
        // the hits (RaycastAll, SphereCastAll, OverlapSphere: the game makes the array) and copy them out.
        // "Full" is still reported as results.Length, so the callers' grow-and-ask-again loops work as on PC.
        // ponytail: one game array per query (garbage). If the headset's timing report shows it, find out why the mod's own
        // Il2CppStructArray<RaycastHit> could not be made and go back to the NonAlloc calls.
        static int Out(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.RaycastHit> all, UnityEngine.RaycastHit[] results) {
            QuestWatch.Mark("Il cast: the game returned");
            int n = all == null ? 0 : Math.Min(all.Length, results.Length);
            for (int i = 0; i < n; i++) results[i] = all![i];
            QuestWatch.Mark("Il cast: hits copied out");
            return n;
        }

        internal static int RaycastNonAlloc(UnityEngine.Vector3 origin, UnityEngine.Vector3 direction, UnityEngine.RaycastHit[] results, float maxDistance, int mask, UnityEngine.QueryTriggerInteraction triggers) {
            QuestWatch.Mark("Il cast: RaycastAll starts");
            return Out(UnityEngine.Physics.RaycastAll(origin, direction, maxDistance, mask, triggers), results);
        }

        internal static int SphereCastNonAlloc(UnityEngine.Vector3 origin, float radius, UnityEngine.Vector3 direction, UnityEngine.RaycastHit[] results, float maxDistance, int mask, UnityEngine.QueryTriggerInteraction triggers) {
            QuestWatch.Mark("Il cast: SphereCastAll starts");
            return Out(UnityEngine.Physics.SphereCastAll(origin, radius, direction, maxDistance, mask, triggers), results);
        }

        internal static int OverlapSphereNonAlloc(UnityEngine.Vector3 centre, float radius, UnityEngine.Collider[] results, int mask, UnityEngine.QueryTriggerInteraction triggers) {
            QuestWatch.Mark("Il overlap: starts");
            var all = UnityEngine.Physics.OverlapSphere(centre, radius, mask, triggers);
            QuestWatch.Mark("Il overlap: the game returned");
            int n = all == null ? 0 : Math.Min(all.Length, results.Length);
            for (int i = 0; i < n; i++) results[i] = all![i];
            QuestWatch.Mark("Il overlap: colliders copied out");
            return n;
        }

        // A managed array handed to Unity becomes a NEW game array on every call (LineRenderer.SetPositions, Gradient.SetKeys: per
        // ghost, per frame while it shimmers, homes or carries strands). These keep one game array per length and refill it. The
        // array types are the ones that conversion already makes, so making them is known to work on the headset.
        static readonly Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.Vector3>> pointBuffers = new Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.Vector3>>();
        static readonly Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.GradientColorKey>> colourBuffers = new Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.GradientColorKey>>();
        static readonly Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.GradientAlphaKey>> alphaBuffers = new Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.GradientAlphaKey>>();

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T> Filled<T>(Dictionary<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T>> buffers, T[] from) where T : unmanaged {
            if (!buffers.TryGetValue(from.Length, out var buffer)) buffers[from.Length] = buffer = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T>(from.Length);
            for (int i = 0; i < from.Length; i++) buffer[i] = from[i];
            return buffer;
        }

        internal static void SetPositions(UnityEngine.LineRenderer line, UnityEngine.Vector3[] points) => line.SetPositions(Filled(pointBuffers, points));
        internal static void SetKeys(UnityEngine.Gradient gradient, UnityEngine.GradientColorKey[] colours, UnityEngine.GradientAlphaKey[] alphas) => gradient.SetKeys(Filled(colourBuffers, colours), Filled(alphaBuffers, alphas));

        sealed class PointerComparer<T> : IEqualityComparer<T> where T : class {
            internal static readonly PointerComparer<T> Instance = new PointerComparer<T>();
            public bool Equals(T? a, T? b) => Same(a, b);
            public int GetHashCode(T o) => o is Il2CppObjectBase b ? b.Pointer.GetHashCode() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
