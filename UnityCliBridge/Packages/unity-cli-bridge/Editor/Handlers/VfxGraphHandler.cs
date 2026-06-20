using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handler for driving Unity VFX Graph authoring via reflection over the
    /// internal UnityEditor.VFX model API (the package exposes no public authoring API),
    /// plus runtime control of a VisualEffect via its public UnityEngine.VFX API.
    /// Commands: vfx_describe_graph (Tier-1 read-back oracle), vfx_list_library
    /// (discovery), vfx_apply (authoring mutator), vfx_runtime (runtime control).
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
        private static Type UIInfoType => T("UnityEditor.VFX.VFXUI+UIInfo");
        private static Type StickyNoteInfoType => T("UnityEditor.VFX.VFXUI+StickyNoteInfo");
        private static Type ErrorReporterType => T("UnityEditor.VFX.VFXErrorReporter");
        private static Type ErrorOriginType => T("UnityEditor.VFX.VFXErrorOrigin");
        private static Type AssetEditorUtilityType => T("UnityEditor.VisualEffectAssetEditorUtility");
        private static Type VFXManagerType => T("UnityEngine.VFX.VFXManager");
        private static Type VFXViewPreferenceType => T("UnityEditor.VFX.VFXViewPreference");

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
            // UnityEngine.Object references (incl. asset references like a Subgraph) — Newtonsoft
            // would recurse through GameObject/Transform; emit a stable identifier instead.
            if (value is UnityEngine.Object uo)
            {
                if (uo == null) return JValue.CreateNull(); // "fake null" pattern
                return new JObject
                {
                    ["type"] = uo.GetType().Name,
                    ["name"] = uo.name,
                    ["assetPath"] = AssetDatabase.GetAssetPath(uo)
                };
            }
            var t = value.GetType();
            if (t.IsEnum) return new JValue(value.ToString());
            // Unity vector/color structs trip Newtonsoft's reflection serializer (Vector3.normalized
            // recurses). Hand-serialize the math types and any VFX struct by public fields.
            if (t == typeof(Vector2)) { var v = (Vector2)value; return new JObject { ["x"] = v.x, ["y"] = v.y }; }
            if (t == typeof(Vector3)) { var v = (Vector3)value; return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z }; }
            if (t == typeof(Vector4)) { var v = (Vector4)value; return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z, ["w"] = v.w }; }
            if (t == typeof(Color)) { var c = (Color)value; return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a }; }
            if (t == typeof(Rect)) { var r = (Rect)value; return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height }; }
            if (t.IsValueType && !t.IsPrimitive && t.Namespace != null &&
                (t.Namespace.StartsWith("UnityEditor.VFX") || t.Namespace.StartsWith("UnityEngine.VFX")))
            {
                var obj = new JObject();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    obj[f.Name] = ToJToken(f.GetValue(value));
                return obj;
            }
            try { return JToken.FromObject(value); }
            catch { return new JValue(value.ToString()); }
        }

        /// <summary>Read a model's [VFXSetting] fields as a name -> value map.</summary>
        private static JObject ModelSettings(object model)
        {
            var result = new JObject();
            // listHidden=true bypasses the visible-flags mask so the oracle surfaces every
            // [VFXSetting] field — including ReadOnly fields like CustomHLSL.m_HLSLCode that
            // would be filtered by the Default mask (which requires InGeneratedCodeComments).
            object settings;
            try
            {
                var defaultFlags = Enum.Parse(SettingFlagsType, "Default");
                settings = Call(model, ModelType, "GetSettings", true, defaultFlags);
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

        /// <summary>Log an error and return it as a { error } result.</summary>
        private static object Fail(string command, Exception ex)
        {
            BridgeLogger.LogError("VfxGraphHandler", $"Error in {command}: {ex.Message}");
            return new { error = ex.Message };
        }

        /// <summary>Tier-1 read-back: contexts (flow links + blocks + slots) and operators with slot links.</summary>
        public static object DescribeGraph(JObject parameters)
        {
            try { return DescribeGraphCore(parameters); }
            catch (Exception ex) { return Fail("vfx_describe_graph", ex); }
        }

        private static object DescribeGraphCore(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(assetPath))
                return new { error = "assetPath is required" };
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
                    JToken value = null;
                    try { value = ToJToken(Prop(slot, "value")); }
                    catch { /* some slot types may not have a readable value */ }
                    arr.Add(new JObject
                    {
                        ["index"] = idx++,
                        ["name"] = SlotName(slot),
                        ["hasLink"] = links.Count > 0,
                        ["links"] = links,
                        ["value"] = value
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
                    bool blockEnabled = true;
                    try { blockEnabled = (bool)Prop(b, "enabled"); } catch { }
                    blocks.Add(new JObject
                    {
                        ["index"] = blockIndex++,
                        ["name"] = ModelName(b),
                        ["type"] = b.GetType().Name,
                        ["enabled"] = blockEnabled,
                        ["settings"] = BlockSettings(b),
                        ["inputSlots"] = SlotsJson(b, true),
                        ["outputSlots"] = SlotsJson(b, false)
                    });
                }
                string ctxType;
                try { ctxType = Prop(ctx, "contextType")?.ToString(); }
                catch { ctxType = "unknown"; }
                // dataInstanceId — identity of the context's VFXData. Contexts in the same
                // particle system share one VFXData (auto-wired by VFXContext.LinkTo), so equal
                // ids prove system membership; different ids prove disjoint systems.
                int? dataId = null;
                // simulationSpace — Local/World on the context's particle data. m_Space is a private
                // non-[VFXSetting] field (so it doesn't surface in `settings`), but VFXDataParticle
                // exposes a public `space` property. Spawn/Event data has no space → leave null.
                string simSpace = null;
                try
                {
                    var data = Call(ctx, ContextType, "GetData");
                    if (data is UnityEngine.Object uo) dataId = uo.GetInstanceID();
                    if (data != null)
                    {
                        try { simSpace = Prop(data, "space")?.ToString(); }
                        catch { /* data without a space property */ }
                    }
                }
                catch { /* contexts without data (Spawn/Event) — leave null */ }

                contexts.Add(new JObject
                {
                    ["index"] = i,
                    ["contextType"] = ctxType,
                    ["type"] = ctx.GetType().Name,
                    ["name"] = ModelName(ctx),
                    ["settings"] = ModelSettings(ctx),
                    ["inputs"] = FlowRefs(ctx, "inputContexts", ctxList),
                    ["outputs"] = FlowRefs(ctx, "outputContexts", ctxList),
                    ["inputSlots"] = SlotsJson(ctx, true),
                    ["outputSlots"] = SlotsJson(ctx, false),
                    ["dataInstanceId"] = dataId,
                    ["simulationSpace"] = simSpace,
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
                    ["settings"] = ModelSettings(op),
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
                JToken value = null, min = null, max = null, valueFilter = null;
                try { exposedName = Prop(p, "exposedName") as string; } catch { }
                try { exposed = (bool)Prop(p, "exposed"); } catch { }
                try { category = Prop(p, "category") as string; } catch { }
                try { tooltip = Prop(p, "tooltip") as string; } catch { }
                try { value = ToJToken(Prop(p, "value")); } catch { }
                try { min = ToJToken(Prop(p, "min")); } catch { }
                try { max = ToJToken(Prop(p, "max")); } catch { }
                try { valueFilter = new JValue(Prop(p, "valueFilter")?.ToString()); } catch { }
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
                    ["valueFilter"] = valueFilter,
                    ["min"] = min,
                    ["max"] = max,
                    ["inputSlots"] = SlotsJson(p, true),
                    ["outputSlots"] = SlotsJson(p, false)
                });
            }

            var stickyNotes = StickyNotesJson(graph);
            JObject instancing = null;
            try { instancing = InstancingJson(Prop(graph, "visualEffectResource")); }
            catch { /* resource unavailable — leave null */ }

            // Opt-in Tier-2 oracle: collect per-model validation errors. Off by default to
            // keep describe cheap; tests that need to assert compile-clean pass includeErrors=true.
            var includeErrors = parameters?["includeErrors"]?.ToObject<bool>() ?? false;
            JArray errors = includeErrors ? CollectErrors(graph) : null;

            return new JObject
            {
                ["assetPath"] = assetPath,
                ["contextCount"] = contexts.Count,
                ["contexts"] = contexts,
                ["operatorCount"] = operators.Count,
                ["operators"] = operators,
                ["parameterCount"] = paramsJson.Count,
                ["parameters"] = paramsJson,
                ["stickyNoteCount"] = stickyNotes.Count,
                ["stickyNotes"] = stickyNotes,
                ["instancing"] = instancing,
                ["errors"] = errors
            };
        }

        /// <summary>
        /// Walk all VFXModels in the graph and ask each to register validation errors into a fresh
        /// VFXErrorReporter, then dump the reporter's m_Errors dictionary as JSON. Tier-2 oracle:
        /// catches HLSL parse failures and similar model-level validation issues that bad ops would
        /// leave invisible to a structural-only describe.
        /// </summary>
        private static JArray CollectErrors(object graph)
        {
            var arr = new JArray();
            try
            {
                var invalidateOrigin = Enum.Parse(ErrorOriginType, "Invalidate");
                var reporter = Activator.CreateInstance(ErrorReporterType, invalidateOrigin);

                void Visit(object model)
                {
                    if (model == null) return;
                    try { Call(model, ModelType, "GenerateErrors", reporter); }
                    catch { /* models that fail validation hard are tolerated */ }
                }

                Visit(graph);
                foreach (var child in Children(graph))
                {
                    Visit(child);
                    if (ContextType.IsInstanceOfType(child))
                        foreach (var block in Children(child))
                            Visit(block);
                }

                var errorsField = ErrorReporterType.GetField("m_Errors", AllInstance);
                var dict = errorsField?.GetValue(reporter) as IDictionary;
                if (dict == null) return arr;
                foreach (DictionaryEntry kv in dict)
                {
                    var modelName = (kv.Key as UnityEngine.Object)?.name
                                    ?? kv.Key?.GetType().Name;
                    var modelType = kv.Key?.GetType().Name;
                    var list = kv.Value as IEnumerable;
                    if (list == null) continue;
                    foreach (var rep in list)
                    {
                        arr.Add(new JObject
                        {
                            ["model"] = modelName,
                            ["modelType"] = modelType,
                            ["type"] = Prop(rep, "type")?.ToString(),
                            ["error"] = Prop(rep, "error") as string,
                            ["description"] = Prop(rep, "description") as string
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                arr.Add(new JObject { ["error"] = $"error-collector failed: {ex.Message}" });
            }
            return arr;
        }

        /// <summary>Read the graph's VFXUI sticky-note array as JSON (title/contents/position/theme).</summary>
        private static JArray StickyNotesJson(object graph)
        {
            var arr = new JArray();
            object ui;
            try { ui = Prop(graph, "UIInfos"); }
            catch { return arr; }
            if (ui == null) return arr;
            var notesField = FindField(ui.GetType(), "stickyNoteInfos");
            if (notesField == null) return arr;
            var notes = notesField.GetValue(ui) as Array;
            if (notes == null) return arr;
            for (int i = 0; i < notes.Length; i++)
            {
                var note = notes.GetValue(i);
                if (note == null) continue;
                var t = note.GetType();
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["title"] = FindField(t, "title")?.GetValue(note) as string,
                    ["contents"] = FindField(t, "contents")?.GetValue(note) as string,
                    ["position"] = ToJToken(FindField(t, "position")?.GetValue(note)),
                    ["theme"] = FindField(t, "theme")?.GetValue(note) as string,
                    ["textSize"] = FindField(t, "textSize")?.GetValue(note) as string,
                    ["colorTheme"] = (int)(FindField(t, "colorTheme")?.GetValue(note) ?? 0)
                });
            }
            return arr;
        }

        /// <summary>Discovery oracle: list available descriptors. kind = block (default)|operator|context|parameter.</summary>
        public static object ListLibrary(JObject parameters)
        {
            try { return ListLibraryCore(parameters); }
            catch (Exception ex) { return Fail("vfx_list_library", ex); }
        }

        /// <summary>List the built-in template .vfx files shipped with the VFX package.</summary>
        private static object ListTemplates(string filter)
        {
            var dir = AssetEditorUtilityType
                .GetProperty("templatePath", AllStatic)?.GetValue(null) as string;
            var items = new JArray();
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                return new JObject { ["kind"] = "template", ["count"] = 0, ["items"] = items, ["templateDir"] = dir };

            foreach (var file in System.IO.Directory.GetFiles(dir, "*.vfx"))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(filter) &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                items.Add(new JObject
                {
                    ["name"] = name,
                    ["category"] = "Default VFX Graph Templates",
                    ["path"] = file.Replace('\\', '/')
                });
            }
            return new JObject
            {
                ["kind"] = "template",
                ["count"] = items.Count,
                ["items"] = items,
                ["templateDir"] = dir.Replace('\\', '/')
            };
        }

        private static object ListLibraryCore(JObject parameters)
        {
            var filter = parameters?["filter"]?.ToString();
            var kind = parameters?["kind"]?.ToString()?.ToLowerInvariant() ?? "block";
            if (kind == "template") return ListTemplates(filter);
            string discovery;
            switch (kind)
            {
                case "operator": discovery = "GetOperators"; break;
                case "context": discovery = "GetContexts"; break;
                case "block": discovery = "GetBlocks"; break;
                case "parameter": discovery = "GetParameters"; break;
                default: return new { error = $"Unknown kind '{kind}'. Supported: block, operator, context, parameter, template" };
            }
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
            try { return ApplyCore(parameters); }
            catch (Exception ex) { return Fail("vfx_apply", ex); }
        }

        private static object ApplyCore(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            // Asset-creation ops target their OWN new path (subgraphPath/targetPath), not an
            // existing parent graph at assetPath, so they're exempt from the assetPath guard.
            if (op != "create_subgraph_asset" && op != "create_from_template"
                && string.IsNullOrEmpty(parameters?["assetPath"]?.ToString()))
                return new { error = "assetPath is required" };
            switch (op)
            {
                case "add_block": return AddBlock(parameters);
                case "set_block_setting": return SetBlockSetting(parameters);
                case "set_operator_setting": return SetOperatorSetting(parameters);
                case "set_context_setting": return SetContextSetting(parameters);
                case "add_context": return AddContext(parameters);
                case "add_operator": return AddOperator(parameters);
                case "add_parameter": return AddParameter(parameters);
                case "link_slots": return LinkSlots(parameters);
                case "set_slot_value": return SetSlotValue(parameters);
                case "unlink_slots": return UnlinkSlots(parameters);
                case "remove_block": return RemoveBlock(parameters);
                case "set_block_enabled": return SetBlockEnabled(parameters);
                case "reorder_block": return ReorderBlock(parameters);
                case "move_block": return MoveBlock(parameters);
                case "remove_operator": return RemoveOperator(parameters);
                case "remove_parameter": return RemoveParameter(parameters);
                case "remove_context": return RemoveContext(parameters);
                case "delete_system": return DeleteSystem(parameters);
                case "link_flow": return LinkFlow(parameters);
                case "set_bounds": return SetBounds(parameters);
                case "add_sticky_note": return AddStickyNote(parameters);
                case "update_sticky_note": return UpdateStickyNote(parameters);
                case "remove_sticky_note": return RemoveStickyNote(parameters);
                case "set_instancing": return SetInstancing(parameters);
                case "create_subgraph_asset": return CreateSubgraphAsset(parameters);
                case "create_from_template": return CreateFromTemplate(parameters);
                default:
                    return new { error = $"Unsupported op: '{op}'. Supported: add_block, set_block_setting, set_block_enabled, reorder_block, move_block, add_context, add_operator, add_parameter, link_slots, set_slot_value, unlink_slots, set_operator_setting, set_context_setting, remove_block, remove_operator, remove_parameter, remove_context, link_flow, set_bounds, add_sticky_note, update_sticky_note, remove_sticky_note, set_instancing, create_subgraph_asset, create_from_template" };
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
                return new { error = "blockName is required" };

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
                    catch (Exception e) { BridgeLogger.LogWarning("VfxGraphHandler", $"setting '{kv.Key}' failed: {e.Message}"); }
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

        /// <summary>
        /// Convert a JSON value to a [VFXSetting] field's type. Object-reference fields (e.g.
        /// VFXSubgraphBlock.m_Subgraph / VFXSubgraphOperator.m_Subgraph) accept an asset-path string,
        /// loaded via AssetDatabase rather than deserialized by Newtonsoft. Shared by
        /// set_block_setting and set_operator_setting.
        /// </summary>
        private static object CoerceSettingValue(FieldInfo field, JToken valueToken, string settingName)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
            {
                var refPath = valueToken.ToString();
                if (string.IsNullOrEmpty(refPath)) return null;
                var loaded = AssetDatabase.LoadAssetAtPath(refPath, field.FieldType);
                if (loaded == null)
                    throw new Exception(
                        $"No {field.FieldType.Name} asset at path '{refPath}' for setting '{settingName}'.");
                return loaded;
            }
            try { return valueToken.ToObject(field.FieldType); }
            catch (Exception e)
            {
                throw new Exception(
                    $"Cannot convert value to {field.FieldType.Name} for setting '{settingName}': {e.Message}");
            }
        }

        /// <summary>
        /// Write a [VFXSetting] field on a graph operator (symmetrical to set_block_setting). Some
        /// settings add/remove ports or change types on write (the model resyncs its slots), so the
        /// caller should re-describe afterwards. Unblocks Operator subgraph references
        /// (m_Subgraph) and Custom HLSL operator source (m_HLSLCode).
        /// </summary>
        private static object SetOperatorSetting(JObject parameters)
        {
            var settingName = parameters?["setting"]?.ToString();
            if (string.IsNullOrEmpty(settingName))
                return new { error = "setting is required" };
            var valueToken = parameters?["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return new { error = "value is required" };
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            if (operatorIndex < 0 || operatorIndex >= ops.Count)
                throw new Exception(
                    $"operatorIndex {operatorIndex} out of range; graph has {ops.Count} operator(s)");
            var op = ops[operatorIndex];

            var field = FindField(op.GetType(), settingName);
            if (field == null)
                throw new Exception(
                    $"Setting '{settingName}' not found on operator '{op.GetType().Name}'. Use vfx_describe_graph to list settings.");

            object converted = CoerceSettingValue(field, valueToken, settingName);
            Call(op, ModelType, "SetSettingValue", settingName, converted);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_operator_setting",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["setting"] = settingName,
                ["value"] = ToJToken(converted)
            };
        }

        /// <summary>
        /// Write a [VFXSetting] field on a context (Spawn loop settings, Update toggles, Output
        /// blend/UV/shader knobs) OR on the context's particle data (Init Capacity, boundsMode,
        /// stripCapacity). Tries the context first, then falls back to GetData() — the same bridge
        /// describe uses to surface data settings on `contexts[].settings`. Address by `contextType`
        /// or `index`. Some settings add/remove ports, so re-describe afterwards.
        /// </summary>
        private static object SetContextSetting(JObject parameters)
        {
            var settingName = parameters?["setting"]?.ToString();
            if (string.IsNullOrEmpty(settingName))
                return new { error = "setting is required" };
            var valueToken = parameters?["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return new { error = "value is required" };
            bool hasIndex = parameters?["index"] != null && parameters["index"].Type != JTokenType.Null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index) is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var ctx = ResolveContextRef(graph, parameters, ctxList, "context");

            // Context-level setting first; else the context's particle data (capacity/boundsMode etc.).
            object targetModel = ctx;
            string via = "context";
            object data = null;
            var field = FindField(ctx.GetType(), settingName);
            if (field == null)
            {
                data = Call(ctx, ContextType, "GetData");
                var dataField = data == null ? null : FindField(data.GetType(), settingName);
                if (dataField != null) { field = dataField; targetModel = data; via = "data"; }
            }

            if (field != null)
            {
                object convertedSetting = CoerceSettingValue(field, valueToken, settingName);
                Call(targetModel, ModelType, "SetSettingValue", settingName, convertedSetting);
                Persist(graph, assetPath);
                return SetContextSettingResult(assetPath, ctx, settingName, via, ToJToken(convertedSetting));
            }

            // Property fallback: a few "settings" are exposed as public properties rather than
            // [VFXSetting] fields — notably VFXDataParticle.space (simulation Local/World), whose
            // m_Space field is private and explicitly not a setting yet. Setting the property runs the
            // model's own invalidation (Modified), so no separate SetSettingValue is needed.
            var prop = FindWritableProperty(ctx.GetType(), settingName);
            if (prop != null) { targetModel = ctx; via = "context-property"; }
            else
            {
                data = data ?? Call(ctx, ContextType, "GetData");
                var dataProp = data == null ? null : FindWritableProperty(data.GetType(), settingName);
                if (dataProp != null) { prop = dataProp; targetModel = data; via = "data-property"; }
            }
            if (prop == null)
                throw new Exception(
                    $"Setting '{settingName}' not found on context '{ctx.GetType().Name}' or its data. Use vfx_describe_graph to list settings.");

            object convertedProp = CoerceToType(valueToken, prop.PropertyType);
            prop.SetValue(targetModel, convertedProp);
            Persist(graph, assetPath);
            return SetContextSettingResult(assetPath, ctx, settingName, via, ToJToken(convertedProp?.ToString()));
        }

        private static JObject SetContextSettingResult(
            string assetPath, object ctx, string settingName, string via, JToken value)
        {
            return new JObject
            {
                ["op"] = "set_context_setting",
                ["assetPath"] = assetPath,
                ["contextType"] = Prop(ctx, "contextType")?.ToString(),
                ["context"] = ctx.GetType().Name,
                ["setting"] = settingName,
                ["via"] = via,
                ["value"] = value
            };
        }

        /// <summary>Find a public/non-public writable instance property by name, walking up base types.</summary>
        private static PropertyInfo FindWritableProperty(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, AllInstance | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite && p.GetSetMethod(true) != null) return p;
            }
            return null;
        }

        /// <summary>
        /// Delete a whole particle system in one op: every context that shares the addressed context's
        /// VFXData (Init/Update/Output of one system). Addressed by `contextType` or `index` (any member).
        /// Mirrors remove_context's cascade — flow UnlinkAll + data-slot unlink — for each member before
        /// RemoveChild, so no dangling links remain on a disjoint system.
        /// </summary>
        private static object DeleteSystem(JObject parameters)
        {
            bool hasIndex = parameters?["index"] != null && parameters["index"].Type != JTokenType.Null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index) is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var target = ResolveContextRef(graph, parameters, ctxList, "context");

            var targetData = Call(target, ContextType, "GetData") as UnityEngine.Object;
            if (targetData == null)
                throw new Exception(
                    $"Context '{target.GetType().Name}' has no VFXData — it isn't part of a particle system " +
                    "(Spawn/Event contexts can't address a system). Address an Init/Update/Output context.");
            int systemId = targetData.GetInstanceID();

            var members = ctxList.Where(c =>
            {
                var d = Call(c, ContextType, "GetData") as UnityEngine.Object;
                return d != null && d.GetInstanceID() == systemId;
            }).ToList();

            foreach (var ctx in members)
            {
                Call(ctx, ContextType, "UnlinkAll");
                UnlinkContainerSlots(ctx);
                Call(graph, ModelType, "RemoveChild", ctx, true);
            }
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "delete_system",
                ["assetPath"] = assetPath,
                ["systemDataInstanceId"] = systemId,
                ["removedContexts"] = members.Count,
                ["removedContextTypes"] = new JArray(members.Select(m => (JToken)(Prop(m, "contextType")?.ToString()))),
                ["remainingContexts"] = Children(graph).Count(c => ContextType.IsInstanceOfType(c))
            };
        }

        private static object SetBlockSetting(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var wantContext = parameters?["contextType"]?.ToString() ?? "Update";
            var settingName = parameters?["setting"]?.ToString();
            if (string.IsNullOrEmpty(settingName))
                return new { error = "setting is required" };
            var valueToken = parameters?["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return new { error = "value is required" };
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

            object converted = CoerceSettingValue(field, valueToken, settingName);
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
                return new { error = "contextName is required" };
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
                return new { error = "operatorName is required" };

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
                return new { error = "parameterName is required" };
            var typeName = parameters?["type"]?.ToString();
            if (string.IsNullOrEmpty(typeName))
                return new { error = "type is required (e.g. Float, Int, Vector3, Color). Use vfx_list_library with kind 'parameter'." };
            bool exposed = parameters?["exposed"]?.ToObject<bool>() ?? true;

            var graph = LoadGraph(assetPath);

            // Find parameter descriptor by type name (exact, then space-insensitive, then contains).
            // Descriptor names carry spaces ("Vector 3", "Texture 2D"), so "Vector3"/"Texture2D" are
            // matched by stripping whitespace before comparing.
            var descriptors = (Call(null, LibraryType, "GetParameters") as IEnumerable).Cast<object>().ToList();
            string Squash(string s) => s?.Replace(" ", "");
            var wantSquashed = Squash(typeName);
            var match = descriptors.FirstOrDefault(d =>
                            string.Equals(Prop(d, "name") as string, typeName, StringComparison.OrdinalIgnoreCase))
                        ?? descriptors.FirstOrDefault(d =>
                            string.Equals(Squash(Prop(d, "name") as string), wantSquashed, StringComparison.OrdinalIgnoreCase))
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

            var paramType = Prop(parameter, "type") as Type;

            // Optional default value. Use ParamCoerce so vectors/colors (array JSON) and Object types
            // (Texture/Mesh by asset path) work, not just the primitives Newtonsoft can build directly.
            var valueToken = parameters?["value"];
            JToken appliedValue = null;
            if (valueToken != null && valueToken.Type != JTokenType.Null)
            {
                object converted = ParamCoerce(valueToken, paramType, "value");
                SetProp(parameter, "value", converted);
                appliedValue = ToJToken(converted);
            }

            // Optional min/max range. VFXParameter gates min/max behind valueFilter=Range; set the
            // filter first (parse the enum off the property's own type), then the bounds.
            var minToken = parameters?["min"];
            var maxToken = parameters?["max"];
            JToken appliedMin = null, appliedMax = null;
            if ((minToken != null && minToken.Type != JTokenType.Null) ||
                (maxToken != null && maxToken.Type != JTokenType.Null))
            {
                var filterProp = parameter.GetType().GetProperty("valueFilter",
                    BindingFlags.Public | BindingFlags.Instance);
                if (filterProp != null)
                    SetProp(parameter, "valueFilter", Enum.Parse(filterProp.PropertyType, "Range", true));
                if (minToken != null && minToken.Type != JTokenType.Null)
                {
                    object cMin = ParamCoerce(minToken, paramType, "min");
                    SetProp(parameter, "min", cMin);
                    appliedMin = ToJToken(cMin);
                }
                if (maxToken != null && maxToken.Type != JTokenType.Null)
                {
                    object cMax = ParamCoerce(maxToken, paramType, "max");
                    SetProp(parameter, "max", cMax);
                    appliedMax = ToJToken(cMax);
                }
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
                ["min"] = appliedMin,
                ["max"] = appliedMax,
                ["parameterIndex"] = parameterIndex
            };
        }

        /// <summary>
        /// Coerce a JSON value to a VFX parameter's CLR type. Like CoerceToType, but additionally
        /// loads UnityEngine.Object types (Texture/Mesh/etc.) from an asset-path string. Used for a
        /// parameter's default value and its min/max bounds.
        /// </summary>
        private static object ParamCoerce(JToken token, Type targetType, string label)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                var refPath = token.ToString();
                if (string.IsNullOrEmpty(refPath)) return null;
                var loaded = AssetDatabase.LoadAssetAtPath(refPath, targetType);
                if (loaded == null)
                    throw new Exception($"No {targetType.Name} asset at path '{refPath}' for {label}.");
                return loaded;
            }
            return CoerceToType(token, targetType);
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
            if (from == null) return new { error = "from is required" };
            if (to == null) return new { error = "to is required" };

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

        /// <summary>
        /// Coerce a JSON value to a concrete CLR type. Handles the Unity math structs that
        /// Newtonsoft can't round-trip (Vector2/3/4 from [n,n,…] arrays, Color from [r,g,b(,a)]),
        /// enums (by name or int), and falls back to JToken.ToObject for primitives/everything else.
        /// </summary>
        private static object CoerceToType(JToken value, Type targetType)
        {
            if (value == null)
                throw new Exception($"value is required (target slot expects {targetType.Name})");
            if (targetType == typeof(Vector2)) return ToVector(value, 2);
            if (targetType == typeof(Vector3)) return ToVector(value, 3);
            if (targetType == typeof(Vector4)) return ToVector(value, 4);
            if (targetType == typeof(Color))
            {
                var arr = value as JArray;
                if (arr == null || arr.Count < 3)
                    throw new Exception("Color value must be an array [r,g,b] or [r,g,b,a]");
                float a = arr.Count >= 4 ? arr[3].ToObject<float>() : 1f;
                return new Color(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), a);
            }
            if (targetType.IsEnum)
            {
                return value.Type == JTokenType.String
                    ? Enum.Parse(targetType, value.ToString(), true)
                    : Enum.ToObject(targetType, value.ToObject<long>());
            }
            try { return value.ToObject(targetType); }
            catch (Exception e)
            {
                throw new Exception($"Cannot convert value to {targetType.Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Walk a subPath into a (possibly nested) value-type struct, setting the leaf. Structs are
        /// value types, so each level is boxed, its field rewritten, and propagated back up — the
        /// same box-and-write trick set_bounds uses, generalized to arbitrary depth (e.g.
        /// ["center","x"] on an AABox).
        /// </summary>
        private static object SetNestedField(object current, string[] path, int i, JToken value)
        {
            if (i >= path.Length)
                return CoerceToType(value, current?.GetType()
                    ?? throw new Exception("Cannot infer leaf type from a null slot value"));
            if (current == null)
                throw new Exception($"Cannot walk subPath segment '{path[i]}' into a null value");
            var t = current.GetType();
            var field = t.GetField(path[i], BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                var available = string.Join(", ",
                    t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
                throw new Exception($"subPath segment '{path[i]}' not found on '{t.Name}'. Available: {available}");
            }
            object boxed = current; // boxing the struct lets SetValue mutate this copy
            var newChild = SetNestedField(field.GetValue(boxed), path, i + 1, value);
            field.SetValue(boxed, newChild);
            return boxed;
        }

        /// <summary>
        /// Write a constant value into an (unlinked) input slot. Addresses the slot by
        /// target = {node, …address, slot}; optional subPath walks into compound value structs
        /// (Vector3 ["x"], AABox ["center","y"], Color N/A — set the whole color). The whole-slot
        /// path coerces the JSON value to the slot's current value type; the subPath path uses the
        /// box-and-write struct walk.
        /// </summary>
        private static object SetSlotValue(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var target = parameters?["target"] as JObject;
            var valueToken = parameters?["value"];
            if (target == null)
                return new { error = "target is required (an object {node, …address, slot})" };
            if (valueToken == null)
                return new { error = "value is required" };

            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            int slotIndex = target["slot"]?.ToObject<int>() ?? 0;
            var slot = GetSlot(node, true, slotIndex, "target");

            var current = Prop(slot, "value");
            var subPath = (parameters?["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();

            object newValue;
            if (subPath != null && subPath.Length > 0)
            {
                if (current == null)
                    throw new Exception("Slot has no readable value to walk subPath into");
                newValue = SetNestedField(current, subPath, 0, valueToken);
            }
            else
            {
                var targetType = current?.GetType()
                    ?? throw new Exception(
                        "Slot value type could not be inferred (slot has a null value); subPath/typed slots unsupported here");
                newValue = CoerceToType(valueToken, targetType);
            }

            SetProp(slot, "value", newValue);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_slot_value",
                ["assetPath"] = assetPath,
                ["target"] = new JObject
                {
                    ["node"] = node.GetType().Name,
                    ["slot"] = slotIndex,
                    ["slotName"] = SlotName(slot)
                },
                ["subPath"] = subPath == null ? null : new JArray(subPath),
                ["value"] = ToJToken(Prop(slot, "value"))
            };
        }

        /// <summary>Count the links currently on a slot (its LinkedSlots).</summary>
        private static int LinkCount(object slot)
        {
            try { return (Prop(slot, "LinkedSlots") as IEnumerable)?.Cast<object>().Count() ?? 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Remove a slot connection. `target` = the input-slot endpoint {node, …address, slot} whose
        /// link(s) to break — by default UnlinkAll (input slots hold one link in VFX, so this is
        /// unambiguous). An optional `from` output-slot endpoint unlinks only that specific edge.
        /// Verifiable via describe: the target slot's `links` array empties / `hasLink` flips false.
        /// </summary>
        private static object UnlinkSlots(JObject parameters)
        {
            var target = parameters?["target"] as JObject ?? parameters?["to"] as JObject;
            if (target == null)
                return new { error = "target is required (an object {node, …address, slot})" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            int slotIndex = target["slot"]?.ToObject<int>() ?? 0;
            var slot = GetSlot(node, true, slotIndex, "target");

            int before = LinkCount(slot);

            var from = parameters?["from"] as JObject;
            if (from != null)
            {
                var fromNode = ResolveNode(graph, from, "from");
                int fromSlot = from["slot"]?.ToObject<int>() ?? 0;
                var outSlot = GetSlot(fromNode, false, fromSlot, "from");
                Call(slot, SlotType, "Unlink", outSlot, true); // (other, notify)
            }
            else
            {
                Call(slot, SlotType, "UnlinkAll", true, true); // (recursive, notify)
            }

            int after = LinkCount(slot);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "unlink_slots",
                ["assetPath"] = assetPath,
                ["target"] = new JObject
                {
                    ["node"] = node.GetType().Name,
                    ["slot"] = slotIndex,
                    ["slotName"] = SlotName(slot)
                },
                ["linksRemoved"] = before - after,
                ["remainingLinks"] = after
            };
        }

        /// <summary>
        /// Unlink every top-level input/output slot of a slot container (block/operator/parameter/
        /// context). RemoveChild does NOT cascade-unlink a removed node's data slots, so callers must
        /// clear them first to avoid dangling links in the nodes on the other end.
        /// </summary>
        private static void UnlinkContainerSlots(object container)
        {
            foreach (var dir in new[] { "inputSlots", "outputSlots" })
            {
                IEnumerable coll;
                try { coll = Prop(container, dir) as IEnumerable; }
                catch { continue; }
                if (coll == null) continue;
                foreach (var slot in coll.Cast<object>().ToList())
                {
                    try { Call(slot, SlotType, "UnlinkAll", true, true); }
                    catch { /* slot may not be linkable; ignore */ }
                }
            }
        }

        private static object RemoveBlock(JObject parameters)
        {
            var wantContext = parameters?["contextType"]?.ToString();
            if (string.IsNullOrEmpty(wantContext))
                return new { error = "contextType is required" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctx = FindContext(graph, wantContext);
            if (ctx == null)
                throw new Exception($"No context of type '{wantContext}' found in {assetPath}");

            var blocks = Children(ctx).ToList();
            if (blockIndex < 0 || blockIndex >= blocks.Count)
                throw new Exception(
                    $"blockIndex {blockIndex} out of range; context '{wantContext}' has {blocks.Count} block(s)");
            var block = blocks[blockIndex];
            var removedType = block.GetType().Name;

            UnlinkContainerSlots(block);
            Call(ctx, ModelType, "RemoveChild", block, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_block",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["removedBlock"] = removedType,
                ["remainingBlocks"] = Children(ctx).Count()
            };
        }

        /// <summary>Locate a block by (contextType, blockIndex); returns its context + the block.</summary>
        private static (object ctx, object block) LocateBlock(object graph, string contextType, int blockIndex)
        {
            var ctx = FindContext(graph, contextType);
            if (ctx == null)
                throw new Exception($"No context of type '{contextType}' found");
            var blocks = Children(ctx).ToList();
            if (blockIndex < 0 || blockIndex >= blocks.Count)
                throw new Exception(
                    $"blockIndex {blockIndex} out of range; context '{contextType}' has {blocks.Count} block(s)");
            return (ctx, blocks[blockIndex]);
        }

        /// <summary>
        /// Enable/disable a block. `enabled` is a read-only computed property derived from the block's
        /// activation slot (default `!m_Disabled`); the editor toggles it by writing the activation
        /// slot's value, so we set that (and keep the serialized `m_Disabled` field consistent).
        /// </summary>
        private static object SetBlockEnabled(JObject parameters)
        {
            var wantContext = parameters?["contextType"]?.ToString();
            if (string.IsNullOrEmpty(wantContext))
                return new { error = "contextType is required" };
            var enabledTok = parameters?["enabled"];
            if (enabledTok == null || enabledTok.Type == JTokenType.Null)
                return new { error = "enabled is required (bool)" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            bool enabled = enabledTok.ToObject<bool>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (_, block) = LocateBlock(graph, wantContext, blockIndex);

            var actSlot = Prop(block, "activationSlot");
            if (actSlot != null) SetProp(actSlot, "value", enabled);
            FindField(block.GetType(), "m_Disabled")?.SetValue(block, !enabled);

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_block_enabled",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["blockIndex"] = blockIndex,
                ["block"] = block.GetType().Name,
                ["enabled"] = (bool)Prop(block, "enabled")
            };
        }

        /// <summary>Move a block to a new position within its own context (RemoveChild → AddChild at index).</summary>
        private static object ReorderBlock(JObject parameters)
        {
            var wantContext = parameters?["contextType"]?.ToString();
            if (string.IsNullOrEmpty(wantContext))
                return new { error = "contextType is required" };
            var toTok = parameters?["toIndex"];
            if (toTok == null || toTok.Type == JTokenType.Null)
                return new { error = "toIndex is required" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            int toIndex = toTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ctx, block) = LocateBlock(graph, wantContext, blockIndex);

            int count = Children(ctx).Count();
            if (toIndex < 0 || toIndex >= count)
                throw new Exception($"toIndex {toIndex} out of range; context '{wantContext}' has {count} block(s)");

            Call(ctx, ModelType, "RemoveChild", block, false); // notify:false — re-add immediately
            Call(ctx, ModelType, "AddChild", block, toIndex, true);
            Persist(graph, assetPath);

            int newIndex = Children(ctx).ToList().FindIndex(b => ReferenceEquals(b, block));
            return new JObject
            {
                ["op"] = "reorder_block",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["block"] = block.GetType().Name,
                ["fromIndex"] = blockIndex,
                ["toIndex"] = newIndex
            };
        }

        /// <summary>
        /// Move a block to a different (compatible) context. Validates via VFXContext.Accept before
        /// re-parenting, so an incompatible target returns a clear error instead of corrupting the graph.
        /// </summary>
        private static object MoveBlock(JObject parameters)
        {
            var wantContext = parameters?["contextType"]?.ToString();
            if (string.IsNullOrEmpty(wantContext))
                return new { error = "contextType is required (the source context)" };
            var toContext = parameters?["toContextType"]?.ToString();
            if (string.IsNullOrEmpty(toContext))
                return new { error = "toContextType is required (the destination context)" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            int toIndex = parameters?["toIndex"]?.ToObject<int>() ?? -1;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (srcCtx, block) = LocateBlock(graph, wantContext, blockIndex);
            var dstCtx = FindContext(graph, toContext);
            if (dstCtx == null)
                throw new Exception($"No destination context of type '{toContext}' found in {assetPath}");

            bool accept = (bool)Call(dstCtx, ContextType, "Accept", block, -1);
            if (!accept)
                return new { error = $"Block '{block.GetType().Name}' is not compatible with context '{toContext}'." };

            Call(srcCtx, ModelType, "RemoveChild", block, false);
            Call(dstCtx, ModelType, "AddChild", block, toIndex, true);
            Persist(graph, assetPath);

            int newIndex = Children(dstCtx).ToList().FindIndex(b => ReferenceEquals(b, block));
            return new JObject
            {
                ["op"] = "move_block",
                ["assetPath"] = assetPath,
                ["block"] = block.GetType().Name,
                ["fromContextType"] = wantContext,
                ["toContextType"] = toContext,
                ["toIndex"] = newIndex,
                ["remainingInSource"] = Children(srcCtx).Count()
            };
        }

        private static object RemoveOperator(JObject parameters)
        {
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            if (operatorIndex < 0 || operatorIndex >= ops.Count)
                throw new Exception(
                    $"operatorIndex {operatorIndex} out of range; graph has {ops.Count} operator(s)");
            var op = ops[operatorIndex];
            var removedType = op.GetType().Name;

            UnlinkContainerSlots(op);
            Call(graph, ModelType, "RemoveChild", op, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_operator",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["removedOperator"] = removedType,
                ["remainingOperators"] = Children(graph).Count(c => OperatorType.IsInstanceOfType(c))
            };
        }

        private static object RemoveParameter(JObject parameters)
        {
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();
            if (parameterIndex < 0 || parameterIndex >= ps.Count)
                throw new Exception(
                    $"parameterIndex {parameterIndex} out of range; graph has {ps.Count} parameter(s)");
            var param = ps[parameterIndex];
            var removedName = ModelName(param);

            UnlinkContainerSlots(param);
            Call(graph, ModelType, "RemoveChild", param, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_parameter",
                ["assetPath"] = assetPath,
                ["parameterIndex"] = parameterIndex,
                ["removedParameter"] = removedName,
                ["remainingParameters"] = Children(graph).Count(c => ParameterType.IsInstanceOfType(c))
            };
        }

        private static object RemoveContext(JObject parameters)
        {
            var idxTok = parameters?["index"];
            bool hasIndex = idxTok != null && idxTok.Type != JTokenType.Null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index) is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();

            object ctx;
            if (hasIndex)
            {
                int idx = idxTok.ToObject<int>();
                if (idx < 0 || idx >= ctxList.Count)
                    throw new Exception($"index {idx} out of range; graph has {ctxList.Count} context(s)");
                ctx = ctxList[idx];
            }
            else
            {
                ctx = FindContext(graph, wantContext);
                if (ctx == null)
                    throw new Exception($"No context of type '{wantContext}' found in {assetPath}");
            }
            var removedType = ctx.GetType().Name;
            var removedContextType = Prop(ctx, "contextType")?.ToString();

            // Contexts don't cascade-unlink on RemoveChild: drop flow edges (VFXContext.UnlinkAll, the
            // no-arg flow variant) AND any data-slot links, else the other endpoints keep dangling refs.
            Call(ctx, ContextType, "UnlinkAll");
            UnlinkContainerSlots(ctx);
            Call(graph, ModelType, "RemoveChild", ctx, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_context",
                ["assetPath"] = assetPath,
                ["removedContext"] = removedType,
                ["removedContextType"] = removedContextType,
                ["remainingContexts"] = Children(graph).Count(c => ContextType.IsInstanceOfType(c))
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
            if (from == null) return new { error = "from is required (the source context)" };
            if (to == null) return new { error = "to is required (the target context)" };

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

        /// <summary>Find a top-level input/output slot on a model by property name.</summary>
        private static object FindSlotByName(object container, string name, bool isInput)
        {
            var coll = Prop(container, isInput ? "inputSlots" : "outputSlots") as IEnumerable;
            if (coll == null) return null;
            foreach (var s in coll)
                if (string.Equals(SlotName(s), name, StringComparison.Ordinal))
                    return s;
            return null;
        }

        /// <summary>
        /// Set bounds on the Initialize context's particle data: switch boundsMode
        /// (Manual/Recorded/Automatic) and write the bounds AABox center/size and
        /// boundsPadding when supplied. The mode change resynces the context's
        /// input slots (Manual exposes bounds; Recorded exposes bounds + padding;
        /// Automatic exposes padding only) — bounds/padding writes target whichever
        /// slots the new mode exposes.
        /// </summary>
        private static object SetBounds(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var wantContext = parameters?["contextType"]?.ToString() ?? "Init";
            var modeStr = parameters?["mode"]?.ToString();
            var centerTok = parameters?["center"];
            var sizeTok = parameters?["size"];
            var paddingTok = parameters?["padding"];
            if (string.IsNullOrEmpty(modeStr) && centerTok == null && sizeTok == null && paddingTok == null)
                return new { error = "set_bounds requires at least one of: mode, center, size, padding" };

            var graph = LoadGraph(assetPath);
            var ctx = FindContext(graph, wantContext);
            if (ctx == null)
                throw new Exception($"No context of type '{wantContext}' found in {assetPath}");

            var data = Call(ctx, ContextType, "GetData");
            if (data == null)
                throw new Exception(
                    $"Context '{wantContext}' has no associated VFXData; bounds live on a particle-data context (Init).");

            JToken appliedMode = null;
            if (!string.IsNullOrEmpty(modeStr))
            {
                var field = FindField(data.GetType(), "boundsMode");
                if (field == null)
                    throw new Exception(
                        $"boundsMode field not found on '{data.GetType().Name}'; this context's data is not VFXDataParticle.");
                object modeValue;
                try { modeValue = Enum.Parse(field.FieldType, modeStr, true); }
                catch (Exception e)
                {
                    throw new Exception(
                        $"Invalid mode '{modeStr}': {e.Message}. Supported: Manual, Recorded, Automatic.");
                }
                Call(data, ModelType, "SetSettingValue", "boundsMode", modeValue);
                appliedMode = new JValue(modeValue.ToString());
            }

            JObject appliedBounds = null;
            if (centerTok != null || sizeTok != null)
            {
                var boundsSlot = FindSlotByName(ctx, "bounds", true);
                if (boundsSlot == null)
                    throw new Exception(
                        "No 'bounds' input slot on this context — the current boundsMode does not expose one (Automatic exposes padding only).");
                // bounds is an AABox struct with `center` and `size` Vector3 fields.
                var current = Prop(boundsSlot, "value");
                var aabType = current.GetType();
                var centerField = aabType.GetField("center");
                var sizeField = aabType.GetField("size");
                if (centerField == null || sizeField == null)
                    throw new Exception($"Unexpected bounds slot value type '{aabType.Name}'");
                object boxed = current;
                if (centerTok != null) centerField.SetValue(boxed, (Vector3)ToVector(centerTok, 3));
                if (sizeTok != null) sizeField.SetValue(boxed, (Vector3)ToVector(sizeTok, 3));
                SetProp(boundsSlot, "value", boxed);
                appliedBounds = new JObject
                {
                    ["center"] = ToJToken((Vector3)centerField.GetValue(boxed)),
                    ["size"] = ToJToken((Vector3)sizeField.GetValue(boxed))
                };
            }

            JToken appliedPadding = null;
            if (paddingTok != null)
            {
                var padSlot = FindSlotByName(ctx, "boundsPadding", true);
                if (padSlot == null)
                    throw new Exception(
                        "No 'boundsPadding' input slot on this context — the current boundsMode does not expose one (Manual exposes bounds only).");
                var padVec = (Vector3)ToVector(paddingTok, 3);
                SetProp(padSlot, "value", padVec);
                appliedPadding = ToJToken(padVec);
            }

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_bounds",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["mode"] = appliedMode,
                ["bounds"] = appliedBounds,
                ["padding"] = appliedPadding
            };
        }

        /// <summary>
        /// Copy a default subgraph template from the VFX package into the target path. Creates a
        /// stand-alone .vfxblock or .vfxoperator asset; the caller then references it from a parent
        /// graph via add_block / add_operator + set_block_setting m_Subgraph.
        /// (System subgraph = a regular .vfx; defer to Pass-2.)
        /// </summary>
        private static object CreateSubgraphAsset(JObject parameters)
        {
            var subgraphPath = parameters?["subgraphPath"]?.ToString();
            if (string.IsNullOrEmpty(subgraphPath))
                return new { error = "subgraphPath is required (target .vfxblock or .vfxoperator path)" };
            var kind = parameters?["kind"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(kind))
                return new { error = "kind is required (block or operator)" };

            string templatePath;
            string expectedExt;
            switch (kind)
            {
                case "block":
                    templatePath = "Packages/com.unity.visualeffectgraph/Editor/Templates/DefaultSubgraphBlock.vfxblock";
                    expectedExt = ".vfxblock";
                    break;
                case "operator":
                    templatePath = "Packages/com.unity.visualeffectgraph/Editor/Templates/DefaultSubgraphOperator.vfxoperator";
                    expectedExt = ".vfxoperator";
                    break;
                default:
                    return new { error = $"Unknown kind '{kind}'. Supported: block, operator." };
            }
            if (!subgraphPath.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
                return new { error = $"subgraphPath must end with '{expectedExt}' for kind '{kind}'." };

            var template = AssetDatabase.LoadMainAssetAtPath(templatePath);
            if (template == null)
                throw new Exception($"Default subgraph template not found at: {templatePath}");

            // Make sure the parent folder exists. AssetDatabase.CopyAsset won't create folders.
            var parentDir = System.IO.Path.GetDirectoryName(subgraphPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDir) && !AssetDatabase.IsValidFolder(parentDir))
                throw new Exception($"Parent folder does not exist: {parentDir}");

            if (!AssetDatabase.CopyAsset(templatePath, subgraphPath))
                throw new Exception($"Failed to copy template '{templatePath}' to '{subgraphPath}'.");
            AssetDatabase.ImportAsset(subgraphPath, ImportAssetOptions.ForceUpdate);

            var created = AssetDatabase.LoadMainAssetAtPath(subgraphPath);
            return new JObject
            {
                ["op"] = "create_subgraph_asset",
                ["subgraphPath"] = subgraphPath,
                ["kind"] = kind,
                ["assetType"] = created?.GetType().Name
            };
        }

        /// <summary>
        /// Instantiate a new .vfx asset from a built-in template via
        /// VisualEffectAssetEditorUtility.CreateTemplateAsset (copies the template's serialized
        /// graph to the target path + imports). `template` is a template name (filename stem in
        /// the package template dir) or an explicit path to a .vfx template.
        /// </summary>
        private static object CreateFromTemplate(JObject parameters)
        {
            var targetPath = parameters?["targetPath"]?.ToString();
            if (string.IsNullOrEmpty(targetPath))
                return new { error = "targetPath is required (the new .vfx asset path)" };
            if (!targetPath.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                return new { error = "targetPath must end with '.vfx'" };
            var template = parameters?["template"]?.ToString();
            if (string.IsNullOrEmpty(template))
                return new { error = "template is required (a template name or path to a .vfx template)" };

            // Resolve template → an absolute/asset path. Accept an explicit path, else a name
            // resolved against the package template dir.
            var templateDir = AssetEditorUtilityType
                .GetProperty("templatePath", AllStatic)?.GetValue(null) as string;
            string templateFile;
            if (template.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(template))
                templateFile = template;
            else
            {
                if (string.IsNullOrEmpty(templateDir))
                    throw new Exception("Could not resolve the VFX package template directory.");
                templateFile = System.IO.Path.Combine(templateDir, template + ".vfx");
                if (!System.IO.File.Exists(templateFile))
                    throw new Exception(
                        $"No template '{template}' in {templateDir}. Use vfx_list_library kind 'template' to discover names.");
            }

            var parentDir = System.IO.Path.GetDirectoryName(targetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDir) && !AssetDatabase.IsValidFolder(parentDir))
                throw new Exception($"Parent folder does not exist: {parentDir}");

            // CreateTemplateAsset(pathName, templateFilePath) copies + imports.
            Call(null, AssetEditorUtilityType, "CreateTemplateAsset", targetPath, templateFile);
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

            var created = AssetDatabase.LoadMainAssetAtPath(targetPath);
            return new JObject
            {
                ["op"] = "create_from_template",
                ["targetPath"] = targetPath,
                ["template"] = template,
                ["templateFile"] = templateFile.Replace('\\', '/'),
                ["assetType"] = created?.GetType().Name
            };
        }

        /// <summary>
        /// Read the asset's VisualEffectResource instancing settings (mode + capacity)
        /// as a JSON block for describe; null when the resource doesn't surface either.
        /// </summary>
        private static JObject InstancingJson(object resource)
        {
            if (resource == null) return null;
            JToken modeTok = null;
            JToken capTok = null;
            try { modeTok = ToJToken(Prop(resource, "instancingMode")); } catch { }
            try { capTok = ToJToken(Prop(resource, "instancingCapacity")); } catch { }
            if (modeTok == null && capTok == null) return null;
            return new JObject { ["mode"] = modeTok, ["capacity"] = capTok };
        }

        /// <summary>Set VisualEffectResource.instancingMode (+ optional instancingCapacity).</summary>
        private static object SetInstancing(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var modeStr = parameters?["mode"]?.ToString();
            var capTok = parameters?["capacity"];
            if (string.IsNullOrEmpty(modeStr) && capTok == null)
                return new { error = "set_instancing requires at least one of: mode, capacity" };

            var graph = LoadGraph(assetPath);
            var resource = Prop(graph, "visualEffectResource");
            if (resource == null)
                throw new Exception("Graph has no VisualEffectResource (unexpected for a valid .vfx).");

            JToken appliedMode = null;
            if (!string.IsNullOrEmpty(modeStr))
            {
                var modeProp = resource.GetType().GetProperty("instancingMode", AllInstance);
                if (modeProp == null)
                    throw new Exception("instancingMode property not found on VisualEffectResource (VFX package too old?).");
                object modeValue;
                try { modeValue = Enum.Parse(modeProp.PropertyType, modeStr, true); }
                catch (Exception e)
                {
                    var names = string.Join(", ", Enum.GetNames(modeProp.PropertyType));
                    throw new Exception($"Invalid mode '{modeStr}': {e.Message}. Supported: {names}.");
                }
                modeProp.SetValue(resource, modeValue);
                appliedMode = new JValue(modeValue.ToString());
            }

            JToken appliedCapacity = null;
            if (capTok != null)
            {
                int cap = capTok.ToObject<int>();
                if (cap < 1) cap = 1;
                var capProp = resource.GetType().GetProperty("instancingCapacity", AllInstance);
                if (capProp != null)
                {
                    // The property is `uint` on current packages — coerce so passing JSON ints works.
                    object capValue = Convert.ChangeType(cap, capProp.PropertyType);
                    capProp.SetValue(resource, capValue);
                    appliedCapacity = new JValue(cap);
                }
                else
                {
                    // Fallback to the serialized field path the inspector uses.
                    var so = new SerializedObject(resource as UnityEngine.Object);
                    var prop = so.FindProperty("m_Infos.m_InstancingCapacity");
                    if (prop == null)
                        throw new Exception("instancingCapacity is not exposed on VisualEffectResource and the serialized fallback (m_Infos.m_InstancingCapacity) was not found.");
                    prop.intValue = cap;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    appliedCapacity = new JValue(cap);
                }
            }

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_instancing",
                ["assetPath"] = assetPath,
                ["mode"] = appliedMode,
                ["capacity"] = appliedCapacity
            };
        }

        /// <summary>Append a sticky note to VFXGraph.UIInfos.stickyNoteInfos.</summary>
        private static object AddStickyNote(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var title = parameters?["title"]?.ToString() ?? "Note";
            var contents = parameters?["contents"]?.ToString() ?? string.Empty;
            int colorTheme = parameters?["colorTheme"]?.ToObject<int>() ?? 1;
            var textSize = parameters?["textSize"]?.ToString();

            // Position: optional [x, y, width, height] (defaults to a 200x100 box at origin).
            float x = 0, y = 0, w = 200, h = 100;
            var posTok = parameters?["position"] as JArray;
            if (posTok != null && posTok.Count >= 4)
            {
                x = posTok[0].ToObject<float>();
                y = posTok[1].ToObject<float>();
                w = posTok[2].ToObject<float>();
                h = posTok[3].ToObject<float>();
            }

            var graph = LoadGraph(assetPath);
            var (ui, notesField, _) = GetStickyNotes(graph);

            var noteType = StickyNoteInfoType;
            var newNote = Activator.CreateInstance(noteType);
            FindField(noteType, "title").SetValue(newNote, title);
            FindField(noteType, "contents").SetValue(newNote, contents);
            FindField(noteType, "position").SetValue(newNote, new Rect(x, y, w, h));
            FindField(noteType, "colorTheme").SetValue(newNote, colorTheme);
            if (!string.IsNullOrEmpty(textSize))
                FindField(noteType, "textSize").SetValue(newNote, textSize);

            var oldArr = notesField.GetValue(ui) as Array;
            int oldLen = oldArr?.Length ?? 0;
            var newArr = Array.CreateInstance(noteType, oldLen + 1);
            if (oldArr != null) Array.Copy(oldArr, newArr, oldLen);
            newArr.SetValue(newNote, oldLen);
            notesField.SetValue(ui, newArr);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_sticky_note",
                ["assetPath"] = assetPath,
                ["stickyNoteIndex"] = oldLen,
                ["title"] = title,
                ["contents"] = contents,
                ["colorTheme"] = colorTheme,
                ["textSize"] = textSize,
                ["position"] = new JArray { x, y, w, h }
            };
        }

        /// <summary>Resolve a graph's VFXUI sidecar + its stickyNoteInfos field + current array.</summary>
        private static (object ui, FieldInfo field, Array arr) GetStickyNotes(object graph)
        {
            var ui = Prop(graph, "UIInfos");
            if (ui == null)
                throw new Exception("Graph has no UIInfos sidecar (unexpected for a valid .vfx).");
            var notesField = FindField(ui.GetType(), "stickyNoteInfos");
            if (notesField == null)
                throw new Exception("stickyNoteInfos field not found on VFXUI.");
            return (ui, notesField, notesField.GetValue(ui) as Array);
        }

        /// <summary>Edit an existing sticky note by index — only the supplied fields are changed.</summary>
        private static object UpdateStickyNote(JObject parameters)
        {
            var idxTok = parameters?["index"];
            if (idxTok == null || idxTok.Type == JTokenType.Null)
                return new { error = "index is required" };
            int index = idxTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ui, _, arr) = GetStickyNotes(graph);
            int len = arr?.Length ?? 0;
            if (index < 0 || index >= len)
                throw new Exception($"index {index} out of range; graph has {len} sticky note(s)");

            var noteType = StickyNoteInfoType;
            var note = arr.GetValue(index);
            var changed = new JArray();
            if (parameters["title"] != null)
            { FindField(noteType, "title").SetValue(note, parameters["title"].ToString()); changed.Add("title"); }
            if (parameters["contents"] != null)
            { FindField(noteType, "contents").SetValue(note, parameters["contents"].ToString()); changed.Add("contents"); }
            if (parameters["colorTheme"] != null)
            { FindField(noteType, "colorTheme").SetValue(note, parameters["colorTheme"].ToObject<int>()); changed.Add("colorTheme"); }
            if (parameters["textSize"] != null)
            { FindField(noteType, "textSize").SetValue(note, parameters["textSize"].ToString()); changed.Add("textSize"); }
            var posTok = parameters["position"] as JArray;
            if (posTok != null && posTok.Count >= 4)
            {
                FindField(noteType, "position").SetValue(note, new Rect(
                    posTok[0].ToObject<float>(), posTok[1].ToObject<float>(),
                    posTok[2].ToObject<float>(), posTok[3].ToObject<float>()));
                changed.Add("position");
            }
            // StickyNoteInfo is a struct/class; SetValue on a boxed array element of a value type would
            // be lost, so write the (possibly re-boxed) element back into the array slot.
            arr.SetValue(note, index);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "update_sticky_note",
                ["assetPath"] = assetPath,
                ["index"] = index,
                ["changed"] = changed
            };
        }

        /// <summary>Remove a sticky note by index (shrinks stickyNoteInfos).</summary>
        private static object RemoveStickyNote(JObject parameters)
        {
            var idxTok = parameters?["index"];
            if (idxTok == null || idxTok.Type == JTokenType.Null)
                return new { error = "index is required" };
            int index = idxTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ui, notesField, arr) = GetStickyNotes(graph);
            int len = arr?.Length ?? 0;
            if (index < 0 || index >= len)
                throw new Exception($"index {index} out of range; graph has {len} sticky note(s)");

            var noteType = StickyNoteInfoType;
            var newArr = Array.CreateInstance(noteType, len - 1);
            int w = 0;
            for (int r = 0; r < len; r++)
                if (r != index) newArr.SetValue(arr.GetValue(r), w++);
            notesField.SetValue(ui, newArr);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_sticky_note",
                ["assetPath"] = assetPath,
                ["index"] = index,
                ["remaining"] = len - 1
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
                default:
                    return new Vector4(arr[0].ToObject<float>(), arr[1].ToObject<float>(),
                    arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
        }

        /// <summary>
        /// Runtime control of a VisualEffect component via its public API. Ops:
        /// set_asset, set_float, set_int, set_bool, set_vector2/3/4, send_event, reinit, get_state.
        /// </summary>
        public static object Runtime(JObject parameters)
        {
            try { return RuntimeCore(parameters); }
            catch (Exception ex) { return Fail("vfx_runtime", ex); }
        }

        private static object RuntimeCore(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            var gameObject = parameters?["gameObject"]?.ToString();
            if (string.IsNullOrEmpty(gameObject))
                return new { error = "gameObject is required (name of a scene object with a VisualEffect)" };

            if (op == "set_asset")
            {
                var assetPath = parameters?["assetPath"]?.ToString();
                if (string.IsNullOrEmpty(assetPath)) return new { error = "assetPath is required" };
                var comp = FindVisualEffect(gameObject);
                var asset = AssetDatabase.LoadAssetAtPath(assetPath, VisualEffectAssetType);
                if (asset == null) return new { error = $"No VisualEffectAsset at path: {assetPath}" };
                SetProp(comp, "visualEffectAsset", asset);
                Call(comp, VisualEffectType, "Reinit");
                return new JObject
                {
                    ["op"] = op,
                    ["gameObject"] = gameObject,
                    ["assetPath"] = assetPath,
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
                        if (string.IsNullOrEmpty(eventName)) return new { error = "eventName is required" };
                        Call(comp2, VisualEffectType, "SendEvent", eventName);
                        return new JObject { ["op"] = op, ["gameObject"] = gameObject, ["eventName"] = eventName };
                    }
                case "reinit":
                    Call(comp2, VisualEffectType, "Reinit");
                    return new JObject { ["op"] = op, ["gameObject"] = gameObject };
                case "get_state":
                    return RuntimeState(comp2, gameObject, name);
                default:
                    return new
                    {
                        error = $"Unsupported runtime op: '{op}'. Supported: set_asset, set_float, set_int, set_bool, " +
                                "set_vector2, set_vector3, set_vector4, send_event, reinit, get_state"
                    };
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

        // ---- vfx_settings: VFX project settings (ProjectSettings/VFXManager.asset) ----------

        private const string VFXManagerAssetPath = "ProjectSettings/VFXManager.asset";

        // Serialized fields on the VFXManager singleton (see VFXManagerEditor) — covers settings
        // that have no public static property (e.g. max capacity, batch empty lifetime).
        private static readonly string[] VfxManagerSerializedFields =
        {
            "m_FixedTimeStep", "m_MaxDeltaTime", "m_MaxScrubTime", "m_MaxCapacity", "m_BatchEmptyLifetime"
        };

        /// <summary>Read/write VFX project settings (no graph — environment capability).</summary>
        public static object Settings(JObject parameters)
        {
            try { return SettingsCore(parameters); }
            catch (Exception ex) { return Fail("vfx_settings", ex); }
        }

        private static object SettingsCore(JObject parameters)
        {
            var op = parameters?["op"]?.ToString();
            var scope = (parameters?["scope"]?.ToString() ?? "project").ToLowerInvariant();
            switch (op)
            {
                case "get":
                    return scope == "preferences" ? GetVfxPreferences() : GetVfxSettings();
                case "set":
                    return scope == "preferences" ? SetVfxPreference(parameters) : SetVfxSetting(parameters);
                default:
                    return new { error = $"Unsupported op: '{op}'. Supported: get, set" };
            }
        }

        private static JObject GetVfxSettings()
        {
            var result = new JObject { ["op"] = "get" };

            // Public static runtime properties — the canonical surface that round-trips immediately
            // on a re-read (UnityEngine.VFX.VFXManager.fixedTimeStep / maxDeltaTime / ...).
            var properties = new JObject();
            foreach (var p in VFXManagerType.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (!p.CanRead) continue;
                if (!IsScalarSettingType(p.PropertyType)) continue;
                try { properties[p.Name] = ToJToken(p.GetValue(null)); } catch { }
            }
            result["properties"] = properties;

            // Serialized asset fields (covers settings without a public static property).
            var serialized = new JObject();
            var asset = AssetDatabase.LoadAllAssetsAtPath(VFXManagerAssetPath).FirstOrDefault();
            if (asset != null)
            {
                var so = new SerializedObject(asset);
                foreach (var name in VfxManagerSerializedFields)
                {
                    var sp = so.FindProperty(name);
                    if (sp != null) serialized[name] = SerializedToJToken(sp);
                }
            }
            result["serialized"] = serialized;
            return result;
        }

        private static object SetVfxSetting(JObject parameters)
        {
            var setting = parameters?["setting"]?.ToString();
            var valueToken = parameters?["value"];
            if (string.IsNullOrEmpty(setting)) return new { error = "setting is required" };
            if (valueToken == null) return new { error = "value is required" };

            // Prefer the public static property setter: it writes through the native VFXManager and
            // the change round-trips immediately via a re-read of the same property.
            var prop = VFXManagerType.GetProperty(setting, BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanRead && prop.CanWrite && IsScalarSettingType(prop.PropertyType))
            {
                prop.SetValue(null, valueToken.ToObject(prop.PropertyType));
                return new JObject
                {
                    ["op"] = "set",
                    ["setting"] = setting,
                    ["value"] = ToJToken(prop.GetValue(null)),
                    ["via"] = "property"
                };
            }

            // Fall back to the serialized asset field (e.g. max capacity has no static setter).
            var asset = AssetDatabase.LoadAllAssetsAtPath(VFXManagerAssetPath).FirstOrDefault();
            if (asset == null) return new { error = $"{VFXManagerAssetPath} not found" };

            var so = new SerializedObject(asset);
            var fieldName = setting.StartsWith("m_")
                ? setting
                : "m_" + char.ToUpperInvariant(setting[0]) + setting.Substring(1);
            var sp = so.FindProperty(fieldName);
            if (sp == null)
                return new { error = $"No writable VFX setting '{setting}' (tried static property and serialized field '{fieldName}')" };

            AssignSerialized(sp, valueToken);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return new JObject
            {
                ["op"] = "set",
                ["setting"] = setting,
                ["value"] = SerializedToJToken(sp),
                ["via"] = "serialized"
            };
        }

        private static bool IsScalarSettingType(Type t) =>
            t == typeof(float) || t == typeof(double) || t == typeof(int) ||
            t == typeof(uint) || t == typeof(bool);

        private static JToken SerializedToJToken(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Float: return new JValue(sp.floatValue);
                case SerializedPropertyType.Integer: return new JValue(sp.longValue);
                case SerializedPropertyType.Boolean: return new JValue(sp.boolValue);
                case SerializedPropertyType.String: return new JValue(sp.stringValue);
                case SerializedPropertyType.ObjectReference: return ToJToken(sp.objectReferenceValue);
                default: return new JValue(sp.propertyType.ToString());
            }
        }

        private static void AssignSerialized(SerializedProperty sp, JToken value)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Float: sp.floatValue = value.ToObject<float>(); break;
                case SerializedPropertyType.Integer: sp.longValue = value.ToObject<long>(); break;
                case SerializedPropertyType.Boolean: sp.boolValue = value.ToObject<bool>(); break;
                case SerializedPropertyType.String: sp.stringValue = value.ToString(); break;
                default:
                    throw new Exception($"Unsupported serialized property type for set: {sp.propertyType}");
            }
        }

        // ---- vfx_settings scope:preferences (EditorPrefs via VFXViewPreference) -------------

        // Canonical preference table — paired property name + matching `xxxKey` const + storage type.
        // The constant strings hold the EditorPrefs key (e.g. "VFX.InstancingEnabled").
        // Type drives EditorPrefs.GetBool/GetInt/GetFloat and the JSON value coercion on set.
        private static readonly (string PropName, string KeyConst, string Type)[] VfxPreferences =
        {
            ("displayExperimentalOperator",        "experimentalOperatorKey",                  "bool"),
            ("displayExtraDebugInfo",              "extraDebugInfoKey",                        "bool"),
            ("forceEditionCompilation",            "forceEditionCompilationKey",               "bool"),
            ("generateShadersWithDebugSymbols",    "generateShadersWithDebugSymbolsKey",       "bool"),
            ("advancedLogs",                       "advancedLogsKey",                          "bool"),
            ("cameraBuffersFallback",              "cameraBuffersFallbackKey",                 "enum"),
            ("multithreadUpdateEnabled",           "multithreadUpdateEnabledKey",              "bool"),
            ("instancingEnabled",                  "instancingEnabledKey",                     "bool"),
            ("authoringPrewarmStepCountPerSeconds","authoringPrewarmStepCountPerSecondsKey",   "int"),
            ("authoringPrewarmMaxTime",            "authoringPrewarmMaxTimeKey",               "float"),
            ("visualEffectTargetListed",           "visualEffectTargetListedKey",              "bool"),
            // No public getter property on VFXViewPreference — only the key constant + a private
            // field — so this one reads/writes EditorPrefs directly (see ReadPref's fallback).
            ("allowShaderExternalization",         "allowShaderExternalizationKey",            "bool"),
        };

        private static string PrefKey(string keyConstName)
        {
            var f = VFXViewPreferenceType.GetField(keyConstName, BindingFlags.Public | BindingFlags.Static);
            if (f == null) throw new Exception($"VFXViewPreference key constant not found: {keyConstName}");
            return (string)f.GetValue(null);
        }

        /// <summary>
        /// Read a preference's current value. Prefers the canonical public static property (which
        /// reflects VFXViewPreference's own cache); for prefs that expose only an EditorPrefs key
        /// constant and no getter property (allowShaderExternalization), reads EditorPrefs directly
        /// by key + type.
        /// </summary>
        private static object ReadPref((string PropName, string KeyConst, string Type) entry)
        {
            var p = VFXViewPreferenceType.GetProperty(entry.PropName, BindingFlags.Public | BindingFlags.Static);
            if (p != null) return p.GetValue(null);
            string key = PrefKey(entry.KeyConst);
            switch (entry.Type)
            {
                case "int":   return EditorPrefs.GetInt(key, 0);
                case "float": return EditorPrefs.GetFloat(key, 0f);
                default:      return EditorPrefs.GetBool(key, false);
            }
        }

        private static JObject GetVfxPreferences()
        {
            var properties = new JObject();
            foreach (var entry in VfxPreferences)
            {
                try { properties[entry.PropName] = ToJToken(ReadPref(entry)); } catch { }
            }
            return new JObject
            {
                ["op"] = "get",
                ["scope"] = "preferences",
                ["properties"] = properties
            };
        }

        private static object SetVfxPreference(JObject parameters)
        {
            var setting = parameters?["setting"]?.ToString();
            var valueToken = parameters?["value"];
            if (string.IsNullOrEmpty(setting)) return new { error = "setting is required" };
            if (valueToken == null) return new { error = "value is required" };

            var entry = VfxPreferences.FirstOrDefault(e =>
                string.Equals(e.PropName, setting, StringComparison.Ordinal));
            if (string.IsNullOrEmpty(entry.PropName))
                return new
                {
                    error = $"Unknown VFX preference '{setting}'. Known: " +
                            string.Join(", ", VfxPreferences.Select(e => e.PropName))
                };

            string key = PrefKey(entry.KeyConst);
            switch (entry.Type)
            {
                case "bool":  EditorPrefs.SetBool(key, valueToken.ToObject<bool>()); break;
                case "int":   EditorPrefs.SetInt(key, valueToken.ToObject<int>()); break;
                case "float": EditorPrefs.SetFloat(key, valueToken.ToObject<float>()); break;
                case "enum":
                {
                    // cameraBuffersFallback is stored as int (the enum's underlying value).
                    int v;
                    if (valueToken.Type == JTokenType.String)
                    {
                        var enumType = VFXViewPreferenceType.GetProperty(entry.PropName,
                            BindingFlags.Public | BindingFlags.Static).PropertyType;
                        v = (int)Enum.Parse(enumType, valueToken.ToString(), ignoreCase: true);
                    }
                    else { v = valueToken.ToObject<int>(); }
                    EditorPrefs.SetInt(key, v);
                    break;
                }
                default: throw new Exception($"Unsupported preference type: {entry.Type}");
            }

            // VFXViewPreference caches values via its private LoadIfNeeded — invalidate so the next
            // property read returns the new value (the canonical round-trip surface).
            try { Call(null, VFXViewPreferenceType, "SetDirty"); } catch { }

            return new JObject
            {
                ["op"] = "set",
                ["scope"] = "preferences",
                ["setting"] = setting,
                ["value"] = ToJToken(ReadPref(entry)),
                ["editorPrefsKey"] = key
            };
        }
    }
}
