using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Spike handler for driving Unity VFX Graph authoring via reflection over the
    /// internal UnityEditor.VFX model API (the package exposes no public authoring API).
    /// Commands: vfx_describe_graph (Tier-1 read-back oracle), vfx_list_library
    /// (discovery), vfx_apply (mutator; currently: add_block).
    /// </summary>
    public static class VfxGraphHandler
    {
        // ---- Reflection type resolution -------------------------------------

        private const string EditorAsmHint = "Unity.VisualEffectGraph.Editor";

        private static Type T(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            throw new Exception($"VFX type not found: {fullName}. Is com.unity.visualeffectgraph installed?");
        }

        private static Type ResourceType => T("UnityEditor.VFX.VisualEffectResource");
        private static Type ResourceExtType => T("UnityEditor.VFX.VisualEffectResourceExtensions");
        private static Type GraphType => T("UnityEditor.VFX.VFXGraph");
        private static Type ModelType => T("UnityEditor.VFX.VFXModel");
        private static Type ContextType => T("UnityEditor.VFX.VFXContext");
        private static Type BlockType => T("UnityEditor.VFX.VFXBlock");
        private static Type LibraryType => T("UnityEditor.VFX.VFXLibrary");

        private const BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AllStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        // ---- Reflection helpers --------------------------------------------

        private static object Call(object target, Type type, string method, params object[] args)
        {
            var flags = target == null ? AllStatic : AllInstance;
            var m = type.GetMethod(method, flags, null,
                args.Select(a => a?.GetType() ?? typeof(object)).ToArray(), null)
                ?? type.GetMethods(flags).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null) throw new Exception($"Method not found: {type.Name}.{method}({args.Length} args)");
            return m.Invoke(target, args);
        }

        private static object Prop(object target, string name)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, AllInstance | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(target);
            }
            throw new Exception($"Property not found: {target.GetType().Name}.{name}");
        }

        private static IEnumerable<object> Children(object model)
        {
            var children = Prop(model, "children") as IEnumerable;
            if (children == null) yield break;
            foreach (var c in children) yield return c;
        }

        private static object LoadGraph(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new Exception("assetPath is required");
            var resource = Call(null, ResourceType, "GetResourceAtPath", assetPath);
            if (resource == null)
                throw new Exception($"No VisualEffectResource at path: {assetPath}");
            var graph = Call(null, ResourceExtType, "GetOrCreateGraph", resource);
            return graph;
        }

        private static string ModelName(object model)
        {
            try
            {
                var n = Prop(model, "name") as string;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return model.GetType().Name;
        }

        // ---- Commands -------------------------------------------------------

        private static Type SettingFlagsType => T("UnityEditor.VFX.VFXSettingAttribute+VisibleFlags");

        private static JToken ToJToken(object value)
        {
            if (value == null) return JValue.CreateNull();
            if (value.GetType().IsEnum) return new JValue(value.ToString());
            try { return JToken.FromObject(value); }
            catch { return new JValue(value.ToString()); }
        }

        /// <summary>Read a model's [VFXSetting] fields as a name -> value map.</summary>
        private static JObject ModelSettings(object model)
        {
            var result = new JObject();
            object settings;
            try
            {
                var defaultFlags = Enum.Parse(SettingFlagsType, "Default");
                settings = Call(model, ModelType, "GetSettings", false, defaultFlags);
            }
            catch { return result; }

            if (settings is IEnumerable e)
            {
                foreach (var s in e)
                {
                    try
                    {
                        var sname = Prop(s, "name") as string;
                        if (!string.IsNullOrEmpty(sname)) result[sname] = ToJToken(Prop(s, "value"));
                    }
                    catch { /* skip unreadable setting */ }
                }
            }
            return result;
        }

        private static JObject BlockSettings(object block) => ModelSettings(block);

        /// <summary>Tier-1 read-back: dump contexts + their blocks (with settings) for an asset.</summary>
        public static object DescribeGraph(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var contexts = new JArray();
            foreach (var child in Children(graph))
            {
                if (!ContextType.IsInstanceOfType(child)) continue;
                var blocks = new JArray();
                int blockIndex = 0;
                foreach (var b in Children(child))
                {
                    blocks.Add(new JObject
                    {
                        ["index"] = blockIndex++,
                        ["name"] = ModelName(b),
                        ["type"] = b.GetType().Name,
                        ["settings"] = BlockSettings(b)
                    });
                }
                string ctxType;
                try { ctxType = Prop(child, "contextType")?.ToString(); }
                catch { ctxType = "unknown"; }
                contexts.Add(new JObject
                {
                    ["contextType"] = ctxType,
                    ["type"] = child.GetType().Name,
                    ["name"] = ModelName(child),
                    ["blocks"] = blocks
                });
            }

            return new JObject
            {
                ["assetPath"] = assetPath,
                ["contextCount"] = contexts.Count,
                ["contexts"] = contexts
            };
        }

        /// <summary>Discovery oracle: list available block descriptors (name/category/type).</summary>
        public static object ListLibrary(JObject parameters)
        {
            var filter = parameters?["filter"]?.ToString();
            var descriptors = Call(null, LibraryType, "GetBlocks") as IEnumerable;
            var blocks = new JArray();
            foreach (var d in descriptors)
            {
                var name = Prop(d, "name") as string;
                var category = Prop(d, "category") as string;
                if (!string.IsNullOrEmpty(filter) &&
                    (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (category?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                    continue;
                blocks.Add(new JObject { ["name"] = name, ["category"] = category });
            }
            return new JObject { ["blockCount"] = blocks.Count, ["blocks"] = blocks };
        }

        /// <summary>Mutator. Supported ops: add_block, set_block_setting.</summary>
        public static object Apply(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            switch (op)
            {
                case "add_block": return AddBlock(parameters);
                case "set_block_setting": return SetBlockSetting(parameters);
                default: throw new Exception($"Unsupported op: '{op}'. Supported: add_block, set_block_setting");
            }
        }

        /// <summary>Find a context child by its contextType enum name (case-insensitive).</summary>
        private static object FindContext(object graph, string contextType)
        {
            foreach (var child in Children(graph))
            {
                if (!ContextType.IsInstanceOfType(child)) continue;
                if (string.Equals(Prop(child, "contextType")?.ToString(), contextType,
                        StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        /// <summary>Find a field by name, walking the type hierarchy.</summary>
        private static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, AllInstance | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Mark the graph dirty, write the asset, and reimport so it recompiles.</summary>
        private static void Persist(object graph, string assetPath)
        {
            Call(graph, GraphType, "SetExpressionGraphDirty", true);
            var resource = Prop(graph, "visualEffectResource");
            Call(null, ResourceExtType, "WriteAssetWithSubAssets", resource);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        private static object AddBlock(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var wantContext = parameters?["contextType"]?.ToString() ?? "Update";
            var blockName = parameters?["blockName"]?.ToString();
            if (string.IsNullOrEmpty(blockName))
                throw new Exception("blockName is required");

            var graph = LoadGraph(assetPath);

            var targetContext = FindContext(graph, wantContext);
            if (targetContext == null)
                throw new Exception($"No context of type '{wantContext}' found in {assetPath}");

            // Find block descriptor by name (exact, then contains).
            var descriptors = (Call(null, LibraryType, "GetBlocks") as IEnumerable).Cast<object>().ToList();
            var match = descriptors.FirstOrDefault(d =>
                            string.Equals(Prop(d, "name") as string, blockName, StringComparison.OrdinalIgnoreCase))
                        ?? descriptors.FirstOrDefault(d =>
                            ((Prop(d, "name") as string)?.IndexOf(blockName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (match == null)
                throw new Exception($"No block descriptor matching '{blockName}'. Try vfx_list_library to discover names.");

            var block = Call(match, match.GetType(), "CreateInstance");
            if (block == null)
                throw new Exception($"CreateInstance returned null for block '{blockName}'");

            // context.AddChild(block, index:-1, notify:true)
            Call(targetContext, ContextType, "AddChild", block, -1, true);

            // Optional settings.
            var settings = parameters?["settings"] as JObject;
            var applied = new JArray();
            if (settings != null)
            {
                foreach (var kv in settings)
                {
                    object val = kv.Value.Type == JTokenType.Integer ? (object)kv.Value.ToObject<int>()
                               : kv.Value.Type == JTokenType.Float ? (object)kv.Value.ToObject<float>()
                               : kv.Value.Type == JTokenType.Boolean ? (object)kv.Value.ToObject<bool>()
                               : kv.Value.ToString();
                    try { Call(block, ModelType, "SetSettingValue", kv.Key, val); applied.Add(kv.Key); }
                    catch (Exception e) { Debug.LogWarning($"[vfx_apply] setting '{kv.Key}' failed: {e.Message}"); }
                }
            }

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["addedBlock"] = block.GetType().Name,
                ["matchedDescriptor"] = Prop(match, "name") as string,
                ["settingsApplied"] = applied
            };
        }

        private static object SetBlockSetting(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var wantContext = parameters?["contextType"]?.ToString() ?? "Update";
            var settingName = parameters?["setting"]?.ToString();
            if (string.IsNullOrEmpty(settingName))
                throw new Exception("setting is required");
            var valueToken = parameters?["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                throw new Exception("value is required");
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;

            var graph = LoadGraph(assetPath);

            var targetContext = FindContext(graph, wantContext);
            if (targetContext == null)
                throw new Exception($"No context of type '{wantContext}' found in {assetPath}");

            var blocks = Children(targetContext).ToList();
            if (blockIndex < 0 || blockIndex >= blocks.Count)
                throw new Exception(
                    $"blockIndex {blockIndex} out of range; context '{wantContext}' has {blocks.Count} block(s)");
            var block = blocks[blockIndex];

            var field = FindField(block.GetType(), settingName);
            if (field == null)
                throw new Exception(
                    $"Setting '{settingName}' not found on block '{block.GetType().Name}'. Use vfx_describe_graph to list settings.");

            object converted;
            try { converted = valueToken.ToObject(field.FieldType); }
            catch (Exception e)
            {
                throw new Exception(
                    $"Cannot convert value to {field.FieldType.Name} for setting '{settingName}': {e.Message}");
            }

            Call(block, ModelType, "SetSettingValue", settingName, converted);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_block_setting",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["blockIndex"] = blockIndex,
                ["block"] = block.GetType().Name,
                ["setting"] = settingName,
                ["value"] = ToJToken(converted)
            };
        }
    }
}
