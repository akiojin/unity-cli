using System.Globalization;
using UnityEngine;
#if UNITY_6000_4_OR_NEWER
using NativeObjectId = UnityEngine.EntityId;
#else
using NativeObjectId = System.Int32;
#endif

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Preserves native object identity without narrowing EntityId to an int or hash.
    /// Identifiers are valid only within the current Unity session.
    /// </summary>
    public static class ObjectIdentifier
    {
        public static NativeObjectId GetId(Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        public static object ToResponseValue(Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            // Preserve every bit, including the version, without JSON number precision loss.
            // ToULong is available in 6.4, unlike the culture-aware EntityId.ToString overload.
            return EntityId.ToULong(GetId(obj)).ToString(CultureInfo.InvariantCulture);
#else
            // Preserve the legacy JSON number on Unity versions without EntityId.
            return GetId(obj);
#endif
        }
    }
}
