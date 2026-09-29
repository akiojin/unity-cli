using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Evaluation
{
    internal static class EvalResultSerializer
    {
        internal static JToken Serialize(object value)
        {
            var budget = 1024;
            var token = ConvertValue(value, 0, new HashSet<object>(ReferenceEquality.Instance), ref budget);
            if (System.Text.Encoding.UTF8.GetByteCount(token.ToString(Newtonsoft.Json.Formatting.None)) > 65536)
                throw new InvalidOperationException("Serialized result exceeds 65536 UTF-8 bytes.");
            return token;
        }

        private static JToken ConvertValue(object value, int depth, HashSet<object> path, ref int budget)
        {
            if (--budget < 0 || depth > 8) throw new InvalidOperationException("Result exceeds 1024 values or depth 8.");
            if (value == null) return JValue.CreateNull();
            if (value is UnityEngine.Object obj)
                return obj == null ? JValue.CreateNull() : new JObject { ["kind"] = "unity_object", ["type"] = obj.GetType().FullName, ["name"] = obj.name, ["instanceId"] = obj.GetInstanceID() };
            if (value is string str)
            {
                if (str.Length > 16384) throw new InvalidOperationException("String exceeds 16384 characters.");
                return new JValue(str);
            }
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d)) || value is float f && (float.IsNaN(f) || float.IsInfinity(f))) throw new InvalidOperationException("Non-finite numbers are not JSON values.");
            var type = value.GetType();
            if (type.IsPrimitive || value is decimal) return new JValue(value);
            if (type.IsEnum) return new JValue(Convert.ChangeType(value, Enum.GetUnderlyingType(type)));
            if (value is Type || value is Delegate || value is Task || value is MemberInfo) throw new InvalidOperationException("Unsupported result type: " + type.FullName);
            if (!path.Add(value)) throw new InvalidOperationException("Result contains a reference cycle.");
            try
            {
                if (value is IDictionary dictionary)
                {
                    var result = new JObject();
                    foreach (DictionaryEntry item in dictionary)
                    {
                        if (!(item.Key is string key) || key.Length > 16384) throw new InvalidOperationException("Dictionary keys must be bounded strings.");
                        result.Add(key, ConvertValue(item.Value, depth + 1, path, ref budget));
                    }
                    return result;
                }
                if (value is IEnumerable sequence)
                {
                    var result = new JArray();
                    foreach (var item in sequence) result.Add(ConvertValue(item, depth + 1, path, ref budget));
                    return result;
                }
                var objResult = new JObject();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) objResult[field.Name] = ConvertValue(field.GetValue(value), depth + 1, path, ref budget);
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod() != null)) objResult[property.Name] = ConvertValue(property.GetValue(value), depth + 1, path, ref budget);
                if (objResult.Count == 0) throw new InvalidOperationException("Unsupported result type: " + type.FullName);
                return objResult;
            }
            finally { path.Remove(value); }
        }

        private sealed class ReferenceEquality : IEqualityComparer<object>
        {
            public static readonly ReferenceEquality Instance = new ReferenceEquality();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

    }
}
