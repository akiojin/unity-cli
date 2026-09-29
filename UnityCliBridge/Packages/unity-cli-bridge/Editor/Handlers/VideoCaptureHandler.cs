using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    // Keep Recorder (and its transitive Timeline dependency) optional for the bridge.
    public static class VideoCaptureHandler
    {
        public static object Start(JObject parameters) => Invoke("Start", parameters);
        public static object Stop(JObject parameters) => Invoke("Stop", parameters);
        public static object Status(JObject parameters) => Invoke("Status", parameters);

        private static object Invoke(string method, JObject parameters)
        {
            var backend = Type.GetType("UnityCliBridge.Recorder.VideoCaptureBackend, UnityCliBridge.Recorder.Editor");
            if (backend == null)
                return new { error = "Install com.unity.recorder to use video capture tools.", code = "RECORDER_PACKAGE_MISSING" };
            try
            {
                return backend.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { parameters ?? new JObject() });
            }
            catch (Exception exception)
            {
                var cause = (exception as TargetInvocationException)?.InnerException ?? exception;
                return new { error = cause.Message, code = "RECORDER_ERROR" };
            }
        }
    }
}
