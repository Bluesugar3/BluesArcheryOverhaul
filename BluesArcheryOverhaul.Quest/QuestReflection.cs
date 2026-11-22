using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

// Quest (IL2CPP) only. On PC the mod reaches the game's private members through HarmonyLib's Traverse and AccessTools, by field
// name. On Quest every game field is a PROPERTY of the interop proxy (same name, public), so HarmonyLib finds no field: a read gives
// null and a write does nothing, silently. These two classes have the PC names and live in the mod's own namespace, so in every
// shared file the name `Traverse` / `AccessTools` means these (a namespace's own types win over a using directive's), with no edit
// to the PC lines. They find the member as a proxy property first, then as a real field or property, and convert values between
// the managed and the Il2Cpp form where that is safe:
//   - an array (T[] <-> Il2CppReferenceArray<T> / Il2CppStructArray<T>): copied, both ways (an array is replaced, never grown);
//   - a managed List<T> written to an Il2Cpp list member: a new Il2Cpp list with the same items;
//   - a game object read as a more derived game type: TryCast.
// What is NOT safe, and so never done: reading an Il2Cpp List or Dictionary as a managed one (a copy would lose every change made
// to it). Such a read gives null and logs one error naming the member: that shared line needs its own `#if QUEST` alternative.
// AccessTools.FieldRef / FieldRefAccess (a ref to a field) cannot exist for a property, so they are not offered here: a line that
// uses them does not compile for Quest and gets its own alternative.
namespace BluesArcheryOverhaul {
    internal sealed class Traverse {
        readonly object? target;
        readonly Type? type;
        readonly MemberInfo? member;

        Traverse(object? target, Type? type, MemberInfo? member) { this.target = target; this.type = type; this.member = member; }

        internal static Traverse Create(object? o) => new Traverse(o, o?.GetType(), null);
        internal static Traverse Create(Type t) => new Traverse(null, t, null);

        internal Traverse Field(string name) => new Traverse(Current(), CurrentType(), QuestMembers.Find(CurrentType(), name));
        internal Traverse Property(string name) => Field(name);
        internal bool FieldExists() => member != null;

        object? Current() => member == null ? target : QuestMembers.Get(member, target);
        Type? CurrentType() => member == null ? type : QuestMembers.TypeOf(member);

        internal object? GetValue() => member == null ? target : QuestMembers.Get(member, target);

        internal T GetValue<T>() {
            object? v = GetValue();
            return (T)QuestMembers.Convert(v, typeof(T), Where())!;
        }

        internal Traverse SetValue(object? value) {
            if (member == null) { QuestMembers.Report($"Traverse: no member to set on {type?.Name ?? "null"}"); return this; }
            QuestMembers.Set(member, target, value, Where());
            return this;
        }

        string Where() => $"{type?.Name ?? "?"}.{member?.Name ?? "?"}";
    }

    internal static class AccessTools {
        internal static FieldInfo? Field(Type type, string name) {
            MemberInfo? m = QuestMembers.Find(type, name);
            return m switch {
                null => null,
                FieldInfo f => f,
                PropertyInfo p => new ProxyField(p),
                _ => null
            };
        }

        internal static PropertyInfo? Property(Type type, string name) => HarmonyLib.AccessTools.Property(type, name);
        internal static MethodInfo? Method(Type type, string name, Type[]? parameters = null, Type[]? generics = null) =>
            HarmonyLib.AccessTools.Method(type, name, parameters, generics);
        internal static MethodInfo? Method(string typeColonName, Type[]? parameters = null, Type[]? generics = null) =>
            HarmonyLib.AccessTools.Method(typeColonName, parameters, generics);
        internal static T MethodDelegate<T>(MethodInfo method, object? instance = null, bool virtualCall = true) where T : Delegate =>
            HarmonyLib.AccessTools.MethodDelegate<T>(method, instance, virtualCall);
    }

