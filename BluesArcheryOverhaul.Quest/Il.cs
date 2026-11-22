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
        internal static bool Contains<T>(this Il2Cpp2.IEnumerable<T> items, T value) => items.Each().Contains(value);

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

        sealed class PointerComparer<T> : IEqualityComparer<T> where T : class {
            internal static readonly PointerComparer<T> Instance = new PointerComparer<T>();
            public bool Equals(T? a, T? b) => Same(a, b);
            public int GetHashCode(T o) => o is Il2CppObjectBase b ? b.Pointer.GetHashCode() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
