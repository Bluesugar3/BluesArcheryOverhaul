using System;
using System.Collections.Generic;
using Il2CppAlta.Networking;
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
    }
}
