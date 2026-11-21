using System.Collections.Generic;
using Alta.Networking;
using MelonLoader;
using UnityEngine;

namespace BluesArcheryOverhaul {
    /// <summary>A real arrow held out of the world where it left the bow: Seeking Volley's locked release (Seek.cs) and a Skyfall
    /// release (Skyfall.Held). While held it is stopped, kinematic (Discrete first: the game sets that itself only after a delay, and a
    /// kinematic body with continuous detection logs Unity's warning), its colliders off (it cannot push the bow or the hand, and no
    /// hand can close on it) and, not headless, hidden. The one place for what a hold changes and how it is turned back, so a fix to
    /// one hold reaches both. Each holder keeps its own table, times, flight-sync refusal and way out.</summary>
    internal abstract class HeldArrow {
        public uint Release; public float Until;
        /// <summary>What the hold changed, listed as it was changed: exactly what Free turns back.</summary>
        public readonly List<Renderer> Hidden = new List<Renderer>();
        public readonly List<Collider> Off = new List<Collider>();

        /// <summary>The holder's name in log lines ("Seek", "Skyfall").</summary>
        protected abstract string Who { get; }

        /// <summary>Holds <paramref name="arrow"/> as <paramref name="hold"/> in <paramref name="holds"/>. An older hold of the same arrow
        /// is freed first. H4 (H23-review minor 2 = Opus minor 4): the hold is recorded BEFORE anything is changed, each change is listed
        /// as it is made, and a throw part-way removes it, frees what was changed and rethrows, so every way out knows exactly what to
        /// turn back.</summary>
        internal static void Take<T>(Dictionary<Arrow, T> holds, Arrow arrow, T hold) where T : HeldArrow {
            if (holds.TryGetValue(arrow, out T old)) { holds.Remove(arrow); old.Free(arrow, false); }
            holds[arrow] = hold;
            try {
                Stop(arrow);
                foreach (Collider c in arrow.GetComponentsInChildren<Collider>(false)) if (c != null && c.enabled) { hold.Off.Add(c); c.enabled = false; }
                // H4 (H23-review minor 1): a pooled game effect parented to the arrow (a charged arrow's glow) is left alone: it can go
                // back to its pool while the arrow is held and would come out of it hidden
                if (!ApplicationManager.IsHeadless)
                    foreach (Renderer r in arrow.GetComponentsInChildren<Renderer>(false))
                        if (r != null && r.enabled && r.GetComponentInParent<PooledObject>() == null) { hold.Hidden.Add(r); r.enabled = false; }
                if (NetworkSceneManager.IsServer && arrow.Entity != null) arrow.Entity.ForcePositionSync();
            }
            catch {
                holds.Remove(arrow);
                hold.Free(arrow, false);
                throw;
            }
        }

        /// <summary>The arrow stopped where it is: its flight no longer updated, no velocity, kinematic (Discrete first). Also each
        /// holder's answer to a flight sync it refuses.</summary>
        internal static void Stop(Arrow arrow) {
            arrow.StopUpdatingArrowFlight();
            Rigidbody rb = arrow.Rigidbody;
            if (rb == null) return;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            if (rb.collisionDetectionMode != CollisionDetectionMode.Discrete) rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
        }

        /// <summary>H3: the held arrow back in the world: colliders on, shown, and (unless <paramref name="leaveBody"/>: the network or an
        /// embed decides it) not kinematic when not embedded. Never throws. <paramref name="arrow"/> null (destroyed): only the lists.</summary>
        internal void Free(Arrow? arrow, bool leaveBody) {
            foreach (Collider c in Off) { try { if (c != null) c.enabled = true; } catch { } }
            Off.Clear();
            foreach (Renderer r in Hidden) { try { if (r != null) r.enabled = true; } catch { } }
            Hidden.Clear();
            if (leaveBody || arrow == null) return;
            try {
                Embeddable embed = arrow.Entity != null ? arrow.Entity.GetComponent<Embeddable>() : null!;
                Rigidbody rb = arrow.Rigidbody;
                if (rb != null && (embed == null || !embed.IsEmbedded)) rb.isKinematic = false;
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Archery] {Who}: arrow {Release}'s real arrow could not be given its body back: {e.Message}"); }
        }
    }
}
