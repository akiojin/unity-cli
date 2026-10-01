using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Tools
{
    /// <summary>One source of truth for both discovery schema and strict argument binding.</summary>
    internal sealed class CustomToolParameter
    {
        private readonly ParameterInfo parameter;
        private readonly Type type;
        private readonly bool nullable;
        internal string Name => parameter.Name;
        internal bool Required => !parameter.HasDefaultValue;
        internal JObject Schema { get; }

        internal CustomToolParameter(ParameterInfo parameter)
        {
            this.parameter = parameter;
            type = parameter.ParameterType;
            if (parameter.IsOut || type.IsByRef || parameter.IsDefined(typeof(ParamArrayAttribute), false))
                throw new ArgumentException($"Parameter '{Name}' cannot be ref, out or params");
            if (parameter.HasDefaultValue && (parameter.DefaultValue == Missing.Value || parameter.DefaultValue == DBNull.Value))
                throw new ArgumentException($"Parameter '{Name}' needs a C# constant default");
            nullable = type == typeof(string) && parameter.HasDefaultValue && parameter.DefaultValue == null;
            Schema = TypeSchema(type);
            if (nullable)
                Schema = new JObject { ["anyOf"] = new JArray(Schema, new JObject { ["type"] = "null" }) };
            if (!Required)
            {
                var value = parameter.DefaultValue;
                Schema["default"] = value == null ? JValue.CreateNull()
                    : JToken.FromObject(type.IsEnum ? value.ToString() : value);
            }
            var description = parameter.GetCustomAttribute<UnityCliArgAttribute>()?.Description;
            if (!string.IsNullOrEmpty(description)) Schema["description"] = description;
        }

        private static JObject TypeSchema(Type type)
        {
            if (type == typeof(string)) return new JObject { ["type"] = "string" };
            if (type == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (type == typeof(int)) return new JObject { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue };
            if (type == typeof(long)) return new JObject { ["type"] = "integer", ["minimum"] = long.MinValue, ["maximum"] = long.MaxValue };
            if (type == typeof(float)) return new JObject { ["type"] = "number", ["minimum"] = -(double)float.MaxValue, ["maximum"] = (double)float.MaxValue };
            if (type == typeof(double)) return new JObject { ["type"] = "number", ["minimum"] = -double.MaxValue, ["maximum"] = double.MaxValue };
            if (type.IsEnum) return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(type)) };
            throw new ArgumentException($"Unsupported parameter type '{type.FullName}'; use string, bool, int, long, float, double or an enum");
        }

        internal object Bind(JObject arguments)
        {
            if (!arguments.TryGetValue(Name, StringComparison.Ordinal, out var value))
            {
                if (Required) throw Invalid("is required");
                return parameter.DefaultValue;
            }
            if (value.Type == JTokenType.Null)
            {
                if (nullable) return null;
                throw Invalid("cannot be null");
            }
            if (type == typeof(string))
            {
                if (value.Type != JTokenType.String) throw Invalid("must be a string");
                return (string)value;
            }
            if (type == typeof(bool))
            {
                if (value.Type != JTokenType.Boolean) throw Invalid("must be a boolean");
                return (bool)value;
            }
            if (type.IsEnum)
            {
                if (value.Type != JTokenType.String || !Enum.GetNames(type).Contains((string)value))
                    throw Invalid("must be one of: " + string.Join(", ", Enum.GetNames(type)));
                return Enum.Parse(type, (string)value);
            }
            try
            {
                if (type == typeof(int) || type == typeof(long))
                {
                    if (value.Type != JTokenType.Integer) throw Invalid("must be an integer");
                    return type == typeof(int) ? (object)value.Value<int>() : value.Value<long>();
                }
                if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
                    throw Invalid("must be a number");
                var number = value.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number)
                    || (type == typeof(float) && (number > float.MaxValue || number < -float.MaxValue)))
                    throw Invalid("is outside the supported numeric range");
                return type == typeof(float) ? (object)(float)number : number;
            }
            catch (Exception error) when (error is OverflowException || error is FormatException || error is InvalidCastException)
            {
                throw Invalid("is outside the supported numeric range");
            }
        }

        private ArgumentException Invalid(string message) => new ArgumentException($"Argument '{Name}' {message}");
    }
}
