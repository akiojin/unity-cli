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
        private static Type OperatorType => T("UnityEditor.VFX.VFXOperator");
        private static Type ParameterType => T("UnityEditor.VFX.VFXParameter");
        private static Type SlotType => T("UnityEditor.VFX.VFXSlot");
        private static Type LibraryType => T("UnityEditor.VFX.VFXLibrary");
        private static Type VisualEffectType => T("UnityEngine.VFX.VisualEffect");
        private static Type VisualEffectAssetType => T("UnityEngine.VFX.VisualEffectAsset");

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

        private static void SetProp(object target, string name, object value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, AllInstance | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
            }
            throw new Exception($"Writable property not found: {target.GetType().Name}.{name}");
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

        /// <summary>Resolve a context's flow links (input/output contexts) to graph indices.</summary>
        private static JArray FlowRefs(object ctx, string propName, List<object> ctxList)
        {
            var refs = new JArray();
            object linked;
            try { linked = Prop(ctx, propName); }
            catch { return refs; }
            if (linked is IEnumerable e)
            {
                foreach (var other in e)
                {
                    string t;
                    try { t = Prop(other, "contextType")?.ToString(); }
                    catch { t = "unknown"; }
                    refs.Add(new JObject { ["index"] = ctxList.IndexOf(other), ["contextType"] = t });
                }
            }
            return refs;
        }

        /// <summary>A slot's exposed property name (VFXSlot.property.name is a struct field).</summary>
        private static string SlotName(object slot)
        {
            try
            {
                var property = Prop(slot, "property");
                var nameField = property.GetType().GetField("name");
                return nameField?.GetValue(property) as string;
            }
            catch { return null; }
        }

        /// <summary>Tier-1 read-back: contexts (flow links + blocks + slots) and operators with slot links.</summary>
        public static object DescribeGraph(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            // Collect contexts and operators first so links can be resolved to stable indices.
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var opList = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            var paramList = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();

            // Resolve any slot-owning model to a stable address within this describe pass.
            JObject ResolveAddress(object container)
            {
                if (container != null)
                {
                    for (int ci = 0; ci < ctxList.Count; ci++)
                    {
                        if (ReferenceEquals(ctxList[ci], container))
                            return new JObject { ["kind"] = "context", ["contextIndex"] = ci };
                        int bi = 0;
                        foreach (var b in Children(ctxList[ci]))
                        {
                            if (ReferenceEquals(b, container))
                                return new JObject { ["kind"] = "block", ["contextIndex"] = ci, ["blockIndex"] = bi };
                            bi++;
                        }
                    }
                    for (int oi = 0; oi < opList.Count; oi++)
                        if (ReferenceEquals(opList[oi], container))
                            return new JObject { ["kind"] = "operator", ["operatorIndex"] = oi };
                    for (int pi = 0; pi < paramList.Count; pi++)
                        if (ReferenceEquals(paramList[pi], container))
                            return new JObject { ["kind"] = "parameter", ["parameterIndex"] = pi };
                }
                return new JObject { ["kind"] = "unknown" };
            }

            // (slotIndex, isInput) for a slot within its owner's top-level slot collection.
            int SlotIndexIn(object slot)
            {
                try
                {
                    var owner = Prop(slot, "owner");
                    if (owner == null) return -1;
                    bool isOutput = Prop(slot, "direction")?.ToString() == "kOutput";
                    var coll = Prop(owner, isOutput ? "outputSlots" : "inputSlots") as IEnumerable;
                    int idx = 0;
                    if (coll != null)
                        foreach (var s in coll) { if (ReferenceEquals(s, slot)) return idx; idx++; }
                }
                catch { }
                return -1;
            }

            JArray LinksJson(object slot)
            {
                var arr = new JArray();
                IEnumerable linked;
                try { linked = Prop(slot, "LinkedSlots") as IEnumerable; }
                catch { return arr; }
                if (linked == null) return arr;
                foreach (var other in linked)
                {
                    object owner = null;
                    try { owner = Prop(other, "owner"); }
                    catch { }
                    arr.Add(new JObject
                    {
                        ["node"] = ResolveAddress(owner),
                        ["slot"] = SlotIndexIn(other),
                        ["name"] = SlotName(other)
                    });
                }
                return arr;
            }

            JArray SlotsJson(object container, bool isInput)
            {
                var arr = new JArray();
                IEnumerable coll;
                try { coll = Prop(container, isInput ? "inputSlots" : "outputSlots") as IEnumerable; }
                catch { return arr; }
                if (coll == null) return arr;
                int idx = 0;
                foreach (var slot in coll)
                {
                    var links = LinksJson(slot);
                    arr.Add(new JObject
                    {
                        ["index"] = idx++,
                        ["name"] = SlotName(slot),
                        ["hasLink"] = links.Count > 0,
                        ["links"] = links
                    });
                }
                return arr;
            }

            var contexts = new JArray();
            for (int i = 0; i < ctxList.Count; i++)
            {
                var ctx = ctxList[i];
                var blocks = new JArray();
                int blockIndex = 0;
                foreach (var b in Children(ctx))
                {
                    blocks.Add(new JObject
                    {
                        ["index"] = blockIndex++,
                        ["name"] = ModelName(b),
                        ["type"] = b.GetType().Name,
                        ["settings"] = BlockSettings(b),
                        ["inputSlots"] = SlotsJson(b, true),
                        ["outputSlots"] = SlotsJson(b, false)
                    });
                }
                string ctxType;
                try { ctxType = Prop(ctx, "contextType")?.ToString(); }
                catch { ctxType = "unknown"; }
                contexts.Add(new JObject
                {
                    ["index"] = i,
                    ["contextType"] = ctxType,
                    ["type"] = ctx.GetType().Name,
                    ["name"] = ModelName(ctx),
                    ["settings"] = ModelSettings(ctx),
                    ["inputs"] = FlowRefs(ctx, "inputContexts", ctxList),
                    ["outputs"] = FlowRefs(ctx, "outputContexts", ctxList),
                    ["blocks"] = blocks
                });
            }

            var operators = new JArray();
            for (int i = 0; i < opList.Count; i++)
            {
                var op = opList[i];
                operators.Add(new JObject
                {
                    ["index"] = i,
                    ["type"] = op.GetType().Name,
                    ["name"] = ModelName(op),
                    ["inputSlots"] = SlotsJson(op, true),
                    ["outputSlots"] = SlotsJson(op, false)
                });
            }

            var paramsJson = new JArray();
            for (int i = 0; i < paramList.Count; i++)
            {
                var p = paramList[i];
                string exposedName = null, category = null, tooltip = null;
                bool exposed = false;
                JToken value = null;
                try { exposedName = Prop(p, "exposedName") as string; } catch { }
                try { exposed = (bool)Prop(p, "exposed"); } catch { }
                try { category = Prop(p, "category") as string; } catch { }
                try { tooltip = Prop(p, "tooltip") as string; } catch { }
                try { value = ToJToken(Prop(p, "value")); } catch { }
                paramsJson.Add(new JObject
                {
                    ["index"] = i,
                    ["type"] = p.GetType().Name,
                    ["parameterType"] = (Prop(p, "type") as Type)?.Name,
                    ["exposedName"] = exposedName,
                    ["exposed"] = exposed,
                    ["category"] = category,
                    ["tooltip"] = tooltip,
                    ["value"] = value,
                    ["inputSlots"] = SlotsJson(p, true),
                    ["outputSlots"] = SlotsJson(p, false)
                });
            }

            return new JObject
            {
                ["assetPath"] = assetPath,
                ["contextCount"] = contexts.Count,
                ["contexts"] = contexts,
                ["operatorCount"] = operators.Count,
                ["operators"] = operators,
                ["parameterCount"] = paramsJson.Count,
                ["parameters"] = paramsJson
            };
        }

        /// <summary>Discovery oracle: list available descriptors. kind = block (default)|operator|context.</summary>
        public static object ListLibrary(JObject parameters)
        {
            var filter = parameters?["filter"]?.ToString();
            var kind = parameters?["kind"]?.ToString()?.ToLowerInvariant() ?? "block";
            string discovery = kind switch
            {
                "operator" => "GetOperators",
                "context" => "GetContexts",
                "block" => "GetBlocks",
                "parameter" => "GetParameters",
                _ => throw new Exception($"Unknown kind '{kind}'. Supported: block, operator, context, parameter")
            };
            var descriptors = Call(null, LibraryType, discovery) as IEnumerable;
            var items = new JArray();
            foreach (var d in descriptors)
            {
                var name = Prop(d, "name") as string;
                var category = Prop(d, "category") as string;
                if (!string.IsNullOrEmpty(filter) &&
                    (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (category?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                    continue;
                items.Add(new JObject { ["name"] = name, ["category"] = category });
            }
            return new JObject { ["kind"] = kind, ["count"] = items.Count, ["items"] = items };
        }

        /// <summary>Mutator. Supported ops: add_block, set_block_setting, add_context, add_operator, add_parameter, link_slots, link_flow.</summary>
        public static object Apply(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            switch (op)
            {
                case "add_block": return AddBlock(parameters);
                case "set_block_setting": return SetBlockSetting(parameters);
                case "add_context": return AddContext(parameters);
                case "add_operator": return AddOperator(parameters);
                case "add_parameter": return AddParameter(parameters);
                case "link_slots": return LinkSlots(parameters);
                case "link_flow": return LinkFlow(parameters);
                default:
                    throw new Exception(
                        $"Unsupported op: '{op}'. Supported: add_block, set_block_setting, add_context, add_operator, add_parameter, link_slots, link_flow");
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

        /// <summary>Apply a name->value settings map to a model, coercing each value to its field type.</summary>
        private static JArray ApplySettings(object model, JObject settings)
        {
            var applied = new JArray();
            if (settings == null) return applied;
            foreach (var kv in settings)
            {
                var field = FindField(model.GetType(), kv.Key);
                if (field == null)
                    throw new Exception(
                        $"Setting '{kv.Key}' not found on '{model.GetType().Name}'. Use vfx_describe_graph to list settings.");
                object converted;
                try { converted = kv.Value.ToObject(field.FieldType); }
                catch (Exception e)
                {
                    throw new Exception(
                        $"Cannot convert value to {field.FieldType.Name} for setting '{kv.Key}': {e.Message}");
                }
                Call(model, ModelType, "SetSettingValue", kv.Key, converted);
                applied.Add(kv.Key);
            }
            return applied;
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

        private static object AddContext(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var contextName = parameters?["contextName"]?.ToString();
            if (string.IsNullOrEmpty(contextName))
                throw new Exception("contextName is required");
            var linkFrom = parameters?["linkFrom"]?.ToString();

            var graph = LoadGraph(assetPath);

            // Find context descriptor by name (exact, then contains).
            var descriptors = (Call(null, LibraryType, "GetContexts") as IEnumerable).Cast<object>().ToList();
            var match = descriptors.FirstOrDefault(d =>
                            string.Equals(Prop(d, "name") as string, contextName, StringComparison.OrdinalIgnoreCase))
                        ?? descriptors.FirstOrDefault(d =>
                            ((Prop(d, "name") as string)?.IndexOf(contextName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (match == null)
            {
                var available = string.Join(", ", descriptors
                    .Select(d => Prop(d, "name") as string)
                    .Where(n => !string.IsNullOrEmpty(n)).Distinct());
                throw new Exception($"No context descriptor matching '{contextName}'. Available: {available}");
            }

            var context = Call(match, match.GetType(), "CreateInstance");
            if (context == null)
                throw new Exception($"CreateInstance returned null for context '{contextName}'");

            Call(graph, ModelType, "AddChild", context, -1, true);

            // Optional context settings (e.g. an Event context's eventName).
            var appliedSettings = ApplySettings(context, parameters?["settings"] as JObject);

            // Optional flow link: an existing context (by contextType) flows INTO the new one.
            JObject linked = null;
            if (!string.IsNullOrEmpty(linkFrom))
            {
                var fromContext = FindContext(graph, linkFrom);
                if (fromContext == null)
                    throw new Exception($"linkFrom context '{linkFrom}' not found in {assetPath}");
                int fromIndex = parameters?["fromIndex"]?.ToObject<int>() ?? 0;
                int toIndex = parameters?["toIndex"]?.ToObject<int>() ?? 0;
                Call(fromContext, ContextType, "LinkTo", context, fromIndex, toIndex);
                linked = new JObject
                {
                    ["from"] = linkFrom,
                    ["fromIndex"] = fromIndex,
                    ["toIndex"] = toIndex
                };
            }

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = assetPath,
                ["addedContext"] = context.GetType().Name,
                ["matchedDescriptor"] = Prop(match, "name") as string,
                ["settingsApplied"] = appliedSettings,
                ["linked"] = linked
            };
        }

        private static object AddOperator(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var operatorName = parameters?["operatorName"]?.ToString();
            if (string.IsNullOrEmpty(operatorName))
                throw new Exception("operatorName is required");

            var graph = LoadGraph(assetPath);

            // Find operator descriptor by name (exact, then contains).
            var descriptors = (Call(null, LibraryType, "GetOperators") as IEnumerable).Cast<object>().ToList();
            var match = descriptors.FirstOrDefault(d =>
                            string.Equals(Prop(d, "name") as string, operatorName, StringComparison.OrdinalIgnoreCase))
                        ?? descriptors.FirstOrDefault(d =>
                            ((Prop(d, "name") as string)?.IndexOf(operatorName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (match == null)
                throw new Exception(
                    $"No operator descriptor matching '{operatorName}'. Use vfx_list_library with kind 'operator' to discover names.");

            var op = Call(match, match.GetType(), "CreateInstance");
            if (op == null)
                throw new Exception($"CreateInstance returned null for operator '{operatorName}'");

            Call(graph, ModelType, "AddChild", op, -1, true);

            Persist(graph, assetPath);

            int operatorIndex = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList()
                .FindIndex(o => ReferenceEquals(o, op));

            return new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = assetPath,
                ["addedOperator"] = op.GetType().Name,
                ["matchedDescriptor"] = Prop(match, "name") as string,
                ["operatorIndex"] = operatorIndex
            };
        }

        private static object AddParameter(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var parameterName = parameters?["parameterName"]?.ToString();
            if (string.IsNullOrEmpty(parameterName))
                throw new Exception("parameterName is required");
            var typeName = parameters?["type"]?.ToString();
            if (string.IsNullOrEmpty(typeName))
                throw new Exception("type is required (e.g. Float, Int, Vector3, Color). Use vfx_list_library with kind 'parameter'.");
            bool exposed = parameters?["exposed"]?.ToObject<bool>() ?? true;

            var graph = LoadGraph(assetPath);

            // Find parameter descriptor by type name (exact, then contains).
            var descriptors = (Call(null, LibraryType, "GetParameters") as IEnumerable).Cast<object>().ToList();
            var match = descriptors.FirstOrDefault(d =>
                            string.Equals(Prop(d, "name") as string, typeName, StringComparison.OrdinalIgnoreCase))
                        ?? descriptors.FirstOrDefault(d =>
                            ((Prop(d, "name") as string)?.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (match == null)
            {
                var available = string.Join(", ", descriptors
                    .Select(d => Prop(d, "name") as string)
                    .Where(n => !string.IsNullOrEmpty(n)).Distinct());
                throw new Exception($"No parameter type matching '{typeName}'. Available: {available}");
            }

            var parameter = Call(match, match.GetType(), "CreateInstance");
            if (parameter == null)
                throw new Exception($"CreateInstance returned null for parameter type '{typeName}'");

            Call(graph, ModelType, "AddChild", parameter, -1, true);

            // exposedName + exposed are [VFXSetting] backing fields.
            Call(parameter, ModelType, "SetSettingValue", "m_ExposedName", parameterName);
            Call(parameter, ModelType, "SetSettingValue", "m_Exposed", exposed);

            // Optional default value (set on the parameter's value slot, coerced to its type).
            var valueToken = parameters?["value"];
            JToken appliedValue = null;
            if (valueToken != null && valueToken.Type != JTokenType.Null)
            {
                var paramType = Prop(parameter, "type") as Type;
                object converted = valueToken.ToObject(paramType);
                SetProp(parameter, "value", converted);
                appliedValue = ToJToken(converted);
            }

            var tooltip = parameters?["tooltip"]?.ToString();
            if (!string.IsNullOrEmpty(tooltip)) SetProp(parameter, "tooltip", tooltip);
            var category = parameters?["category"]?.ToString();
            if (!string.IsNullOrEmpty(category)) SetProp(parameter, "category", category);

            Persist(graph, assetPath);

            int parameterIndex = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList()
                .FindIndex(p => ReferenceEquals(p, parameter));

            return new JObject
            {
                ["op"] = "add_parameter",
                ["assetPath"] = assetPath,
                ["parameterName"] = parameterName,
                ["parameterType"] = (Prop(parameter, "type") as Type)?.Name,
                ["matchedDescriptor"] = Prop(match, "name") as string,
                ["exposed"] = exposed,
                ["value"] = appliedValue,
                ["parameterIndex"] = parameterIndex
            };
        }

        /// <summary>Resolve a node address (operator/context/block) to its model object.</summary>
        private static object ResolveNode(object graph, JObject node, string label)
        {
            if (node == null)
                throw new Exception($"{label} is required (an object with 'node' = operator|context|block)");
            var kind = node["node"]?.ToString();
            switch (kind)
            {
                case "operator":
                {
                    int idx = node["operatorIndex"]?.ToObject<int>() ?? 0;
                    var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
                    if (idx < 0 || idx >= ops.Count)
                        throw new Exception($"{label} operatorIndex {idx} out of range; graph has {ops.Count} operator(s)");
                    return ops[idx];
                }
                case "parameter":
                {
                    int idx = node["parameterIndex"]?.ToObject<int>() ?? 0;
                    var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();
                    if (idx < 0 || idx >= ps.Count)
                        throw new Exception($"{label} parameterIndex {idx} out of range; graph has {ps.Count} parameter(s)");
                    return ps[idx];
                }
                case "context":
                {
                    var ct = node["contextType"]?.ToString();
                    var ctx = FindContext(graph, ct);
                    if (ctx == null)
                        throw new Exception($"{label} context of type '{ct}' not found");
                    return ctx;
                }
                case "block":
                {
                    var ct = node["contextType"]?.ToString();
                    var ctx = FindContext(graph, ct);
                    if (ctx == null)
                        throw new Exception($"{label} context of type '{ct}' not found");
                    int bi = node["blockIndex"]?.ToObject<int>() ?? 0;
                    var blocks = Children(ctx).ToList();
                    if (bi < 0 || bi >= blocks.Count)
                        throw new Exception($"{label} blockIndex {bi} out of range; context '{ct}' has {blocks.Count} block(s)");
                    return blocks[bi];
                }
                default:
                    throw new Exception($"{label} has unknown node kind '{kind}'. Supported: operator, parameter, context, block");
            }
        }

        /// <summary>Get a top-level input/output slot of a slot container by index.</summary>
        private static object GetSlot(object container, bool isInput, int index, string label)
        {
            var coll = (Prop(container, isInput ? "inputSlots" : "outputSlots") as IEnumerable)?.Cast<object>().ToList()
                       ?? new List<object>();
            if (index < 0 || index >= coll.Count)
                throw new Exception(
                    $"{label} {(isInput ? "input" : "output")} slot index {index} out of range; {container.GetType().Name} has {coll.Count}");
            return coll[index];
        }

        private static object LinkSlots(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var from = parameters?["from"] as JObject;
            var to = parameters?["to"] as JObject;
            if (from == null) throw new Exception("from is required");
            if (to == null) throw new Exception("to is required");

            var graph = LoadGraph(assetPath);

            var fromNode = ResolveNode(graph, from, "from");
            var toNode = ResolveNode(graph, to, "to");
            int fromSlot = from["slot"]?.ToObject<int>() ?? 0;
            int toSlot = to["slot"]?.ToObject<int>() ?? 0;

            var outSlot = GetSlot(fromNode, false, fromSlot, "from");
            var inSlot = GetSlot(toNode, true, toSlot, "to");

            bool ok = (bool)Call(outSlot, SlotType, "Link", inSlot, true);
            if (!ok)
                throw new Exception(
                    "Link rejected: output slot type is incompatible with the input slot (or directions are wrong). " +
                    "'from' must reference an output slot, 'to' an input slot.");

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "link_slots",
                ["assetPath"] = assetPath,
                ["from"] = new JObject
                {
                    ["node"] = fromNode.GetType().Name,
                    ["slot"] = fromSlot,
                    ["slotName"] = SlotName(outSlot)
                },
                ["to"] = new JObject
                {
                    ["node"] = toNode.GetType().Name,
                    ["slot"] = toSlot,
                    ["slotName"] = SlotName(inSlot)
                }
            };
        }

        /// <summary>Resolve a flow endpoint ({index} into the context list, or {contextType}) to a context.</summary>
        private static object ResolveContextRef(object graph, JObject endpoint, List<object> ctxList, string label)
        {
            if (endpoint == null)
                throw new Exception($"{label} is required (an object with 'index' or 'contextType')");
            var idxTok = endpoint["index"];
            if (idxTok != null && idxTok.Type != JTokenType.Null)
            {
                int idx = idxTok.ToObject<int>();
                if (idx < 0 || idx >= ctxList.Count)
                    throw new Exception($"{label} index {idx} out of range; graph has {ctxList.Count} context(s)");
                return ctxList[idx];
            }
            var ct = endpoint["contextType"]?.ToString();
            var ctx = FindContext(graph, ct);
            if (ctx == null)
                throw new Exception($"{label} context of type '{ct}' not found (or use 'index')");
            return ctx;
        }

        /// <summary>Flow-link one context's output into another context's input (VFXContext.LinkTo).</summary>
        private static object LinkFlow(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var from = parameters?["from"] as JObject;
            var to = parameters?["to"] as JObject;
            if (from == null) throw new Exception("from is required (the source context)");
            if (to == null) throw new Exception("to is required (the target context)");

            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();

            var fromCtx = ResolveContextRef(graph, from, ctxList, "from");
            var toCtx = ResolveContextRef(graph, to, ctxList, "to");
            int fromIndex = parameters?["fromIndex"]?.ToObject<int>() ?? 0;
            int toIndex = parameters?["toIndex"]?.ToObject<int>() ?? 0;

            // LinkTo throws (via CanLink) on incompatible flow.
            Call(fromCtx, ContextType, "LinkTo", toCtx, fromIndex, toIndex);

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "link_flow",
                ["assetPath"] = assetPath,
                ["from"] = new JObject
                {
                    ["contextType"] = Prop(fromCtx, "contextType")?.ToString(),
                    ["type"] = fromCtx.GetType().Name,
                    ["fromIndex"] = fromIndex
                },
                ["to"] = new JObject
                {
                    ["contextType"] = Prop(toCtx, "contextType")?.ToString(),
                    ["type"] = toCtx.GetType().Name,
                    ["toIndex"] = toIndex
                }
            };
        }

        // ---- Runtime control (public UnityEngine.VFX.VisualEffect API) -------

        /// <summary>Find an active VisualEffect component on a named GameObject.</summary>
        private static object FindVisualEffect(string gameObject)
        {
            if (string.IsNullOrEmpty(gameObject))
                throw new Exception("gameObject is required (name of a scene object with a VisualEffect)");
            var go = GameObject.Find(gameObject);
            if (go == null)
                throw new Exception($"GameObject '{gameObject}' not found in the active scene");
            var comp = go.GetComponent(VisualEffectType);
            if (comp == null)
                throw new Exception($"GameObject '{gameObject}' has no VisualEffect component");
            return comp;
        }

        private static object ToVector(JToken token, int n)
        {
            var arr = token as JArray;
            if (arr == null || arr.Count < n)
                throw new Exception($"value must be an array of {n} numbers");
            switch (n)
            {
                case 2: return new Vector2(arr[0].ToObject<float>(), arr[1].ToObject<float>());
                case 3: return new Vector3(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>());
                default: return new Vector4(arr[0].ToObject<float>(), arr[1].ToObject<float>(),
                    arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
        }

        /// <summary>
        /// Runtime control of a VisualEffect component via its public API. Ops:
        /// set_asset, set_float, set_int, set_bool, set_vector2/3/4, send_event, reinit, get_state.
        /// </summary>
        public static object Runtime(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            var gameObject = parameters?["gameObject"]?.ToString();

            if (op == "set_asset")
            {
                var assetPath = parameters?["assetPath"]?.ToString();
                if (string.IsNullOrEmpty(assetPath)) throw new Exception("assetPath is required");
                var comp = FindVisualEffect(gameObject);
                var asset = AssetDatabase.LoadAssetAtPath(assetPath, VisualEffectAssetType);
                if (asset == null) throw new Exception($"No VisualEffectAsset at path: {assetPath}");
                SetProp(comp, "visualEffectAsset", asset);
                Call(comp, VisualEffectType, "Reinit");
                return new JObject
                {
                    ["op"] = op, ["gameObject"] = gameObject, ["assetPath"] = assetPath,
                    ["asset"] = (asset as UnityEngine.Object)?.name
                };
            }

            var comp2 = FindVisualEffect(gameObject);
            var name = parameters?["name"]?.ToString();
            var valueToken = parameters?["value"];

            switch (op)
            {
                case "set_float":
                    Call(comp2, VisualEffectType, "SetFloat", name, valueToken.ToObject<float>());
                    break;
                case "set_int":
                    Call(comp2, VisualEffectType, "SetInt", name, valueToken.ToObject<int>());
                    break;
                case "set_bool":
                    Call(comp2, VisualEffectType, "SetBool", name, valueToken.ToObject<bool>());
                    break;
                case "set_vector2":
                    Call(comp2, VisualEffectType, "SetVector2", name, ToVector(valueToken, 2));
                    break;
                case "set_vector3":
                    Call(comp2, VisualEffectType, "SetVector3", name, ToVector(valueToken, 3));
                    break;
                case "set_vector4":
                    Call(comp2, VisualEffectType, "SetVector4", name, ToVector(valueToken, 4));
                    break;
                case "send_event":
                {
                    var eventName = parameters?["eventName"]?.ToString();
                    if (string.IsNullOrEmpty(eventName)) throw new Exception("eventName is required");
                    Call(comp2, VisualEffectType, "SendEvent", eventName);
                    return new JObject { ["op"] = op, ["gameObject"] = gameObject, ["eventName"] = eventName };
                }
                case "reinit":
                    Call(comp2, VisualEffectType, "Reinit");
                    return new JObject { ["op"] = op, ["gameObject"] = gameObject };
                case "get_state":
                    return RuntimeState(comp2, gameObject, name);
                default:
                    throw new Exception(
                        $"Unsupported runtime op: '{op}'. Supported: set_asset, set_float, set_int, set_bool, " +
                        "set_vector2, set_vector3, set_vector4, send_event, reinit, get_state");
            }

            // Echo the new value back via get_state so the caller can verify the round-trip.
            var state = RuntimeState(comp2, gameObject, name);
            state["op"] = op;
            return state;
        }

        private static JObject RuntimeState(object comp, string gameObject, string name)
        {
            var asset = Prop(comp, "visualEffectAsset");
            var state = new JObject
            {
                ["op"] = "get_state",
                ["gameObject"] = gameObject,
                ["hasAsset"] = asset != null,
                ["asset"] = (asset as UnityEngine.Object)?.name
            };
            try { state["aliveParticleCount"] = (int)Prop(comp, "aliveParticleCount"); } catch { }
            try { state["pause"] = (bool)Prop(comp, "pause"); } catch { }
            try { state["playRate"] = (float)Prop(comp, "playRate"); } catch { }

            if (!string.IsNullOrEmpty(name))
            {
                state["name"] = name;
                try { state["hasFloat"] = (bool)Call(comp, VisualEffectType, "HasFloat", name); } catch { }
                if (state.Value<bool?>("hasFloat") == true)
                    try { state["floatValue"] = (float)Call(comp, VisualEffectType, "GetFloat", name); } catch { }
            }
            return state;
        }
    }
}
