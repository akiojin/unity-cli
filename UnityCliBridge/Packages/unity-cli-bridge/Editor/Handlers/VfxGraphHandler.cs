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
        private static Type GroupInfoType => T("UnityEditor.VFX.VFXUI+GroupInfo");
        private static Type NodeIDType => T("UnityEditor.VFX.VFXNodeID");
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

            // DFS from a top-level slot to `target`, recording each child step's descriptor name into
            // `path`. Returns true (path filled) when found; an empty path means `target` IS `current`.
            bool FindSlotPath(object current, object target, JArray path)
            {
                if (ReferenceEquals(current, target)) return true;
                IEnumerable children;
                try { children = Prop(current, "children") as IEnumerable; }
                catch { return false; }
                if (children == null) return false;
                foreach (var child in children)
                {
                    path.Add(SlotName(child));
                    if (FindSlotPath(child, target, path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
                return false;
            }

            // Address a slot within its owner as (top-level index, descriptor-named child subPath).
            // A top-level slot returns (idx, empty); a compound slot's child returns its ancestor's
            // top-level index plus the child-name path — the read-side analogue of link_slots' subPath.
            (int index, JArray subPath) LocateSlot(object slot)
            {
                var empty = new JArray();
                try
                {
                    var owner = Prop(slot, "owner");
                    if (owner == null) return (-1, empty);
                    bool isOutput = Prop(slot, "direction")?.ToString() == "kOutput";
                    var coll = Prop(owner, isOutput ? "outputSlots" : "inputSlots") as IEnumerable;
                    int idx = 0;
                    if (coll != null)
                        foreach (var top in coll)
                        {
                            var path = new JArray();
                            if (FindSlotPath(top, slot, path)) return (idx, path);
                            idx++;
                        }
                }
                catch { }
                return (-1, empty);
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
                    var (idx, subPath) = LocateSlot(other);
                    var link = new JObject
                    {
                        ["node"] = ResolveAddress(owner),
                        ["slot"] = idx,
                        ["name"] = SlotName(other)
                    };
                    // A sub-slot endpoint (the edge lands on a compound slot's child) carries the
                    // descriptor-named path from its top-level slot, so the link round-trips through
                    // link_slots'/unlink_slots' `subPath` instead of collapsing to an unaddressable -1.
                    if (subPath.Count > 0) link["subPath"] = subPath;
                    arr.Add(link);
                }
                return arr;
            }

            // Serialize one slot, recursively surfacing any compound child sub-slots that carry a link
            // somewhere in their subtree. Pruned to linked descendants so simple/unlinked compound slots
            // stay as lean as before — only sub-slot LINKS add nodes.
            JObject SlotEntry(object slot, int index)
            {
                var links = LinksJson(slot);
                JToken value = null;
                try { value = ToJToken(Prop(slot, "value")); }
                catch { /* some slot types may not have a readable value */ }
                var entry = new JObject
                {
                    ["index"] = index,
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

                // Compound slots (Vector2/Vector3/Sphere/Transform/…) expose child sub-slots that can be
                // linked independently of the parent. Surface only children whose subtree carries a link so
                // describe no longer hides sub-slot connections (e.g. a Vector2 range whose x/y feed a
                // Random's min/max). The parent's own `hasLink` stays own-links only — a slot with only
                // linked children still reports hasLink:false; its `children` array carries the truth.
                var children = new JArray();
                IEnumerable childColl = null;
                try { childColl = Prop(slot, "children") as IEnumerable; }
                catch { /* leaf slot without children */ }
                if (childColl != null)
                {
                    int ci = 0;
                    foreach (var child in childColl)
                    {
                        var childEntry = SlotEntry(child, ci++);
                        if (childEntry.Value<bool>("hasLink") || childEntry["children"] != null)
                            children.Add(childEntry);
                    }
                }
                if (children.Count > 0) entry["children"] = children;
                return entry;
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
                    arr.Add(SlotEntry(slot, idx++));
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
                    ["instanceId"] = (ctx as UnityEngine.Object)?.GetInstanceID(),
                    ["contextType"] = ctxType,
                    ["type"] = ctx.GetType().Name,
                    ["name"] = ModelName(ctx),
                    ["position"] = PositionJson(ModelPosition(ctx)),
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
                    ["instanceId"] = (op as UnityEngine.Object)?.GetInstanceID(),
                    ["type"] = op.GetType().Name,
                    ["name"] = ModelName(op),
                    ["position"] = PositionJson(ModelPosition(op)),
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
                // Canvas nodes: a parameter appears on the canvas once per VFXParameter.Node.
                var canvasNodes = new JArray();
                try
                {
                    if (Prop(p, "nodes") is IEnumerable nodeList)
                        foreach (var n in nodeList)
                            canvasNodes.Add(new JObject
                            {
                                ["id"] = Convert.ToInt32(Prop(n, "id")),
                                ["position"] = PositionJson((Vector2)(FindField(n.GetType(), "position")?.GetValue(n) ?? Vector2.zero))
                            });
                }
                catch { /* parameter without canvas nodes — leave empty */ }
                paramsJson.Add(new JObject
                {
                    ["index"] = i,
                    ["instanceId"] = (p as UnityEngine.Object)?.GetInstanceID(),
                    ["type"] = p.GetType().Name,
                    ["parameterType"] = (Prop(p, "type") as Type)?.Name,
                    ["exposedName"] = exposedName,
                    ["exposed"] = exposed,
                    ["isOutput"] = isOutput,
                    ["category"] = category,
                    ["order"] = order,
                    ["position"] = PositionJson(ModelPosition(p)),
                    ["nodes"] = canvasNodes,
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

            // Group boxes (VFXUI.groupInfos): title + canvas rect + member nodes resolved to the
            // same stable addresses the rest of describe uses (sticky-note members carry their id).
            var groupsJson = new JArray();
            try
            {
                var uiInfos = Prop(graph, "UIInfos");
                if (FindField(uiInfos?.GetType(), "groupInfos")?.GetValue(uiInfos) is Array groupArr)
                {
                    foreach (var g in groupArr)
                    {
                        var rect = (Rect)(FindField(GroupInfoType, "position")?.GetValue(g) ?? default(Rect));
                        var members = new JArray();
                        if (FindField(GroupInfoType, "contents")?.GetValue(g) is Array contents)
                        {
                            foreach (var nid in contents)
                            {
                                bool sticky = (bool)(FindField(NodeIDType, "isStickyNote")?.GetValue(nid) ?? false);
                                var member = sticky
                                    ? new JObject { ["kind"] = "stickyNote" }
                                    : ResolveAddress(FindField(NodeIDType, "model")?.GetValue(nid));
                                member["id"] = Convert.ToInt32(FindField(NodeIDType, "id")?.GetValue(nid) ?? 0);
                                members.Add(member);
                            }
                        }
                        groupsJson.Add(new JObject
                        {
                            ["title"] = FindField(GroupInfoType, "title")?.GetValue(g) as string,
                            ["position"] = new JArray { rect.x, rect.y, rect.width, rect.height },
                            ["contents"] = members
                        });
                    }
                }
            }
            catch { /* graphs without UI sidecar — leave empty */ }

            var categories = CategoriesJson(graph);
            var customAttributes = CustomAttributesJson(graph);
            string initialEventName = null;
            try { initialEventName = InitialEventNameOf(Prop(graph, "visualEffectResource")); }
            catch { /* resource unavailable — leave null */ }
            JObject instancing = null;
            try { instancing = InstancingJson(graph); }
            catch { /* resource unavailable — leave null */ }

            // Tier-2 oracle (on by default; pass includeErrors:false to skip): per-model validation
            // errors (Invalidate tier) + the last compile's errors/exception (Compilation tier).
            var includeErrors = parameters?["includeErrors"]?.ToObject<bool>() ?? true;
            JArray errors = includeErrors ? AllErrors(graph, assetPath) : null;

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
                ["groupCount"] = groupsJson.Count,
                ["groups"] = groupsJson,
                ["categories"] = categories,
                ["customAttributeCount"] = customAttributes.Count,
                ["customAttributes"] = customAttributes,
                ["initialEventName"] = initialEventName,
                ["instancing"] = instancing,
                ["template"] = TemplateInfoJson(assetPath),
                ["errors"] = errors,
                ["compile"] = LastCompileFor(assetPath),
                ["layout"] = LayoutJson(graph),
                ["touched"] = TouchedJson(assetPath, ctxList, opList, paramList)
            };
        }

        /// <summary>The nodes recorded as touched this session, resolved to describe indices.</summary>
        private static JObject TouchedJson(string assetPath, List<object> ctxList, List<object> opList, List<object> paramList)
        {
            s_Touched.TryGetValue(assetPath, out var set);
            JArray Idx(List<object> list) => new JArray(list.Select((m, i) => (m, i))
                .Where(t => set != null && set.Contains((t.m as UnityEngine.Object)?.GetInstanceID() ?? 0))
                .Select(t => (JToken)t.i));
            return new JObject { ["contexts"] = Idx(ctxList), ["operators"] = Idx(opList), ["parameters"] = Idx(paramList) };
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

                foreach (var e in ReporterErrorsJson(reporter, "Invalidate")) arr.Add(e);
            }
            catch (Exception ex)
            {
                arr.Add(new JObject { ["error"] = $"error-collector failed: {ex.Message}" });
            }
            return arr;
        }

        /// <summary>
        /// Every error the graph currently carries: the Invalidate tier (per-model validation, e.g. HLSL
        /// parse failures) plus the Compilation tier (what the last import's compiler registered), plus a
        /// synthetic `CompileException` entry when the last import of this asset threw inside the compiler.
        /// </summary>
        private static JArray AllErrors(object graph, string assetPath)
        {
            var arr = CollectErrors(graph);
            foreach (var e in CompileReporterErrors(graph)) arr.Add(e);
            var last = LastCompileFor(assetPath);
            var exception = last?["exception"]?.Type == JTokenType.String ? (string)last["exception"] : null;
            if (!string.IsNullOrEmpty(exception))
            {
                arr.Add(new JObject
                {
                    ["model"] = null,
                    ["modelType"] = null,
                    ["type"] = "Error",
                    ["error"] = "CompileException",
                    ["description"] = exception,
                    ["origin"] = "Compilation"
                });
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
        // ---- Touched tracking -----------------------------------------------------------------
        //
        // auto_layout defaults to the systems an agent actually worked on, so the rest of a person's
        // canvas is left alone. "Worked on" is derived, not declared: every vfx_apply op fingerprints
        // the graph (settings, values, links — not positions) before and after, and any context,
        // operator or parameter whose fingerprint appeared or changed is recorded per asset for the
        // editor session (a domain reload clears it; auto_layout then asks for an explicit scope).

        private static readonly Dictionary<string, HashSet<int>> s_Touched =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        // Nodes that did not exist before an op this session: a system made only of created contexts
        // is "new" and may be placed in a fresh column; every other system only ever moves vertically.
        private static readonly Dictionary<string, HashSet<int>> s_Created =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<int> CreatedFor(string assetPath)
        {
            if (!s_Created.TryGetValue(assetPath, out var set)) s_Created[assetPath] = set = new HashSet<int>();
            return set;
        }

        private static readonly HashSet<string> s_NonTrackedOps = new HashSet<string>
        { "auto_layout", "compile", "move_node", "group_nodes", "remove_group", "add_sticky_note",
          "update_sticky_note", "remove_sticky_note", "reorder_sticky_note", "create_subgraph_asset",
          "create_from_template", "designate_template" };

        /// <summary>instanceId → fingerprint of every context/operator/parameter (layout fields stripped).</summary>
        private static Dictionary<int, string> Fingerprint(string assetPath)
        {
            var result = new Dictionary<int, string>();
            try
            {
                var d = DescribeGraphCore(new JObject { ["assetPath"] = assetPath, ["includeErrors"] = false }) as JObject;
                if (d == null) return result;
                foreach (var key in new[] { "contexts", "operators", "parameters" })
                    foreach (var e in (JArray)d[key])
                    {
                        var copy = (JObject)e.DeepClone();
                        copy.Remove("position"); copy.Remove("index"); copy.Remove("nodes");
                        int id = copy.Value<int?>("instanceId") ?? 0;
                        copy.Remove("instanceId");
                        if (id != 0) result[id] = copy.ToString(Newtonsoft.Json.Formatting.None);
                    }
            }
            catch { /* tracking is best-effort */ }
            return result;
        }

        private static HashSet<int> TouchedFor(string assetPath)
        {
            if (!s_Touched.TryGetValue(assetPath, out var set)) s_Touched[assetPath] = set = new HashSet<int>();
            return set;
        }

        public static object Apply(JObject parameters)
        {
            s_LastCompile = null;
            try
            {
                var op = parameters?["op"]?.ToString();
                var assetPath = parameters?["assetPath"]?.ToString();
                bool track = !string.IsNullOrEmpty(op) && !s_NonTrackedOps.Contains(op) && !string.IsNullOrEmpty(assetPath);
                Dictionary<int, string> before = track ? Fingerprint(assetPath) : null;
                var result = ApplyCore(parameters);
                if (track && result is JObject okResult && okResult["error"] == null)
                {
                    var after = Fingerprint(assetPath);
                    var touched = TouchedFor(assetPath);
                    var created = CreatedFor(assetPath);
                    foreach (var kv in after)
                    {
                        if (!before.TryGetValue(kv.Key, out var prev)) { touched.Add(kv.Key); created.Add(kv.Key); }
                        else if (prev != kv.Value) touched.Add(kv.Key);
                    }
                }
                // Every persisted op reports the outcome of the recompile it triggered, so a mutation
                // that leaves the graph uncompilable is visible in the op's own response.
                if (result is JObject jo && s_LastCompile != null && jo["compile"] == null)
                    jo["compile"] = s_LastCompile;
                return result;
            }
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
                case "move_node": return MoveNode(parameters);
                case "group_nodes": return GroupNodes(parameters);
                case "remove_group": return RemoveGroup(parameters);
                case "auto_layout": return AutoLayout(parameters);
                case "compile": return CompileGraph(parameters);
                case "set_parameter": return SetParameter(parameters);
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
                    return new { error = $"Unsupported op: '{op}'. Supported: add_block, set_block_setting, set_operator_setting, add_operator_input, remove_operator_input, set_operator_operand_type, rename_operator_input, reorder_operator_input, set_context_setting, add_context, add_operator, add_parameter, link_slots, set_slot_value, set_slot_space, convert_to_property, convert_to_inline, unlink_slots, remove_block, set_block_enabled, reorder_block, move_block, move_node, group_nodes, remove_group, auto_layout, compile, duplicate_block, duplicate_operator, remove_operator, remove_parameter, set_parameter, rename_parameter, set_parameter_category, rename_category, reorder_category, reorder_parameter, duplicate_parameter, remove_context, delete_system, set_system_name, add_custom_attribute, link_flow, unlink_flow, set_bounds, add_sticky_note, update_sticky_note, remove_sticky_note, reorder_sticky_note, set_instancing, set_initial_event_name, create_subgraph_asset, create_from_template, insert_template, designate_template" };
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
                // Same coercion as set_block_setting / set_operator_setting / set_context_setting, so an
                // inline `settings` map accepts enum names, asset paths for Object fields, and the
                // Custom HLSL function selector — and fails loudly instead of silently skipping.
                object converted = CoerceSettingValue(field, kv.Value, kv.Key);
                Call(model, ModelType, "SetSettingValue", kv.Key, converted);
                applied.Add(kv.Key);
            }
            return applied;
        }

        // ---- Compile-outcome capture ------------------------------------------
        //
        // Reimporting a .vfx runs the VFX compiler (VFXGraph.OnCompileResource → CompileForImport).
        // Two failure channels exist and neither reaches a structural describe:
        //   * graph.errorManager.compileReporter — "Compilation"-origin ReportErrors registered by
        //     the compiler (e.g. Shader Graph validity failures);
        //   * a thrown exception inside VFXGraphCompiledData.Compile — caught by the package and
        //     ONLY Debug.LogError'd ("Unity cannot compile the VisualEffectAsset at path …"), e.g.
        //     a Custom HLSL operator with > 4 inputs ("An expression can only take up to 4 parent
        //     expressions").
        // Persist captures Error/Exception console output for the duration of the import and
        // combines it with the compile reporter into a `compile` summary that every vfx_apply
        // response carries, so an op that leaves the graph uncompilable says so immediately.

        private static readonly List<(LogType type, string message)> s_ImportLogs =
            new List<(LogType, string)>();
        private static bool s_CapturingImportLogs;
        private static JObject s_LastCompile;
        private static readonly Dictionary<string, JObject> s_LastCompileByAsset =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        static VfxGraphHandler()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (!s_CapturingImportLogs) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            lock (s_ImportLogs) s_ImportLogs.Add((type, message));
        }

        /// <summary>The Error/Exception logs emitted while the last import of this asset ran, as JSON.</summary>
        private static JArray ImportLogsJson(out string compileException)
        {
            compileException = null;
            var logs = new JArray();
            lock (s_ImportLogs)
            {
                foreach (var (type, message) in s_ImportLogs)
                {
                    if (message.IndexOf("cannot compile the VisualEffectAsset", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // "Unity cannot compile the VisualEffectAsset at path "…" because of the following
                        // exception:\nSystem.ArgumentException: <reason>\n  at …" → surface the reason line.
                        var lines = message.Split('\n');
                        compileException = (lines.Length > 1 ? lines[1] : lines[0]).Trim();
                    }
                    logs.Add(new JObject
                    {
                        ["type"] = type.ToString(),
                        ["message"] = message.Length > 800 ? message.Substring(0, 800) + "…" : message
                    });
                }
            }
            return logs;
        }

        /// <summary>Dump a VFXErrorReporter's m_Errors dictionary as JSON entries tagged with `origin`.</summary>
        private static JArray ReporterErrorsJson(object reporter, string origin)
        {
            var arr = new JArray();
            if (reporter == null) return arr;
            var errorsField = reporter.GetType().GetField("m_Errors", AllInstance);
            var dict = errorsField?.GetValue(reporter) as IDictionary;
            if (dict == null) return arr;
            foreach (DictionaryEntry kv in dict)
            {
                var modelName = (kv.Key as UnityEngine.Object)?.name ?? kv.Key?.GetType().Name;
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
                        ["description"] = Prop(rep, "description") as string,
                        ["origin"] = origin
                    });
                }
            }
            return arr;
        }

        /// <summary>The "Compilation"-origin errors the last compile registered on the graph's error manager.</summary>
        private static JArray CompileReporterErrors(object graph)
        {
            try
            {
                var manager = Prop(graph, "errorManager");
                var reporter = manager == null ? null : Prop(manager, "compileReporter");
                return ReporterErrorsJson(reporter, "Compilation");
            }
            catch { return new JArray(); }
        }

        /// <summary>
        /// Summarize the outcome of the import that Persist just ran: `success` is false when the compiler
        /// threw (`exception` carries the reason), registered an Error-tier compile report, or any
        /// Error/Exception was logged during the import (`logs`).
        /// </summary>
        private static JObject CompileSummary(object graph, string assetPath)
        {
            var logs = ImportLogsJson(out var exception);
            var errors = CompileReporterErrors(graph);
            bool hasErrorTier = errors.Any(e => string.Equals((string)e["type"], "Error", StringComparison.Ordinal));
            return new JObject
            {
                ["success"] = exception == null && !hasErrorTier && logs.Count == 0,
                ["exception"] = exception,
                ["errors"] = errors,
                ["logs"] = logs
            };
        }

        /// <summary>The last compile summary recorded for an asset this editor session (null if none).</summary>
        private static JObject LastCompileFor(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && s_LastCompileByAsset.TryGetValue(assetPath, out var s) ? s : null;
        }

        /// <summary>
        /// Mark the graph dirty, write the asset, and reimport so it recompiles. Returns the compile
        /// summary of that import (also remembered per asset for describe, and attached to the
        /// vfx_apply response by <see cref="Apply"/>).
        /// </summary>
        private static JObject Persist(object graph, string assetPath)
        {
            Call(graph, GraphType, "SetExpressionGraphDirty", true);
            var resource = Prop(graph, "visualEffectResource");
            Call(null, ResourceExtType, "WriteAssetWithSubAssets", resource);
            lock (s_ImportLogs) s_ImportLogs.Clear();
            s_CapturingImportLogs = true;
            try { AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate); }
            finally { s_CapturingImportLogs = false; }
            AssetDatabase.SaveAssets();
            var summary = CompileSummary(graph, assetPath);
            s_LastCompile = summary;
            s_LastCompileByAsset[assetPath] = summary;
            return summary;
        }

        /// <summary>
        /// `compile` op: force a recompile of the graph (reimport) and report the outcome — the
        /// explicit "is this graph compilable?" check. Also returns the Invalidate-tier validation
        /// errors so one call answers both tiers.
        /// </summary>
        private static object CompileGraph(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var compile = Persist(graph, assetPath);
            var validation = CollectErrors(graph);
            return new JObject
            {
                ["op"] = "compile",
                ["assetPath"] = assetPath,
                ["compile"] = compile,
                ["validationErrors"] = validation,
                ["ok"] = (bool)compile["success"]
                        && !validation.Any(e => string.Equals((string)e["type"], "Error", StringComparison.Ordinal))
            };
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

            // Optional `index` = insert position within the context (-1 / omitted = append).
            int insertIndex = parameters?["index"]?.ToObject<int>() ?? -1;
            Call(targetContext, ContextType, "AddChild", block, insertIndex, true);

            // Optional settings — same coercion + fail-loud contract as set_block_setting. A failed
            // setting removes the block again so the in-memory graph is not left with a stray node.
            JArray applied;
            try { applied = ApplySettings(block, parameters?["settings"] as JObject); }
            catch { Call(targetContext, ModelType, "RemoveChild", block, true); throw; }

            Persist(graph, assetPath);

            int blockIndex = Children(targetContext).ToList().FindIndex(b => ReferenceEquals(b, block));
            return new JObject
            {
                ["op"] = "add_block",
                ["assetPath"] = assetPath,
                ["contextType"] = wantContext,
                ["contextIndex"] = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList()
                    .FindIndex(c => ReferenceEquals(c, targetContext)),
                ["blockIndex"] = blockIndex,
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
            object previous = field.GetValue(op);
            Call(op, ModelType, "SetSettingValue", settingName, converted);

            // A Custom HLSL operator compiles to ONE VFXExpressionHLSL whose parents are its inputs, and
            // a VFX expression takes at most 4 parents — the compiler throws on the 5th and the asset
            // silently stops compiling. Refuse (and revert) here so the graph never enters that state.
            var guard = CustomHlslOperatorInputGuard(op);
            if (guard != null)
            {
                Call(op, ModelType, "SetSettingValue", settingName, previous);
                return new { error = guard + " The setting was not applied." };
            }
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

        /// <summary>The hard cap on a VFX expression's parent count (VFXExpression ctor throws above it).</summary>
        private const int MaxExpressionParents = 4;

        private static bool IsCustomHlslOperator(object op) =>
            op != null && op.GetType().FullName == "UnityEditor.VFX.Operator.CustomHLSL";

        /// <summary>
        /// Null when `op` is not a Custom HLSL operator or its selected function exposes ≤ 4 inputs;
        /// otherwise the error message explaining why the graph would not compile.
        /// </summary>
        private static string CustomHlslOperatorInputGuard(object op)
        {
            if (!IsCustomHlslOperator(op)) return null;
            int inputs = 0;
            try { inputs = (Prop(op, "inputSlots") as IEnumerable)?.Cast<object>().Count() ?? 0; }
            catch { return null; }
            if (inputs <= MaxExpressionParents) return null;
            return $"Custom HLSL operator '{ModelName(op)}' exposes {inputs} inputs, but a VFX expression " +
                   $"takes at most {MaxExpressionParents} parents — the graph would fail to compile " +
                   "(\"An expression can only take up to 4 parent expressions\"). Pack inputs into " +
                   "Vector2/Vector3/Vector4 parameters or split the function (a Custom HLSL BLOCK has no such limit).";
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
            bool hasIndex = ContextIndexToken(parameters) != null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index/contextIndex) is required" };

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
            bool hasIndex = ContextIndexToken(parameters) != null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index/contextIndex) is required" };

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
            bool hasIndex = ContextIndexToken(parameters) != null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index/contextIndex) is required" };
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

        // ---- Canvas layout helpers ---------------------------------------

        /// <summary>Optional `[x, y]` canvas position parameter. Null when absent.</summary>
        private static Vector2? PositionParam(JObject parameters)
        {
            if (!(parameters?["position"] is JArray arr) || arr.Count < 2) return null;
            return new Vector2(arr[0].ToObject<float>(), arr[1].ToObject<float>());
        }

        private static Vector2 ModelPosition(object model)
        {
            try { return (Vector2)Prop(model, "position"); }
            catch { return Vector2.zero; }
        }

        private static JArray PositionJson(Vector2 pos) => new JArray { pos.x, pos.y };

        // Spacing constants for auto-placement. VFX Graph systems flow top-to-bottom
        // (Spawn → Init → Update → Output) with operators feeding in from the left.
        private const float ContextFlowStepY = 450f;
        private const float SystemColumnStepX = 700f;
        private const float OperatorColumnOffsetX = 600f;
        private const float OperatorStackStepY = 180f;

        /// <summary>
        /// Default canvas position for a new context: below its flow source when linking (systems
        /// read top-to-bottom), otherwise right of the rightmost existing context so a new system
        /// starts its own column instead of stacking at the origin.
        /// </summary>
        private static Vector2 AutoContextPosition(object graph, object linkFromContext, object newContext)
        {
            if (linkFromContext != null)
                return ModelPosition(linkFromContext) + new Vector2(0, ContextFlowStepY);
            var others = Children(graph)
                .Where(c => ContextType.IsInstanceOfType(c) && !ReferenceEquals(c, newContext)).ToList();
            if (others.Count == 0) return Vector2.zero;
            return new Vector2(others.Max(c => ModelPosition(c).x) + SystemColumnStepX, 0);
        }

        /// <summary>
        /// Default canvas position for a new operator: a staggered column left of the leftmost
        /// context (operators feed rightward into blocks/contexts), stepping down per operator.
        /// </summary>
        private static Vector2 AutoOperatorPosition(object graph, object newOp)
        {
            var ctxs = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            float baseX = (ctxs.Count > 0 ? ctxs.Min(c => ModelPosition(c).x) : 0f) - OperatorColumnOffsetX;
            int stack = Children(graph)
                .Count(c => OperatorType.IsInstanceOfType(c) && !ReferenceEquals(c, newOp));
            return new Vector2(baseX, stack * OperatorStackStepY);
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

            // Optional context settings (e.g. an Event context's eventName). A failed setting removes
            // the context again so the in-memory graph is not left with a stray node.
            JArray appliedSettings;
            try { appliedSettings = ApplySettings(context, parameters?["settings"] as JObject); }
            catch { Call(graph, ModelType, "RemoveChild", context, true); throw; }

            // Optional flow link: an existing context (by contextType) flows INTO the new one.
            JObject linked = null;
            object fromContext = null;
            if (!string.IsNullOrEmpty(linkFrom))
            {
                fromContext = FindContext(graph, linkFrom);
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

            // Canvas position: explicit `position:[x,y]`, else auto-place so nodes never stack at
            // the origin (below the flow source, or a fresh column for an unlinked context).
            var pos = PositionParam(parameters) ?? AutoContextPosition(graph, fromContext, context);
            SetProp(context, "position", pos);

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "add_context",
                ["assetPath"] = assetPath,
                ["addedContext"] = context.GetType().Name,
                ["matchedDescriptor"] = matchedDescriptor,
                ["settingsApplied"] = appliedSettings,
                ["linked"] = linked,
                ["position"] = PositionJson(pos)
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

            // Optional settings (e.g. a Custom HLSL operator's m_HLSLCode) — same contract as
            // set_operator_setting, including the Custom HLSL input-count guard.
            JArray appliedSettings;
            try { appliedSettings = ApplySettings(op, parameters?["settings"] as JObject); }
            catch
            {
                Call(graph, ModelType, "RemoveChild", op, true);
                throw;
            }
            var guard = CustomHlslOperatorInputGuard(op);
            if (guard != null)
            {
                // Expected user-input validation → quiet error (no logged exception), node not left behind.
                Call(graph, ModelType, "RemoveChild", op, true);
                return new { error = guard + " The operator was not added." };
            }

            // Canvas position: explicit `position:[x,y]`, else a staggered column left of the
            // contexts so operators stay readable instead of stacking at the origin.
            var pos = PositionParam(parameters) ?? AutoOperatorPosition(graph, op);
            SetProp(op, "position", pos);

            Persist(graph, assetPath);

            int operatorIndex = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList()
                .FindIndex(o => ReferenceEquals(o, op));

            return new JObject
            {
                ["op"] = "add_operator",
                ["assetPath"] = assetPath,
                ["addedOperator"] = op.GetType().Name,
                ["matchedDescriptor"] = Prop(match, "name") as string,
                ["operatorIndex"] = operatorIndex,
                ["settingsApplied"] = appliedSettings,
                ["position"] = PositionJson(pos)
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
            // `target.subPath` descends into a compound slot's CHILD slot (link_slots' addressing); the
            // top-level `subPath` below walks the VALUE struct instead. Both end at the same leaf.
            var targetSub = (target["subPath"] as JArray)?.Select(t => t.ToString()).ToArray();
            slot = DescendSlot(slot, targetSub, "target");

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

            var result = new JObject
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
            // A linked input ignores its constant — say so instead of letting the write look effective.
            if (LinkCount(slot) > 0)
                result["warning"] = $"slot '{SlotName(slot)}' is linked; the link drives it and this constant is ignored until unlink_slots.";
            return result;
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
        /// Set a node's canvas position — layout only, no functional graph change. `target` uses the
        /// same addressing as link_slots endpoints ({node: context|operator|parameter, …address});
        /// `position` is `[x, y]`. Contexts and operators carry one VFXModel.position; a parameter's
        /// canvas presence is its VFXParameter.Node list, so every node of the parameter is moved
        /// (nodes after the first are staggered vertically to stay individually clickable), and the
        /// model position doubles as the seed for the node the editor auto-creates when a linked
        /// parameter has no canvas node yet.
        /// </summary>
        private static object MoveNode(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var target = parameters?["target"] as JObject;
            if (target == null)
                return new { error = "target is required (an object {node: context|operator|parameter, …address})" };
            var pos = PositionParam(parameters);
            if (pos == null)
                return new { error = "position is required ([x, y])" };

            var graph = LoadGraph(assetPath);
            var node = ResolveNode(graph, target, "target");
            if (BlockType.IsInstanceOfType(node))
                return new { error = "Blocks have no canvas position (they are ordered inside their context); use reorder_block or move_block." };

            // A context that existed before this session keeps its x: systems and their contexts only
            // move vertically (a person's horizontal lanes are theirs). Contexts created this session
            // may be placed freely.
            string note = null;
            if (ContextType.IsInstanceOfType(node))
            {
                s_Created.TryGetValue(assetPath, out var createdSet);
                bool created = createdSet != null && createdSet.Contains((node as UnityEngine.Object)?.GetInstanceID() ?? 0);
                var current = ModelPosition(node);
                if (!created && Math.Abs(current.x - pos.Value.x) > 0.5f)
                {
                    pos = new Vector2(current.x, pos.Value.y);
                    note = "contexts of existing systems move vertically only; x was kept";
                }
            }

            int movedParameterNodes = 0;
            // Contexts/operators carry one VFXModel.position. A parameter's canvas presence is its
            // VFXParameter.Node list — move every node (staggered so they stay individually
            // clickable). Setting the model position too is deliberate: for a parameter with links
            // but no canvas node yet, VFXParameter.OnEnable seeds the auto-created node from it.
            SetProp(node, "position", pos.Value);
            if (ParameterType.IsInstanceOfType(node))
            {
                var nodes = (Prop(node, "nodes") as IEnumerable)?.Cast<object>().ToList()
                            ?? new List<object>();
                var posField = nodes.Count > 0 ? FindField(nodes[0].GetType(), "position") : null;
                for (int i = 0; i < nodes.Count; i++)
                {
                    posField.SetValue(nodes[i], pos.Value + new Vector2(0, i * OperatorStackStepY));
                    movedParameterNodes++;
                }
            }

            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "move_node",
                ["assetPath"] = assetPath,
                ["node"] = node.GetType().Name,
                ["position"] = PositionJson(pos.Value),
                ["movedParameterNodes"] = ParameterType.IsInstanceOfType(node) ? (int?)movedParameterNodes : null,
                ["note"] = note
            };
        }

        /// <summary>
        /// Edit an existing blackboard parameter in place (the post-creation twin of add_parameter):
        /// `value` (coerced to the parameter's type — number/bool, [x,y,z] vector, [r,g,b,a] color, or an
        /// asset path for Texture/Mesh), `min`/`max` (switch `valueFilter` to Range), `valueFilter`
        /// (Default/Range/Enum), `tooltip`, `exposed` (bool), `category`, `exposedName` (unique).
        /// </summary>
        private static object SetParameter(JObject parameters)
        {
            int parameterIndex = parameters?["parameterIndex"]?.ToObject<int>() ?? 0;
            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var (param, all) = ResolveParameter(graph, parameterIndex);
            var paramType = Prop(param, "type") as Type;
            var changed = new JArray();

            JToken Tok(string key)
            {
                var t = parameters?[key];
                return t == null || t.Type == JTokenType.Null ? null : t;
            }

            var newName = Tok("exposedName")?.ToString();
            if (!string.IsNullOrEmpty(newName))
            {
                if (all.Any(p => !ReferenceEquals(p, param) &&
                                 string.Equals(Prop(p, "exposedName") as string, newName, StringComparison.Ordinal)))
                    return new { error = $"another parameter is already named '{newName}' (exposed names must be unique)" };
                Call(param, ModelType, "SetSettingValue", "m_ExposedName", newName);
                changed.Add("exposedName");
            }
            if (Tok("exposed") != null)
            {
                Call(param, ModelType, "SetSettingValue", "m_Exposed", Tok("exposed").ToObject<bool>());
                changed.Add("exposed");
            }
            if (Tok("value") != null)
            {
                SetProp(param, "value", ParamCoerce(Tok("value"), paramType, "value"));
                changed.Add("value");
            }
            var filterProp = param.GetType().GetProperty("valueFilter", BindingFlags.Public | BindingFlags.Instance);
            if (Tok("valueFilter") != null)
            {
                if (filterProp == null) return new { error = "this parameter type has no valueFilter" };
                object f;
                try { f = Enum.Parse(filterProp.PropertyType, Tok("valueFilter").ToString(), true); }
                catch { return new { error = $"invalid valueFilter; valid: {string.Join(", ", Enum.GetNames(filterProp.PropertyType))}" }; }
                SetProp(param, "valueFilter", f);
                changed.Add("valueFilter");
            }
            if (Tok("min") != null || Tok("max") != null)
            {
                if (filterProp != null && Tok("valueFilter") == null)
                    SetProp(param, "valueFilter", Enum.Parse(filterProp.PropertyType, "Range", true));
                if (Tok("min") != null) { SetProp(param, "min", ParamCoerce(Tok("min"), paramType, "min")); changed.Add("min"); }
                if (Tok("max") != null) { SetProp(param, "max", ParamCoerce(Tok("max"), paramType, "max")); changed.Add("max"); }
            }
            if (Tok("tooltip") != null) { SetProp(param, "tooltip", Tok("tooltip").ToString()); changed.Add("tooltip"); }
            if (Tok("category") != null) { SetProp(param, "category", Tok("category").ToString()); changed.Add("category"); }

            if (changed.Count == 0)
                return new { error = "set_parameter requires at least one of: value, min, max, valueFilter, tooltip, exposed, category, exposedName" };

            Persist(graph, assetPath);

            JToken Safe(string prop) { try { return ToJToken(Prop(param, prop)); } catch { return null; } }
            return new JObject
            {
                ["op"] = "set_parameter",
                ["assetPath"] = assetPath,
                ["parameterIndex"] = parameterIndex,
                ["changed"] = changed,
                ["exposedName"] = Prop(param, "exposedName") as string,
                ["exposed"] = (bool)Prop(param, "exposed"),
                ["value"] = Safe("value"),
                ["valueFilter"] = Safe("valueFilter"),
                ["min"] = Safe("min"),
                ["max"] = Safe("max"),
                ["tooltip"] = Safe("tooltip"),
                ["category"] = Safe("category")
            };
        }

        /// <summary>Remove a group box by `title` (or `index` into describe's `groups[]`); member nodes stay.</summary>
        private static object RemoveGroup(JObject parameters)
        {
            var title = parameters?["title"]?.ToString();
            var idxTok = parameters?["index"];
            bool hasIndex = idxTok != null && idxTok.Type != JTokenType.Null;
            if (string.IsNullOrEmpty(title) && !hasIndex)
                return new { error = "title (or index) is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ui = Prop(graph, "UIInfos");
            if (ui == null) throw new Exception("Graph has no UIInfos sidecar (unexpected for a valid .vfx).");
            var groupsField = FindField(ui.GetType(), "groupInfos");
            var groups = groupsField.GetValue(ui) as Array ?? Array.CreateInstance(GroupInfoType, 0);
            var titleField = FindField(GroupInfoType, "title");

            int gi = -1;
            if (hasIndex) gi = idxTok.ToObject<int>();
            else
                for (int i = 0; i < groups.Length; i++)
                    if (string.Equals(titleField.GetValue(groups.GetValue(i)) as string, title, StringComparison.Ordinal))
                    { gi = i; break; }
            if (gi < 0 || gi >= groups.Length)
                return new { error = hasIndex
                    ? $"index {gi} out of range; graph has {groups.Length} group(s)"
                    : $"no group titled '{title}'; existing: [{string.Join(", ", groups.Cast<object>().Select(g => titleField.GetValue(g) as string))}]" };

            var removedTitle = titleField.GetValue(groups.GetValue(gi)) as string;
            var newGroups = Array.CreateInstance(GroupInfoType, groups.Length - 1);
            int w = 0;
            for (int r = 0; r < groups.Length; r++)
                if (r != gi) newGroups.SetValue(groups.GetValue(r), w++);
            groupsField.SetValue(ui, newGroups);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "remove_group",
                ["assetPath"] = assetPath,
                ["removedTitle"] = removedTitle,
                ["remainingGroups"] = newGroups.Length
            };
        }

        // ---- Layout estimation + auto layout ------------------------------------
        //
        // The editor measures nodes with UI Toolkit, which a headless bridge cannot run, so the layout
        // code estimates node bounds from what the model knows (slot and block counts). The estimates
        // are generous on purpose: an overlap the estimator reports is a real overlap or a near-miss,
        // and auto_layout spaces nodes by the same estimates so the result stays readable in the editor.

        /// <summary>Reference-identity comparer for model-keyed dictionaries (VFXModel overrides Equals via UnityEngine.Object).</summary>
        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        // Spacing calibrated against a hand-laid-out production graph (feeders ~220-280 px from what
        // they feed, ~30 px between stacked nodes).
        private const float LayoutContextWidth = 440f;
        private const float LayoutOperatorWidth = 220f;
        private const float LayoutParameterWidth = 200f;
        private const float LayoutContextHeaderHeight = 90f;
        private const float LayoutBlockHeaderHeight = 44f;
        private const float LayoutSlotRowHeight = 24f;
        private const float LayoutContextGapX = 120f;
        private const float LayoutContextGapY = 110f;
        private const float LayoutSystemGapX = 260f;
        private const float LayoutOperatorColumnGapX = 60f;
        private const float LayoutOperatorGapY = 30f;
        private const float LayoutParamSplitDistance = 600f; // consumers further apart than this get their own parameter node
        private const float LayoutGroupPadding = 32f;
        private const float LayoutGroupHeaderHeight = 48f;

        private static int TopLevelSlotCount(object model, string collection)
        {
            try { return (Prop(model, collection) as IEnumerable)?.Cast<object>().Count() ?? 0; }
            catch { return 0; }
        }

        /// <summary>Estimated on-canvas height of one block row inside a context.</summary>
        private static float EstimateBlockHeight(object block) =>
            LayoutBlockHeaderHeight + LayoutSlotRowHeight * TopLevelSlotCount(block, "inputSlots");

        /// <summary>Estimated on-canvas size of a context, operator, or parameter node.</summary>
        private static Vector2 EstimateSize(object model)
        {
            if (ContextType.IsInstanceOfType(model))
            {
                float h = LayoutContextHeaderHeight + LayoutSlotRowHeight * TopLevelSlotCount(model, "inputSlots");
                foreach (var b in Children(model)) h += EstimateBlockHeight(b);
                return new Vector2(LayoutContextWidth, h + 24f);
            }
            if (ParameterType.IsInstanceOfType(model))
                return new Vector2(LayoutParameterWidth, LayoutBlockHeaderHeight + LayoutSlotRowHeight);
            int rows = Math.Max(TopLevelSlotCount(model, "inputSlots"), TopLevelSlotCount(model, "outputSlots"));
            return new Vector2(LayoutOperatorWidth, LayoutBlockHeaderHeight + LayoutSlotRowHeight * Math.Max(rows, 1));
        }

        /// <summary>The vertical offset of a block's row inside its context (for aligning feeders to it).</summary>
        private static float BlockRowOffset(object ctx, object block)
        {
            float y = LayoutContextHeaderHeight + LayoutSlotRowHeight * TopLevelSlotCount(ctx, "inputSlots");
            foreach (var b in Children(ctx))
            {
                if (ReferenceEquals(b, block)) return y + EstimateBlockHeight(b) * 0.5f;
                y += EstimateBlockHeight(b);
            }
            return y;
        }

        /// <summary>Every canvas node (contexts, operators, one entry per parameter canvas node) with its estimated rect.</summary>
        private static List<(JObject address, Rect rect)> CanvasNodes(object graph)
        {
            var items = new List<(JObject, Rect)>();
            var ctxs = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();
            for (int i = 0; i < ctxs.Count; i++)
            {
                var size = EstimateSize(ctxs[i]);
                var pos = ModelPosition(ctxs[i]);
                items.Add((new JObject { ["kind"] = "context", ["index"] = i, ["name"] = ModelName(ctxs[i]) },
                           new Rect(pos.x, pos.y, size.x, size.y)));
            }
            for (int i = 0; i < ops.Count; i++)
            {
                var size = EstimateSize(ops[i]);
                var pos = ModelPosition(ops[i]);
                items.Add((new JObject { ["kind"] = "operator", ["index"] = i, ["name"] = ModelName(ops[i]) },
                           new Rect(pos.x, pos.y, size.x, size.y)));
            }
            for (int i = 0; i < ps.Count; i++)
            {
                var size = EstimateSize(ps[i]);
                var pname = (Prop(ps[i], "exposedName") as string) ?? ModelName(ps[i]);
                IEnumerable nodes = null;
                try { nodes = Prop(ps[i], "nodes") as IEnumerable; } catch { }
                int nodeCount = 0;
                if (nodes != null)
                    foreach (var n in nodes)
                    {
                        nodeCount++;
                        var pos = (Vector2)(FindField(n.GetType(), "position")?.GetValue(n) ?? Vector2.zero);
                        items.Add((new JObject
                        {
                            ["kind"] = "parameter", ["index"] = i, ["name"] = pname,
                            ["nodeId"] = Convert.ToInt32(Prop(n, "id"))
                        }, new Rect(pos.x, pos.y, size.x, size.y)));
                    }
                // A linked parameter authored headlessly has no canvas node yet; the editor creates one at
                // the model position on open, so that spot counts for layout too. Unused parameters live
                // only on the blackboard and take no canvas space.
                if (nodeCount == 0 && Consumers(ps[i]).Count > 0)
                {
                    var pos = ModelPosition(ps[i]);
                    items.Add((new JObject { ["kind"] = "parameter", ["index"] = i, ["name"] = pname, ["nodeId"] = null },
                               new Rect(pos.x, pos.y, size.x, size.y)));
                }
            }
            return items;
        }

        /// <summary>
        /// Layout diagnostics for describe: `overlapCount` + the overlapping node pairs (estimated
        /// bounds; capped at 50 pairs) and the canvas `bounds`. An agent finishing a graph should
        /// see `overlapCount: 0` here — run `auto_layout` (or `move_node`) until it does.
        /// </summary>
        private static JObject LayoutJson(object graph)
        {
            try
            {
                var items = CanvasNodes(graph);
                var overlaps = new JArray();
                int count = 0;
                for (int i = 0; i < items.Count; i++)
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        // Shrink slightly so nodes that merely touch are not reported.
                        var a = items[i].rect; var b = items[j].rect;
                        a.xMin += 4; a.yMin += 4; a.xMax -= 4; a.yMax -= 4;
                        if (!a.Overlaps(b)) continue;
                        count++;
                        if (overlaps.Count < 50)
                            overlaps.Add(new JObject { ["a"] = items[i].address, ["b"] = items[j].address });
                    }
                JObject bounds = null;
                if (items.Count > 0)
                {
                    float xMin = items.Min(it => it.rect.xMin), yMin = items.Min(it => it.rect.yMin);
                    float xMax = items.Max(it => it.rect.xMax), yMax = items.Max(it => it.rect.yMax);
                    bounds = new JObject { ["x"] = xMin, ["y"] = yMin, ["width"] = xMax - xMin, ["height"] = yMax - yMin };
                }
                return new JObject { ["nodeCount"] = items.Count, ["overlapCount"] = count, ["overlaps"] = overlaps, ["bounds"] = bounds };
            }
            catch (Exception ex)
            {
                return new JObject { ["error"] = $"layout-estimator failed: {ex.Message}" };
            }
        }

        /// <summary>The models that consume `node`'s outputs (blocks are reported as their owning context).</summary>
        private static List<object> Consumers(object node)
        {
            var result = new List<object>();
            IEnumerable outs = null;
            try { outs = Prop(node, "outputSlots") as IEnumerable; } catch { }
            if (outs == null) return result;

            void Visit(object slot)
            {
                IEnumerable linked = null;
                try { linked = Prop(slot, "LinkedSlots") as IEnumerable; } catch { }
                if (linked != null)
                    foreach (var other in linked)
                    {
                        object owner = null;
                        try { owner = Prop(other, "owner"); } catch { }
                        if (owner == null) continue;
                        if (BlockType.IsInstanceOfType(owner))
                        {
                            try { owner = Call(owner, ModelType, "GetParent"); } catch { }
                        }
                        if (owner != null && !result.Any(r => ReferenceEquals(r, owner))) result.Add(owner);
                    }
                IEnumerable children = null;
                try { children = Prop(slot, "children") as IEnumerable; } catch { }
                if (children != null) foreach (var c in children) Visit(c);
            }
            foreach (var s in outs) Visit(s);
            return result;
        }

        /// <summary>Same as <see cref="Consumers"/> but keeps blocks as blocks (for row alignment).</summary>
        private static List<(object owner, object block)> ConsumerSlotsOwners(object node)
        {
            var result = new List<(object, object)>();
            IEnumerable outs = null;
            try { outs = Prop(node, "outputSlots") as IEnumerable; } catch { }
            if (outs == null) return result;
            void Visit(object slot)
            {
                IEnumerable linked = null;
                try { linked = Prop(slot, "LinkedSlots") as IEnumerable; } catch { }
                if (linked != null)
                    foreach (var other in linked)
                    {
                        object owner = null;
                        try { owner = Prop(other, "owner"); } catch { }
                        if (owner == null) continue;
                        object block = null;
                        if (BlockType.IsInstanceOfType(owner))
                        {
                            block = owner;
                            try { owner = Call(owner, ModelType, "GetParent"); } catch { owner = null; }
                        }
                        if (owner != null) result.Add((owner, block));
                    }
                IEnumerable children = null;
                try { children = Prop(slot, "children") as IEnumerable; } catch { }
                if (children != null) foreach (var c in children) Visit(c);
            }
            foreach (var s in outs) Visit(s);
            return result;
        }

        /// <summary>One placeable feeder for auto_layout: an operator, a parameter canvas node, or a node-less parameter.</summary>
        private sealed class LayoutItem
        {
            public object model;                              // VFXOperator or VFXParameter
            public object node;                               // VFXParameter.Node (null for operators / node-less params)
            public List<(object owner, object block)> consumers;
            public Vector2 size;
            public int rank;
            public int component;
            public float target;
            public Rect rect;
            public float CurrentY()
            {
                try
                {
                    if (node != null) return ((Vector2)FindField(node.GetType(), "position").GetValue(node)).y;
                    return ModelPosition(model).y;
                }
                catch { return 0f; }
            }
        }

        /// <summary>model → index of the first group box listing it (operators and contexts; parameters by any node).</summary>
        private static Dictionary<object, int> GroupOfModels(object graph)
        {
            var map = new Dictionary<object, int>(RefEq.Instance);
            try
            {
                var ui = Prop(graph, "UIInfos");
                if (!(FindField(ui?.GetType(), "groupInfos")?.GetValue(ui) is Array groups)) return map;
                for (int g = 0; g < groups.Length; g++)
                {
                    if (!(FindField(GroupInfoType, "contents")?.GetValue(groups.GetValue(g)) is Array contents)) continue;
                    foreach (var nid in contents)
                    {
                        if ((bool)(FindField(NodeIDType, "isStickyNote")?.GetValue(nid) ?? false)) continue;
                        var model = FindField(NodeIDType, "model")?.GetValue(nid);
                        if (model != null && !map.ContainsKey(model)) map[model] = g;
                    }
                }
            }
            catch { }
            return map;
        }

        /// <summary>Indices of the group boxes whose contents list the parameter canvas node (model, id).</summary>
        private static List<int> GroupsOfParameterNode(object graph, object param, int nodeId)
        {
            var result = new List<int>();
            try
            {
                var ui = Prop(graph, "UIInfos");
                if (!(FindField(ui?.GetType(), "groupInfos")?.GetValue(ui) is Array groups)) return result;
                for (int g = 0; g < groups.Length; g++)
                {
                    if (!(FindField(GroupInfoType, "contents")?.GetValue(groups.GetValue(g)) is Array contents)) continue;
                    foreach (var nid in contents)
                    {
                        if ((bool)(FindField(NodeIDType, "isStickyNote")?.GetValue(nid) ?? false)) continue;
                        if (ReferenceEquals(FindField(NodeIDType, "model")?.GetValue(nid), param)
                            && Convert.ToInt32(FindField(NodeIDType, "id")?.GetValue(nid) ?? -1) == nodeId)
                        { result.Add(g); break; }
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>Append a parameter canvas node (model, id) to a group box's contents.</summary>
        private static void AddParameterNodeToGroup(object graph, int groupIndex, object param, int nodeId)
        {
            var ui = Prop(graph, "UIInfos");
            var groupsField = FindField(ui.GetType(), "groupInfos");
            var groups = groupsField.GetValue(ui) as Array;
            var group = groups.GetValue(groupIndex);
            var contentsField = FindField(GroupInfoType, "contents");
            var contents = contentsField.GetValue(group) as Array ?? Array.CreateInstance(NodeIDType, 0);
            var merged = Array.CreateInstance(NodeIDType, contents.Length + 1);
            Array.Copy(contents, merged, contents.Length);
            merged.SetValue(Activator.CreateInstance(NodeIDType, param, nodeId), contents.Length);
            contentsField.SetValue(group, merged);
            groups.SetValue(group, groupIndex);
            EditorUtility.SetDirty(ui as UnityEngine.Object);
        }

        /// <summary>Drop group-content entries that point at parameter canvas nodes that no longer exist.</summary>
        private static int PruneStaleParameterNodeIds(object graph)
        {
            int removed = 0;
            try
            {
                var ui = Prop(graph, "UIInfos");
                if (!(FindField(ui?.GetType(), "groupInfos")?.GetValue(ui) is Array groups)) return 0;
                var contentsField = FindField(GroupInfoType, "contents");
                for (int g = 0; g < groups.Length; g++)
                {
                    var group = groups.GetValue(g);
                    if (!(contentsField?.GetValue(group) is Array contents)) continue;
                    var keep = new List<object>();
                    foreach (var nid in contents)
                    {
                        bool sticky = (bool)(FindField(NodeIDType, "isStickyNote")?.GetValue(nid) ?? false);
                        var model = FindField(NodeIDType, "model")?.GetValue(nid);
                        if (!sticky && ParameterType.IsInstanceOfType(model))
                        {
                            int id = Convert.ToInt32(FindField(NodeIDType, "id")?.GetValue(nid) ?? -1);
                            bool exists = false;
                            try { exists = Call(model, ParameterType, "GetNode", id) != null; } catch { }
                            if (!exists) { removed++; continue; }
                        }
                        keep.Add(nid);
                    }
                    if (keep.Count == contents.Length) continue;
                    var arr = Array.CreateInstance(NodeIDType, keep.Count);
                    for (int i = 0; i < keep.Count; i++) arr.SetValue(keep[i], i);
                    contentsField.SetValue(group, arr);
                    groups.SetValue(group, g);
                }
                if (removed > 0) EditorUtility.SetDirty(ui as UnityEngine.Object);
            }
            catch { }
            return removed;
        }

        /// <summary>The consumers wired to ONE parameter canvas node (VFXParameter.Node.linkedSlots).</summary>
        private static List<(object owner, object block)> ParameterNodeConsumers(object node)
        {
            var result = new List<(object, object)>();
            IEnumerable linked = null;
            try { linked = FindField(node.GetType(), "linkedSlots")?.GetValue(node) as IEnumerable; } catch { }
            if (linked == null) return result;
            foreach (var ls in linked)
            {
                object inSlot = null;
                try { inSlot = FindField(ls.GetType(), "inputSlot")?.GetValue(ls); } catch { }
                if (inSlot == null) continue;
                object owner = null;
                try { owner = Prop(inSlot, "owner"); } catch { }
                if (owner == null) continue;
                object block = null;
                if (BlockType.IsInstanceOfType(owner))
                {
                    block = owner;
                    try { owner = Call(owner, ModelType, "GetParent"); } catch { owner = null; }
                }
                if (owner != null) result.Add((owner, block));
            }
            return result;
        }

        private static Type NodeLinkedSlotType => T("UnityEditor.VFX.VFXParameter+NodeLinkedSlot");

        /// <summary>Every slot in a slot subtree with its child-index path from the top-level slot.</summary>
        private static IEnumerable<(object slot, List<int> path)> SlotTree(object top)
        {
            var stack = new Stack<(object, List<int>)>();
            stack.Push((top, new List<int>()));
            while (stack.Count > 0)
            {
                var (slot, path) = stack.Pop();
                yield return (slot, path);
                IEnumerable children = null;
                try { children = Prop(slot, "children") as IEnumerable; } catch { }
                if (children == null) continue;
                int i = 0;
                foreach (var c in children) { var p = new List<int>(path) { i++ }; stack.Push((c, p)); }
            }
        }

        private static object ResolveSlotPath(object top, List<int> path)
        {
            var slot = top;
            foreach (var i in path)
            {
                var children = (Prop(slot, "children") as IEnumerable)?.Cast<object>().ToList();
                if (children == null || i >= children.Count) return null;
                slot = children[i];
            }
            return slot;
        }

        /// <summary>(consumer model, block) for one input slot: a block's owner is reported as its context.</summary>
        private static (object owner, object block) ConsumerOf(object inputSlot)
        {
            object owner = null;
            try { owner = Prop(inputSlot, "owner"); } catch { }
            if (owner == null) return (null, null);
            object block = null;
            if (BlockType.IsInstanceOfType(owner))
            {
                block = owner;
                try { owner = Call(owner, ModelType, "GetParent"); } catch { owner = null; }
            }
            return (owner, block);
        }

        /// <summary>Every (outputSlot, inputSlot) edge leaving a model's output slot trees.</summary>
        private static List<(object outSlot, object inSlot)> OutgoingLinks(object model)
        {
            var result = new List<(object, object)>();
            IEnumerable outs = null;
            try { outs = Prop(model, "outputSlots") as IEnumerable; } catch { }
            if (outs == null) return result;
            foreach (var top in outs)
                foreach (var (slot, _) in SlotTree(top))
                {
                    IEnumerable linked = null;
                    try { linked = Prop(slot, "LinkedSlots") as IEnumerable; } catch { }
                    if (linked == null) continue;
                    foreach (var other in linked.Cast<object>().ToList()) result.Add((slot, other));
                }
            return result;
        }

        /// <summary>
        /// `auto_layout` op: reposition every context, operator, and parameter node into a readable
        /// layered layout, then refit group boxes around their members. Sticky notes are left alone.
        ///
        /// Systems: contexts are grouped by flow connectivity; each connected group is one column laid
        /// out top-to-bottom by flow depth (Spawn → Init → Update → Output), with parallel branches side
        /// by side. Each system's feeders (the operators and parameter nodes that drive it) sit in
        /// columns immediately to its LEFT, so the canvas reads feeders→system, feeders→system, … and
        /// no edge crosses another system's chain.
        ///
        /// Shared nodes: an operator that feeds more than one system is DUPLICATED per system
        /// (`duplicateShared`, default true — same settings, same input links, its output edges to that
        /// system moved to the clone); a parameter gets one canvas node per consuming context
        /// (`splitParameters`, default true). Both are what a person does by hand to avoid long edges;
        /// duplicating an operator is functionally equivalent (same expression, evaluated per copy).
        ///
        /// Feeders are ranked by distance from the contexts they feed (rank 1 feeds a context/block
        /// directly, rank 2 feeds a rank-1 operator, …) — one column per rank, each node vertically
        /// aligned with what it feeds (a block-feeder aligns with that block's row) and pushed apart so
        /// nothing overlaps. Describe's `layout.overlapCount` is the oracle; the response reports it.
        /// </summary>
        private static object AutoLayout(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            bool duplicateShared = parameters?["duplicateShared"]?.ToObject<bool>() ?? true;
            bool splitParameters = parameters?["splitParameters"]?.ToObject<bool>() ?? true;
            var scope = parameters?["scope"]?.ToString()?.ToLowerInvariant() ?? "touched";
            var explicitContexts = (parameters?["contexts"] as JArray)?.Select(t => t.ToObject<int>()).ToList();
            if (scope != "touched" && scope != "all")
                return new { error = $"Unknown scope '{scope}'. Use \"touched\" (default: only the systems edited this session), \"all\", or pass contexts:[…]." };

            var graph = LoadGraph(assetPath);
            var ctxs = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var ops = Children(graph).Where(c => OperatorType.IsInstanceOfType(c)).ToList();
            var ps = Children(graph).Where(c => ParameterType.IsInstanceOfType(c)).ToList();
            int IdOf(object m) => (m as UnityEngine.Object)?.GetInstanceID() ?? 0;
            // Where every node was before this pass, by model (first rect per model): group boxes that
            // contain sticky notes move their notes by the same displacement as their other members.
            var beforeRectOfModel = new Dictionary<object, Rect>(RefEq.Instance);
            foreach (var (address, rect) in CanvasNodes(graph))
            {
                string kind = (string)address["kind"]; int idx = (int)address["index"];
                object model = kind == "context" ? ctxs[idx] : kind == "operator" ? ops[idx] : ps[idx];
                if (!beforeRectOfModel.ContainsKey(model)) beforeRectOfModel[model] = rect;
            }

            // ---- 1. Systems: flow components + depth ------------------------------------------
            List<object> FlowNeighbors(object ctx, string prop)
            {
                try { return (Prop(ctx, prop) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>(); }
                catch { return new List<object>(); }
            }
            var component = new Dictionary<object, int>(RefEq.Instance);
            int componentCount = 0;
            foreach (var c in ctxs)
            {
                if (component.ContainsKey(c)) continue;
                var stack = new Stack<object>(); stack.Push(c); component[c] = componentCount;
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    foreach (var n in FlowNeighbors(cur, "inputContexts").Concat(FlowNeighbors(cur, "outputContexts")))
                        if (ctxs.Any(x => ReferenceEquals(x, n)) && !component.ContainsKey(n)) { component[n] = componentCount; stack.Push(n); }
                }
                componentCount++;
            }
            var depth = new Dictionary<object, int>(RefEq.Instance);
            foreach (var c in ctxs) depth[c] = 0;
            for (int iter = 0; iter < ctxs.Count + 1; iter++)
            {
                bool changed = false;
                foreach (var c in ctxs)
                {
                    int d = 0;
                    foreach (var i in FlowNeighbors(c, "inputContexts"))
                        if (depth.TryGetValue(i, out var di)) d = Math.Max(d, di + 1);
                    if (d != depth[c]) { depth[c] = d; changed = true; }
                }
                if (!changed) break;
            }
            var componentOrder = Enumerable.Range(0, componentCount)
                .OrderBy(k => ctxs.Where(c => component[c] == k).Min(c => ModelPosition(c).x)).ToList();
            int firstComponent = componentOrder.Count > 0 ? componentOrder[0] : 0;

            // ---- 2. Operators: rank + the system each one feeds ---------------------------------
            var rankOf = new Dictionary<object, int>(RefEq.Instance);
            var visiting = new HashSet<object>(RefEq.Instance);
            int RankModel(object model)
            {
                if (ContextType.IsInstanceOfType(model)) return 0;
                if (rankOf.TryGetValue(model, out var r)) return r;
                if (!visiting.Add(model)) return 1;
                int best = 0;
                foreach (var consumer in Consumers(model)) best = Math.Max(best, RankModel(consumer));
                visiting.Remove(model);
                return rankOf[model] = best + 1;
            }
            foreach (var op in ops) RankModel(op);
            var opComponent = new Dictionary<object, int>(RefEq.Instance);
            int ComponentOfConsumer(object owner)
            {
                if (owner == null) return -1;
                if (ContextType.IsInstanceOfType(owner) && component.TryGetValue(owner, out var k)) return k;
                if (OperatorType.IsInstanceOfType(owner) && opComponent.TryGetValue(owner, out var ok)) return ok;
                return -1;
            }
            List<IGrouping<int, (object outSlot, object inSlot)>> ConsumerGroups(object op) => OutgoingLinks(op)
                .GroupBy(e => ComponentOfConsumer(ConsumerOf(e.inSlot).owner))
                .Where(g => g.Key >= 0).OrderByDescending(g => g.Count()).ToList();
            int maxOpRank = ops.Count > 0 ? ops.Max(o => rankOf[o]) : 0;
            for (int r = 1; r <= maxOpRank; r++)
                foreach (var op in ops.Where(o => rankOf[o] == r))
                {
                    var groups = ConsumerGroups(op);
                    opComponent[op] = groups.Count == 0 ? -1 : groups[0].Key;
                }

            // ---- 3. Scope: which systems get laid out ------------------------------------------
            var selected = new HashSet<int>();
            if (scope == "all") foreach (var k in componentOrder) selected.Add(k);
            if (explicitContexts != null)
                foreach (var ci in explicitContexts)
                {
                    if (ci < 0 || ci >= ctxs.Count) return new { error = $"contexts[] index {ci} out of range; graph has {ctxs.Count} context(s)" };
                    selected.Add(component[ctxs[ci]]);
                }
            if (scope == "touched" && explicitContexts == null)
            {
                s_Touched.TryGetValue(assetPath, out var touched);
                if (touched == null || touched.Count == 0)
                    return new
                    {
                        error = "No nodes are recorded as touched for this asset in this editor session, so there is " +
                                "nothing to lay out by default. Pass scope:\"all\" to lay out the whole graph, or " +
                                "contexts:[<describe index>, …] to lay out specific systems."
                    };
                foreach (var c in ctxs) if (touched.Contains(IdOf(c))) selected.Add(component[c]);
                foreach (var op in ops) if (touched.Contains(IdOf(op)) && opComponent.TryGetValue(op, out var k) && k >= 0) selected.Add(k);
                foreach (var p in ps)
                    if (touched.Contains(IdOf(p)))
                        foreach (var (owner, _) in ConsumerSlotsOwners(p)) { int k = ComponentOfConsumer(owner); if (k >= 0) selected.Add(k); }
                if (selected.Count == 0)
                    selected.Add(firstComponent); // touched nodes feed nothing yet — tidy the first system's feeders
            }

            // ---- 4. Duplicate operators shared across systems (clones only for SELECTED systems) --
            int duplicatedOperators = 0;
            if (duplicateShared)
                for (int r = 1; r <= maxOpRank; r++)
                    foreach (var op in ops.Where(o => rankOf[o] == r).ToList())
                    {
                        var groups = ConsumerGroups(op);
                        if (groups.Count <= 1) continue;
                        var outTops = (Prop(op, "outputSlots") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                        foreach (var group in groups.Skip(1))
                        {
                            if (!selected.Contains(group.Key)) continue; // leave untouched systems as they are
                            var clone = DuplicateModelViaSerializer(op, OperatorType);
                            Call(graph, ModelType, "AddChild", clone, -1, true);
                            var opIns = (Prop(op, "inputSlots") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                            var cloneIns = (Prop(clone, "inputSlots") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                            for (int i = 0; i < Math.Min(opIns.Count, cloneIns.Count); i++)
                                Call(null, SlotType, "CopyLinks", cloneIns[i], opIns[i], true);
                            var cloneOuts = (Prop(clone, "outputSlots") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                            foreach (var (outSlot, inSlot) in group)
                            {
                                int topIndex = -1; List<int> path = null;
                                for (int t = 0; t < outTops.Count && topIndex < 0; t++)
                                    foreach (var (slot, p) in SlotTree(outTops[t]))
                                        if (ReferenceEquals(slot, outSlot)) { topIndex = t; path = p; break; }
                                if (topIndex < 0 || topIndex >= cloneOuts.Count) continue;
                                var cloneOut = ResolveSlotPath(cloneOuts[topIndex], path);
                                if (cloneOut == null) continue;
                                Call(outSlot, SlotType, "Unlink", inSlot, true);
                                Call(cloneOut, SlotType, "Link", inSlot, true);
                            }
                            rankOf[clone] = r;
                            opComponent[clone] = group.Key;
                            ops.Add(clone);
                            duplicatedOperators++;
                        }
                    }

            // ---- 5a. Preliminary context rows per selected system (local y, contexts stacked by depth) --
            // Only used to decide how far apart a parameter's consumers are; the placement pass below
            // recomputes the same stacking.
            var rowOfContext = new Dictionary<object, float>(RefEq.Instance); // context → local top y
            foreach (var k in componentOrder)
            {
                if (!selected.Contains(k)) continue;
                var members = ctxs.Where(c => component[c] == k).ToList();
                int maxDepth = members.Max(c => depth[c]);
                float y = 0f;
                for (int d = 0; d <= maxDepth; d++)
                {
                    var layer = members.Where(c => depth[c] == d).ToList();
                    float layerHeight = 0f;
                    foreach (var c in layer) { rowOfContext[c] = y; layerHeight = Math.Max(layerHeight, EstimateSize(c).y); }
                    y += layerHeight + LayoutContextGapY;
                }
            }
            float RowOf(object owner, object block)
            {
                if (owner != null && rowOfContext.TryGetValue(owner, out var top))
                    return block != null ? top + BlockRowOffset(owner, block) : top + EstimateSize(owner).y * 0.5f;
                return float.NaN;
            }
            // Row an operator ultimately feeds: follow its first consumer down to a context.
            float DownstreamRow(object model)
            {
                var cur = model;
                for (int guard = 0; guard < 64 && cur != null; guard++)
                {
                    var cs = ConsumerSlotsOwners(cur);
                    if (cs.Count == 0) return float.NaN;
                    var (owner, block) = cs[0];
                    if (ContextType.IsInstanceOfType(owner)) return RowOf(owner, block);
                    cur = owner;
                }
                return float.NaN;
            }

            var groupOfModels = GroupOfModels(graph);

            // ---- 5. Parameters: one node per system, per consumer group, plus one per band of far-apart consumers --
            int parameterNodesCreated = 0;
            var paramItems = new List<LayoutItem>();
            var nlsListType = typeof(List<>).MakeGenericType(NodeLinkedSlotType);
            var slotListType = typeof(List<>).MakeGenericType(SlotType);
            foreach (var p in ps)
            {
                bool isOutput = false;
                try { isOutput = (bool)Prop(p, "isOutput"); } catch { }
                List<(object outSlot, object inSlot)> edges = isOutput ? new List<(object outSlot, object inSlot)>() : OutgoingLinks(p);
                var existing = (Prop(p, "nodes") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                int EdgeComponent((object outSlot, object inSlot) e) => ComponentOfConsumer(ConsumerOf(e.inSlot).owner);
                bool Selected((object outSlot, object inSlot) e) => selected.Contains(EdgeComponent(e));

                if (edges.Count == 0 || !splitParameters)
                {
                    // Positions only: every existing node in a selected system is re-placed; others stay.
                    foreach (var n in existing)
                    {
                        var consumers = ParameterNodeConsumers(n);
                        int comp = consumers.Select(c => ComponentOfConsumer(c.owner)).Where(k => k >= 0).DefaultIfEmpty(-1).First();
                        if (comp < 0 && edges.Count == 0) comp = firstComponent;
                        if (!selected.Contains(comp)) continue;
                        paramItems.Add(new LayoutItem { model = p, node = n, consumers = consumers, size = EstimateSize(p), component = comp });
                    }
                    if (existing.Count == 0 && edges.Count > 0)
                    {
                        int comp = edges.Select(EdgeComponent).Where(k => k >= 0).DefaultIfEmpty(-1).First();
                        if (selected.Contains(comp))
                            paramItems.Add(new LayoutItem { model = p, consumers = ConsumerSlotsOwners(p), size = EstimateSize(p), component = comp });
                    }
                    continue;
                }

                // Split: links into selected systems leave their current node(s) and get one node per
                // consuming context; nodes that only serve untouched systems keep their links + position.
                var nodesField = FindField(p.GetType(), "m_Nodes");
                var nodeList = (System.Collections.IList)nodesField.GetValue(p) ?? (System.Collections.IList)Activator.CreateInstance(nodesField.FieldType);
                nodesField.SetValue(p, nodeList);
                // Which existing node each edge lived on, and that node's group boxes — a new node
                // inherits the membership of the node its links came from, so group boxes keep
                // following their parameters after the split.
                var oldNodeOfInSlot = new Dictionary<object, int>(RefEq.Instance);
                var groupsOfOldNode = new Dictionary<int, List<int>>();
                foreach (var n in existing)
                {
                    int oldId = Convert.ToInt32(Prop(n, "id"));
                    groupsOfOldNode[oldId] = GroupsOfParameterNode(graph, p, oldId);
                    if (FindField(n.GetType(), "linkedSlots")?.GetValue(n) is System.Collections.IList ls)
                        foreach (var entry in ls)
                        {
                            var inSlot = FindField(NodeLinkedSlotType, "inputSlot").GetValue(entry);
                            if (inSlot != null) oldNodeOfInSlot[inSlot] = oldId;
                        }
                }
                foreach (var n in existing)
                {
                    var links = FindField(n.GetType(), "linkedSlots")?.GetValue(n) as System.Collections.IList;
                    if (links == null) continue;
                    var keep = (System.Collections.IList)Activator.CreateInstance(nlsListType);
                    foreach (var entry in links)
                    {
                        var inSlot = FindField(NodeLinkedSlotType, "inputSlot").GetValue(entry);
                        var outSlot = FindField(NodeLinkedSlotType, "outputSlot").GetValue(entry);
                        if (!Selected((outSlot, inSlot))) keep.Add(entry);
                    }
                    FindField(n.GetType(), "linkedSlots").SetValue(n, keep);
                    if (keep.Count == 0) nodeList.Remove(n); // fully re-homed below
                }
                // One node per system; a further node whenever consumers sit more than
                // LayoutParamSplitDistance apart vertically. A hand-made graph keeps most parameters
                // single and only duplicates the ones that fan out across the canvas.
                // Split key: the system, then the GROUP BOX the consumer sits in (a parameter node is
                // never shared between groups — it belongs to the group of what it feeds), then row bands.
                var bands = new List<(int component, int group, List<(object outSlot, object inSlot)> edges)>();
                int ConsumerGroup((object outSlot, object inSlot) e)
                {
                    var (owner, block) = ConsumerOf(e.inSlot);
                    // Only an OPERATOR consumer's group counts: a group box around a system's contexts is
                    // a system label, not a home for the parameters feeding its blocks.
                    return OperatorType.IsInstanceOfType(owner) && groupOfModels.TryGetValue(owner, out var gi) ? gi : -1;
                }
                foreach (var bySystem in edges.Where(Selected).GroupBy(EdgeComponent))
                foreach (var byGroup in bySystem.GroupBy(ConsumerGroup))
                {
                    var rows = byGroup.Select(e =>
                    {
                        var (owner, block) = ConsumerOf(e.inSlot);
                        float row = ContextType.IsInstanceOfType(owner) ? RowOf(owner, block) : DownstreamRow(owner);
                        return (e, row: float.IsNaN(row) ? float.MaxValue : row);
                    }).OrderBy(t => t.row).ToList();
                    float bandStart = float.NaN;
                    List<(object outSlot, object inSlot)> band = null;
                    foreach (var (e, row) in rows)
                    {
                        if (band == null || (row != float.MaxValue && row - bandStart > LayoutParamSplitDistance))
                        { band = new List<(object outSlot, object inSlot)>(); bands.Add((bySystem.Key, byGroup.Key, band)); bandStart = row; }
                        band.Add(e);
                    }
                }
                foreach (var (bandComponent, bandGroup, g) in bands)
                {
                    int id = (int)Call(p, ParameterType, "AddNode", ModelPosition(p));
                    var node = Call(p, ParameterType, "GetNode", id);
                    var links = (System.Collections.IList)Activator.CreateInstance(nlsListType);
                    var consumers = new List<(object owner, object block)>();
                    foreach (var (outSlot, inSlot) in g)
                    {
                        var entry = Activator.CreateInstance(NodeLinkedSlotType);
                        FindField(NodeLinkedSlotType, "outputSlot").SetValue(entry, outSlot);
                        FindField(NodeLinkedSlotType, "inputSlot").SetValue(entry, inSlot);
                        links.Add(entry);
                        consumers.Add(ConsumerOf(inSlot));
                    }
                    FindField(node.GetType(), "linkedSlots").SetValue(node, links);
                    FindField(node.GetType(), "expandedSlots").SetValue(node, Activator.CreateInstance(slotListType));
                    parameterNodesCreated++;
                    // Inherit group membership from the old node most of these links came from.
                    var origins = g.Select(e => oldNodeOfInSlot.TryGetValue(e.inSlot, out var oid) ? oid : int.MinValue)
                        .Where(o => o != int.MinValue).GroupBy(o => o).OrderByDescending(gr => gr.Count()).Select(gr => gr.Key).ToList();
                    // A freshly loaded asset may not have per-node link records yet (the editor fills
                    // them on validation) — then every existing node's groups are inherited.
                    List<int> inherited = null;
                    if (bandGroup >= 0)
                        inherited = new List<int> { bandGroup }; // the group of what it feeds
                    else if (origins.Count > 0 && groupsOfOldNode.TryGetValue(origins[0], out var fromOrigin) && fromOrigin.Count > 0)
                        inherited = fromOrigin;
                    else if (origins.Count == 0 || groupsOfOldNode.Count == 1)
                        inherited = groupsOfOldNode.Values.SelectMany(x => x).Distinct().ToList();
                    if (inherited != null)
                        foreach (var gi in inherited) AddParameterNodeToGroup(graph, gi, p, id);
                    paramItems.Add(new LayoutItem
                    {
                        model = p, node = node, consumers = consumers, size = EstimateSize(p),
                        component = bandComponent,
                        rank = 1 + consumers.Select(c => OperatorType.IsInstanceOfType(c.owner) && rankOf.TryGetValue(c.owner, out var cr) ? cr : 0).DefaultIfEmpty(0).Max()
                    });
                }
            }

            // ---- 6. Items to place (feeders of selected systems only) ---------------------------
            var items = new List<LayoutItem>();
            foreach (var op in ops)
            {
                int comp = opComponent.TryGetValue(op, out var oc) ? oc : -1;
                if (comp < 0) comp = firstComponent;
                if (!selected.Contains(comp)) continue;
                items.Add(new LayoutItem { model = op, consumers = ConsumerSlotsOwners(op), size = EstimateSize(op), rank = rankOf[op], component = comp });
            }
            foreach (var it in paramItems)
            {
                if (it.component < 0) it.component = firstComponent;
                if (!selected.Contains(it.component)) continue;
                if (it.rank == 0) it.rank = 1 + it.consumers.Select(c => OperatorType.IsInstanceOfType(c.owner) && rankOf.TryGetValue(c.owner, out var cr) ? cr : 0).DefaultIfEmpty(0).Max();
                items.Add(it);
            }

            // Everything that is NOT re-placed keeps its position and must not be overlapped.
            var laidOutModels = new HashSet<object>(RefEq.Instance);
            foreach (var c in ctxs) if (selected.Contains(component[c])) laidOutModels.Add(c);
            foreach (var it in items) laidOutModels.Add(it.model);
            var occupied = new List<Rect>();
            foreach (var (address, rect) in CanvasNodes(graph))
            {
                // CanvasNodes addresses by kind+index; resolve to the model to test membership.
                string kind = (string)address["kind"]; int idx = (int)address["index"];
                bool moving;
                if (kind == "parameter")
                {
                    int? nodeId = address["nodeId"]?.Type == JTokenType.Integer ? (int?)address["nodeId"] : null;
                    moving = items.Any(it => ReferenceEquals(it.model, ps[idx]) &&
                                             (it.node == null ? nodeId == null : nodeId != null && Convert.ToInt32(Prop(it.node, "id")) == nodeId));
                }
                else moving = laidOutModels.Contains(kind == "context" ? ctxs[idx] : ops[idx]);
                if (!moving) occupied.Add(rect);
            }
            // Sticky notes are annotations, not obstacles: a note must never push a whole system away.
            // They stay where they are; any note left over a node is reported (`stickyNotesOverNodes`).
            var noteRects = new List<Rect>();
            foreach (var note in StickyNotesJson(graph))
                if (note["position"] is JObject np)
                    noteRects.Add(new Rect((float)np["x"], (float)np["y"], (float)np["width"], (float)np["height"]));

            // ---- 7. Place each selected system: [feeders][contexts], anchored where it is now -------
            var placed = new Dictionary<object, Rect>(RefEq.Instance);
            int systemsLaidOut = 0, shiftedDown = 0, newSystems = 0;
            foreach (var k in componentOrder)
            {
                if (!selected.Contains(k)) continue;
                var members = ctxs.Where(c => component[c] == k).ToList();
                var feeders = items.Where(it => it.component == k).ToList();
                int maxRank = feeders.Count > 0 ? feeders.Max(it => it.rank) : 0;
                float feederWidth = maxRank * (LayoutOperatorWidth + LayoutOperatorColumnGapX);

                // Local layout: contexts start at x = feederWidth, y = 0.
                var local = new Dictionary<object, Rect>(RefEq.Instance);
                // A system whose contexts were ALL created this session is new and gets its own column;
                // any other system keeps every context's x (a person's lanes) and only moves vertically.
                s_Created.TryGetValue(assetPath, out var createdSet);
                bool isNew = createdSet != null && members.All(c => createdSet.Contains(IdOf(c)));
                float minCtxX = members.Min(c => ModelPosition(c).x);
                var localItems = new List<(LayoutItem it, Rect rect)>();
                int maxDepth = members.Max(c => depth[c]);
                var slotOf = new Dictionary<object, int>(RefEq.Instance);
                float y = 0f, width = 0f;
                for (int d = 0; d <= maxDepth; d++)
                {
                    var layer = members.Where(c => depth[c] == d).OrderBy(c =>
                    {
                        var ins = FlowNeighbors(c, "inputContexts").Where(slotOf.ContainsKey).Select(i => slotOf[i]).ToList();
                        return ins.Count == 0 ? float.MaxValue : (float)ins.Average();
                    }).ThenBy(c => ModelPosition(c).x).ToList();
                    float layerHeight = 0f;
                    for (int sIdx = 0; sIdx < layer.Count; sIdx++)
                    {
                        slotOf[layer[sIdx]] = sIdx;
                        var size = EstimateSize(layer[sIdx]);
                        float lx = isNew ? sIdx * (LayoutContextWidth + LayoutContextGapX) : ModelPosition(layer[sIdx]).x - minCtxX;
                        local[layer[sIdx]] = new Rect(feederWidth + lx, y, size.x, size.y);
                        width = Math.Max(width, lx + size.x);
                        layerHeight = Math.Max(layerHeight, size.y);
                    }
                    y += layerHeight + LayoutContextGapY;
                }
                var localFeeder = new Dictionary<object, Rect>(RefEq.Instance);
                for (int r = 1; r <= maxRank; r++)
                {
                    float x = feederWidth - r * (LayoutOperatorWidth + LayoutOperatorColumnGapX);
                    var column = feeders.Where(it => it.rank == r).Select(it =>
                    {
                        var targets = new List<float>();
                        foreach (var (owner, block) in it.consumers)
                        {
                            Rect rect;
                            if (owner == null || !(local.TryGetValue(owner, out rect) || localFeeder.TryGetValue(owner, out rect))) continue;
                            targets.Add(block != null && ContextType.IsInstanceOfType(owner) ? rect.y + BlockRowOffset(owner, block) : rect.center.y);
                        }
                        it.target = targets.Count == 0 ? float.MaxValue : (float)targets.Average();
                        return it;
                    }).OrderBy(it => it.target).ThenBy(it => it.CurrentY()).ToList();
                    float bottom = float.NegativeInfinity;
                    foreach (var it in column)
                    {
                        float yy = it.target == float.MaxValue ? (float.IsInfinity(bottom) ? 0f : bottom + LayoutOperatorGapY) : it.target - it.size.y * 0.5f;
                        if (!float.IsInfinity(bottom)) yy = Math.Max(yy, bottom + LayoutOperatorGapY);
                        var rect = new Rect(x, yy, it.size.x, it.size.y);
                        localItems.Add((it, rect));
                        bottom = yy + it.size.y;
                        if (!localFeeder.ContainsKey(it.model)) localFeeder[it.model] = rect;
                    }
                }

                // Anchor. A NEW system goes into a fresh column right of everything on the canvas
                // (feeders included). An EXISTING system keeps its contexts' x exactly — feeders grow to
                // its left — and the whole block only drops below anything it would cover (vertical
                // motion only; horizontal collisions are resolved by moving down, never sideways).
                float minLocalX = Math.Min(0f, localItems.Count > 0 ? localItems.Min(t => t.rect.xMin) : 0f);
                float blockWidth = (feederWidth + width) - minLocalX;
                float offX, offY;
                if (isNew)
                {
                    float rightEdge = occupied.Count > 0 ? occupied.Max(o => o.xMax) : 0f;
                    offX = (occupied.Count > 0 ? rightEdge + LayoutSystemGapX : 0f) - minLocalX;
                    offY = occupied.Count > 0 ? occupied.Min(o => o.yMin) : 0f;
                    newSystems++;
                }
                else
                {
                    offX = minCtxX - feederWidth;
                    offY = members.Min(c => ModelPosition(c).y);
                    float blockHeight = Math.Max(y, localItems.Count > 0 ? localItems.Max(t => t.rect.yMax) : 0f) - Math.Min(0f, localItems.Count > 0 ? localItems.Min(t => t.rect.yMin) : 0f);
                    float blockTop = Math.Min(0f, localItems.Count > 0 ? localItems.Min(t => t.rect.yMin) : 0f);
                    for (int guard = 0; guard < 20; guard++)
                    {
                        var block = new Rect(offX + minLocalX, offY + blockTop, blockWidth, blockHeight);
                        var hits = occupied.Where(o => o.Overlaps(block)).ToList();
                        if (hits.Count == 0) break;
                        offY = hits.Max(h => h.yMax) + LayoutContextGapY - blockTop;
                        shiftedDown++;
                    }
                }
                foreach (var kv in local) { var rr = kv.Value; rr.x += offX; rr.y += offY; placed[kv.Key] = rr; occupied.Add(rr); }
                foreach (var (it, rect) in localItems)
                {
                    var rr = rect; rr.x += offX; rr.y += offY; it.rect = rr; occupied.Add(rr);
                    if (!placed.ContainsKey(it.model)) placed[it.model] = rr;
                }
                systemsLaidOut++;
            }

            foreach (var c in ctxs) if (placed.TryGetValue(c, out var cr)) SetProp(c, "position", new Vector2(cr.x, cr.y));
            int parameterNodes = 0;
            foreach (var it in items)
            {
                var pos = new Vector2(it.rect.x, it.rect.y);
                if (it.node != null)
                {
                    FindField(it.node.GetType(), "position")?.SetValue(it.node, pos);
                    parameterNodes++;
                }
                if (placed.TryGetValue(it.model, out var first) && (it.node == null || first == it.rect))
                    SetProp(it.model, "position", pos);
            }

            int staleGroupEntries = PruneStaleParameterNodeIds(graph);

            // ---- 8. Refit group boxes that contain a re-placed member ------------------------------
            int groupsRefit = 0, stickyNotesMoved = 0;
            try
            {
                var ui = Prop(graph, "UIInfos");
                var groupsField = ui == null ? null : FindField(ui.GetType(), "groupInfos");
                if (groupsField?.GetValue(ui) is Array groupArr)
                {
                    var contentsField = FindField(GroupInfoType, "contents");
                    var posField = FindField(GroupInfoType, "position");
                    var current = CanvasNodes(graph).ToList();
                    var (noteUi, noteField, noteArr) = GetStickyNotes(graph);
                    var notePosField = noteArr != null && noteArr.Length > 0 ? FindField(StickyNoteInfoType, "position") : null;
                    for (int g = 0; g < groupArr.Length; g++)
                    {
                        var group = groupArr.GetValue(g);
                        var rects = new List<Rect>();
                        var oldRects = new List<Rect>();
                        var memberNoteIds = new List<int>();
                        bool touchesLaidOut = false;
                        if (contentsField?.GetValue(group) is Array contents)
                            foreach (var nid in contents)
                            {
                                bool sticky = (bool)(FindField(NodeIDType, "isStickyNote")?.GetValue(nid) ?? false);
                                if (sticky)
                                {
                                    int id = Convert.ToInt32(FindField(NodeIDType, "id")?.GetValue(nid) ?? -1);
                                    if (id >= 0 && noteArr != null && id < noteArr.Length) memberNoteIds.Add(id);
                                    continue;
                                }
                                var model = FindField(NodeIDType, "model")?.GetValue(nid);
                                if (model == null) continue;
                                if (laidOutModels.Contains(model)) touchesLaidOut = true;
                                if (beforeRectOfModel.TryGetValue(model, out var before)) oldRects.Add(before);
                                int ci = ctxs.FindIndex(x => ReferenceEquals(x, model)), oi = ops.FindIndex(x => ReferenceEquals(x, model)), pi = ps.FindIndex(x => ReferenceEquals(x, model));
                                foreach (var (address, rect) in current)
                                {
                                    string kind = (string)address["kind"]; int idx = (int)address["index"];
                                    if ((kind == "context" && idx == ci) || (kind == "operator" && idx == oi) || (kind == "parameter" && idx == pi)) rects.Add(rect);
                                }
                            }
                        if (rects.Count == 0 || !touchesLaidOut) continue;
                        // Member sticky notes travel with the group: same displacement as its nodes' top-left.
                        if (memberNoteIds.Count > 0 && oldRects.Count > 0 && notePosField != null)
                        {
                            var delta = new Vector2(rects.Min(rc => rc.xMin) - oldRects.Min(rc => rc.xMin),
                                                    rects.Min(rc => rc.yMin) - oldRects.Min(rc => rc.yMin));
                            foreach (var id in memberNoteIds)
                            {
                                var note = noteArr.GetValue(id);
                                var nr = (Rect)notePosField.GetValue(note);
                                nr.x += delta.x; nr.y += delta.y;
                                notePosField.SetValue(note, nr);
                                noteArr.SetValue(note, id);
                                rects.Add(nr);
                                stickyNotesMoved++;
                            }
                        }
                        float xMin = rects.Min(rc => rc.xMin) - LayoutGroupPadding;
                        float yMin = rects.Min(rc => rc.yMin) - LayoutGroupPadding - LayoutGroupHeaderHeight;
                        float xMax = rects.Max(rc => rc.xMax) + LayoutGroupPadding;
                        float yMax = rects.Max(rc => rc.yMax) + LayoutGroupPadding;
                        posField?.SetValue(group, new Rect(xMin, yMin, xMax - xMin, yMax - yMin));
                        groupArr.SetValue(group, g);
                        groupsRefit++;
                    }
                    EditorUtility.SetDirty(ui as UnityEngine.Object);
                }
            }
            catch { /* graphs without a UI sidecar */ }

            Persist(graph, assetPath);
            // The laid-out systems are tidy now; forget their touched marks so the next pass is scoped again.
            if (s_Touched.TryGetValue(assetPath, out var touchedSet))
                foreach (var m in laidOutModels) touchedSet.Remove(IdOf(m));

            var layout = LayoutJson(graph);
            int stickyNotesOverNodes = 0;
            foreach (var note in StickyNotesJson(graph))
                if (note["position"] is JObject np && occupied.Any(o => o.Overlaps(new Rect((float)np["x"], (float)np["y"], (float)np["width"], (float)np["height"]))))
                    stickyNotesOverNodes++;
            return new JObject
            {
                ["op"] = "auto_layout",
                ["assetPath"] = assetPath,
                ["scope"] = explicitContexts != null ? "contexts" : scope,
                ["systems"] = componentCount,
                ["systemsLaidOut"] = systemsLaidOut,
                ["systemsLeftAlone"] = componentCount - systemsLaidOut,
                ["laidOutContexts"] = new JArray(ctxs.Select((c, i) => (c, i)).Where(t => placed.ContainsKey(t.c)).Select(t => (JToken)t.i)),
                ["shiftedDown"] = shiftedDown,
                ["stickyNotesOverNodes"] = stickyNotesOverNodes,
                ["stickyNotesMovedWithGroups"] = stickyNotesMoved,
                ["staleGroupEntriesPruned"] = staleGroupEntries,
                ["newSystemsPlaced"] = newSystems,
                ["contexts"] = ctxs.Count,
                ["operators"] = ops.Count,
                ["duplicatedOperators"] = duplicatedOperators,
                ["parameters"] = ps.Count,
                ["parameterNodes"] = parameterNodes,
                ["parameterNodesCreated"] = parameterNodesCreated,
                ["groupsRefit"] = groupsRefit,
                ["layout"] = layout
            };
        }

        /// <summary>
        /// Add nodes to a named group box (VFXUI.groupInfos), creating the group when no group with
        /// that title exists. `nodes` = array of node addresses ({node: context|operator|parameter,
        /// …index}); optional `position` = [x, y, w, h] applies only when creating. A parameter entry
        /// uses its first canvas node's id (0 when the parameter has no canvas node yet — the id the
        /// editor assigns when it auto-creates one).
        /// </summary>
        private static object GroupNodes(JObject parameters)
        {
            var assetPath = parameters?["assetPath"]?.ToString();
            var title = parameters?["title"]?.ToString();
            if (string.IsNullOrEmpty(title))
                return new { error = "title is required" };
            if (!(parameters?["nodes"] is JArray nodesTok) || nodesTok.Count == 0)
                return new { error = "nodes is required (array of node addresses: {node: context|operator|parameter, …index})" };

            var graph = LoadGraph(assetPath);
            var ui = Prop(graph, "UIInfos");
            if (ui == null)
                throw new Exception("Graph has no UIInfos sidecar (unexpected for a valid .vfx).");
            var groupsField = FindField(ui.GetType(), "groupInfos");
            var groups = groupsField.GetValue(ui) as Array ?? Array.CreateInstance(GroupInfoType, 0);

            var ids = new List<object>();
            foreach (var tok in nodesTok)
            {
                var node = ResolveNode(graph, tok as JObject, "nodes[]");
                if (BlockType.IsInstanceOfType(node))
                    return new { error = "Blocks cannot be grouped (they live inside their context); group the context instead." };
                int id = 0;
                if (ParameterType.IsInstanceOfType(node) && Prop(node, "nodes") is IEnumerable pn)
                    foreach (var n in pn) { id = Convert.ToInt32(Prop(n, "id")); break; }
                ids.Add(Activator.CreateInstance(NodeIDType, node, id));
            }

            int gi = -1;
            var titleField = FindField(GroupInfoType, "title");
            for (int i = 0; i < groups.Length; i++)
                if (string.Equals(titleField.GetValue(groups.GetValue(i)) as string, title, StringComparison.Ordinal))
                { gi = i; break; }

            object group;
            bool created = gi < 0;
            if (created)
            {
                group = Activator.CreateInstance(GroupInfoType);
                titleField.SetValue(group, title);
                var rect = new Rect(0, 0, 500, 300);
                if (parameters?["position"] is JArray gp && gp.Count >= 4)
                    rect = new Rect(gp[0].ToObject<float>(), gp[1].ToObject<float>(),
                                    gp[2].ToObject<float>(), gp[3].ToObject<float>());
                FindField(GroupInfoType, "position").SetValue(group, rect);
                FindField(GroupInfoType, "contents").SetValue(group, Array.CreateInstance(NodeIDType, 0));
                var newGroups = Array.CreateInstance(GroupInfoType, groups.Length + 1);
                Array.Copy(groups, newGroups, groups.Length);
                newGroups.SetValue(group, groups.Length);
                groupsField.SetValue(ui, newGroups);
                gi = newGroups.Length - 1;
            }
            else
            {
                group = groups.GetValue(gi);
            }

            var contentsField = FindField(GroupInfoType, "contents");
            var contents = contentsField.GetValue(group) as Array ?? Array.CreateInstance(NodeIDType, 0);
            var merged = Array.CreateInstance(NodeIDType, contents.Length + ids.Count);
            Array.Copy(contents, merged, contents.Length);
            for (int i = 0; i < ids.Count; i++)
                merged.SetValue(ids[i], contents.Length + i);
            contentsField.SetValue(group, merged);

            EditorUtility.SetDirty(ui as UnityEngine.Object);
            Persist(graph, assetPath);

            return new JObject
            {
                ["op"] = "group_nodes",
                ["assetPath"] = assetPath,
                ["title"] = title,
                ["groupIndex"] = gi,
                ["createdGroup"] = created,
                ["added"] = ids.Count,
                ["contentCount"] = merged.Length
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
            bool hasIndex = ContextIndexToken(parameters) != null;
            var wantContext = parameters?["contextType"]?.ToString();
            if (!hasIndex && string.IsNullOrEmpty(wantContext))
                return new { error = "contextType (or index/contextIndex) is required" };

            var assetPath = parameters?["assetPath"]?.ToString();
            var graph = LoadGraph(assetPath);
            var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
            var ctx = ResolveContextRef(graph, parameters, ctxList, "context");
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

        /// <summary>`index` or its alias `contextIndex` — the absolute position in the graph's context list.</summary>
        private static JToken ContextIndexToken(JObject o)
        {
            var t = o?["index"];
            if (t == null || t.Type == JTokenType.Null) t = o?["contextIndex"];
            return t == null || t.Type == JTokenType.Null ? null : t;
        }

        /// <summary>Resolve a context endpoint ({index}/{contextIndex} into the context list, or {contextType}).</summary>
        private static object ResolveContextRef(object graph, JObject endpoint, List<object> ctxList, string label)
        {
            if (endpoint == null)
                throw new Exception($"{label} is required (an object with 'index'/'contextIndex' or 'contextType')");
            var idxTok = ContextIndexToken(endpoint);
            if (idxTok != null && idxTok.Type != JTokenType.Null)
            {
                int idx = idxTok.ToObject<int>();
                if (idx < 0 || idx >= ctxList.Count)
                    throw new Exception($"{label} index {idx} out of range; graph has {ctxList.Count} context(s)");
                return ctxList[idx];
            }
            var ct = endpoint["contextType"]?.ToString();
            if (string.IsNullOrEmpty(ct))
                throw new Exception($"{label} needs 'contextType' or 'index'/'contextIndex'");
            var ctx = FindContext(graph, ct);
            if (ctx == null)
                throw new Exception($"{label} context of type '{ct}' not found (or use 'index'/'contextIndex')");
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
            object ctx;
            if (ContextIndexToken(parameters) != null)
            {
                var ctxList = Children(graph).Where(c => ContextType.IsInstanceOfType(c)).ToList();
                ctx = ResolveContextRef(graph, parameters, ctxList, "context");
                wantContext = Prop(ctx, "contextType")?.ToString();
            }
            else
            {
                ctx = FindContext(graph, wantContext);
                if (ctx == null)
                    throw new Exception($"No context of type '{wantContext}' found in {assetPath}");
            }

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
