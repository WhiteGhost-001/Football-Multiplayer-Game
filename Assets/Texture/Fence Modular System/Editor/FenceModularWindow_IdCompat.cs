using System;
using UnityEditor;

namespace WB3DAssets.FenceModularSystem
{
    // Unity 6000.3 hat die 32 Bit Instance ID durch die EntityId-Struktur ersetzt.
    // Ab 6000.5 sind die alten APIs Compile-Fehler (CS0619), inklusive dem Cast
    // EntityId -> int: die ID ist dort echt 64 Bit und passt nicht mehr in ein int.
    //
    // Damit dieselbe Quelle auf 6000.0, 6000.3 und 6000.5 ohne Fehler und ohne
    // Warnungen baut, speichert das Tool keine rohen int-IDs mehr, sondern diesen
    // Wrapper. Er haelt intern das, was die laufende Unity-Version anbietet, und
    // wird nie in eine Zahl umgewandelt.
    internal readonly struct FenceId : IEquatable<FenceId>, IComparable<FenceId>
    {
#if UNITY_6000_3_OR_NEWER
        readonly UnityEngine.EntityId _value;

        FenceId(UnityEngine.EntityId value) { _value = value; }

        public static FenceId Of(UnityEngine.Object obj) { return new FenceId(obj.GetEntityId()); }

        public UnityEngine.Object ToObject() { return EditorUtility.EntityIdToObject(_value); }
#else
        readonly int _value;

        FenceId(int value) { _value = value; }

        public static FenceId Of(UnityEngine.Object obj) { return new FenceId(obj.GetInstanceID()); }

        public UnityEngine.Object ToObject() { return EditorUtility.InstanceIDToObject(_value); }
#endif

#if UNITY_6000_5_OR_NEWER
        public static FenceId FromEvent(UnityEngine.EntityId value) { return new FenceId(value); }
#else
        // Vor 6000.5 liefern die ObjectChangeEvents noch eine int-Instance-ID.
        public static FenceId FromEvent(int value) { return new FenceId(value); }
#endif

        public static readonly FenceId None = default;

        public bool IsValid { get { return !Equals(None); } }

        public bool Equals(FenceId other) { return _value.Equals(other._value); }

        public override bool Equals(object obj) { return obj is FenceId other && Equals(other); }

        public override int GetHashCode() { return _value.GetHashCode(); }

        public override string ToString() { return _value.ToString(); }

        public int CompareTo(FenceId other) { return _value.CompareTo(other._value); }

        public static bool operator ==(FenceId a, FenceId b) { return a.Equals(b); }

        public static bool operator !=(FenceId a, FenceId b) { return !a.Equals(b); }

        public static bool operator <(FenceId a, FenceId b) { return a.CompareTo(b) < 0; }

        public static bool operator >(FenceId a, FenceId b) { return a.CompareTo(b) > 0; }
    }

    internal static class FenceIds
    {
        public static FenceId StableId(this UnityEngine.Object obj) { return FenceId.Of(obj); }

        public static UnityEngine.Object ObjectFromId(FenceId id) { return id.ToObject(); }

        // FindObjectsSortMode ist ab 6000.5 deprecated, die Overloads ohne
        // Sortiermodus gibt es erst dort. Beides sind aktive Objekte ohne Inaktive.
        public static T[] FindAll<T>() where T : UnityEngine.Object
        {
#if UNITY_6000_5_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(UnityEngine.FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsByType<T>(UnityEngine.FindObjectsSortMode.InstanceID);
#endif
        }
    }
}