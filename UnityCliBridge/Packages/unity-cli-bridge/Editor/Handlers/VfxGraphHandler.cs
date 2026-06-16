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

        /// <summary>Tier-1 read-back: dump contexts + their blocks for an asset.</summary>
        public static object DescribeGraph(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var contexts = new JArray();
            foreach (var child in Children(graph))
            {
                if (!ContextType.IsInstanceOfType(child)) continue;
                var blocks = new JArray();
                foreach (var b in Children(child))
                {
                    blocks.Add(new JObject
                    {
                        ["name"] = ModelName(b),
                        ["type"] = b.GetType().Name
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

        /// <summary>Mutator. Supported op: add_block.</summary>
        public static object Apply(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            switch (op)
            {
                case "add_block": return AddBlock(parameters);
                default: throw new Exception($"Unsupported op: '{op}'. Supported: add_block");
            }
        }

        private static object AddBlock(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var wantContext = parameters?["contextType"]?.ToString() ?? "Update";
            var blockName = parameters?["blockName"]?.ToString();
            if (string.IsNullOrEmpty(blockName))
                throw new Exception("blockName is required");

            var graph = LoadGraph(assetPath);

            // Find target context by contextType (enum ToString match, case-insensitive).
            object targetContext = null;
            foreach (var child in Children(graph))
            {
                if (!ContextType.IsInstanceOfType(child)) continue;
                var ct = Prop(child, "contextType")?.ToString();
                if (string.Equals(ct, wantContext, StringComparison.OrdinalIgnoreCase))
                {
                    targetContext = child;
                    break;
                }
            }
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

            // Persist + recompile.
            Call(graph, GraphType, "SetExpressionGraphDirty", true);
            var resource = Prop(graph, "visualEffectResource");
            Call(null, ResourceExtType, "WriteAssetWithSubAssets", resource);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

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
    }
}