    /// <summary>A proxy property seen as a field, for the shared lines that hold a FieldInfo (AccessTools.Field(...).SetValue(...)).</summary>
    internal sealed class ProxyField : FieldInfo {
        readonly PropertyInfo p;
        internal ProxyField(PropertyInfo p) { this.p = p; }
        public override Type FieldType => p.PropertyType;
        public override string Name => p.Name;
        public override Type? DeclaringType => p.DeclaringType;
        public override Type? ReflectedType => p.ReflectedType;
        public override FieldAttributes Attributes => FieldAttributes.Public | (IsStaticProp ? FieldAttributes.Static : 0);
        public override RuntimeFieldHandle FieldHandle => throw new NotSupportedException("a Quest proxy property has no field handle");
        bool IsStaticProp => (p.GetMethod ?? p.SetMethod)?.IsStatic ?? false;
        public override object? GetValue(object? obj) => QuestMembers.Get(p, obj);
        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, CultureInfo? culture) =>
            QuestMembers.Set(p, obj, value, $"{p.DeclaringType?.Name}.{p.Name}");
        public override object[] GetCustomAttributes(bool inherit) => p.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => p.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => p.IsDefined(attributeType, inherit);
    }

    internal static class QuestMembers {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        static readonly HashSet<string> reported = new HashSet<string>();

        internal static void Report(string what) {
            lock (reported) if (!reported.Add(what)) return;
            MelonLogger.Error($"[Archery] Quest: {what}");
        }

        internal static MemberInfo? Find(Type? t, string name) {
            for (Type? x = t; x != null; x = x.BaseType) {
                PropertyInfo? p = x.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null) return p;
                FieldInfo? f = x.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            Report($"no member '{name}' on {t?.FullName ?? "null"}");
            return null;
        }

        internal static Type? TypeOf(MemberInfo m) => m is PropertyInfo p ? p.PropertyType : (m as FieldInfo)?.FieldType;

        static bool IsStatic(MemberInfo m) => m is PropertyInfo p ? ((p.GetMethod ?? p.SetMethod)?.IsStatic ?? false) : ((FieldInfo)m).IsStatic;

        internal static object? Get(MemberInfo m, object? target) {
            object? on = IsStatic(m) ? null : target;
            if (on == null && !IsStatic(m)) return null;
            return m is PropertyInfo p ? p.GetValue(on) : ((FieldInfo)m).GetValue(on);
        }

        internal static void Set(MemberInfo m, object? target, object? value, string where) {
            Type want = TypeOf(m)!;
            object? v = Convert(value, want, where);
            if (value != null && v == null) return;   // reported by Convert
            object? on = IsStatic(m) ? null : target;
            if (m is PropertyInfo p) p.SetValue(on, v);
            else ((FieldInfo)m).SetValue(on, v);
        }

        /// <summary>value as <paramref name="want"/>, or null (and one error line) when that is not safe.</summary>
        internal static object? Convert(object? value, Type want, string where) {
            if (value == null) return null;
            Type have = value.GetType();
            if (want.IsAssignableFrom(have)) return value;

            // game object to a more derived (or interface) game type
            if (value is Il2CppObjectBase obj && typeof(Il2CppObjectBase).IsAssignableFrom(want)) {
                MethodInfo tryCast = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast))!.MakeGenericMethod(want);
                object? cast = tryCast.Invoke(obj, null);
                if (cast == null) Report($"{where}: a {have.Name} is not a {want.Name}");
                return cast;
            }

            // arrays, both ways
            if (want.IsArray && IsIl2CppArray(have)) {
                Type el = want.GetElementType()!;
                var list = new List<object?>();
                foreach (object? x in (IEnumerable)value) list.Add(x);
                Array a = Array.CreateInstance(el, list.Count);
                for (int i = 0; i < list.Count; i++) a.SetValue(list[i], i);
                return a;
            }
            if (have.IsArray && IsIl2CppArray(want)) {
                // A generic game class's T[] is the abstract Il2CppArrayBase<T>: make the concrete kind for T.
                Type made = want;
                if (want.IsAbstract && want.IsGenericType) {
                    Type el = want.GetGenericArguments()[0];
                    made = el == typeof(string) ? typeof(Il2CppStringArray)
                         : el.IsValueType ? typeof(Il2CppStructArray<>).MakeGenericType(el)
                         : typeof(Il2CppReferenceArray<>).MakeGenericType(el);
                }
                return Activator.CreateInstance(made, value);
            }

            // a managed list written to an Il2Cpp list
            if (IsGeneric(want, "Il2CppSystem.Collections.Generic.List`1") && value is IEnumerable items && !(value is Il2CppObjectBase)) {
                object made = Activator.CreateInstance(want)!;
                MethodInfo add = want.GetMethod("Add")!;
                foreach (object? x in items) add.Invoke(made, new[] { x });
                return made;
            }

            Report($"{where}: a {Describe(have)} cannot be used as a {Describe(want)} on Quest; this line needs a Quest alternative");
            return null;
        }

        static bool IsIl2CppArray(Type t) {
            for (Type? x = t; x != null; x = x.BaseType)
                if (x.IsGenericType && x.GetGenericTypeDefinition().FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1") return true;
            return false;
        }

        static bool IsGeneric(Type t, string def) => t.IsGenericType && t.GetGenericTypeDefinition().FullName == def;

        static string Describe(Type t) => t.IsGenericType ? $"{t.Namespace}.{t.Name}" : t.FullName ?? t.Name;
    }
}
