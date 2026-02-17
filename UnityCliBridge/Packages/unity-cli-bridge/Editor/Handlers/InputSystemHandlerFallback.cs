#if UNITY_EDITOR && !ENABLE_INPUT_SYSTEM
using System;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Fallback handler when Input System is not available
    /// </summary>
    public static class InputSystemHandler
    {
        public static object SimulateKeyboardInput(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }

        public static object SimulateMouseInput(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }

        public static object SimulateGamepadInput(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }

        public static object SimulateTouchInput(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }

        public static object CreateInputSequence(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }

        public static object GetCurrentInputState(JObject parameters)
        {
            return new
            {
                error = "Unity Input System package is not installed. Please install it via Package Manager to use input simulation features.",
                code = "INPUT_SYSTEM_NOT_AVAILABLE"
            };
        }
    }
}
#endif