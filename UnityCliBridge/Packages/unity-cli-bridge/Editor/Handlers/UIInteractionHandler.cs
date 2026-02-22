using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;
using UITK = UnityEngine.UIElements;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles UI interaction operations
    /// </summary>
    public static class UIInteractionHandler
    {
        public static Func<bool> PlayModeDetector = () => Application.isPlaying;
        private const string UiToolkitPrefix = "uitk:";
        private const string ImguiPrefix = "imgui:";

        /// <summary>
        /// Finds UI elements based on various filters
        /// </summary>
        public static object FindUIElements(JObject parameters)
        {
            try
            {
                // Parse parameters
                string elementType = parameters["elementType"]?.ToString();
                string tagFilter = parameters["tagFilter"]?.ToString();
                string namePattern = parameters["namePattern"]?.ToString();
                bool includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? false;
                string canvasFilter = parameters["canvasFilter"]?.ToString();
                string uiDocumentFilter = parameters["uiDocumentFilter"]?.ToString();
                string uiSystem = parameters["uiSystem"]?.ToString();
                ParseUiSystemFilter(uiSystem, out bool wantUgui, out bool wantUiToolkit, out bool wantImgui);

                List<object> elements = new List<object>();

                if (wantUgui)
                {
                    // Find all canvases in the scene
                    Canvas[] allCanvases = includeInactive
                        ? Resources.FindObjectsOfTypeAll<Canvas>()
                        : UnityEngine.Object.FindObjectsOfType<Canvas>();

                    // Search through each canvas
                    foreach (var canvas in allCanvases)
                    {
                        // Skip if canvas filter doesn't match
                        if (!string.IsNullOrEmpty(canvasFilter) && canvas.name != canvasFilter)
                            continue;

                        // Get all UI components in the canvas
                        var allComponents = includeInactive
                            ? canvas.GetComponentsInChildren<Component>(true)
                            : canvas.GetComponentsInChildren<Component>(false);

                        foreach (var component in allComponents)
                        {
                            // Skip if not a UI component
                            if (!IsUIComponent(component))
                                continue;

                            // Apply filters
                            if (!PassesFilters(component, elementType, tagFilter, namePattern))
                                continue;

                            // Get interactable status
                            bool isInteractable = GetInteractableStatus(component.gameObject);

                            // Create element info
                            var elementInfo = new
                            {
                                path = GetGameObjectPath(component.gameObject),
                                uiSystem = "ugui",
                                elementType = component.GetType().Name,
                                name = component.gameObject.name,
                                isActive = component.gameObject.activeInHierarchy,
                                isInteractable = isInteractable,
                                tag = component.gameObject.tag,
                                canvasPath = GetGameObjectPath(canvas.gameObject)
                            };

                            elements.Add(elementInfo);
                        }
                    }
                }

                if (wantUiToolkit)
                {
                    elements.AddRange(FindUiToolkitElements(
                        elementType,
                        tagFilter,
                        namePattern,
                        includeInactive,
                        uiDocumentFilter ?? canvasFilter
                    ));
                }

                if (wantImgui)
                {
                    elements.AddRange(FindImguiElements(elementType, tagFilter, namePattern));
                }

                return new
                {
                    elements = elements,
                    count = elements.Count
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("UIInteractionHandler", $"Error in FindUIElements: {e.Message}");
                return new { error = $"Failed to find UI elements: {e.Message}" };
            }
        }

        /// <summary>
        /// Clicks on a UI element
        /// </summary>
        public static async Task<object> ClickUIElement(JObject parameters)
        {
            try
            {
                bool isInPlayMode = PlayModeDetector?.Invoke() ?? Application.isPlaying;
                if (!isInPlayMode)
                {
                    return new { error = "Play Mode is required for click_ui_element", code = "PLAY_MODE_REQUIRED", state = new { isPlaying = isInPlayMode, isCompiling = UnityEditor.EditorApplication.isCompiling } };
                }
                string elementPath = parameters["elementPath"]?.ToString();
                string clickType = parameters["clickType"]?.ToString() ?? "left";
                int holdDurationMs = parameters["holdDuration"]?.ToObject<int>() ?? 0;
                JObject position = parameters["position"] as JObject;
                
                if (string.IsNullOrEmpty(elementPath))
                {
                    return new { error = "elementPath is required" };
                }

                if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
                {
                    return await ClickUiToolkitElement(uiDocumentPath, uiElementName, clickType, holdDurationMs, position);
                }

                if (TryParseImguiElementPath(elementPath, out var controlId))
                {
                    return ClickImguiControl(controlId, clickType, holdDurationMs, position);
                }

                // Find the GameObject
                var warnings = new List<string>();
                GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings);
                if (targetObject == null)
                {
                    return new { error = $"UI element not found at path: {elementPath}" };
                }

                // Check if it's a UI element
                if (!IsUIElementGameObject(targetObject))
                {
                    return new { error = $"GameObject at {elementPath} is not a UI element" };
                }

                // Check if interactable
                var selectable = targetObject.GetComponent<Selectable>();
                if (selectable != null && !selectable.interactable)
                {
                    warnings.Add($"UI element at {elementPath} is not interactable (attempting click anyway)");
                }

                if (holdDurationMs < 0) holdDurationMs = 0;
                if (holdDurationMs > 10000) holdDurationMs = 10000;
                if (!TryParseClickButton(clickType, out var button))
                {
                    return new { error = $"Unsupported clickType: {clickType}" };
                }

                // Simulate click based on UI events
                bool success = await SimulateClickAsync(targetObject, button, clickCount: 1, holdDurationMs: holdDurationMs, position: position, warnings: warnings);

                if (!success)
                {
                    return new { error = "Failed to simulate click on UI element" };
                }

                return new
                {
                    success = true,
                    elementPath = elementPath,
                    clickType = clickType,
                    holdDuration = holdDurationMs,
                    message = $"Successfully clicked {targetObject.name}"
                    ,
                    warnings = warnings.Count > 0 ? warnings : null
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("UIInteractionHandler", $"Error in ClickUIElement: {e.Message}");
                return new { error = $"Failed to click UI element: {e.Message}" };
            }
        }

        /// <summary>
        /// Gets the state of a UI element
        /// </summary>
        public static object GetUIElementState(JObject parameters)
        {
            try
            {
                string elementPath = parameters["elementPath"]?.ToString();
                bool includeChildren = parameters["includeChildren"]?.ToObject<bool>() ?? false;
                bool includeInteractableInfo = parameters["includeInteractableInfo"]?.ToObject<bool>() ?? true;

                if (string.IsNullOrEmpty(elementPath))
                {
                    return new { error = "elementPath is required" };
                }

                if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
                {
                    return GetUiToolkitElementState(uiDocumentPath, uiElementName, includeChildren, includeInteractableInfo);
                }

                if (TryParseImguiElementPath(elementPath, out var controlId))
                {
                    return GetImguiElementState(controlId);
                }

                GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings: null);
                if (targetObject == null)
                {
                    return new { error = $"UI element not found at path: {elementPath}" };
                }

                var state = GetElementState(targetObject, includeChildren, includeInteractableInfo);
                return state;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("UIInteractionHandler", $"Error in GetUIElementState: {e.Message}");
                return new { error = $"Failed to get UI element state: {e.Message}" };
            }
        }

        /// <summary>
        /// Sets the value of a UI element
        /// </summary>
        public static object SetUIElementValue(JObject parameters)
        {
            try
            {
                bool isInPlayMode = PlayModeDetector?.Invoke() ?? Application.isPlaying;
                if (!isInPlayMode)
                {
                    return new { error = "Play Mode is required for set_ui_element_value", code = "PLAY_MODE_REQUIRED", state = new { isPlaying = isInPlayMode, isCompiling = UnityEditor.EditorApplication.isCompiling } };
                }

                string elementPath = parameters["elementPath"]?.ToString();
                var value = parameters["value"];
                bool triggerEvents = parameters["triggerEvents"]?.ToObject<bool>() ?? true;

                if (string.IsNullOrEmpty(elementPath))
                {
                    return new { error = "elementPath is required" };
                }

                if (value == null)
                {
                    return new { error = "value is required" };
                }

                if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
                {
                    return SetUiToolkitElementValue(uiDocumentPath, uiElementName, value, triggerEvents);
                }

                if (TryParseImguiElementPath(elementPath, out var controlId))
                {
                    return SetImguiElementValue(controlId, value);
                }

                GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings: null);
                if (targetObject == null)
                {
                    return new { error = $"UI element not found at path: {elementPath}" };
                }

                bool success = SetElementValue(targetObject, value, triggerEvents);
                if (!success)
                {
                    return new { error = "Failed to set UI element value - unsupported element type" };
                }

                return new
                {
                    success = true,
                    elementPath = elementPath,
                    newValue = value.ToString(),
                    message = $"Successfully set value for {targetObject.name}"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("UIInteractionHandler", $"Error in SetUIElementValue: {e.Message}");
                return new { error = $"Failed to set UI element value: {e.Message}" };
            }
        }

        /// <summary>
        /// Simulates a complex UI input sequence
        /// </summary>
        public static async Task<object> SimulateUIInput(JObject parameters)
        {
            try
            {
                bool isInPlayMode = PlayModeDetector?.Invoke() ?? Application.isPlaying;
                if (!isInPlayMode)
                {
                    return new { error = "Play Mode is required for simulate_ui_input", code = "PLAY_MODE_REQUIRED", state = new { isPlaying = isInPlayMode, isCompiling = UnityEditor.EditorApplication.isCompiling } };
                }
                var inputSequence = parameters["inputSequence"]?.ToObject<JArray>();
                int waitBetween = parameters["waitBetween"]?.ToObject<int>() ?? 100;
                bool validateState = parameters["validateState"]?.ToObject<bool>() ?? true;

                if (inputSequence == null || inputSequence.Count == 0)
                {
                    return new { error = "inputSequence is required and must not be empty" };
                }

                List<object> results = new List<object>();
                int executedActions = 0;
                
                for (int i = 0; i < inputSequence.Count; i++)
                {
                    var action = inputSequence[i] as JObject;
                    if (action == null)
                    {
                        var invalid = new { error = "Invalid action format" };
                        results.Add(new { type = "(invalid)", result = invalid });
                        return new
                        {
                            success = false,
                            error = "Invalid action format",
                            results = results,
                            totalActions = inputSequence.Count,
                            executedActions = executedActions,
                            stoppedAtIndex = i
                        };
                    }
                    string actionType = action["type"]?.ToString();
                    var actionParams = action["params"] as JObject;

                    if (string.IsNullOrEmpty(actionType) || actionParams == null)
                    {
                        var invalid = new { error = "Invalid action format" };
                        results.Add(new { type = actionType ?? "(null)", result = invalid });
                        return new
                        {
                            success = false,
                            error = "Invalid action format",
                            results = results,
                            totalActions = inputSequence.Count,
                            executedActions = executedActions,
                            stoppedAtIndex = i
                        };
                    }

                    // Execute action based on type
                    object result = null;
                    switch (actionType.ToLower())
                    {
                        case "click":
                            result = await ClickUIElement(actionParams);
                            break;
                        case "doubleclick":
                            result = await SimulateDoubleClick(actionParams);
                            break;
                        case "rightclick":
                            {
                                var merged = new JObject(actionParams);
                                merged["clickType"] = "right";
                                result = await ClickUIElement(merged);
                                break;
                            }
                        case "hover":
                            result = HoverUIElement(actionParams);
                            break;
                        case "focus":
                            result = FocusUIElement(actionParams);
                            break;
                        case "type":
                            result = TypeIntoElement(actionParams);
                            break;
                        case "setvalue":
                        case "set_value":
                            result = SetUIElementValue(actionParams);
                            break;
                        default:
                            result = new { error = $"Unknown action type: {actionType}" };
                            break;
                    }

                    var step = new Dictionary<string, object>
                    {
                        ["type"] = actionType,
                        ["result"] = result
                    };
                    
                    if (validateState)
                    {
                        string elementPath = actionParams["elementPath"]?.ToString();
                        if (!string.IsNullOrEmpty(elementPath))
                        {
                            var stateParams = new JObject
                            {
                                ["elementPath"] = elementPath,
                                ["includeChildren"] = false,
                                ["includeInteractableInfo"] = true
                            };
                            step["postState"] = GetUIElementState(stateParams);
                        }
                    }

                    results.Add(step);
                    executedActions++;
                    if (HasError(result))
                    {
                        var remaining = new List<object>();
                        for (int j = i + 1; j < inputSequence.Count; j++)
                        {
                            if (inputSequence[j] is JObject remainingAction)
                            {
                                remaining.Add(new
                                {
                                    type = remainingAction["type"]?.ToString(),
                                    @params = remainingAction["params"]
                                });
                            }
                        }
                        return new
                        {
                            success = false,
                            error = "Sequence stopped due to error",
                            results = results,
                            totalActions = inputSequence.Count,
                            executedActions = executedActions,
                            stoppedAtIndex = i,
                            remainingActions = remaining
                        };
                    }

                    // Wait between actions if needed
                    if (waitBetween > 0 && i < inputSequence.Count - 1)
                    {
                        await DelayMs(waitBetween);
                    }
                }

                return new
                {
                    success = true,
                    results = results,
                    totalActions = inputSequence.Count,
                    executedActions = executedActions
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("UIInteractionHandler", $"Error in SimulateUIInput: {e.Message}");
                return new { error = $"Failed to simulate UI input: {e.Message}" };
            }
        }

        #region Helper Methods

        private static bool IsUIComponent(Component component)
        {
            // Check if component is a UI component
            return component is Graphic || 
                   component is Selectable || 
                   component is LayoutGroup ||
                   component is ContentSizeFitter ||
                   component is AspectRatioFitter ||
                   component is CanvasScaler ||
                   component is GraphicRaycaster;
        }

        private static bool PassesFilters(Component component, string elementType, string tagFilter, string namePattern)
        {
            // Check element type filter
            if (!string.IsNullOrEmpty(elementType))
            {
                string componentType = component.GetType().Name;
                if (!componentType.Equals(elementType, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Check tag filter
            if (!string.IsNullOrEmpty(tagFilter))
            {
                if (component.gameObject.tag != tagFilter)
                    return false;
            }

            // Check name pattern
            if (!string.IsNullOrEmpty(namePattern))
            {
                try
                {
                    Regex regex = new Regex(namePattern);
                    if (!regex.IsMatch(component.gameObject.name))
                        return false;
                }
                catch
                {
                    // If regex is invalid, do simple contains check
                    if (!component.gameObject.name.Contains(namePattern))
                        return false;
                }
            }

            return true;
        }

        private static bool GetInteractableStatus(GameObject gameObject)
        {
            if (gameObject == null) return false;

            var selectable = gameObject.GetComponent<Selectable>();
            if (selectable != null)
                return selectable.interactable;

            // For non-selectable UI elements, consider them interactable if active
            return gameObject.activeInHierarchy;
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            Transform parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            
            return path;
        }

        private static async Task<bool> SimulateClickAsync(GameObject target, PointerEventData.InputButton button, int clickCount, int holdDurationMs, JObject position, List<string> warnings)
        {
            var eventSystem = EnsureEventSystem(warnings);
            if (eventSystem == null)
            {
                return false;
            }

            Vector2 screenPoint;
            if (!TryGetScreenPoint(target, position, warnings, out screenPoint))
            {
                // Best-effort fallback when we cannot resolve a point
                screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            var pointer = new PointerEventData(eventSystem)
            {
                button = button,
                position = screenPoint,
                pressPosition = screenPoint,
                clickCount = clickCount,
                clickTime = Time.unscaledTime
            };

            eventSystem.SetSelectedGameObject(target);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
            await DelayMs(holdDurationMs);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerExitHandler);

            return true;
        }

        private static object GetElementState(GameObject target, bool includeChildren, bool includeInteractableInfo)
        {
            var state = new Dictionary<string, object>
            {
                ["path"] = GetGameObjectPath(target),
                ["name"] = target.name,
                ["isActive"] = target.activeInHierarchy,
                ["tag"] = target.tag,
                ["layer"] = LayerMask.LayerToName(target.layer)
            };

            // Get component information
            var components = new List<string>();
            foreach (var component in target.GetComponents<Component>())
            {
                components.Add(component.GetType().Name);
            }
            state["components"] = components;

            // Get UI-specific state
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null)
            {
                state["color"] = ColorToHex(graphic.color);
                state["raycastTarget"] = graphic.raycastTarget;
            }

            if (includeInteractableInfo)
            {
                state["isInteractable"] = GetInteractableStatus(target);
                
                // Get specific UI element values
                var inputField = target.GetComponent<InputField>();
                if (inputField != null)
                {
                    state["value"] = inputField.text;
                    state["placeholder"] = inputField.placeholder?.GetComponent<Text>()?.text;
                }

                var toggle = target.GetComponent<Toggle>();
                if (toggle != null)
                {
                    state["isOn"] = toggle.isOn;
                }

                var slider = target.GetComponent<Slider>();
                if (slider != null)
                {
                    state["value"] = slider.value;
                    state["minValue"] = slider.minValue;
                    state["maxValue"] = slider.maxValue;
                }

                var dropdown = target.GetComponent<Dropdown>();
                if (dropdown != null)
                {
                    state["value"] = dropdown.value;
                    state["options"] = dropdown.options.Select(o => o.text).ToList();
                }

                var text = target.GetComponent<Text>();
                if (text != null)
                {
                    state["text"] = text.text;
                    state["fontSize"] = text.fontSize;
                    state["font"] = text.font?.name;
                }
            }

            if (includeChildren)
            {
                var children = new List<object>();
                foreach (Transform child in target.transform)
                {
                    children.Add(GetElementState(child.gameObject, false, includeInteractableInfo));
                }
                state["children"] = children;
            }

            return state;
        }

        private static bool HasError(object result)
        {
            if (result == null) return true;
            try
            {
                var token = JToken.FromObject(result);
                return token.Type == JTokenType.Object && token["error"] != null;
            }
            catch
            {
                return false;
            }
        }

        private static void ParseUiSystemFilter(string uiSystem, out bool wantUgui, out bool wantUiToolkit, out bool wantImgui)
        {
            // Default: include all systems
            wantUgui = true;
            wantUiToolkit = true;
            wantImgui = true;

            if (string.IsNullOrWhiteSpace(uiSystem))
            {
                return;
            }

            var parts = uiSystem
                .Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();

            if (parts.Length == 0)
            {
                return;
            }

            bool hasAll = parts.Any(p => string.Equals(p, "all", StringComparison.OrdinalIgnoreCase));
            if (hasAll)
            {
                wantUgui = true;
                wantUiToolkit = true;
                wantImgui = true;
                return;
            }

            wantUgui = parts.Any(p => string.Equals(p, "ugui", StringComparison.OrdinalIgnoreCase));
            wantUiToolkit = parts.Any(p =>
                string.Equals(p, "uitk", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "ui-toolkit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "uitoolkit", StringComparison.OrdinalIgnoreCase)
            );
            wantImgui = parts.Any(p => string.Equals(p, "imgui", StringComparison.OrdinalIgnoreCase));

            // If nothing matched, fall back to all (avoid surprising "empty" results)
            if (!wantUgui && !wantUiToolkit && !wantImgui)
            {
                wantUgui = true;
                wantUiToolkit = true;
                wantImgui = true;
            }
        }

        private static bool TryParseUiToolkitElementPath(string elementPath, out string uiDocumentPath, out string elementName)
        {
            uiDocumentPath = null;
            elementName = null;
            if (string.IsNullOrEmpty(elementPath)) return false;
            if (!elementPath.StartsWith(UiToolkitPrefix, StringComparison.OrdinalIgnoreCase)) return false;

            var withoutPrefix = elementPath.Substring(UiToolkitPrefix.Length);
            var hashIndex = withoutPrefix.IndexOf('#');
            if (hashIndex < 0) return false;

            uiDocumentPath = withoutPrefix.Substring(0, hashIndex);
            elementName = withoutPrefix.Substring(hashIndex + 1);
            if (string.IsNullOrEmpty(uiDocumentPath) || string.IsNullOrEmpty(elementName)) return false;
            return true;
        }

        private static bool TryParseImguiElementPath(string elementPath, out string controlId)
        {
            controlId = null;
            if (string.IsNullOrEmpty(elementPath)) return false;
            if (!elementPath.StartsWith(ImguiPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            controlId = elementPath.Substring(ImguiPrefix.Length);
            return !string.IsNullOrEmpty(controlId);
        }

        private static List<UITK.VisualElement> CollectUiToolkitElements(UITK.VisualElement root)
        {
            var results = new List<UITK.VisualElement>();
            if (root == null) return results;

            var stack = new Stack<UITK.VisualElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                results.Add(current);

                try
                {
                    foreach (var child in current.hierarchy.Children())
                    {
                        if (child == null) continue;
                        stack.Push(child);
                    }
                }
                catch
                {
                    // Some panels may not be ready yet
                }
            }

            return results;
        }

        private static List<UITK.VisualElement> FindUiToolkitElementsByName(UITK.VisualElement root, string elementName)
        {
            var matches = new List<UITK.VisualElement>();
            if (root == null) return matches;
            if (string.IsNullOrEmpty(elementName)) return matches;

            var stack = new Stack<UITK.VisualElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                if (string.Equals(current.name, elementName, StringComparison.Ordinal))
                {
                    matches.Add(current);
                }

                try
                {
                    foreach (var child in current.hierarchy.Children())
                    {
                        if (child == null) continue;
                        stack.Push(child);
                    }
                }
                catch
                {
                    // Some panels may not be ready yet
                }
            }

            return matches;
        }

        private static IEnumerable<object> FindUiToolkitElements(string elementType, string tagFilter, string namePattern, bool includeInactive, string uiDocumentFilter)
        {
            var results = new List<object>();

            UITK.UIDocument[] documents = includeInactive
                ? Resources.FindObjectsOfTypeAll<UITK.UIDocument>()
                : UnityEngine.Object.FindObjectsOfType<UITK.UIDocument>();

            foreach (var doc in documents)
            {
                if (doc == null || doc.gameObject == null) continue;
                if (!string.IsNullOrEmpty(uiDocumentFilter) && !string.Equals(doc.gameObject.name, uiDocumentFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                var root = doc.rootVisualElement;
                if (root == null) continue;

                var docPath = GetGameObjectPath(doc.gameObject);
                List<UITK.VisualElement> all;
                all = CollectUiToolkitElements(root);

                foreach (var ve in all)
                {
                    if (ve == null) continue;
                    if (string.IsNullOrEmpty(ve.name)) continue;

                    if (!string.IsNullOrEmpty(elementType))
                    {
                        var t = ve.GetType().Name;
                        if (!t.Equals(elementType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(tagFilter))
                    {
                        // UI Toolkit: treat tagFilter as class filter
                        if (!ve.ClassListContains(tagFilter))
                        {
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(namePattern))
                    {
                        if (!NameMatchesPattern(ve.name, namePattern))
                        {
                            continue;
                        }
                    }

                    var elementPath = $"{UiToolkitPrefix}{docPath}#{ve.name}";
                    var isActive = doc.gameObject.activeInHierarchy && ve.visible;
                    var isInteractable = ve.enabledInHierarchy;

                    List<string> classes = null;
                    try
                    {
                        classes = ve.GetClasses()?.ToList();
                    }
                    catch { }

                    results.Add(new
                    {
                        path = elementPath,
                        uiSystem = "uitk",
                        elementType = ve.GetType().Name,
                        name = ve.name,
                        isActive = isActive,
                        isInteractable = isInteractable,
                        documentPath = docPath,
                        classes = classes
                    });
                }
            }

            return results;
        }

        private static IEnumerable<object> FindImguiElements(string elementType, string tagFilter, string namePattern)
        {
            var results = new List<object>();
            try
            {
                var snapshot = UnityCliBridge.Runtime.IMGUI.ImguiControlRegistry.GetSnapshot();
                foreach (var ctrl in snapshot)
                {
                    if (!string.IsNullOrEmpty(elementType) &&
                        !string.Equals(ctrl.controlType, elementType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(namePattern) && !NameMatchesPattern(ctrl.controlId, namePattern))
                    {
                        continue;
                    }

                    results.Add(new
                    {
                        path = $"{ImguiPrefix}{ctrl.controlId}",
                        uiSystem = "imgui",
                        elementType = ctrl.controlType,
                        name = ctrl.controlId,
                        isActive = ctrl.isActive,
                        isInteractable = ctrl.isInteractable,
                        rect = new { x = ctrl.rect.x, y = ctrl.rect.y, width = ctrl.rect.width, height = ctrl.rect.height }
                    });
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogWarning("UIInteractionHandler", $"IMGUI registry unavailable: {e.Message}");
            }

            return results;
        }

        private static bool NameMatchesPattern(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                var regex = new Regex(pattern);
                return regex.IsMatch(name);
            }
            catch
            {
                return name.Contains(pattern);
            }
        }

        private static async Task<object> ClickUiToolkitElement(string uiDocumentPath, string elementName, string clickType, int holdDurationMs, JObject position)
        {
            var warnings = new List<string>();

            if (!TryParseClickButton(clickType, out var _))
            {
                warnings.Add($"clickType '{clickType}' is not supported for UI Toolkit; treating as left-click");
            }

            if (holdDurationMs != 0)
            {
                warnings.Add("holdDuration is not supported for UI Toolkit; ignored");
            }

            if (position != null)
            {
                warnings.Add("position is not supported for UI Toolkit; ignored");
            }

            var docObject = FindGameObjectByPath(uiDocumentPath, includeInactive: true, warnings: null);
            if (docObject == null)
            {
                return new { error = $"UIDocument GameObject not found at path: {uiDocumentPath}" };
            }

            var doc = docObject.GetComponent<UITK.UIDocument>();
            if (doc == null)
            {
                return new { error = $"GameObject at {uiDocumentPath} does not have a UIDocument component" };
            }

            var root = doc.rootVisualElement;
            if (root == null)
            {
                return new { error = $"UIDocument at {uiDocumentPath} has no rootVisualElement (panel not ready)" };
            }

            var matches = FindUiToolkitElementsByName(root, elementName);
            if (matches.Count == 0)
            {
                return new { error = $"UI Toolkit element not found: {elementName}" };
            }
            if (matches.Count > 1)
            {
                warnings.Add($"Multiple UI Toolkit elements found with name '{elementName}'; using the first match");
            }

            var element = matches[0];
            if (element == null)
            {
                return new { error = $"UI Toolkit element not found: {elementName}" };
            }

            bool clicked = false;
            if (element is UITK.Button button)
            {
                clicked = TryClickUiToolkitButton(button, warnings);
            }
            else if (element is UITK.Toggle toggle)
            {
                try
                {
                    toggle.value = !toggle.value;
                    clicked = true;
                }
                catch (Exception e)
                {
                    warnings.Add($"Failed to toggle UI Toolkit Toggle: {e.Message}");
                }
            }

            if (!clicked)
            {
                // Best effort: focus the element
                try
                {
                    element.Focus();
                    warnings.Add("Element does not support click; applied focus instead");
                    clicked = true;
                }
                catch
                {
                    return new { error = "Failed to simulate UI Toolkit click" };
                }
            }

            await Task.CompletedTask;
            return new
            {
                success = true,
                elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}",
                uiSystem = "uitk",
                message = $"Successfully clicked {elementName}",
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private static bool TryClickUiToolkitButton(UITK.Button button, List<string> warnings)
        {
            if (button == null) return false;

            // 1) Try Button.Click() if available
            try
            {
                var clickMethod = button
                    .GetType()
                    .GetMethod("Click", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (clickMethod != null)
                {
                    clickMethod.Invoke(button, null);
                    return true;
                }
            }
            catch (Exception e)
            {
                warnings?.Add($"Failed to invoke UI Toolkit Button.Click(): {e.Message}");
            }

            // 2) Prefer invoking Clickable via reflection (API differs by Unity versions)
            try
            {
                var clickable = button.clickable;
                if (clickable != null)
                {
                    var t = clickable.GetType();
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                    // 2-a) Known method names (prefer parameterless)
                    var knownNames = new[] { "SimulateSingleClick", "SimulateClick", "Click", "Invoke" };
                    foreach (var name in knownNames)
                    {
                        var m = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                        if (m != null)
                        {
                            m.Invoke(clickable, null);
                            return true;
                        }
                    }

                    // 2-b) Delegate fields/properties (best-effort)
                    foreach (var field in t.GetFields(flags))
                    {
                        if (field.FieldType != typeof(Action)) continue;
                        if (field.Name.IndexOf("click", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var del = field.GetValue(clickable) as Action;
                        if (del == null) continue;
                        del.Invoke();
                        return true;
                    }

                    foreach (var prop in t.GetProperties(flags))
                    {
                        if (!prop.CanRead) continue;
                        if (prop.PropertyType != typeof(Action)) continue;
                        if (prop.Name.IndexOf("click", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var del = prop.GetValue(clickable, null) as Action;
                        if (del == null) continue;
                        del.Invoke();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                warnings?.Add($"Failed to simulate UI Toolkit button click: {e.Message}");
            }

            // 3) Try to invoke a private 'clicked' delegate (best-effort)
            try
            {
                for (var t = button.GetType(); t != null; t = t.BaseType)
                {
                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    foreach (var field in fields)
                    {
                        if (field.FieldType != typeof(Action)) continue;
                        if (field.Name.IndexOf("click", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        var del = field.GetValue(button) as Action;
                        if (del == null) continue;

                        del.Invoke();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                warnings?.Add($"Failed to invoke UI Toolkit button clicked delegate: {e.Message}");
            }

            // 4) Dispatch ClickEvent (best-effort, reflection-based to reduce API surface assumptions)
            try
            {
                var clickEventType = typeof(UITK.ClickEvent);
                var getPooled = clickEventType.GetMethod("GetPooled", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (getPooled != null)
                {
                    var evt = getPooled.Invoke(null, null) as UITK.EventBase;
                    if (evt != null)
                    {
                        try
                        {
                            button.SendEvent(evt);
                            return true;
                        }
                        finally
                        {
                            try
                            {
                                var dispose = evt.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance);
                                dispose?.Invoke(evt, null);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                warnings?.Add($"Failed to dispatch UI Toolkit ClickEvent: {e.Message}");
            }

            return false;
        }

        private static object GetUiToolkitElementState(string uiDocumentPath, string elementName, bool includeChildren, bool includeInteractableInfo)
        {
            var docObject = FindGameObjectByPath(uiDocumentPath, includeInactive: true, warnings: null);
            if (docObject == null)
            {
                return new { error = $"UIDocument GameObject not found at path: {uiDocumentPath}" };
            }

            var doc = docObject.GetComponent<UITK.UIDocument>();
            if (doc == null)
            {
                return new { error = $"GameObject at {uiDocumentPath} does not have a UIDocument component" };
            }

            var root = doc.rootVisualElement;
            if (root == null)
            {
                return new { error = $"UIDocument at {uiDocumentPath} has no rootVisualElement (panel not ready)" };
            }

            var matches = FindUiToolkitElementsByName(root, elementName);
            if (matches.Count == 0)
            {
                return new { error = $"UI Toolkit element not found: {elementName}" };
            }

            var element = matches[0];
            return BuildUiToolkitElementState(uiDocumentPath, element, includeChildren, includeInteractableInfo);
        }

        private static object BuildUiToolkitElementState(string uiDocumentPath, UITK.VisualElement element, bool includeChildren, bool includeInteractableInfo)
        {
            if (element == null)
            {
                return new { error = "UI Toolkit element is null" };
            }

            var state = new Dictionary<string, object>
            {
                ["path"] = $"{UiToolkitPrefix}{uiDocumentPath}#{element.name}",
                ["uiSystem"] = "uitk",
                ["name"] = element.name,
                ["elementType"] = element.GetType().Name,
                ["isActive"] = element.visible,
            };

            if (includeInteractableInfo)
            {
                state["isInteractable"] = element.enabledInHierarchy;
            }

            // Values / text (best-effort)
            if (element is UITK.TextField textField)
            {
                state["value"] = textField.value;
            }
            else if (element is UITK.Toggle toggle)
            {
                state["value"] = toggle.value;
            }
            else if (element is UITK.Slider slider)
            {
                state["value"] = slider.value;
                state["lowValue"] = slider.lowValue;
                state["highValue"] = slider.highValue;
            }
            else if (element is UITK.SliderInt sliderInt)
            {
                state["value"] = sliderInt.value;
                state["lowValue"] = sliderInt.lowValue;
                state["highValue"] = sliderInt.highValue;
            }
            else if (element is UITK.DropdownField dropdown)
            {
                state["value"] = dropdown.value;
                state["choices"] = dropdown.choices?.ToList();
            }
            else if (element is UITK.Label label)
            {
                state["text"] = label.text;
            }
            else if (element is UITK.Button button)
            {
                state["text"] = button.text;
            }

            if (includeChildren)
            {
                var children = new List<object>();
                for (int i = 0; i < element.hierarchy.childCount; i++)
                {
                    var child = element.hierarchy[i];
                    if (child != null && !string.IsNullOrEmpty(child.name))
                    {
                        children.Add(BuildUiToolkitElementState(uiDocumentPath, child, includeChildren: false, includeInteractableInfo: includeInteractableInfo));
                    }
                }
                state["children"] = children;
            }

            return state;
        }

        private static object SetUiToolkitElementValue(string uiDocumentPath, string elementName, JToken value, bool triggerEvents)
        {
            var docObject = FindGameObjectByPath(uiDocumentPath, includeInactive: true, warnings: null);
            if (docObject == null)
            {
                return new { error = $"UIDocument GameObject not found at path: {uiDocumentPath}" };
            }

            var doc = docObject.GetComponent<UITK.UIDocument>();
            if (doc == null)
            {
                return new { error = $"GameObject at {uiDocumentPath} does not have a UIDocument component" };
            }

            var root = doc.rootVisualElement;
            if (root == null)
            {
                return new { error = $"UIDocument at {uiDocumentPath} has no rootVisualElement (panel not ready)" };
            }

            var matches = FindUiToolkitElementsByName(root, elementName);
            if (matches.Count == 0)
            {
                return new { error = $"UI Toolkit element not found: {elementName}" };
            }

            var element = matches[0];
            if (element is UITK.TextField textField)
            {
                var newValue = value.Type == JTokenType.Null ? string.Empty : value.ToString();
                if (triggerEvents)
                {
                    textField.value = newValue;
                }
                else
                {
                    textField.SetValueWithoutNotify(newValue);
                }
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }
            if (element is UITK.Toggle toggle)
            {
                var newValue = value.ToObject<bool>();
                if (triggerEvents)
                {
                    toggle.value = newValue;
                }
                else
                {
                    toggle.SetValueWithoutNotify(newValue);
                }
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }
            if (element is UITK.Slider slider)
            {
                var newValue = value.ToObject<float>();
                if (triggerEvents)
                {
                    slider.value = newValue;
                }
                else
                {
                    slider.SetValueWithoutNotify(newValue);
                }
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }
            if (element is UITK.SliderInt sliderInt)
            {
                var newValue = value.ToObject<int>();
                if (triggerEvents)
                {
                    sliderInt.value = newValue;
                }
                else
                {
                    sliderInt.SetValueWithoutNotify(newValue);
                }
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }
            if (element is UITK.DropdownField dropdown)
            {
                string newValue = null;
                if (value.Type == JTokenType.Integer)
                {
                    var index = value.ToObject<int>();
                    if (dropdown.choices == null || index < 0 || index >= dropdown.choices.Count)
                    {
                        return new { error = $"Dropdown index out of range: {index}" };
                    }
                    newValue = dropdown.choices[index];
                }
                else
                {
                    newValue = value.ToString();
                }

                if (triggerEvents)
                {
                    dropdown.value = newValue;
                }
                else
                {
                    dropdown.SetValueWithoutNotify(newValue);
                }
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }
            if (element is UITK.Label label)
            {
                var newValue = value.Type == JTokenType.Null ? string.Empty : value.ToString();
                label.text = newValue;
                return new { success = true, elementPath = $"{UiToolkitPrefix}{uiDocumentPath}#{elementName}", uiSystem = "uitk", newValue = newValue };
            }

            return new { error = "Failed to set UI Toolkit element value - unsupported element type" };
        }

        private static object ClickImguiControl(string controlId, string clickType, int holdDurationMs, JObject position)
        {
            var warnings = new List<string>();

            if (!string.Equals(clickType, "left", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"IMGUI clickType '{clickType}' is not supported; treating as left-click");
            }
            if (holdDurationMs != 0)
            {
                warnings.Add("IMGUI holdDuration is not supported; ignored");
            }
            if (position != null)
            {
                warnings.Add("IMGUI position is not supported; ignored");
            }

            if (UnityCliBridge.Runtime.IMGUI.ImguiControlRegistry.TryInvokeClick(controlId, out var error))
            {
                return new
                {
                    success = true,
                    elementPath = $"{ImguiPrefix}{controlId}",
                    uiSystem = "imgui",
                    message = $"Successfully clicked {controlId}",
                    warnings = warnings.Count > 0 ? warnings : null
                };
            }

            return new { error = error ?? $"IMGUI control not found: {controlId}" };
        }

        private static object SetImguiElementValue(string controlId, JToken value)
        {
            if (UnityCliBridge.Runtime.IMGUI.ImguiControlRegistry.TrySetValue(controlId, value, out var error))
            {
                return new { success = true, elementPath = $"{ImguiPrefix}{controlId}", uiSystem = "imgui", newValue = value?.ToString() };
            }

            return new { error = error ?? $"IMGUI control not found: {controlId}" };
        }

        private static object GetImguiElementState(string controlId)
        {
            if (UnityCliBridge.Runtime.IMGUI.ImguiControlRegistry.TryGetState(controlId, out var state, out var error))
            {
                return state;
            }

            return new { error = error ?? $"IMGUI control not found: {controlId}" };
        }

        private static async Task<object> SimulateDoubleClick(JObject parameters)
        {
            var warnings = new List<string>();
            var first = new JObject(parameters);
            first["clickType"] = "left";
            first["holdDuration"] = 0;

            var second = new JObject(parameters);
            second["clickType"] = "left";
            second["holdDuration"] = 0;

            var firstResult = await ClickUIElement(first);
            if (HasError(firstResult))
            {
                return firstResult;
            }
            
            await DelayMs(50);
            var secondResult = await ClickUIElement(second);
            if (HasError(secondResult))
            {
                return secondResult;
            }
            
            return new
            {
                success = true,
                message = "Successfully double-clicked element",
                clicks = new[] { firstResult, secondResult },
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private static object HoverUIElement(JObject parameters)
        {
            string elementPath = parameters["elementPath"]?.ToString();
            JObject position = parameters["position"] as JObject;
            if (string.IsNullOrEmpty(elementPath))
            {
                return new { error = "elementPath is required" };
            }

            if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
            {
                // UI Toolkit hover is best-effort (no direct hover API); treat as success.
                return new
                {
                    success = true,
                    elementPath = elementPath,
                    uiSystem = "uitk",
                    message = $"Hover is best-effort for UI Toolkit; no state change applied ({uiElementName})",
                    warnings = new[] { "hover is not fully simulated for UI Toolkit" }
                };
            }

            if (TryParseImguiElementPath(elementPath, out var controlId))
            {
                return new
                {
                    success = true,
                    elementPath = elementPath,
                    uiSystem = "imgui",
                    message = $"Hover is not supported for IMGUI controls ({controlId})",
                    warnings = new[] { "hover is not supported for IMGUI" }
                };
            }

            var warnings = new List<string>();
            GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings);
            if (targetObject == null)
            {
                return new { error = $"UI element not found at path: {elementPath}" };
            }

            if (!IsUIElementGameObject(targetObject))
            {
                return new { error = $"GameObject at {elementPath} is not a UI element" };
            }

            var eventSystem = EnsureEventSystem(warnings);
            if (eventSystem == null)
            {
                return new { error = "EventSystem is required for hover" };
            }

            Vector2 screenPoint;
            if (!TryGetScreenPoint(targetObject, position, warnings, out screenPoint))
            {
                screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = screenPoint
            };

            ExecuteEvents.Execute(targetObject, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(targetObject, pointer, ExecuteEvents.pointerMoveHandler);

            return new
            {
                success = true,
                elementPath = elementPath,
                message = $"Successfully hovered {targetObject.name}",
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private static object FocusUIElement(JObject parameters)
        {
            string elementPath = parameters["elementPath"]?.ToString();
            if (string.IsNullOrEmpty(elementPath))
            {
                return new { error = "elementPath is required" };
            }

            if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
            {
                var docObject = FindGameObjectByPath(uiDocumentPath, includeInactive: true, warnings: null);
                if (docObject == null)
                {
                    return new { error = $"UIDocument GameObject not found at path: {uiDocumentPath}" };
                }
                var doc = docObject.GetComponent<UITK.UIDocument>();
                if (doc == null)
                {
                    return new { error = $"GameObject at {uiDocumentPath} does not have a UIDocument component" };
                }
                var root = doc.rootVisualElement;
                if (root == null)
                {
                    return new { error = $"UIDocument at {uiDocumentPath} has no rootVisualElement (panel not ready)" };
                }
                var matches = FindUiToolkitElementsByName(root, uiElementName);
                var element = matches.Count > 0 ? matches[0] : null;
                if (element == null)
                {
                    return new { error = $"UI Toolkit element not found: {uiElementName}" };
                }
                element.Focus();
                return new
                {
                    success = true,
                    elementPath = elementPath,
                    uiSystem = "uitk",
                    message = $"Successfully focused {uiElementName}"
                };
            }

            if (TryParseImguiElementPath(elementPath, out var controlId))
            {
                return new
                {
                    success = true,
                    elementPath = elementPath,
                    uiSystem = "imgui",
                    message = $"Focus is not supported for IMGUI controls ({controlId})",
                    warnings = new[] { "focus is not supported for IMGUI" }
                };
            }

            var warnings = new List<string>();
            GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings);
            if (targetObject == null)
            {
                return new { error = $"UI element not found at path: {elementPath}" };
            }

            if (!IsUIElementGameObject(targetObject))
            {
                return new { error = $"GameObject at {elementPath} is not a UI element" };
            }

            var eventSystem = EnsureEventSystem(warnings);
            if (eventSystem == null)
            {
                return new { error = "EventSystem is required for focus" };
            }

            eventSystem.SetSelectedGameObject(targetObject);
            var inputField = targetObject.GetComponent<InputField>();
            if (inputField != null)
            {
                inputField.ActivateInputField();
            }

            return new
            {
                success = true,
                elementPath = elementPath,
                message = $"Successfully focused {targetObject.name}",
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private static object TypeIntoElement(JObject parameters)
        {
            string elementPath = parameters["elementPath"]?.ToString();
            string inputData = parameters["inputData"]?.ToString() ?? parameters["value"]?.ToString();
            if (string.IsNullOrEmpty(elementPath))
            {
                return new { error = "elementPath is required" };
            }

            if (TryParseUiToolkitElementPath(elementPath, out var uiDocumentPath, out var uiElementName))
            {
                return SetUiToolkitElementValue(uiDocumentPath, uiElementName, new JValue(inputData ?? string.Empty), triggerEvents: true);
            }

            if (TryParseImguiElementPath(elementPath, out var controlId))
            {
                return SetImguiElementValue(controlId, new JValue(inputData ?? string.Empty));
            }

            var warnings = new List<string>();
            GameObject targetObject = FindGameObjectByPath(elementPath, includeInactive: true, warnings);
            if (targetObject == null)
            {
                return new { error = $"UI element not found at path: {elementPath}" };
            }

            if (!IsUIElementGameObject(targetObject))
            {
                return new { error = $"GameObject at {elementPath} is not a UI element" };
            }

            var inputField = targetObject.GetComponent<InputField>();
            if (inputField == null)
            {
                return new { error = $"UI element at {elementPath} does not support typing (InputField required)" };
            }

            inputField.ActivateInputField();
            inputField.text = inputData ?? string.Empty;
            inputField.onValueChanged.Invoke(inputField.text);
            inputField.onEndEdit.Invoke(inputField.text);

            return new
            {
                success = true,
                elementPath = elementPath,
                newValue = inputData,
                message = $"Successfully typed into {targetObject.name}",
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private static bool IsUIElementGameObject(GameObject gameObject)
        {
            if (gameObject == null) return false;
            if (gameObject.GetComponent<RectTransform>() == null) return false;
            return gameObject.GetComponentInParent<Canvas>() != null;
        }

        private static bool TryParseClickButton(string clickType, out PointerEventData.InputButton button)
        {
            button = PointerEventData.InputButton.Left;
            if (string.IsNullOrEmpty(clickType)) return true;
            switch (clickType.ToLowerInvariant())
            {
                case "left":
                    button = PointerEventData.InputButton.Left;
                    return true;
                case "right":
                    button = PointerEventData.InputButton.Right;
                    return true;
                case "middle":
                    button = PointerEventData.InputButton.Middle;
                    return true;
                default:
                    return false;
            }
        }

        private static EventSystem EnsureEventSystem(List<string> warnings)
        {
            if (EventSystem.current != null)
            {
                return EventSystem.current;
            }

            try
            {
                var existing = Resources.FindObjectsOfTypeAll<EventSystem>();
                if (existing != null && existing.Length > 0)
                {
                    warnings?.Add("EventSystem.current was null; using an existing EventSystem");
                    return existing[0];
                }
            }
            catch { }

            warnings?.Add("No EventSystem found; created a temporary EventSystem for UI interaction");
            var go = new GameObject("MCP_EventSystem");
            go.hideFlags = HideFlags.HideInHierarchy;
            return go.AddComponent<EventSystem>();
        }

        private static bool TryGetScreenPoint(GameObject target, JObject position, List<string> warnings, out Vector2 screenPoint)
        {
            screenPoint = default;
            if (target == null) return false;

            var rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null) return false;

            float x = 0.5f;
            float y = 0.5f;
            if (position != null)
            {
                if (position["x"] != null && float.TryParse(position["x"].ToString(), out var parsedX))
                {
                    x = parsedX;
                }
                if (position["y"] != null && float.TryParse(position["y"].ToString(), out var parsedY))
                {
                    y = parsedY;
                }

                float clampedX = Mathf.Clamp01(x);
                float clampedY = Mathf.Clamp01(y);
                if (!Mathf.Approximately(clampedX, x) || !Mathf.Approximately(clampedY, y))
                {
                    warnings?.Add("position was out of bounds; clamped to 0-1");
                    x = clampedX;
                    y = clampedY;
                }
            }

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var bottom = Vector3.Lerp(corners[0], corners[3], x);
            var top = Vector3.Lerp(corners[1], corners[2], x);
            var worldPoint = Vector3.Lerp(bottom, top, y);

            var canvas = target.GetComponentInParent<Canvas>();
            Camera cam = canvas != null ? canvas.worldCamera : null;
            screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPoint);
            return true;
        }

        private static GameObject FindGameObjectByPath(string elementPath, bool includeInactive, List<string> warnings)
        {
            if (string.IsNullOrEmpty(elementPath)) return null;

            // Fast path for active objects
            var found = GameObject.Find(elementPath);
            if (found != null) return found;
            if (!includeInactive) return null;

            string trimmed = elementPath.Trim('/');
            if (string.IsNullOrEmpty(trimmed)) return null;

            string[] parts = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                var candidates = roots.Where(r => r != null && r.name == parts[0]).ToList();
                if (candidates.Count > 1)
                {
                    warnings?.Add($"Multiple root GameObjects found with name '{parts[0]}'; using the first match");
                }

                foreach (var root in candidates)
                {
                    Transform current = root.transform;
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var child = current.Find(parts[i]);
                        if (child == null)
                        {
                            current = null;
                            break;
                        }
                        current = child;
                    }

                    if (current != null)
                    {
                        return current.gameObject;
                    }
                }
            }

            return null;
        }

        private static Task DelayMs(int milliseconds)
        {
            if (milliseconds <= 0) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();
            double start = EditorApplication.timeSinceStartup;
            void Tick()
            {
                if (EditorApplication.timeSinceStartup - start >= milliseconds / 1000.0)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }
            EditorApplication.update += Tick;
            return tcs.Task;
        }

        private static bool SetElementValue(GameObject target, JToken value, bool triggerEvents)
        {
            // Handle InputField
            var inputField = target.GetComponent<InputField>();
            if (inputField != null)
            {
                inputField.text = value.ToString();
                if (triggerEvents)
                {
                    inputField.onValueChanged.Invoke(inputField.text);
                    inputField.onEndEdit.Invoke(inputField.text);
                }
                return true;
            }

            // Handle Toggle
            var toggle = target.GetComponent<Toggle>();
            if (toggle != null)
            {
                toggle.isOn = value.ToObject<bool>();
                if (triggerEvents)
                {
                    toggle.onValueChanged.Invoke(toggle.isOn);
                }
                return true;
            }

            // Handle Slider
            var slider = target.GetComponent<Slider>();
            if (slider != null)
            {
                slider.value = value.ToObject<float>();
                if (triggerEvents)
                {
                    slider.onValueChanged.Invoke(slider.value);
                }
                return true;
            }

            // Handle Dropdown
            var dropdown = target.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                dropdown.value = value.ToObject<int>();
                if (triggerEvents)
                {
                    dropdown.onValueChanged.Invoke(dropdown.value);
                }
                return true;
            }

            // Handle Text
            var text = target.GetComponent<Text>();
            if (text != null)
            {
                text.text = value.ToString();
                return true;
            }

            return false;
        }

        private static string ColorToHex(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }

        #endregion
    }
}
