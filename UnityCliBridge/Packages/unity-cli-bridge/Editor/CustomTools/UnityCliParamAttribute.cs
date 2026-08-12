using System;

namespace UnityCliBridge
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class UnityCliParamAttribute : Attribute
    {
        public string Description { get; set; }
        public bool Required { get; set; }
        public string[] Aliases { get; set; }
    }
}
