using System;

namespace UnityCliBridge
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnityCliToolAttribute : Attribute
    {
        public UnityCliToolAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string Description { get; set; }
        public bool Mutating { get; set; }
        public bool AllowInPlayMode { get; set; }
    }
}
