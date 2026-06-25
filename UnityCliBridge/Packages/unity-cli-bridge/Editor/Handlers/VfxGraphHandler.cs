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
        private static Type CategoryInfoType => T("UnityEditor.VFX.VFXUI+CategoryInfo");
        private static Type TemplateHelperType => T("UnityEditor.VFX.VFXTemplateHelperInternal");
        private static Type TemplateDescriptorType => T("UnityEditor.Experimental.GraphView.GraphViewTemplateDescriptor");
        private static Type ErrorReporterType => T("UnityEditor.VFX.VFXErrorReporter");
        private static Type ErrorOriginType => T("UnityEditor.VFX.VFXErrorOrigin");
        private static Type AssetEditorUtilityType => T("UnityEditor.VisualEffectAssetEditorUtility");
        private static Type VFXManagerType => T("UnityEngine.VFX.VFXManager");
        private static Type VFXViewPreferenceType => T("UnityEditor.VFX.VFXViewPreference");
        private static Type MemorySerializerType => T("UnityEditor.VFX.VFXMemorySerializer");
        private static Type SystemNamesType => T("UnityEditor.VFX.VFXSystemNames");
        private static Type SubgraphContextType => T("UnityEditor.VFX.VFXSubgraphContext");

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
            // Gradient is a plain class — Newtonsoft would emit its ToString ("UnityEngine.Gradient"),
            // hiding the keys. Hand-serialize color/alpha keys so a gradient value round-trips in describe.
            if (value is Gradient grad)
            {
                var ck = new JArray();
                foreach (var k in grad.colorKeys)
                    ck.Add(new JObject { ["color"] = ToJToken(k.color), ["time"] = k.time });
                var ak = new JArray();
                foreach (var k in grad.alphaKeys)
                    ak.Add(new JObject { ["alpha"] = k.alpha, ["time"] = k.time });
                return new JObject { ["colorKeys"] = ck, ["alphaKeys"] = ak, ["mode"] = grad.mode.ToString() };
            }
            // MultipleValuesChoice<T> (the Custom HLSL function selector): selection/selectedIndex are
            // private; `values` is a rebuilt (non-serialized) list. Surface the active selection + choices
            // so the function selector is verifiable in describe.
            if (t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("MultipleValuesChoice"))
            {
                var obj = new JObject();
                try { obj["selection"] = ToJToken(t.GetMethod("GetSelection").Invoke(value, null)); }
                catch { obj["selection"] = JValue.CreateNull(); }
                try
                {
                    var vals = t.GetProperty("values")?.GetValue(value) as IEnumerable;
                    var arr = new JArray();
                    if (vals != null) foreach (var v in vals) arr.Add(v?.ToString());
                    obj["values"] = arr;
                }
                catch { /* values unavailable */ }
                return obj;
            }
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

        /// <summary>The slot's declared CLR value type (e.g. Single/Vector3/Texture2D), resolved from
        /// `VFXProperty.type` (a property on VFXSlot). Survives a null current value — Object-typed slots
        /// (Texture/Mesh) default to null, so this is the only way to know what to coerce a value into.</summary>
        private static Type SlotClrType(object slot)
        {
            try
            {
                var property = Prop(slot, "property");
                var typeProp = property.GetType().GetProperty("type"); // VFXProperty.type is a property
                return typeProp?.GetValue(property) as Type;
            }
            catch { return null; }
        }

        /// <summary>The slot's declared value type name (e.g. "Single"/"Vector3") — surfaces operand-type
        /// changes on dynamic operators.</summary>
        private static string SlotValueTypeName(object slot) => SlotClrType(slot)?.Name;

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
                    var entry = new JObject
                    {
                        ["index"] = idx++,
                        ["name"] = SlotName(slot),
                        ["valueType"] = SlotValueTypeName(slot),
                        ["hasLink"] = links.Count > 0,
                        ["links"] = links,
                        ["value"] = value
                    };
                    // Spaceable slots (Position/Vector/Direction-style) carry a coordinate space —
                    // surface it only when present so set_slot_space round-trips and non-spaceable
                    // slots stay uncluttered.
                    try
                    {
                        if ((bool)Prop(slot, "spaceable"))
                            entry["space"] = ToJToken(Prop(slot, "space"));
                    }
                    catch { }
                    arr.Add(entry);
                }
                return arr;
            }

            // A block's activation slot is the per-particle/frame boolean "Activation" port. It is NOT
            // in the block's inputSlots collection (it's the special `activationSlot`), so the regular
            // SlotsJson misses it — surface it here so link-driven activation (link_slots …activation:true)
            // is verifiable. Returns null for blocks/models without one.
            JObject ActivationSlotJson(object block)
            {
                object actSlot = null;
                try { actSlot = Prop(block, "activationSlot"); }
                catch { return null; }
                if (actSlot == null) return null;
                var links = LinksJson(actSlot);
                JToken value = null;
                try { value = ToJToken(Prop(actSlot, "value")); }
                catch { }
                return new JObject
                {
                    ["name"] = SlotName(actSlot),
                    ["valueType"] = SlotValueTypeName(actSlot),
                    ["hasLink"] = links.Count > 0,
                    ["links"] = links,
                    ["value"] = value
                };
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
                        ["outputSlots"] = SlotsJson(b, false),
                        ["activationSlot"] = ActivationSlotJson(b)
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

                // systemName — the system's display label. Stored on VFXData.title (particle
                // systems) or VFXContext.label (Spawner), surfaced via the static helper
                // VFXSystemNames.GetSystemName. Contexts sharing one VFXData report the same name;
                // empty/unset systems return null.
                string systemName = null;
                try
                {
                    systemName = Call(null, SystemNamesType, "GetSystemName", ctx) as string;
                }
                catch { /* contexts whose data has no name helper — leave null */ }

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
                    ["systemName"] = systemName,
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
                bool isOutput = false;
                JToken value = null, min = null, max = null, valueFilter = null, order = null;
                try { exposedName = Prop(p, "exposedName") as string; } catch { }
                try { exposed = (bool)Prop(p, "exposed"); } catch { }
                try { isOutput = (bool)Prop(p, "isOutput"); } catch { }
                try { category = Prop(p, "category") as string; } catch { }
                try { tooltip = Prop(p, "tooltip") as string; } catch { }
                try { value = ToJToken(Prop(p, "value")); } catch { }
                try { min = ToJToken(Prop(p, "min")); } catch { }
                try { max = ToJToken(Prop(p, "max")); } catch { }
                try { valueFilter = new JValue(Prop(p, "valueFilter")?.ToString()); } catch { }
                try { order = new JValue(Convert.ToInt32(Prop(p, "order"))); } catch { }
                paramsJson.Add(new JObject
                {
                    ["index"] = i,
                    ["type"] = p.GetType().Name,
                    ["parameterType"] = (Prop(p, "type") as Type)?.Name,
                    ["exposedName"] = exposedName,
                    ["exposed"] = exposed,
                    ["isOutput"] = isOutput,
                    ["category"] = category,
                    ["order"] = order,
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
            var categories = CategoriesJson(graph);
            var customAttributes = CustomAttributesJson(graph);
            string initialEventName = null;
            try { initialEventName = InitialEventNameOf(Prop(graph, "visualEffectResource")); }
            catch { /* resource unavailable — leave null */ }
            JObject instancing = null;
            try { instancing = InstancingJson(graph); }
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
                ["categories"] = categories,
                ["customAttributeCount"] = customAttributes.Count,
                ["customAttributes"] = customAttributes,
                ["initialEventName"] = initialEventName,
                ["instancing"] = instancing,
                ["template"] = TemplateInfoJson(assetPath),
                ["errors"] = errors
            };
        }

        /// <summary>The graph's blackboard-managed custom attributes (name/type/description).</summary>
        private static JArray CustomAttributesJson(object graph)
        {
            var arr = new JArray();
            try
            {
                if (!(Prop(graph, "customAttributes") is IEnumerable list)) return arr;
                foreach (var desc in list)
                {
                    arr.Add(new JObject
                    {
                        ["attributeName"] = Prop(desc, "attributeName")?.ToString(),
                        ["type"] = Prop(desc, "type")?.ToString(),
                        ["description"] = Prop(desc, "description")?.ToString(),
                        ["isReadOnly"] = (bool)(Prop(desc, "isReadOnly") ?? false)
                    });
                }
            }
            catch { /* older package without customAttributes — leave empty */ }
            return arr;
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

        /// <summary>Read the graph's blackboard category list (order = display order) as JSON. Reflects the
        /// stored VFXUI.categories; an unsynced graph may report fewer entries than the params reference until
        /// a category op (e.g. reorder_category) syncs them.</summary>
        private static JArray CategoriesJson(object graph)
        {
            var arr = new JArray();
            object ui;
            try { ui = Prop(graph, "UIInfos"); }
            catch { return arr; }
            if (ui == null) return arr;
            var list = FindField(ui.GetType(), "categories")?.GetValue(ui) as IEnumerable;
            if (list == null) return arr;
            int idx = 0;
            var nameField = FindField(CategoryInfoType, "name");
            var collapsedField = FindField(CategoryInfoType, "collapsed");
            foreach (var c in list)
            {
                arr.Add(new JObject
                {
                    ["index"] = idx++,
                    ["name"] = nameField?.GetValue(c) as string,
                    ["collapsed"] = (bool)(collapsedField?.GetValue(c) ?? false)
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
                case "add_operator_input": return AddOperatorInput(parameters);
                case "remove_operator_input": return RemoveOperatorInput(parameters);
                case "set_operator_operand_type": return SetOperatorOperandType(parameters);
                case "rename_operator_input": return RenameOperatorInput(parameters);
                case "reorder_operator_input": return ReorderOperatorInput(parameters);
                case "set_context_setting": return SetContextSetting(parameters);
                case "add_context": return AddContext(parameters);
                case "add_operator": return AddOperator(parameters);
                case "add_parameter": return AddParameter(parameters);
                case "link_slots": return LinkSlots(parameters);
                case "set_slot_value": return SetSlotValue(parameters);
                case "set_slot_space": return SetSlotSpace(parameters);
                case "convert_to_property": return ConvertToProperty(parameters);
                case "convert_to_inline": return ConvertToInline(parameters);
                case "unlink_slots": return UnlinkSlots(parameters);
                case "remove_block": return RemoveBlock(parameters);
                case "set_block_enabled": return SetBlockEnabled(parameters);
                case "reorder_block": return ReorderBlock(parameters);
                case "move_block": return MoveBlock(parameters);
                case "duplicate_block": return DuplicateBlock(parameters);
                case "duplicate_operator": return DuplicateOperator(parameters);
                case "remove_operator": return RemoveOperator(parameters);
                case "remove_parameter": return RemoveParameter(parameters);
                case "rename_parameter": return RenameParameter(parameters);
                case "set_parameter_category": return SetParameterCategory(parameters);
                case "rename_category": return RenameCategory(parameters);
                case "reorder_category": return ReorderCategory(parameters);
                case "reorder_parameter": return ReorderParameter(parameters);
                case "duplicate_parameter": return DuplicateParameter(parameters);
                case "remove_context": return RemoveContext(parameters);
                case "delete_system": return DeleteSystem(parameters);
                case "set_system_name": return SetSystemName(parameters);
                case "add_custom_attribute": return AddCustomAttribute(parameters);
                case "link_flow": return LinkFlow(parameters);
                case "unlink_flow": return UnlinkFlow(parameters);
                case "set_bounds": return SetBounds(parameters);
                case "add_sticky_note": return AddStickyNote(parameters);
                case "update_sticky_note": return UpdateStickyNote(parameters);
                case "remove_sticky_note": return RemoveStickyNote(parameters);
                case "reorder_sticky_note": return ReorderStickyNote(parameters);
                case "set_instancing": return SetInstancing(parameters);
                case "set_initial_event_name": return SetInitialEventName(parameters);
                case "create_subgraph_asset": return CreateSubgraphAsset(parameters);
                case "create_from_template": return CreateFromTemplate(parameters);
                case "insert_template": return InsertTemplate(parameters);
                case "designate_template": return DesignateTemplate(parameters);
                default:
                    return new { error = $"Unsupported op: '{op}'. Supported: add_block, set_block_setting, set_block_enabled, reorder_block, move_block, duplicate_block, duplicate_operator, add_context, add_operator, add_parameter, link_slots, set_slot_value, unlink_slots, set_operator_setting, set_context_setting, remove_block, remove_operator, remove_parameter, remove_context, link_flow, set_bounds, add_sticky_note, update_sticky_note, remove_sticky_note, set_instancing, create_subgraph_asset, create_from_template" };
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

        /// <summary>
        /// Resolve the context a block op (or a link endpoint) targets. Prefers an explicit
        /// `contextIndex` — the absolute position in the graph's context list — so a caller can
        /// disambiguate two contexts of the SAME `contextType` (e.g. two Spawners across two systems);
        /// otherwise falls back to the first context whose `contextType` matches. `idxKey`/`typeKey`
        /// let move/duplicate address a *destination* (`toContextIndex`/`toContextType`). `defaultType`
        /// supplies a fallback contextType when neither is given (only AddBlock uses it, default "Update").
        /// Throws on out-of-range / not-found / neither-supplied.
        /// </summary>
        private static object ResolveBlockContext(object graph, JObject node, string idxKey = "contextIndex",
            string typeKey = "contextType", string defaultType = null)
        {
            var ciTok = node?[idxKey];
            if (ciTok != null && ciTok.Type != JTokenType.Null)
            {
                var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
                int ci = ciTok.ToObject<int>();
                if (ci < 0 || ci >= ctxList.Count)
                    throw new Exception($"{idxKey} {ci} out of range; graph has {ctxList.Count} context(s)");
                return ctxList[ci];
            }
            var ct = node?[typeKey]?.ToString();
            if (string.IsNullOrEmpty(ct)) ct = defaultType;
            if (string.IsNullOrEmpty(ct))
                throw new Exception($"{typeKey} or {idxKey} is required");
            var ctx = FindContext(graph, ct);
            if (ctx == null)
                throw new Exception($"No context of type '{ct}' found (or use {idxKey} to address it by position)");
            return ctx;
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
            var blockName = parameters?["blockName"]?.ToString();
            if (string.IsNullOrEmpty(blockName))
                return new { error = "blockName is required" };

            var graph = LoadGraph(assetPath);

            var targetContext = ResolveBlockContext(graph, parameters, defaultType: "Update");
            var wantContext = Prop(targetContext, "contextType")?.ToString();

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
            // MultipleValuesChoice<T> (Custom HLSL m_AvailableFunction(s)): the setting's serialized
            // state is the selected value; the choice list is rebuilt on resync. Accept a plain string
            // (the function name) and stamp it as the selection — the block/operator's resync then picks
            // the matching function and reshapes its slots.
            if (field.FieldType.IsGenericType &&
                field.FieldType.GetGenericTypeDefinition().Name.StartsWith("MultipleValuesChoice"))
            {
                var sel = valueToken.ToString();
                var argType = field.FieldType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(argType);
                var list = Activator.CreateInstance(listType);
                listType.GetMethod("Add").Invoke(list, new[] { (object)sel });
                object choice = Activator.CreateInstance(field.FieldType); // boxed struct
                field.FieldType.GetProperty("values").SetValue(choice, list);
                field.FieldType.GetMethod("SetSelection").Invoke(choice, new[] { (object)sel });
                return choice;
            }
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

        /// <summary>Resolve a graph operator by `operatorIndex` (order among graph operators).</summary>
        private static object ResolveOperatorByIndex(object graph, int operatorIndex)
        {
            var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            if (operatorIndex < 0 || operatorIndex >= ops.Count)
                throw new Exception($"operatorIndex {operatorIndex} out of range; graph has {ops.Count} operator(s)");
            return ops[operatorIndex];
        }

        /// <summary>Resolve a user type name (e.g. "Vector3", "float") against the operator's validTypes.</summary>
        private static Type ResolveOperandType(object op, string typeName)
        {
            var valid = (Prop(op, "validTypes") as IEnumerable)?.Cast<Type>().ToList()
                ?? throw new Exception($"Operator '{op.GetType().Name}' has no operand types (not a dynamic operator).");
            string Squash(string s) => s.Replace(" ", string.Empty);
            var match = valid.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? valid.FirstOrDefault(t => string.Equals(Squash(t.Name), Squash(typeName), StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new Exception(
                    $"Type '{typeName}' is not valid for operator '{op.GetType().Name}'. Valid: {string.Join(", ", valid.Select(t => t.Name))}");
            return match;
        }

        private static bool HasMethod(object op, string name, int paramCount) =>
            op.GetType().GetMethods(AllInstance).Any(m => m.Name == name && m.GetParameters().Length == paramCount);

        /// <summary>
        /// Add an operand (input) to a cascaded numeric operator (Add/Multiply/… — VFXOperatorNumericCascaded).
        /// Optional `operandType` (defaults to the operator's current default type). Slots grow by one.
        /// </summary>
        private static object AddOperatorInput(JObject parameters)
        {
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;
            var operandType = parameters?["operandType"]?.ToString() ?? parameters?["type"]?.ToString();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var op = ResolveOperatorByIndex(graph, operatorIndex);
            if (!HasMethod(op, "AddOperand", 1))
                return new
                {
                    error =
                        $"Operator '{op.GetType().Name}' is not a cascaded operator — it has no add/remove input " +
                        "(only operators like Add/Multiply do)."
                };

            Type t = string.IsNullOrEmpty(operandType) ? null : ResolveOperandType(op, operandType);
            Call(op, op.GetType(), "AddOperand", t);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_operator_input",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["operandCount"] = ToJToken(Prop(op, "operandCount"))
            };
        }

        /// <summary>
        /// Remove an operand (input) from a cascaded numeric operator. Optional `index` (default: last).
        /// Refuses to drop below the operator's MinimalOperandCount.
        /// </summary>
        private static object RemoveOperatorInput(JObject parameters)
        {
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;
            var idxTok = parameters?["index"];
            bool hasIndex = idxTok != null && idxTok.Type != JTokenType.Null;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var op = ResolveOperatorByIndex(graph, operatorIndex);
            if (!HasMethod(op, "RemoveOperand", 1))
                return new
                {
                    error =
                        $"Operator '{op.GetType().Name}' is not a cascaded operator — it has no add/remove input."
                };

            int count = Convert.ToInt32(Prop(op, "operandCount"));
            int minimal = 2;
            try { minimal = Convert.ToInt32(Prop(op, "MinimalOperandCount")); } catch { /* default 2 */ }
            if (count <= minimal)
                return new { error = $"Cannot remove input: operator '{op.GetType().Name}' is at its minimum of {minimal} operand(s)." };

            if (hasIndex)
            {
                int idx = idxTok.ToObject<int>();
                if (idx < 0 || idx >= count)
                    return new { error = $"index {idx} out of range; operator has {count} operand(s)." };
                Call(op, op.GetType(), "RemoveOperand", idx); // RemoveOperand(int)
            }
            else
            {
                Call(op, op.GetType(), "RemoveOperand"); // removes the last operand
            }
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_operator_input",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["operandCount"] = ToJToken(Prop(op, "operandCount"))
            };
        }

        /// <summary>
        /// Set a dynamic operator's operand type. Uniform operators (one shared type) take just
        /// `operandType`; unified/cascaded operators take an `index` (else all operands are set). The
        /// type must be one of the operator's validTypes (e.g. "Float", "Vector3"). Slots re-type.
        /// </summary>
        private static object SetOperatorOperandType(JObject parameters)
        {
            var operandType = parameters?["operandType"]?.ToString() ?? parameters?["type"]?.ToString();
            if (string.IsNullOrEmpty(operandType))
                return new { error = "operandType is required (e.g. \"Float\", \"Vector3\")" };
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;
            var idxTok = parameters?["index"];
            bool hasIndex = idxTok != null && idxTok.Type != JTokenType.Null;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var op = ResolveOperatorByIndex(graph, operatorIndex);
            Type t = ResolveOperandType(op, operandType);

            string via;
            if (HasMethod(op, "SetOperandType", 1)) // uniform: SetOperandType(Type)
            {
                Call(op, op.GetType(), "SetOperandType", t);
                via = "uniform";
            }
            else if (HasMethod(op, "SetOperandType", 2)) // unified/cascaded: SetOperandType(int, Type)
            {
                int count = Convert.ToInt32(Prop(op, "operandCount"));
                if (hasIndex)
                {
                    int idx = idxTok.ToObject<int>();
                    if (idx < 0 || idx >= count)
                        return new { error = $"index {idx} out of range; operator has {count} operand(s)." };
                    Call(op, op.GetType(), "SetOperandType", idx, t);
                    via = $"operand[{idx}]";
                }
                else
                {
                    for (int i = 0; i < count; i++) Call(op, op.GetType(), "SetOperandType", i, t);
                    via = "all-operands";
                }
            }
            else
            {
                return new { error = $"Operator '{op.GetType().Name}' has no settable operand type (not a dynamic operator)." };
            }
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_operator_operand_type",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["operandType"] = t.Name,
                ["via"] = via
            };
        }

        /// <summary>
        /// Rename a cascaded operator's operand (input) — `SetOperandName(index, name)`. The operand name
        /// drives the input slot's name, so describe surfaces the change as `operators[].inputSlots[].name`.
        /// Only cascaded operators (Add/Multiply/Append-style) have named operands.
        /// </summary>
        private static object RenameOperatorInput(JObject parameters)
        {
            var name = parameters?["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { error = "name is required" };
            var idxTok = parameters?["index"];
            if (idxTok == null || idxTok.Type == JTokenType.Null)
                return new { error = "index is required (which operand to rename)" };
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var op = ResolveOperatorByIndex(graph, operatorIndex);
            if (!HasMethod(op, "SetOperandName", 2))
                return new
                {
                    error =
                        $"Operator '{op.GetType().Name}' has no named operands — only cascaded operators " +
                        "(Add/Multiply/Append-style) can rename inputs."
                };

            int count = Convert.ToInt32(Prop(op, "operandCount"));
            int idx = idxTok.ToObject<int>();
            if (idx < 0 || idx >= count)
                return new { error = $"index {idx} out of range; operator has {count} operand(s)." };

            Call(op, op.GetType(), "SetOperandName", idx, name);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "rename_operator_input",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["index"] = idx,
                ["name"] = ToJToken(Call(op, op.GetType(), "GetOperandName", idx))
            };
        }

        /// <summary>
        /// Reorder a cascaded operator's operands — `OperandMoved(movedIndex, targetIndex)` (it moves the
        /// matching input slot in lockstep so links survive). `index` = the operand to move, `toIndex` =
        /// its new position. Describe surfaces the new order via `operators[].inputSlots[]`.
        /// </summary>
        private static object ReorderOperatorInput(JObject parameters)
        {
            var idxTok = parameters?["index"];
            if (idxTok == null || idxTok.Type == JTokenType.Null)
                return new { error = "index is required (which operand to move)" };
            var toTok = parameters?["toIndex"];
            if (toTok == null || toTok.Type == JTokenType.Null)
                return new { error = "toIndex is required (the operand's new position)" };
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var op = ResolveOperatorByIndex(graph, operatorIndex);
            if (!HasMethod(op, "OperandMoved", 2))
                return new
                {
                    error =
                        $"Operator '{op.GetType().Name}' has no reorderable operands — only cascaded operators " +
                        "(Add/Multiply/Append-style) can reorder inputs."
                };

            int count = Convert.ToInt32(Prop(op, "operandCount"));
            int idx = idxTok.ToObject<int>();
            int toIndex = toTok.ToObject<int>();
            if (idx < 0 || idx >= count)
                return new { error = $"index {idx} out of range; operator has {count} operand(s)." };
            if (toIndex < 0 || toIndex >= count)
                return new { error = $"toIndex {toIndex} out of range; operator has {count} operand(s)." };

            Call(op, op.GetType(), "OperandMoved", idx, toIndex);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "reorder_operator_input",
                ["assetPath"] = assetPath,
                ["operatorIndex"] = operatorIndex,
                ["operator"] = op.GetType().Name,
                ["index"] = idx,
                ["toIndex"] = toIndex,
                ["operandCount"] = ToJToken(Prop(op, "operandCount"))
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

            // Composed-output settings (e.g. a Shader Graph output's `shaderGraph`) live on a nested
            // sub-object, so FindField on the context/data TYPE misses them. The model's own virtual
            // GetSetting resolves composed/nested settings (returns the FieldInfo + its owning instance);
            // use that field for coercion and the model's SetSettingValue, which writes the nested
            // instance and runs the proper invalidation.
            var composedSetting = Call(ctx, ModelType, "GetSetting", settingName);
            var composedField = composedSetting?.GetType()
                .GetField("field", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(composedSetting) as FieldInfo;
            if (composedField != null)
            {
                object convertedComposed = CoerceSettingValue(composedField, valueToken, settingName);
                Call(ctx, ModelType, "SetSettingValue", settingName, convertedComposed);
                Persist(graph, assetPath);
                return SetContextSettingResult(assetPath, ctx, settingName, "context-composed", ToJToken(convertedComposed));
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

        /// <summary>
        /// Set a system's display label, addressing the system by any one member context
        /// (`contextType` or `index`). The name lives on VFXData.title for a particle system
        /// (so every Init/Update/Output member reports it) or VFXContext.label for a Spawner —
        /// VFXSystemNames.SetSystemName routes to the right one. Verified via the describe oracle's
        /// per-context `systemName`.
        /// </summary>
        private static object SetSystemName(JObject parameters)
        {
            bool hasIndex = parameters?["index"] != null && parameters["index"].Type != JTokenType.Null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index) is required" };
            var name = parameters?["name"]?.ToString();
            if (name == null)
                return new { error = "name is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var target = ResolveContextRef(graph, parameters, ctxList, "context");

            // Static helper: routes Spawner→context.label, data-backed context→VFXData.title.
            Call(null, SystemNamesType, "SetSystemName", target, name);
            var applied = Call(null, SystemNamesType, "GetSystemName", target) as string;
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_system_name",
                ["assetPath"] = assetPath,
                ["contextType"] = Prop(target, "contextType")?.ToString(),
                ["systemName"] = applied
            };
        }

        /// <summary>
        /// Create a blackboard-managed custom attribute on the graph (VFXGraph.TryAddCustomAttribute).
        /// The user type is one of the VFX Signature names (Float/Vector2/Vector3/Vector4/Bool/Uint/Int),
        /// mapped to a VFXValueType via the package's CustomAttributeUtility. Once created, a Set/Get
        /// block referencing the name (`|Set|_<Name>` / `Get|_<Name>`) composes via add_block/add_operator.
        /// </summary>
        private static object AddCustomAttribute(JObject parameters)
        {
            var name = parameters?["attributeName"]?.ToString() ?? parameters?["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { error = "attributeName is required" };
            var typeName = parameters?["attributeType"]?.ToString() ?? parameters?["type"]?.ToString();
            if (string.IsNullOrEmpty(typeName))
                return new { error = "attributeType is required (Float/Vector2/Vector3/Vector4/Bool/Uint/Int)" };
            var description = parameters?["description"]?.ToString() ?? string.Empty;
            bool isReadOnly = parameters?["isReadOnly"]?.ToObject<bool>() ?? false;

            // Resolve the friendly type name → a Signature through the package's own enum, so we stay
            // aligned with whatever value types the installed package supports. A bad type is expected
            // user-input validation → quiet early return (before LoadGraph), not a logged exception.
            var sigType = T("UnityEditor.VFX.Block.CustomAttributeUtility+Signature");
            object signature;
            try { signature = Enum.Parse(sigType, typeName, true); }
            catch
            {
                return new
                {
                    error =
                        $"Unknown attribute type '{typeName}'. Valid: {string.Join(", ", Enum.GetNames(sigType))}"
                };
            }

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var custUtilType = T("UnityEditor.VFX.Block.CustomAttributeUtility");
            var valueType = Call(null, custUtilType, "GetValueType", signature);

            // TryAddCustomAttribute(string, VFXValueType, string, bool, out VFXAttribute) — the out param
            // needs a manual Invoke (the Call helper can't surface a by-ref result).
            var method = GraphType.GetMethod("TryAddCustomAttribute", AllInstance);
            if (method == null)
                throw new Exception("VFXGraph.TryAddCustomAttribute not found (package version mismatch).");
            var args = new object[] { name, valueType, description, isReadOnly, null };
            bool ok = (bool)method.Invoke(graph, args);
            if (!ok)
                // A name collision (built-in or existing custom attribute) is expected user-input
                // validation → quiet early return, not a logged exception.
                return new
                {
                    error =
                        $"Failed to add custom attribute '{name}' — the name may collide with a built-in " +
                        "attribute or an existing custom attribute (names are case-insensitive)."
                };

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_custom_attribute",
                ["assetPath"] = assetPath,
                ["attributeName"] = name,
                ["attributeType"] = signature.ToString(),
                ["description"] = description,
                ["isReadOnly"] = isReadOnly,
                ["customAttributeCount"] = CustomAttributesJson(graph).Count
            };
        }

        private static object SetBlockSetting(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var settingName = parameters?["setting"]?.ToString();
            if (string.IsNullOrEmpty(settingName))
                return new { error = "setting is required" };
            var valueToken = parameters?["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return new { error = "value is required" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;

            var graph = LoadGraph(assetPath);

            var targetContext = ResolveBlockContext(graph, parameters, defaultType: "Update");
            var wantContext = Prop(targetContext, "contextType")?.ToString();

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

            object context;
            string matchedDescriptor;
            // System-subgraph reference: VFXSubgraphContext is NOT in the node library (it's added by
            // dropping a .vfx onto the canvas), so instantiate it directly and point m_Subgraph at the
            // referenced .vfx by path. Mirrors VFXConvertSubgraph's CreateInstance + AddChild + m_Subgraph.
            if (contextName.IndexOf("subgraph", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                context = ScriptableObject.CreateInstance(SubgraphContextType);
                if (context == null)
                    throw new Exception("Failed to instantiate VFXSubgraphContext");
                Call(graph, ModelType, "AddChild", context, -1, true);

                var subgraphPath = parameters?["subgraphPath"]?.ToString();
                if (!string.IsNullOrEmpty(subgraphPath))
                {
                    var subAsset = AssetDatabase.LoadAssetAtPath(subgraphPath, VisualEffectAssetType);
                    if (subAsset == null)
                        throw new Exception($"No VisualEffectAsset (.vfx) at subgraphPath: {subgraphPath}");
                    Call(context, ModelType, "SetSettingValue", "m_Subgraph", subAsset);
                }
                matchedDescriptor = "Subgraph";
            }
            else
            {
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

                context = Call(match, match.GetType(), "CreateInstance");
                if (context == null)
                    throw new Exception($"CreateInstance returned null for context '{contextName}'");

                Call(graph, ModelType, "AddChild", context, -1, true);
                matchedDescriptor = Prop(match, "name") as string;
            }

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
                ["matchedDescriptor"] = matchedDescriptor,
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

            // Output parameter (operator/system subgraph): isOutput=true makes the param a SUBGRAPH
            // OUTPUT — VFXSubgraphOperator's OutputPredicate is `param.isOutput`, so the parent's
            // subgraph node surfaces it as an output slot. The property setter swaps the param's slot
            // from output→input (the value flows IN from inside the subgraph) and forces m_Exposed=false,
            // so set it LAST (after value, which the swap preserves).
            bool isOutput = parameters?["isOutput"]?.ToObject<bool>() ?? false;
            if (isOutput) SetProp(parameter, "isOutput", true);

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
                ["exposed"] = (bool)Prop(parameter, "exposed"),
                ["isOutput"] = (bool)Prop(parameter, "isOutput"),
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
                        return ResolveBlockContext(graph, node);
                    }
                case "block":
                    {
                        var ctx = ResolveBlockContext(graph, node);
                        int bi = node["blockIndex"]?.ToObject<int>() ?? 0;
                        var blocks = Children(ctx).ToList();
                        if (bi < 0 || bi >= blocks.Count)
                            throw new Exception($"{label} blockIndex {bi} out of range; context has {blocks.Count} block(s)");
                        return blocks[bi];
                    }
                default:
                    throw new Exception($"{label} has unknown node kind '{kind}'. Supported: operator, parameter, context, block");
            }
        }

        /// <summary>
        /// Resolve the input slot an endpoint addresses. Normally the index-th input slot, but when the
        /// endpoint carries `activation:true` it's the block's special `activationSlot` — the boolean
        /// "Activation" port the editor exposes to drive a block on/off per particle/frame. That slot is
        /// NOT in `inputSlots`, so it can only be reached by the flag. Only blocks have an activation slot.
        /// </summary>
        private static object ResolveInputSlot(object node, JObject endpoint, string label)
        {
            if (endpoint?["activation"]?.ToObject<bool>() == true)
            {
                object actSlot = null;
                try { actSlot = Prop(node, "activationSlot"); }
                catch { /* operators/contexts have no activation slot */ }
                if (actSlot == null)
                    throw new Exception(
                        $"{label} activation:true requires a block with an activation slot; " +
                        $"'{node.GetType().Name}' has none");
                return actSlot;
            }
            int idx = endpoint?["slot"]?.ToObject<int>() ?? 0;
            return GetSlot(node, true, idx, label);
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

        /// <summary>
        /// Walk into a compound slot's descriptor-named child sub-slots. Each `subPath` element selects a
        /// child by name (e.g. a Sphere slot's "radius"/"transform") or by integer index; nesting is
        /// supported (e.g. ["transform","position"]). Returns the slot itself when subPath is null/empty.
        /// Lets link_slots/unlink_slots target a sub-slot (e.g. link a float into `sphere`'s `radius`),
        /// the link-side analogue of set_slot_value's value-struct `subPath`.
        /// </summary>
        private static object DescendSlot(object slot, string[] subPath, string label)
        {
            if (subPath == null || subPath.Length == 0) return slot;
            foreach (var key in subPath)
            {
                var children = (Prop(slot, "children") as IEnumerable)?.Cast<object>().ToList()
                               ?? new List<object>();
                object next = null;
                if (int.TryParse(key, out int idx))
                {
                    if (idx >= 0 && idx < children.Count) next = children[idx];
                }
                else
                {
                    next = children.FirstOrDefault(c =>
                        string.Equals(SlotName(c), key, StringComparison.OrdinalIgnoreCase));
                }
                if (next == null)
                    throw new Exception(
                        $"{label} sub-slot '{key}' not found; available children: " +
                        $"[{string.Join(", ", children.Select(SlotName))}]");
                slot = next;
            }
            return slot;
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
            bool toActivation = to["activation"]?.ToObject<bool>() == true;
            int toSlot = toActivation ? -1 : (to["slot"]?.ToObject<int>() ?? 0);

            var outSlot = GetSlot(fromNode, false, fromSlot, "from");
            var inSlot = ResolveInputSlot(toNode, to, "to");

            // Optional descriptor-named sub-slot descent on either endpoint (e.g. link a float into a
            // Sphere slot's `radius` child via to.subPath = ["radius"]).
            var fromSub = (from["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
            var toSub = (to["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
            outSlot = DescendSlot(outSlot, fromSub, "from");
            inSlot = DescendSlot(inSlot, toSub, "to");

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
                    ["activation"] = toActivation,
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
            // Object-typed slots (Texture2D/Texture3D/Cubemap/Mesh/…) take an asset PATH — load it,
            // same convention as set_block_setting/set_operator_setting for Object-typed fields.
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                var path = value.ToString();
                if (string.IsNullOrEmpty(path))
                    throw new Exception($"value for a {targetType.Name} slot must be an asset path string");
                var asset = AssetDatabase.LoadAssetAtPath(path, targetType);
                if (asset == null)
                    throw new Exception($"No {targetType.Name} asset at path: {path}");
                return asset;
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
                // Prefer the current value's type (preserves existing behavior for value-type leaves);
                // fall back to the slot's declared CLR type so Object-typed slots (Texture/Mesh), whose
                // default value is null, can still be set — by asset path (see CoerceToType).
                var targetType = current?.GetType() ?? SlotClrType(slot)
                    ?? throw new Exception(
                        "Slot value type could not be inferred (null value and no declared property type)");
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

        /// <summary>
        /// Set the coordinate space (World/Local/None) of a spaceable slot — Position/Vector/Direction
        /// -style inputs. `target` addresses the slot ({node, …address, slot}, optional `subPath`); `space`
        /// is the enum name. The space lives on the slot's master data, so non-spaceable slots return a
        /// quiet error (writing one would log an error). Describe surfaces it as `inputSlots[].space`.
        /// </summary>
        private static object SetSlotSpace(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var target = parameters?["target"] as JObject;
            var spaceToken = parameters?["space"];
            if (target == null)
                return new { error = "target is required (an object {node, …address, slot})" };
            if (spaceToken == null || spaceToken.Type == JTokenType.Null)
                return new { error = "space is required (World/Local/None)" };

            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            int slotIndex = target["slot"]?.ToObject<int>() ?? 0;
            var slot = GetSlot(node, true, slotIndex, "target");
            var targetSub = (target["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
            slot = DescendSlot(slot, targetSub, "target");

            bool spaceable = false;
            try { spaceable = (bool)Prop(slot, "spaceable"); } catch { }
            if (!spaceable)
                return new { error = $"slot '{SlotName(slot)}' is not spaceable; only Position/Vector/Direction-style slots carry a space" };

            var spaceType = Prop(slot, "space").GetType(); // VFXSpace enum value → its type
            object spaceVal;
            try { spaceVal = Enum.Parse(spaceType, spaceToken.ToString(), true); }
            catch
            {
                return new { error = $"invalid space '{spaceToken}'; valid: {string.Join(", ", Enum.GetNames(spaceType))}" };
            }

            SetProp(slot, "space", spaceVal);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_slot_space",
                ["assetPath"] = assetPath,
                ["slotName"] = SlotName(slot),
                ["space"] = spaceVal.ToString()
            };
        }

        private static Type InlineOperatorType => T("UnityEditor.VFX.VFXInlineOperator");
        private static Type SerializableTypeType => T("UnityEditor.VFX.SerializableType");

        /// <summary>
        /// Convert an inline-constant operator (VFXInlineOperator) into an exposed/constant blackboard
        /// parameter (VFXParameter), preserving its value and moving its output links — the headless
        /// equivalent of the editor's "Convert to Property". `target` addresses the inline operator
        /// ({node:"operator", operatorIndex}); `name` sets the exposed name; `exposed` (default false)
        /// toggles whether it's a blackboard-exposed parameter or a non-exposed constant.
        /// </summary>
        private static object ConvertToProperty(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var target = parameters?["target"] as JObject;
            if (target == null)
                return new { error = "target is required (the inline operator to convert, {node:\"operator\", operatorIndex})" };
            bool exposed = parameters?["exposed"]?.ToObject<bool>() ?? false;
            var name = parameters?["name"]?.ToString();

            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            if (!InlineOperatorType.IsInstanceOfType(node))
                return new { error = $"target must be an inline operator (VFXInlineOperator); got {node.GetType().Name}. " +
                                     "Inline operators come from add_operator with an Operator/Inline type (e.g. \"float\", \"Vector2\")." };

            var inlineType = Prop(node, "type") as Type;
            if (inlineType == null) return new { error = "could not read the inline operator's value type" };

            var descriptors = (Call(null, LibraryType, "GetParameters") as IEnumerable).Cast<object>().ToList();
            var desc = descriptors.FirstOrDefault(d => (Prop(d, "modelType") as Type) == inlineType);
            if (desc == null)
                return new { error = $"no blackboard parameter type matches the inline operator's type '{inlineType.Name}'" };

            var param = Call(desc, desc.GetType(), "CreateInstance");
            Call(param, ModelType, "SetSettingValue", "m_Exposed", exposed);
            if (!string.IsNullOrEmpty(name))
                Call(param, ModelType, "SetSettingValue", "m_ExposedName", name);

            // Move the inline operator's output links onto the new parameter's output (CopyLinks
            // re-points the single-link inputs, so the inline op is left link-free for a clean remove).
            var inlineOut = GetSlot(node, false, 0, "inline");
            var paramOut = GetSlot(param, false, 0, "parameter");
            Call(null, SlotType, "CopyLinks", paramOut, inlineOut, false);

            Call(graph, ModelType, "AddChild", param, -1, true);

            // Carry the constant over: the inline operator holds it on input slot 0.
            try { SetProp(param, "value", Prop(GetSlot(node, true, 0, "inline"), "value")); } catch { }

            Call(graph, ModelType, "RemoveChild", node, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "convert_to_property",
                ["assetPath"] = assetPath,
                ["exposedName"] = name,
                ["exposed"] = exposed,
                ["type"] = inlineType.Name
            };
        }

        /// <summary>
        /// Convert a blackboard parameter (VFXParameter) into an inline-constant operator
        /// (VFXInlineOperator) of the same type, preserving its value and moving its output links — the
        /// headless equivalent of "Convert to Inline". `target` addresses the parameter
        /// ({node:"parameter", parameterIndex}).
        /// </summary>
        private static object ConvertToInline(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var target = parameters?["target"] as JObject;
            if (target == null)
                return new { error = "target is required (the parameter to convert, {node:\"parameter\", parameterIndex})" };

            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            if (!ParameterType.IsInstanceOfType(node))
                return new { error = $"target must be a blackboard parameter (VFXParameter); got {node.GetType().Name}" };

            var paramType = Prop(node, "type") as Type;
            if (paramType == null) return new { error = "could not read the parameter's value type" };

            var inline = ScriptableObject.CreateInstance(InlineOperatorType);
            // m_Type is a SerializableType setting; setting it resyncs the inline op's typed slots.
            var serType = Activator.CreateInstance(SerializableTypeType, new object[] { paramType });
            Call(inline, ModelType, "SetSettingValue", "m_Type", serType);
            Call(graph, ModelType, "AddChild", inline, -1, true);

            // Carry the value over to the inline operator's input slot, then move the output links.
            try { SetProp(GetSlot(inline, true, 0, "inline"), "value", Prop(node, "value")); } catch { }

            var paramOut = GetSlot(node, false, 0, "parameter");
            var inlineOut = GetSlot(inline, false, 0, "inline");
            Call(null, SlotType, "CopyLinks", inlineOut, paramOut, false);

            Call(graph, ModelType, "RemoveChild", node, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "convert_to_inline",
                ["assetPath"] = assetPath,
                ["type"] = paramType.Name
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
            var slot = ResolveInputSlot(node, target, "target");
            int slotIndex = target["activation"]?.ToObject<bool>() == true
                ? -1
                : (target["slot"]?.ToObject<int>() ?? 0);
            var targetSub = (target["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
            slot = DescendSlot(slot, targetSub, "target");

            int before = LinkCount(slot);

            var from = parameters?["from"] as JObject;
            if (from != null)
            {
                var fromNode = ResolveNode(graph, from, "from");
                int fromSlot = from["slot"]?.ToObject<int>() ?? 0;
                var outSlot = GetSlot(fromNode, false, fromSlot, "from");
                var fromSub = (from["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
                outSlot = DescendSlot(outSlot, fromSub, "from");
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

        /// <summary>
        /// Clone a VFXModel (block or operator) via the editor's VFXMemorySerializer.DuplicateObjects —
        /// the clone carries the same [VFXSetting]s + slot values but fresh GUIDs (mirrors the GraphView's
        /// VFXContextController.DuplicateBlock). The clone's slots (incl. a block's activationSlot) are
        /// unlinked so the duplicate is detached; the caller AddChilds it into the target. Shared by
        /// duplicate_block / duplicate_operator.
        /// </summary>
        private static object DuplicateModelViaSerializer(object model, Type wantedType)
        {
            var deps = new HashSet<UnityEngine.ScriptableObject> { (UnityEngine.ScriptableObject)model };
            Call(model, ModelType, "CollectDependencies", deps, true);
            // Box the array as a single arg so Call's params object[] doesn't splat it into N args.
            var duplicated = (Array)Call(null, MemorySerializerType, "DuplicateObjects", (object)deps.ToArray());
            var clone = duplicated.Cast<object>().FirstOrDefault(o => wantedType.IsInstanceOfType(o));
            if (clone == null)
                throw new Exception($"DuplicateObjects produced no {wantedType.Name} clone");

            object actSlot = null;
            try { actSlot = Prop(clone, "activationSlot"); } catch { /* operators have no activationSlot */ }
            if (actSlot != null)
                try { Call(actSlot, SlotType, "UnlinkAll", true, false); } catch { }
            UnlinkContainerSlots(clone);
            return clone;
        }

        private static object RemoveBlock(JObject parameters)
        {
            if (!HasContextRef(parameters))
                return new { error = "contextType or contextIndex is required" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctx = ResolveBlockContext(graph, parameters);
            var wantContext = Prop(ctx, "contextType")?.ToString();

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

        /// <summary>True when an endpoint supplies either a `contextType` or a `contextIndex`.</summary>
        private static bool HasContextRef(JObject node, string idxKey = "contextIndex", string typeKey = "contextType")
        {
            var ci = node?[idxKey];
            if (ci != null && ci.Type != JTokenType.Null) return true;
            return !string.IsNullOrEmpty(node?[typeKey]?.ToString());
        }

        /// <summary>
        /// Locate a block by (context, blockIndex); returns its context + the block. The context is
        /// resolved via <see cref="ResolveBlockContext"/>, so callers can address it by `contextType`
        /// OR by `contextIndex` (to disambiguate two same-typed contexts).
        /// </summary>
        private static (object ctx, object block) LocateBlock(object graph, JObject parameters, int blockIndex)
        {
            var ctx = ResolveBlockContext(graph, parameters);
            var blocks = Children(ctx).ToList();
            if (blockIndex < 0 || blockIndex >= blocks.Count)
            {
                var ctName = Prop(ctx, "contextType")?.ToString();
                throw new Exception(
                    $"blockIndex {blockIndex} out of range; context '{ctName}' has {blocks.Count} block(s)");
            }
            return (ctx, blocks[blockIndex]);
        }

        /// <summary>
        /// Enable/disable a block. `enabled` is a read-only computed property derived from the block's
        /// activation slot (default `!m_Disabled`); the editor toggles it by writing the activation
        /// slot's value, so we set that (and keep the serialized `m_Disabled` field consistent).
        /// </summary>
        private static object SetBlockEnabled(JObject parameters)
        {
            if (!HasContextRef(parameters))
                return new { error = "contextType or contextIndex is required" };
            var enabledTok = parameters?["enabled"];
            if (enabledTok == null || enabledTok.Type == JTokenType.Null)
                return new { error = "enabled is required (bool)" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            bool enabled = enabledTok.ToObject<bool>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ctx, block) = LocateBlock(graph, parameters, blockIndex);
            var wantContext = Prop(ctx, "contextType")?.ToString();

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
            if (!HasContextRef(parameters))
                return new { error = "contextType or contextIndex is required" };
            var toTok = parameters?["toIndex"];
            if (toTok == null || toTok.Type == JTokenType.Null)
                return new { error = "toIndex is required" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            int toIndex = toTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ctx, block) = LocateBlock(graph, parameters, blockIndex);
            var wantContext = Prop(ctx, "contextType")?.ToString();

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
            if (!HasContextRef(parameters))
                return new { error = "contextType or contextIndex is required (the source context)" };
            if (!HasContextRef(parameters, "toContextIndex", "toContextType"))
                return new { error = "toContextType or toContextIndex is required (the destination context)" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            int toIndex = parameters?["toIndex"]?.ToObject<int>() ?? -1;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (srcCtx, block) = LocateBlock(graph, parameters, blockIndex);
            var wantContext = Prop(srcCtx, "contextType")?.ToString();
            var dstCtx = ResolveBlockContext(graph, parameters, "toContextIndex", "toContextType");
            var toContext = Prop(dstCtx, "contextType")?.ToString();

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

        /// <summary>
        /// Duplicate a block (clone all settings + slot values via VFXMemorySerializer) into its own
        /// context, or into another compatible context when `toContextType` is given (validated via
        /// VFXContext.Accept, like move_block). Optional `index` sets the insert position (-1 = append).
        /// </summary>
        private static object DuplicateBlock(JObject parameters)
        {
            if (!HasContextRef(parameters))
                return new { error = "contextType or contextIndex is required (the source context)" };
            int blockIndex = parameters?["blockIndex"]?.ToObject<int>() ?? 0;
            int toIndex = parameters?["index"]?.ToObject<int>() ?? -1;
            // optional: copy into another context (toContextType / toContextIndex)
            bool hasDest = HasContextRef(parameters, "toContextIndex", "toContextType");

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (srcCtx, block) = LocateBlock(graph, parameters, blockIndex);
            var wantContext = Prop(srcCtx, "contextType")?.ToString();

            object dstCtx = hasDest
                ? ResolveBlockContext(graph, parameters, "toContextIndex", "toContextType")
                : srcCtx;
            var toContext = Prop(dstCtx, "contextType")?.ToString();

            var clone = DuplicateModelViaSerializer(block, BlockType);

            if (!ReferenceEquals(dstCtx, srcCtx))
            {
                bool accept = (bool)Call(dstCtx, ContextType, "Accept", clone, -1);
                if (!accept)
                    return new { error = $"Block '{clone.GetType().Name}' is not compatible with context '{toContext}'." };
            }

            Call(dstCtx, ContextType, "AddChild", clone, toIndex, true);
            Persist(graph, assetPath);

            int newIndex = Children(dstCtx).ToList().FindIndex(b => ReferenceEquals(b, clone));
            return new JObject
            {
                ["op"] = "duplicate_block",
                ["assetPath"] = assetPath,
                ["sourceContextType"] = wantContext,
                ["sourceBlockIndex"] = blockIndex,
                ["toContextType"] = toContext,
                ["duplicatedBlock"] = clone.GetType().Name,
                ["toIndex"] = newIndex,
                ["blockCountInTarget"] = Children(dstCtx).Count()
            };
        }

        /// <summary>
        /// Duplicate a graph operator (clone all settings + slot values via VFXMemorySerializer). Mirrors
        /// duplicate_block; the clone is appended to the graph with its slots unlinked.
        /// </summary>
        private static object DuplicateOperator(JObject parameters)
        {
            int operatorIndex = parameters?["operatorIndex"]?.ToObject<int>() ?? 0;
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);

            var srcOps = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            if (operatorIndex < 0 || operatorIndex >= srcOps.Count)
                throw new Exception(
                    $"operatorIndex {operatorIndex} out of range; graph has {srcOps.Count} operator(s)");
            var op = srcOps[operatorIndex];

            var clone = DuplicateModelViaSerializer(op, OperatorType);
            Call(graph, ModelType, "AddChild", clone, -1, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "duplicate_operator",
                ["assetPath"] = assetPath,
                ["sourceOperatorIndex"] = operatorIndex,
                ["duplicatedOperator"] = clone.GetType().Name,
                ["operatorCount"] = Children(graph).Count(c => OperatorType.IsInstanceOfType(c))
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

        /// <summary>Resolve a blackboard parameter by `parameterIndex` (order among graph parameters).</summary>
        private static (object param, List<object> all) ResolveParameter(object graph, int parameterIndex)
        {
            var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();
            if (parameterIndex < 0 || parameterIndex >= ps.Count)
                throw new Exception($"parameterIndex {parameterIndex} out of range; graph has {ps.Count} parameter(s)");
            return (ps[parameterIndex], ps);
        }

        /// <summary>Rename a parameter's exposedName (m_ExposedName is a [VFXSetting]; the node + its
        /// links are untouched — same VFXParameter, new name). Optionally enforce uniqueness.</summary>
        private static object RenameParameter(JObject parameters)
        {
            var newName = parameters?["exposedName"]?.ToString() ?? parameters?["name"]?.ToString();
            if (string.IsNullOrEmpty(newName))
                return new { error = "exposedName (the new name) is required" };
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (param, all) = ResolveParameter(graph, parameterIndex);

            if (all.Any(p => !ReferenceEquals(p, param) &&
                             string.Equals(Prop(p, "exposedName") as string, newName, StringComparison.Ordinal)))
                return new { error = $"another parameter is already named '{newName}' (exposed names must be unique)" };

            Call(param, ModelType, "SetSettingValue", "m_ExposedName", newName);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "rename_parameter",
                ["assetPath"] = assetPath,
                ["parameterIndex"] = parameterIndex,
                ["exposedName"] = Prop(param, "exposedName") as string
            };
        }

        /// <summary>Assign a parameter's blackboard category (creates the category implicitly if new).</summary>
        private static object SetParameterCategory(JObject parameters)
        {
            var category = parameters?["category"];
            if (category == null) // empty string is allowed (clears to the default/uncategorized group)
                return new { error = "category is required" };
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (param, _) = ResolveParameter(graph, parameterIndex);

            SetProp(param, "category", category.ToString());
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_parameter_category",
                ["assetPath"] = assetPath,
                ["parameterIndex"] = parameterIndex,
                ["category"] = Prop(param, "category") as string
            };
        }

        /// <summary>Rename a whole category: every parameter whose category equals `category` is moved to
        /// `newCategory`. Categories are derived from the parameters' category strings (no separate list).</summary>
        private static object RenameCategory(JObject parameters)
        {
            var oldCategory = parameters?["category"]?.ToString();
            var newCategory = parameters?["newCategory"]?.ToString();
            if (string.IsNullOrEmpty(oldCategory))
                return new { error = "category (the existing category name) is required" };
            if (newCategory == null)
                return new { error = "newCategory is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();

            int moved = 0;
            foreach (var p in ps)
            {
                if (string.Equals(Prop(p, "category") as string, oldCategory, StringComparison.Ordinal))
                {
                    SetProp(p, "category", newCategory);
                    moved++;
                }
            }
            if (moved == 0)
                return new { error = $"no parameters are in category '{oldCategory}'" };
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "rename_category",
                ["assetPath"] = assetPath,
                ["category"] = oldCategory,
                ["newCategory"] = newCategory,
                ["parametersMoved"] = moved
            };
        }

        /// <summary>
        /// Reorder a blackboard *category* (vs reorder_parameter, which orders params within a category).
        /// Category order lives on VFXUI.categories (a List&lt;CategoryInfo&gt;; list position = display
        /// order). The VFXView's MoveCategory is controller-coupled, so this replicates it at model level:
        /// first sync any param categories missing from the list (mirrors VFXViewController, which lazily
        /// populates categories from the params), then move `category` to `toIndex`. Describe surfaces the
        /// result as the top-level `categories` array.
        /// </summary>
        private static object ReorderCategory(JObject parameters)
        {
            var category = parameters?["category"]?.ToString();
            if (string.IsNullOrEmpty(category))
                return new { error = "category (the category name to move) is required" };
            var toTok = parameters?["toIndex"];
            if (toTok == null || toTok.Type == JTokenType.Null)
                return new { error = "toIndex is required (the category's new position)" };
            int toIndex = toTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ui, list) = GetCategories(graph);
            SyncCategoriesFromParams(graph, list);

            var nameField = FindField(CategoryInfoType, "name");
            int oldIndex = -1;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(nameField?.GetValue(list[i]) as string, category, StringComparison.Ordinal))
                { oldIndex = i; break; }
            if (oldIndex < 0)
            {
                var names = list.Cast<object>().Select(c => nameField?.GetValue(c) as string);
                return new { error = $"category '{category}' not found; existing: [{string.Join(", ", names)}]" };
            }
            if (toIndex < 0 || toIndex >= list.Count)
                return new { error = $"toIndex {toIndex} out of range; graph has {list.Count} categor(ies)." };

            var moved = list[oldIndex];
            list.RemoveAt(oldIndex);
            list.Insert(toIndex, moved);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "reorder_category",
                ["assetPath"] = assetPath,
                ["category"] = category,
                ["toIndex"] = toIndex,
                ["categories"] = CategoriesJson(graph)
            };
        }

        /// <summary>Resolve the graph's VFXUI.categories list (creating it if null).</summary>
        private static (object ui, System.Collections.IList list) GetCategories(object graph)
        {
            var ui = Prop(graph, "UIInfos");
            if (ui == null)
                throw new Exception("Graph has no UIInfos sidecar (unexpected for a valid .vfx).");
            var field = FindField(ui.GetType(), "categories");
            if (field == null)
                throw new Exception("categories field not found on VFXUI.");
            var list = field.GetValue(ui) as System.Collections.IList;
            if (list == null)
            {
                list = (System.Collections.IList)Activator.CreateInstance(field.FieldType);
                field.SetValue(ui, list);
            }
            return (ui, list);
        }

        /// <summary>Append any param-referenced category names not already in the list (preserving the
        /// list's existing order) — the model-level equivalent of VFXViewController's lazy category sync.</summary>
        private static void SyncCategoriesFromParams(object graph, System.Collections.IList list)
        {
            var nameField = FindField(CategoryInfoType, "name");
            var existing = new HashSet<string>(
                list.Cast<object>().Select(c => nameField?.GetValue(c) as string));
            var paramCats = Children(graph)
                .Where(c => ParameterType.IsInstanceOfType(c))
                .Select(p => Prop(p, "category") as string)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct();
            foreach (var pc in paramCats)
            {
                if (existing.Contains(pc)) continue;
                object boxed = Activator.CreateInstance(CategoryInfoType);
                nameField?.SetValue(boxed, pc);
                list.Add(boxed);
                existing.Add(pc);
            }
        }

        /// <summary>Set a parameter's blackboard order (its position within its category).</summary>
        private static object ReorderParameter(JObject parameters)
        {
            var orderTok = parameters?["order"];
            if (orderTok == null || orderTok.Type == JTokenType.Null)
                return new { error = "order (the new integer position) is required" };
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (param, _) = ResolveParameter(graph, parameterIndex);

            SetProp(param, "order", orderTok.ToObject<int>());
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "reorder_parameter",
                ["assetPath"] = assetPath,
                ["parameterIndex"] = parameterIndex,
                ["order"] = Convert.ToInt32(Prop(param, "order"))
            };
        }

        /// <summary>Duplicate a parameter (VFXParameter.Duplicate: same type/default/category, order+1),
        /// adding the clone to the graph. The clone's exposedName defaults to "&lt;name&gt; (1)".</summary>
        private static object DuplicateParameter(JObject parameters)
        {
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;
            var copyName = parameters?["exposedName"]?.ToString() ?? parameters?["name"]?.ToString();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (param, all) = ResolveParameter(graph, parameterIndex);

            if (string.IsNullOrEmpty(copyName))
                copyName = (Prop(param, "exposedName") as string ?? "Parameter") + " (1)";
            if (all.Any(p => string.Equals(Prop(p, "exposedName") as string, copyName, StringComparison.Ordinal)))
                return new { error = $"a parameter named '{copyName}' already exists (exposed names must be unique)" };

            var clone = Call(null, ParameterType, "Duplicate", copyName, param);
            Call(graph, ModelType, "AddChild", clone, -1, true);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "duplicate_parameter",
                ["assetPath"] = assetPath,
                ["sourceParameterIndex"] = parameterIndex,
                ["exposedName"] = Prop(clone, "exposedName") as string,
                ["parameterCount"] = Children(graph).Count(c => ParameterType.IsInstanceOfType(c))
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

        /// <summary>
        /// Remove a single context→context flow edge (companion to link_flow). Endpoints `from`/`to`
        /// resolve by `{contextType}`/`{index}` like link_flow; `VFXContext.UnlinkTo` drops just that
        /// edge (vs remove_context's no-arg UnlinkAll which clears every flow edge). Sibling edges stay.
        /// </summary>
        private static object UnlinkFlow(JObject parameters)
        {
            var from = parameters?["from"] as JObject;
            var to = parameters?["to"] as JObject;
            if (from == null) return new { error = "from is required (the source context)" };
            if (to == null) return new { error = "to is required (the target context)" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();

            var fromCtx = ResolveContextRef(graph, from, ctxList, "from");
            var toCtx = ResolveContextRef(graph, to, ctxList, "to");
            int fromIndex = parameters?["fromIndex"]?.ToObject<int>() ?? 0;
            int toIndex = parameters?["toIndex"]?.ToObject<int>() ?? 0;

            Call(fromCtx, ContextType, "UnlinkTo", toCtx, fromIndex, toIndex);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "unlink_flow",
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
                return new { error = "kind is required (block, operator, or system)" };

            // System subgraph = a plain .vfx (no .vfxblock/.vfxoperator template); created via
            // VisualEffectAssetEditorUtility.CreateNewAsset, then referenced in a parent graph by
            // add_context "Subgraph" + subgraphPath (which instantiates a VFXSubgraphContext).
            if (kind == "system")
            {
                if (!subgraphPath.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                    return new { error = "subgraphPath must end with '.vfx' for kind 'system'." };
                var parentDirSys = System.IO.Path.GetDirectoryName(subgraphPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parentDirSys) && !AssetDatabase.IsValidFolder(parentDirSys))
                    throw new Exception($"Parent folder does not exist: {parentDirSys}");
                var createdSys = Call(null, AssetEditorUtilityType, "CreateNewAsset", subgraphPath);
                if (createdSys == null)
                    throw new Exception($"CreateNewAsset returned null for '{subgraphPath}'.");
                AssetDatabase.ImportAsset(subgraphPath, ImportAssetOptions.ForceUpdate);
                return new JObject
                {
                    ["op"] = "create_subgraph_asset",
                    ["subgraphPath"] = subgraphPath,
                    ["kind"] = kind,
                    ["assetType"] = (createdSys as UnityEngine.Object)?.GetType().Name
                };
            }

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
                    return new { error = $"Unknown kind '{kind}'. Supported: block, operator, system." };
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

            var templateFile = ResolveTemplateFile(template);

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
        /// Resolve a `template` (a built-in template name like "01_Minimal_System", or an explicit
        /// `.vfx` path) to an AssetDatabase path. Returns forward-slash form so it works for both the
        /// File APIs (CreateTemplateAsset) and GetResourceAtPath (insert_template).
        /// </summary>
        private static string ResolveTemplateFile(string template)
        {
            if (template.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase) &&
                (System.IO.File.Exists(template) || AssetDatabase.LoadMainAssetAtPath(template) != null))
                return template.Replace('\\', '/');

            var templateDir = AssetEditorUtilityType
                .GetProperty("templatePath", AllStatic)?.GetValue(null) as string;
            if (string.IsNullOrEmpty(templateDir))
                throw new Exception("Could not resolve the VFX package template directory.");
            var templateFile = (templateDir.TrimEnd('/', '\\') + "/" + template + ".vfx");
            if (!System.IO.File.Exists(templateFile) && AssetDatabase.LoadMainAssetAtPath(templateFile) == null)
                throw new Exception(
                    $"No template '{template}' in {templateDir}. Use vfx_list_library kind 'template' to discover names.");
            return templateFile;
        }

        /// <summary>
        /// Merge a template's nodes into an EXISTING graph (vs create_from_template, which makes a new
        /// asset). Clones every top-level node (context/operator/parameter) of the template — with the
        /// template's internal flow + slot links preserved — via VFXMemorySerializer.DuplicateObjects,
        /// then AddChilds the clones into the target graph. The GraphView's merge path (VFXCopy/VFXPaste)
        /// is Controller/View-coupled; this is the model-level equivalent (a template is self-contained,
        /// so no boundary-I/O inference is needed).
        /// </summary>
        private static object InsertTemplate(JObject parameters)
        {
            var template = parameters?["template"]?.ToString();
            if (string.IsNullOrEmpty(template))
                return new { error = "template is required (a template name or path to a .vfx template)" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var templateFile = ResolveTemplateFile(template);
            var templateGraph = LoadGraph(templateFile);

            // Top-level functional nodes of the template (skip VFXUI etc.).
            bool IsNode(object m) => ContextType.IsInstanceOfType(m)
                || OperatorType.IsInstanceOfType(m) || ParameterType.IsInstanceOfType(m);
            var topLevel = Children(templateGraph).Where(IsNode).ToList();
            if (topLevel.Count == 0)
                return new { error = $"Template '{template}' has no insertable nodes." };

            // Collect the whole object graph (nodes + blocks + slots) so DuplicateObjects clones it
            // with internal flow/slot links intact, then add back only the graph-level clones.
            var deps = new HashSet<UnityEngine.ScriptableObject>();
            foreach (var node in topLevel)
            {
                deps.Add((UnityEngine.ScriptableObject)node);
                Call(node, ModelType, "CollectDependencies", deps, true);
            }
            var duplicated = (Array)Call(null, MemorySerializerType, "DuplicateObjects", (object)deps.ToArray());

            int added = 0;
            var addedTypes = new JArray();
            foreach (var clone in duplicated.Cast<object>())
            {
                if (!IsNode(clone)) continue;
                Call(graph, ModelType, "AddChild", clone, -1, true);
                added++;
                addedTypes.Add(clone.GetType().Name);
            }
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "insert_template",
                ["assetPath"] = assetPath,
                ["template"] = template,
                ["templateFile"] = templateFile.Replace('\\', '/'),
                ["addedNodes"] = added,
                ["addedTypes"] = addedTypes
            };
        }

        /// <summary>
        /// Designate an existing .vfx as a custom template (it shows up in the Templates window). Writes
        /// the VFX importer's template metadata (name/category/description + optional icon/thumbnail by
        /// asset path) and flips useAsTemplate, via the package's official VFXTemplateHelperInternal.
        /// TrySetTemplateStatic (the same entry the GraphView's "Set as Template" uses) — then persists
        /// the importer settings and reimports so the .meta carries the `template:` block. Describe
        /// surfaces it as the top-level `template` field.
        /// </summary>
        private static object DesignateTemplate(JObject parameters)
        {
            var name = parameters?["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return new { error = "name is required (the template's display name)" };
            var assetPath = parameters?["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(assetPath))
                return new { error = "assetPath is required" };
            if (!System.IO.File.Exists(assetPath))
                return new { error = $"No .vfx asset at path: {assetPath}" };

            var descType = TemplateDescriptorType
                ?? throw new Exception("GraphViewTemplateDescriptor type not found (UnityEditor.Experimental.GraphView).");
            var helperType = TemplateHelperType
                ?? throw new Exception("VFXTemplateHelperInternal type not found.");

            object desc = Activator.CreateInstance(descType);
            FindField(descType, "name")?.SetValue(desc, name);
            FindField(descType, "category")?.SetValue(desc, parameters?["category"]?.ToString() ?? "");
            FindField(descType, "description")?.SetValue(desc, parameters?["description"]?.ToString() ?? "");
            var iconPath = parameters?["icon"]?.ToString();
            if (!string.IsNullOrEmpty(iconPath))
            {
                var icon = AssetDatabase.LoadAssetAtPath(iconPath, typeof(Texture2D));
                if (icon == null) return new { error = $"No Texture2D icon at path: {iconPath}" };
                FindField(descType, "icon")?.SetValue(desc, icon);
            }
            var thumbPath = parameters?["thumbnail"]?.ToString();
            if (!string.IsNullOrEmpty(thumbPath))
            {
                var thumb = AssetDatabase.LoadAssetAtPath(thumbPath, typeof(Texture2D));
                if (thumb == null) return new { error = $"No Texture2D thumbnail at path: {thumbPath}" };
                FindField(descType, "thumbnail")?.SetValue(desc, thumb);
            }

            var setMethod = helperType.GetMethod("TrySetTemplateStatic",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (setMethod == null)
                throw new Exception("VFXTemplateHelperInternal.TrySetTemplateStatic not found.");
            bool ok = (bool)setMethod.Invoke(null, new[] { assetPath, desc });
            if (!ok)
                return new { error = $"Failed to set template metadata on {assetPath}." };

            // The helper sets the importer's properties (dirtying it) but leaves persisting to the caller.
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["op"] = "designate_template",
                ["assetPath"] = assetPath,
                ["template"] = TemplateInfoJson(assetPath)
            };
        }

        /// <summary>Read an asset's custom-template metadata via VFXTemplateHelperInternal.TryGetTemplateStatic
        /// (name/category/description). Returns null when the asset is not designated as a template.</summary>
        private static JObject TemplateInfoJson(string assetPath)
        {
            try
            {
                var helperType = TemplateHelperType;
                var descType = TemplateDescriptorType;
                if (helperType == null || descType == null || string.IsNullOrEmpty(assetPath)) return null;
                var getMethod = helperType.GetMethod("TryGetTemplateStatic",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (getMethod == null) return null;
                var args = new object[] { assetPath, null };
                bool ok = (bool)getMethod.Invoke(null, args);
                if (!ok) return null;
                var d = args[1];
                return new JObject
                {
                    ["name"] = FindField(descType, "name")?.GetValue(d) as string,
                    ["category"] = FindField(descType, "category")?.GetValue(d) as string,
                    ["description"] = FindField(descType, "description")?.GetValue(d) as string
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Read the asset's VisualEffectResource instancing settings (mode + capacity) plus the
        /// graph-derived force-disable reason as a JSON block for describe; null when the resource
        /// surfaces neither setting.
        ///
        /// <para>`disabledReason` mirrors <c>VFXGraphCompiledData.ValidateInstancing</c>: instancing
        /// is force-disabled (regardless of the asset mode or the project-wide preference) when the
        /// graph contains an Output Event context (<c>VFXOutputEvent</c>) or a static-mesh output
        /// (<c>VFXStaticMeshOutput</c>). We recompute it from the live context types so the oracle
        /// needs no access to the editor compiler's internal compiled-data state. "None" means no
        /// graph-level block — the effective state then depends on the asset mode + the preference
        /// master (the other two of the three instancing gates).</para>
        /// </summary>
        private static JObject InstancingJson(object graph)
        {
            var resource = graph == null ? null : Prop(graph, "visualEffectResource");
            if (resource == null) return null;
            JToken modeTok = null;
            JToken capTok = null;
            try { modeTok = ToJToken(Prop(resource, "instancingMode")); } catch { }
            try { capTok = ToJToken(Prop(resource, "instancingCapacity")); } catch { }
            if (modeTok == null && capTok == null) return null;

            var reasons = new List<string>();
            foreach (var child in Children(graph))
            {
                if (!ContextType.IsInstanceOfType(child)) continue;
                var typeName = child.GetType().Name;
                if (typeName == "VFXOutputEvent" && !reasons.Contains("OutputEvent")) reasons.Add("OutputEvent");
                else if (typeName == "VFXStaticMeshOutput" && !reasons.Contains("MeshOutput")) reasons.Add("MeshOutput");
            }
            return new JObject
            {
                ["mode"] = modeTok,
                ["capacity"] = capTok,
                ["disabledReason"] = reasons.Count == 0 ? "None" : string.Join(", ", reasons)
            };
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

        /// <summary>Read the asset's default Initial Event Name (the event fired when the effect plays;
        /// default "OnPlay"). Stored as m_Infos.m_InitialEventName on the resource (the inspector path).</summary>
        private static string InitialEventNameOf(object resource)
        {
            if (resource == null) return null;
            try
            {
                var so = new SerializedObject(resource as UnityEngine.Object);
                return so.FindProperty("m_Infos.m_InitialEventName")?.stringValue;
            }
            catch { return null; }
        }

        /// <summary>
        /// Set the asset's default Initial Event Name — the event sent when the effect activates
        /// (default "OnPlay"). Written via the resource's serialized m_Infos.m_InitialEventName (the same
        /// path the asset inspector uses; there is no public property). This is the per-asset default;
        /// the per-instance override is the runtime VisualEffect.initialEventName (vfx_runtime).
        /// </summary>
        private static object SetInitialEventName(JObject parameters)
        {
            var eventName = parameters?["eventName"]?.ToString();
            if (eventName == null) // empty string is allowed (clears to no auto-play); null means missing
                return new { error = "eventName is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var resource = Prop(graph, "visualEffectResource");
            if (resource == null)
                throw new Exception("Graph has no VisualEffectResource (unexpected for a valid .vfx).");

            var so = new SerializedObject(resource as UnityEngine.Object);
            var prop = so.FindProperty("m_Infos.m_InitialEventName");
            if (prop == null)
                throw new Exception("m_Infos.m_InitialEventName not found on VisualEffectResource (VFX package too old?).");
            prop.stringValue = eventName;
            so.ApplyModifiedPropertiesWithoutUndo();

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "set_initial_event_name",
                ["assetPath"] = assetPath,
                ["initialEventName"] = InitialEventNameOf(resource)
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

        /// <summary>
        /// Reorder a sticky note: move the entry at `index` to `toIndex` within stickyNoteInfos.
        /// The array position IS the note's order (StickyNoteInfo has no order field), so this is a
        /// plain array move (mirrors reorder_block/reorder_parameter, which reorder their containers).
        /// </summary>
        private static object ReorderStickyNote(JObject parameters)
        {
            var idxTok = parameters?["index"];
            if (idxTok == null || idxTok.Type == JTokenType.Null)
                return new { error = "index is required" };
            var toTok = parameters?["toIndex"];
            if (toTok == null || toTok.Type == JTokenType.Null)
                return new { error = "toIndex is required" };
            int index = idxTok.ToObject<int>();
            int toIndex = toTok.ToObject<int>();

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (ui, notesField, arr) = GetStickyNotes(graph);
            int len = arr?.Length ?? 0;
            if (index < 0 || index >= len)
                throw new Exception($"index {index} out of range; graph has {len} sticky note(s)");
            if (toIndex < 0 || toIndex >= len)
                throw new Exception($"toIndex {toIndex} out of range; graph has {len} sticky note(s)");

            var noteType = StickyNoteInfoType;
            var moved = arr.GetValue(index);
            var newArr = Array.CreateInstance(noteType, len);
            int w = 0;
            // Copy all but the moved element, inserting it at toIndex in the compacted sequence.
            for (int r = 0; r < len; r++)
            {
                if (w == toIndex) newArr.SetValue(moved, w++);
                if (r == index) continue;
                newArr.SetValue(arr.GetValue(r), w++);
            }
            if (w == toIndex) newArr.SetValue(moved, w); // moved goes last
            notesField.SetValue(ui, newArr);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "reorder_sticky_note",
                ["assetPath"] = assetPath,
                ["index"] = index,
                ["toIndex"] = toIndex,
                ["count"] = len
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
        /// set_asset, set_float, set_int, set_bool, set_vector2/3/4, set_texture, set_mesh,
        /// send_event, set_initial_event_name, reinit, simulate, get_state.
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

                        // Optional event-attribute payload: { "spawnCount": 17, "position": [x,y,z], ... }.
                        // Each entry becomes a value on a VFXEventAttribute carried by the event — the
                        // payload bus that seeds spawn-state (spawnCount/spawnTime) and source particle
                        // attributes for the spawned particles (see VFXSpawnerState.vfxEventAttribute).
                        var attrs = parameters?["attributes"] as JObject;
                        if (attrs == null || attrs.Count == 0)
                        {
                            Call(comp2, VisualEffectType, "SendEvent", eventName);
                            return new JObject { ["op"] = op, ["gameObject"] = gameObject, ["eventName"] = eventName };
                        }

                        var evtAttr = Call(comp2, VisualEffectType, "CreateVFXEventAttribute");
                        if (evtAttr == null) return new { error = "CreateVFXEventAttribute returned null" };
                        var evtAttrType = evtAttr.GetType();
                        var applied = new JObject();
                        foreach (var kv in attrs)
                        {
                            var an = kv.Key;
                            var tok = kv.Value;
                            if (tok is JArray ja)
                            {
                                switch (ja.Count)
                                {
                                    case 2: Call(evtAttr, evtAttrType, "SetVector2", an, ToVector(tok, 2)); break;
                                    case 3: Call(evtAttr, evtAttrType, "SetVector3", an, ToVector(tok, 3)); break;
                                    case 4: Call(evtAttr, evtAttrType, "SetVector4", an, ToVector(tok, 4)); break;
                                    default: return new { error = $"attribute '{an}': array payload must have 2-4 numbers" };
                                }
                            }
                            else if (tok.Type == JTokenType.Boolean)
                            {
                                Call(evtAttr, evtAttrType, "SetBool", an, tok.ToObject<bool>());
                            }
                            else
                            {
                                // VFX attributes (including the special spawnCount/spawnTime) are float-typed.
                                Call(evtAttr, evtAttrType, "SetFloat", an, tok.ToObject<float>());
                            }
                            applied[an] = tok;
                        }

                        var sendWithAttr = VisualEffectType.GetMethod("SendEvent", new[] { typeof(string), evtAttrType });
                        if (sendWithAttr == null) return new { error = "VisualEffect.SendEvent(string, VFXEventAttribute) not found" };
                        sendWithAttr.Invoke(comp2, new object[] { eventName, evtAttr });
                        return new JObject { ["op"] = op, ["gameObject"] = gameObject, ["eventName"] = eventName, ["attributes"] = applied };
                    }
                case "set_initial_event_name":
                    {
                        // Per-instance override of the asset's default initial event (OnPlay). The
                        // asset default is set authoring-side via vfx_apply set_initial_event_name;
                        // this is the runtime VisualEffect.initialEventName property. Empty string
                        // suppresses auto-play. Reinit so the change takes effect immediately.
                        if (name == null) return new { error = "name is required (the initial event name; \"\" suppresses auto-play)" };
                        SetProp(comp2, "initialEventName", name);
                        Call(comp2, VisualEffectType, "Reinit");
                        var s = RuntimeState(comp2, gameObject, null);
                        s["op"] = op;
                        return s;
                    }
                case "set_texture":
                    {
                        // Object-typed exposed property: load a Texture by path and bind it through
                        // the public SetTexture(string, Texture). Round-trips via HasTexture/GetTexture
                        // in get_state (only survives if the exposed param is USED in the graph).
                        if (string.IsNullOrEmpty(name)) return new { error = "name is required (exposed texture parameter name)" };
                        var texPath = parameters?["assetPath"]?.ToString() ?? valueToken?.ToString();
                        if (string.IsNullOrEmpty(texPath)) return new { error = "assetPath is required (path to a Texture asset)" };
                        var tex = AssetDatabase.LoadAssetAtPath(texPath, typeof(Texture));
                        if (tex == null) return new { error = $"No Texture asset at path: {texPath}" };
                        var setTex = VisualEffectType.GetMethod("SetTexture", new[] { typeof(string), typeof(Texture) });
                        if (setTex == null) return new { error = "VisualEffect.SetTexture(string, Texture) not found" };
                        setTex.Invoke(comp2, new object[] { name, tex });
                        var s = RuntimeState(comp2, gameObject, name);
                        s["op"] = op;
                        return s;
                    }
                case "set_mesh":
                    {
                        // Object-typed exposed property: load a Mesh by path and bind it through the
                        // public SetMesh(string, Mesh). Same used-param survival rule as set_texture.
                        if (string.IsNullOrEmpty(name)) return new { error = "name is required (exposed mesh parameter name)" };
                        var meshPath = parameters?["assetPath"]?.ToString() ?? valueToken?.ToString();
                        if (string.IsNullOrEmpty(meshPath)) return new { error = "assetPath is required (path to a Mesh asset)" };
                        var mesh = AssetDatabase.LoadAssetAtPath(meshPath, typeof(Mesh));
                        if (mesh == null) return new { error = $"No Mesh asset at path: {meshPath}" };
                        var setMesh = VisualEffectType.GetMethod("SetMesh", new[] { typeof(string), typeof(Mesh) });
                        if (setMesh == null) return new { error = "VisualEffect.SetMesh(string, Mesh) not found" };
                        setMesh.Invoke(comp2, new object[] { name, mesh });
                        var s = RuntimeState(comp2, gameObject, name);
                        s["op"] = op;
                        return s;
                    }
                case "reinit":
                    Call(comp2, VisualEffectType, "Reinit");
                    return new JObject { ["op"] = op, ["gameObject"] = gameObject };
                case "simulate":
                    {
                        // Advance the effect's simulation headlessly via the public
                        // VisualEffect.Simulate(float deltaTime, uint stepCount). Without this an
                        // effect outside Play mode never ticks, so aliveParticleCount stays 0/-1.
                        // NOTE: a culled (unrendered) effect spawns nothing — the caller's rig should
                        // frame it with a Camera (and, in Play mode, advance frames) for spawn/output
                        // behaviour; see the runtime eval harness. `deltaTime` defaults to 0.05s,
                        // `steps` to 1.
                        float dt = parameters?["deltaTime"]?.ToObject<float>() ?? 0.05f;
                        uint steps = parameters?["steps"]?.ToObject<uint>() ?? 1u;
                        var simulate = VisualEffectType.GetMethod("Simulate", new[] { typeof(float), typeof(uint) });
                        if (simulate == null) return new { error = "VisualEffect.Simulate(float, uint) not found" };
                        simulate.Invoke(comp2, new object[] { dt, steps });
                        var s = RuntimeState(comp2, gameObject, name);
                        s["op"] = op;
                        s["deltaTime"] = dt;
                        s["steps"] = (int)steps;
                        return s;
                    }
                case "get_state":
                    return RuntimeState(comp2, gameObject, name);
                default:
                    return new
                    {
                        error = $"Unsupported runtime op: '{op}'. Supported: set_asset, set_float, set_int, set_bool, " +
                                "set_vector2, set_vector3, set_vector4, set_texture, set_mesh, send_event, set_initial_event_name, reinit, simulate, get_state"
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
            try { state["initialEventName"] = (string)Prop(comp, "initialEventName"); } catch { }

            if (!string.IsNullOrEmpty(name))
            {
                state["name"] = name;
                try { state["hasFloat"] = (bool)Call(comp, VisualEffectType, "HasFloat", name); } catch { }
                if (state.Value<bool?>("hasFloat") == true)
                    try { state["floatValue"] = (float)Call(comp, VisualEffectType, "GetFloat", name); } catch { }
                try { state["hasTexture"] = (bool)Call(comp, VisualEffectType, "HasTexture", name); } catch { }
                if (state.Value<bool?>("hasTexture") == true)
                    try { state["textureName"] = (Call(comp, VisualEffectType, "GetTexture", name) as UnityEngine.Object)?.name; } catch { }
                try { state["hasMesh"] = (bool)Call(comp, VisualEffectType, "HasMesh", name); } catch { }
                if (state.Value<bool?>("hasMesh") == true)
                    try { state["meshName"] = (Call(comp, VisualEffectType, "GetMesh", name) as UnityEngine.Object)?.name; } catch { }
            }
            return state;
        }

        // ---- vfx_settings: VFX project settings (ProjectSettings/VFXManager.asset) ----------

        private const string VFXManagerAssetPath = "ProjectSettings/VFXManager.asset";

        // Serialized fields on the VFXManager singleton (see VFXManagerEditor) — covers settings
        // that have no public static property (e.g. max capacity, batch empty lifetime).
        private static readonly string[] VfxManagerSerializedFields =
        {
            "m_FixedTimeStep", "m_MaxDeltaTime", "m_MaxScrubTime", "m_MaxCapacity", "m_BatchEmptyLifetime",
            // Object-ref plumbing (usually Unity-managed defaults): the compute/empty shaders +
            // the runtime-resources ScriptableObject + the render-pipe settings path. Surfaced for
            // inspection; settable by asset path via AssignSerialized's ObjectReference branch.
            "m_IndirectShader", "m_CopyBufferShader", "m_PrefixSumShader", "m_SortShader",
            "m_StripUpdateShader", "m_EmptyShader", "m_RuntimeResources", "m_RenderPipeSettingsPath",
        };

        /// <summary>Read/write VFX project settings (no graph — environment capability).</summary>
        // ---- SDF baking (public UnityEngine.VFX.SDF.MeshToSDFBaker API) -------

        private static Type MeshToSdfBakerType => T("UnityEngine.VFX.SDF.MeshToSDFBaker");

        /// <summary>
        /// Bake a Mesh into a Signed Distance Field Texture3D asset — the programmatic equivalent of the
        /// SDF Bake Tool window. Uses the package's PUBLIC runtime MeshToSDFBaker (one of the few public
        /// VFX APIs): construct → BakeSDF() → read back the 3D SdfTexture RenderTexture → save as a
        /// Texture3D asset. The package's own SaveToAsset is internal and routes through an interactive
        /// SaveFilePanel (a headless blocker), so we do our own AsyncGPUReadback + AssetDatabase.CreateAsset
        /// at the caller's path. Baking is GPU-compute work — returns a clean error when compute is
        /// unavailable (e.g. some headless CI).
        /// </summary>
        public static object BakeSdf(JObject parameters)
        {
            try { return BakeSdfCore(parameters); }
            catch (Exception ex) { return Fail("vfx_bake_sdf", ex); }
        }

        private static object BakeSdfCore(JObject parameters)
        {
            var meshPath = parameters?["meshPath"]?.ToString();
            if (string.IsNullOrEmpty(meshPath))
                return new { error = "meshPath is required (asset path to the source Mesh)" };
            var outputPath = parameters?["outputPath"]?.ToString();
            if (string.IsNullOrEmpty(outputPath))
                return new { error = "outputPath is required (where to save the .asset Texture3D)" };
            if (!outputPath.StartsWith("Assets/") || !outputPath.EndsWith(".asset"))
                return new { error = "outputPath must start with 'Assets/' and end with '.asset'" };

            // Arg/asset validation first (so a bad mesh/path reports clearly regardless of GPU capability).
            var mesh = AssetDatabase.LoadAssetAtPath(meshPath, typeof(Mesh)) as Mesh;
            if (mesh == null)
                return new { error = $"No Mesh asset at path: {meshPath}" };

            var outDir = System.IO.Path.GetDirectoryName(outputPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(outDir))
                return new { error = $"Output folder '{outDir}' does not exist; create it first." };

            // Capability checks after validation.
            var bakerType = MeshToSdfBakerType;
            if (bakerType == null)
                return new { error = "MeshToSDFBaker not found (the VFX Graph package's SDF Bake Tool is unavailable)." };
            if (!SystemInfo.supportsComputeShaders)
                return new { error = "SDF baking requires compute shader support, which this device/editor lacks." };

            bool overwrite = parameters?["overwrite"]?.ToObject<bool>() ?? false;
            var existing = AssetDatabase.LoadAssetAtPath(outputPath, typeof(Texture3D));
            if (existing != null && !overwrite)
                return new { error = $"An asset already exists at {outputPath}; set overwrite:true to replace it." };

            int maxRes = parameters?["maxResolution"]?.ToObject<int>() ?? 64;
            if (maxRes < 1) maxRes = 1;
            int signPassCount = parameters?["signPassCount"]?.ToObject<int>() ?? 1;
            float threshold = parameters?["threshold"]?.ToObject<float>() ?? 0.5f;
            float sdfOffset = parameters?["sdfOffset"]?.ToObject<float>() ?? 0f;

            // Box defaults to the mesh's local bounds (fit-to-mesh) when center/size aren't given.
            Vector3 center = mesh.bounds.center;
            if (parameters?["center"] is JArray ca && ca.Count >= 3) center = (Vector3)ToVector(ca, 3);
            Vector3 size = mesh.bounds.size;
            if (parameters?["size"] is JArray sa && sa.Count >= 3) size = (Vector3)ToVector(sa, 3);
            // A zero dimension produces a degenerate box — clamp each axis to a small positive size.
            size = new Vector3(Mathf.Max(size.x, 1e-4f), Mathf.Max(size.y, 1e-4f), Mathf.Max(size.z, 1e-4f));

            var ctor = bakerType.GetConstructor(new[]
            {
                typeof(Vector3), typeof(Vector3), typeof(int), typeof(Mesh),
                typeof(int), typeof(float), typeof(float), typeof(UnityEngine.Rendering.CommandBuffer)
            });
            if (ctor == null)
                return new { error = "MeshToSDFBaker(sizeBox,center,maxRes,mesh,signPasses,threshold,sdfOffset,cmd) ctor not found." };

            object baker = ctor.Invoke(new object[] { size, center, maxRes, mesh, signPassCount, threshold, sdfOffset, null });
            try
            {
                Call(baker, bakerType, "BakeSDF");
                if (!(Prop(baker, "SdfTexture") is RenderTexture rt))
                    return new { error = "BakeSDF produced no SdfTexture." };
                var gridSize = (Vector3Int)Call(baker, bakerType, "GetGridSize");
                var actualBox = (Vector3)Call(baker, bakerType, "GetActualBoxSize");

                var tex = ReadbackSdfToTexture3D(rt, gridSize);

                if (existing != null) AssetDatabase.DeleteAsset(outputPath);
                AssetDatabase.CreateAsset(tex, outputPath);
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["op"] = "vfx_bake_sdf",
                    ["meshPath"] = meshPath,
                    ["outputPath"] = outputPath,
                    ["resolution"] = new JArray { gridSize.x, gridSize.y, gridSize.z },
                    ["actualBoxSize"] = new JArray { actualBox.x, actualBox.y, actualBox.z },
                    ["guid"] = AssetDatabase.AssetPathToGUID(outputPath)
                };
            }
            finally
            {
                try { Call(baker, bakerType, "Dispose"); }
                catch { /* best-effort cleanup */ }
            }
        }

        /// <summary>Read a 1-channel (RHalf / R16_SFloat) 3D RenderTexture back into a Texture3D via a
        /// synchronous AsyncGPUReadback — the readback the package's internal SaveToAsset does, but to an
        /// explicit path (its own path goes through an interactive save dialog).</summary>
        private static Texture3D ReadbackSdfToTexture3D(RenderTexture rt, Vector3Int grid)
        {
            // Request the full 3D region explicitly — the basic Request(rt, mip) overload reads only the
            // first depth slice of a Tex3D RenderTexture.
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(
                rt, 0, 0, grid.x, 0, grid.y, 0, grid.z, null);
            req.WaitForCompletion();
            if (req.hasError)
                throw new Exception("AsyncGPUReadback failed to read the baked SDF RenderTexture.");

            // A 3D readback exposes one depth slice per layer (GetData(layer)); concatenate them into the
            // full volume (RHalf = R16_SFloat = 1 ushort/voxel).
            int perLayer = grid.x * grid.y;
            var all = new ushort[perLayer * grid.z];
            for (int layer = 0; layer < req.layerCount; layer++)
            {
                var slice = req.GetData<ushort>(layer);
                Unity.Collections.NativeArray<ushort>.Copy(slice, 0, all, layer * perLayer, perLayer);
            }

            var tex = new Texture3D(grid.x, grid.y, grid.z, TextureFormat.RHalf, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixelData(all, 0);
            tex.Apply(false);
            return tex;
        }

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
                case SerializedPropertyType.ObjectReference:
                {
                    // Object-typed setting (e.g. a VFXManager compute shader / runtime-resources):
                    // load the asset by path; an empty/null value clears the reference.
                    var path = value.Type == JTokenType.Null ? null : value.ToString();
                    if (string.IsNullOrEmpty(path))
                        sp.objectReferenceValue = null;
                    else
                        sp.objectReferenceValue = AssetDatabase.LoadMainAssetAtPath(path)
                            ?? throw new Exception($"No asset at path '{path}' for object-reference setting.");
                    break;
                }
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
