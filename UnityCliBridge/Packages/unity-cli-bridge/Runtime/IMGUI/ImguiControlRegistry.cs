using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliBridge.Runtime.IMGUI
{
    public static class ImguiControlRegistry
    {
        public struct ControlSnapshot
        {
            public string controlId;
            public string controlType;
            public Rect rect;
            public bool isActive;
            public bool isInteractable;
        }

        private sealed class Entry
        {
            public string controlId;
            public string controlType;
            public Rect rect;
            public bool isInteractable;
            public int lastUpdatedFrame;
            public Func<object> getValue;
            public Action<JToken> setValue;
            public Action onClick;
        }

        private static readonly object RegistryLock = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private const int StaleFrameThreshold = 240;

        /// <summary>
        /// Register an IMGUI control that can be discovered / operated by automation tools.
        /// Call this from OnGUI (Layout/Repaint).
        /// </summary>
        public static void RegisterControl(
            string controlId,
            string controlType,
            Rect rect,
            bool isInteractable,
            Func<object> getValue = null,
            Action<JToken> setValue = null,
            Action onClick = null)
        {
            if (string.IsNullOrEmpty(controlId)) return;
            if (string.IsNullOrEmpty(controlType)) controlType = "Control";

            lock (RegistryLock)
            {
                var frame = Time.frameCount;
                Entries[controlId] = new Entry
                {
                    controlId = controlId,
                    controlType = controlType,
                    rect = rect,
                    isInteractable = isInteractable,
                    lastUpdatedFrame = frame,
                    getValue = getValue,
                    setValue = setValue,
                    onClick = onClick
                };
            }
        }

        public static IReadOnlyList<ControlSnapshot> GetSnapshot()
        {
            lock (RegistryLock)
            {
                var nowFrame = Time.frameCount;
                var staleKeys = Entries
                    .Where(pair => nowFrame - pair.Value.lastUpdatedFrame > StaleFrameThreshold)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var key in staleKeys)
                {
                    Entries.Remove(key);
                }

                return Entries.Values
                    .Select(entry => new ControlSnapshot
                    {
                        controlId = entry.controlId,
                        controlType = entry.controlType,
                        rect = entry.rect,
                        isActive = nowFrame - entry.lastUpdatedFrame <= 1,
                        isInteractable = entry.isInteractable
                    })
                    .ToList();
            }
        }

        public static bool TryInvokeClick(string controlId, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(controlId))
            {
                error = "controlId is required";
                return false;
            }

            Entry entry;
            lock (RegistryLock)
            {
                if (!Entries.TryGetValue(controlId, out entry) || entry == null)
                {
                    error = $"IMGUI control not found: {controlId}";
                    return false;
                }
            }

            if (entry.onClick == null)
            {
                error = $"IMGUI control is not clickable: {controlId}";
                return false;
            }

            try
            {
                entry.onClick.Invoke();
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to click IMGUI control: {e.Message}";
                return false;
            }
        }

        public static bool TrySetValue(string controlId, JToken value, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(controlId))
            {
                error = "controlId is required";
                return false;
            }

            Entry entry;
            lock (RegistryLock)
            {
                if (!Entries.TryGetValue(controlId, out entry) || entry == null)
                {
                    error = $"IMGUI control not found: {controlId}";
                    return false;
                }
            }

            if (entry.setValue == null)
            {
                error = $"IMGUI control does not support setting a value: {controlId}";
                return false;
            }

            try
            {
                entry.setValue.Invoke(value);
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to set IMGUI control value: {e.Message}";
                return false;
            }
        }

        public static bool TryGetState(string controlId, out object state, out string error)
        {
            state = null;
            error = null;
            if (string.IsNullOrEmpty(controlId))
            {
                error = "controlId is required";
                return false;
            }

            Entry entry;
            lock (RegistryLock)
            {
                if (!Entries.TryGetValue(controlId, out entry) || entry == null)
                {
                    error = $"IMGUI control not found: {controlId}";
                    return false;
                }
            }

            object value = null;
            if (entry.getValue != null)
            {
                try { value = entry.getValue.Invoke(); } catch { }
            }

            state = new Dictionary<string, object>
            {
                ["path"] = $"imgui:{entry.controlId}",
                ["uiSystem"] = "imgui",
                ["name"] = entry.controlId,
                ["elementType"] = entry.controlType,
                ["isActive"] = Time.frameCount - entry.lastUpdatedFrame <= 1,
                ["isInteractable"] = entry.isInteractable,
                ["rect"] = new Dictionary<string, object>
                {
                    ["x"] = entry.rect.x,
                    ["y"] = entry.rect.y,
                    ["width"] = entry.rect.width,
                    ["height"] = entry.rect.height
                },
                ["value"] = value
            };

            return true;
        }
    }
}
