using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    // Keep the always-loaded bridge independent of the optional Timeline assembly.
    public static class TimelineHandler
    {
        public static object GetTimeline(JObject parameters) => Invoke("GetTimeline", parameters);
        public static object ManageTimeline(JObject parameters) => Invoke("ManageTimeline", parameters);

        private static object Invoke(string method, JObject parameters)
        {
            var backend = Type.GetType("UnityCliBridge.Timeline.TimelineBackend, UnityCliBridge.Timeline.Editor");
            if (backend == null)
                return new { error = "Install com.unity.timeline to use Timeline tools.", code = "TIMELINE_PACKAGE_MISSING" };
            try
            {
                return backend.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { parameters ?? new JObject() });
            }
            catch (Exception exception)
            {
                var cause = (exception as TargetInvocationException)?.InnerException ?? exception;
                return new { error = cause.Message, code = "TIMELINE_ERROR" };
            }
        }
    }
}
