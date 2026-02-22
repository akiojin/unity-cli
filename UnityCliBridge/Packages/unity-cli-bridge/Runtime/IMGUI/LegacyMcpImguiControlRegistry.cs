using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMCPServer.Runtime.IMGUI
{
    /// <summary>
    /// Compatibility bridge for code that still references
    /// <c>UnityMCPServer.Runtime.IMGUI.McpImguiControlRegistry</c>.
    /// The implementation delegates to
    /// <see cref="UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry"/> so migrated projects can keep
    /// legacy namespace references during transition.
    /// Remove only after all supported projects (including TestProject and downstream user projects)
    /// no longer reference <c>UnityMCPServer.*</c> IMGUI registry types.
    /// </summary>
    public static class McpImguiControlRegistry
    {
        public struct ControlSnapshot
        {
            public string controlId;
            public string controlType;
            public Rect rect;
            public bool isActive;
            public bool isInteractable;
        }

        public static void RegisterControl(
            string controlId,
            string controlType,
            Rect rect,
            bool isInteractable,
            Func<object> getValue = null,
            Action<JToken> setValue = null,
            Action onClick = null)
        {
            UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry.RegisterControl(
                controlId,
                controlType,
                rect,
                isInteractable,
                getValue,
                setValue,
                onClick);
        }

        public static IReadOnlyList<ControlSnapshot> GetSnapshot()
        {
            return UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry.GetSnapshot()
                .Select(s => new ControlSnapshot
                {
                    controlId = s.controlId,
                    controlType = s.controlType,
                    rect = s.rect,
                    isActive = s.isActive,
                    isInteractable = s.isInteractable
                })
                .ToList();
        }

        public static bool TryInvokeClick(string controlId, out string error)
        {
            return UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry.TryInvokeClick(controlId, out error);
        }

        public static bool TrySetValue(string controlId, JToken value, out string error)
        {
            return UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry.TrySetValue(controlId, value, out error);
        }

        public static bool TryGetState(string controlId, out object state, out string error)
        {
            return UnityCliBridge.Runtime.IMGUI.McpImguiControlRegistry.TryGetState(controlId, out state, out error);
        }
    }
}
