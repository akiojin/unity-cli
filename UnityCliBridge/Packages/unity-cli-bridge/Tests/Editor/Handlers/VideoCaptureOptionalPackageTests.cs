using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor.Handlers
{
    public class VideoCaptureOptionalPackageTests
    {
        [Test]
        public void AlwaysLoadedBridge_DoesNotReferenceRecorder()
        {
            var references = typeof(VideoCaptureHandler).Assembly.GetReferencedAssemblies();
            Assert.IsFalse(references.Any(reference => reference.Name.StartsWith("Unity.Recorder", StringComparison.Ordinal)),
                "Recorder must be optional because it transitively installs Timeline.");
        }

        [Test]
        public void MissingRecorder_ReturnsDedicatedErrorForAllOperations()
        {
            if (Type.GetType("UnityEditor.Recorder.RecorderController, Unity.Recorder.Editor") != null)
                Assert.Ignore("Run in the package-absent project.");
            foreach (var result in new[] { VideoCaptureHandler.Start(new JObject()),
                VideoCaptureHandler.Stop(new JObject()), VideoCaptureHandler.Status(new JObject()) })
                Assert.AreEqual("RECORDER_PACKAGE_MISSING", JObject.FromObject(result)["code"]?.Value<string>());
        }

        [Test]
        public void InstalledRecorder_PreservesStatusAndValidation()
        {
            if (Type.GetType("UnityEditor.Recorder.RecorderController, Unity.Recorder.Editor") == null)
                Assert.Ignore("Recorder package required.");
            var status = JObject.FromObject(VideoCaptureHandler.Status(new JObject()));
            Assert.IsNull(status["error"], status.ToString());
            Assert.IsFalse(status["isRecording"].Value<bool>());
            var invalid = JObject.FromObject(VideoCaptureHandler.Start(new JObject { ["captureMode"] = "invalid" }));
            Assert.AreEqual("E_INVALID_MODE", invalid["code"]?.Value<string>());
        }
    }
}
