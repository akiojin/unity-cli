using System;

namespace UnityCliBridge.Tools
{
    /// <summary>
    /// Exposes a public static Editor method as a project-local unity-cli tool.
    /// Methods run synchronously on the Unity main thread. Put them in an Editor
    /// folder or an Editor-only assembly; no CLI rebuild is needed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnityCliToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; set; }
        /// <summary>Set false only for methods without side effects. Defaults to true for dry-run.</summary>
        public bool Mutating { get; set; } = true;

        public UnityCliToolAttribute(string name) => Name = name;
    }
}
