using UnityEngine;

namespace UnityCliBridge.Helpers
{
    internal static class ObjectIdentity
    {
        /// <summary>Returns the legacy integer ID used by the CLI wire contract.</summary>
        internal static int GetInstanceId(Object target)
        {
#if UNITY_6000_5_OR_NEWER
            // GetInstanceID and EntityId's int conversion are errors in 6000.5+.
            // Match the legacy GetInstanceID implementation's low 32 bits, not
            // EntityId.GetHashCode(). Keep this compatibility projection here.
            return unchecked((int)EntityId.ToULong(target.GetEntityId()));
#else
#pragma warning disable CS0618 // Still supported (warning only) in 6000.4.
            return target.GetInstanceID();
#pragma warning restore CS0618
#endif
        }

        /// <summary>Resolves a wire-contract integer ID from <see cref="GetInstanceId"/>.</summary>
        internal static Object FindObject(int instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            // InstanceIDToObject is an error in 6000.7+ and the int projection
            // is not reversible to an EntityId, so match the same projection.
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Object>())
                if (candidate != null && GetInstanceId(candidate) == instanceId) return candidate;
            return null;
#else
            return UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
#endif
        }
    }
}
