using System.Diagnostics;
using System.IO;
using System.Net;
using NUnit.Framework;
using UnityCliBridge.Core;
using BridgeHost = UnityCliBridge.Core.UnityCliBridge;

namespace UnityCliBridge.Tests.Editor.Core
{
    [TestFixture]
    public class EditorLockfileTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "unity-cli-lockfile-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ResolveDirectory_UsesOverrideOrUserProfile()
        {
            Assert.AreEqual("/custom/editors", EditorLockfile.ResolveDirectory(" /custom/editors ", "/Users/me"));
            Assert.AreEqual(
                Path.Combine("/Users/me", ".unity-cli", "editors"),
                EditorLockfile.ResolveDirectory(null, "/Users/me"));
        }

        [Test]
        public void ConnectHostFor_MapsWildcardAndLoopbackToLoopbackLiteral()
        {
            Assert.AreEqual("127.0.0.1", EditorLockfile.ConnectHostFor(IPAddress.Any));
            Assert.AreEqual("127.0.0.1", EditorLockfile.ConnectHostFor(IPAddress.Loopback));
            Assert.AreEqual("::1", EditorLockfile.ConnectHostFor(IPAddress.IPv6Loopback));
            Assert.AreEqual("192.168.1.5", EditorLockfile.ConnectHostFor(IPAddress.Parse("192.168.1.5")));
        }

        [Test]
        public void BuildContent_ContainsDiscoveryFields()
        {
            var content = EditorLockfile.BuildContent(
                42, "/work/ProjectA", "127.0.0.1", 6401, 6400, "6000.3.25f1", "0.15.3", "ready", 10, 20);

            Assert.AreEqual(EditorLockfile.SchemaVersion, content.Value<int>("schemaVersion"));
            Assert.AreEqual(42, content.Value<int>("pid"));
            Assert.AreEqual("/work/ProjectA", content.Value<string>("projectPath"));
            Assert.AreEqual(6401, content.Value<int>("port"));
            Assert.AreEqual(6400, content.Value<int>("configuredPort"));
            Assert.AreEqual("ready", content.Value<string>("state"));
            Assert.AreEqual(20d, content.Value<double>("heartbeatAt"));
        }

        [Test]
        public void RemoveStaleLockfiles_DeletesOnlyDeadEditorsOfSameProject()
        {
            var deadPid = DeadPid();
            var livePid = Process.GetCurrentProcess().Id;
            Write("dead-same.json", deadPid, "/work/ProjectA");
            Write("dead-other.json", deadPid, "/work/ProjectB");
            Write("live-same.json", livePid, "/work/ProjectA");

            var removed = EditorLockfile.RemoveStaleLockfiles(directory, "/work/ProjectA", currentPid: -1);

            Assert.AreEqual(1, removed);
            Assert.IsFalse(File.Exists(Path.Combine(directory, "dead-same.json")));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "dead-other.json")), "other projects stay listed as unreachable");
            Assert.IsTrue(File.Exists(Path.Combine(directory, "live-same.json")));
        }

        [Test]
        public void BuildPortCandidates_FallsBackToFollowingPorts()
        {
            var candidates = BridgeHost.BuildPortCandidates(6400, 0, 0);

            Assert.AreEqual(6400, candidates[0]);
            Assert.AreEqual(6401, candidates[1]);
            Assert.AreEqual(BridgeHost.PortFallbackRange + 1, candidates.Count);
        }

        [Test]
        public void BuildPortCandidates_PrefersPortBoundBeforeReloadForSameConfiguration()
        {
            Assert.AreEqual(6403, BridgeHost.BuildPortCandidates(6400, 6403, 6400)[0]);
            Assert.AreEqual(7000, BridgeHost.BuildPortCandidates(7000, 6403, 6400)[0], "a changed configured port wins");
            CollectionAssert.AreEqual(new[] { 0 }, BridgeHost.BuildPortCandidates(0, 6403, 0));
        }

        [Test]
        public void BuildPortCandidates_StopsAtHighestPort()
        {
            var candidates = BridgeHost.BuildPortCandidates(65530, 0, 0);

            Assert.AreEqual(65535, candidates[candidates.Count - 1]);
            Assert.AreEqual(6, candidates.Count);
        }

        private void Write(string name, int pid, string projectPath)
        {
            var content = EditorLockfile.BuildContent(pid, projectPath, "127.0.0.1", 6400, 6400, "x", "y", "ready", 0, 0);
            EditorLockfile.WritePrivateFile(Path.Combine(directory, name), content.ToString());
        }

        private static int DeadPid()
        {
            var windows = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor;
            var process = Process.Start(new ProcessStartInfo(windows ? "cmd.exe" : "/usr/bin/true", windows ? "/c exit" : string.Empty)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process.WaitForExit();
            var pid = process.Id;
            process.Dispose();
            return pid;
        }
    }
}
