using System;

namespace UnityCliBridge.Tools
{
    /// <summary>
    /// Adds a parameter description to the generated JSON Schema. The C# name,
    /// type and optional default define the wire name, type and requiredness.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class UnityCliArgAttribute : Attribute
    {
        public string Description { get; set; }
        public UnityCliArgAttribute(string description = null) => Description = description;
    }
}
