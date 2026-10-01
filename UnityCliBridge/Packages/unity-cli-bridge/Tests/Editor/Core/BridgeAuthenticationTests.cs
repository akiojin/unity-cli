using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityCliBridge.Core;
using BridgeHost = UnityCliBridge.Core.UnityCliBridge;

namespace UnityCliBridge.Tests.Editor.Core
{
    public class BridgeAuthenticationTests
    {
        [TestCase(null, null, false)]
        [TestCase(null, "0", false)]
        [TestCase(null, "true", false)]
        [TestCase(null, " 1", false)]
        [TestCase(null, "1", true)]
        [TestCase("wrong", "1", false)]
        [TestCase("wrong", null, false)]
        public void Authentication_OnlyExactOptOutAcceptsMissingToken(string token, string optOut, bool expected)
        {
            Assert.AreEqual(expected, BridgeAuthentication.IsAuthorized(token, optOut));
        }

        [Test]
        public void Authentication_AcceptsCorrectTokenAndRejectsSameLengthMismatch()
        {
            Assert.IsTrue(BridgeAuthentication.IsAuthorized(BridgeAuthentication.Token, null));
            var token = BridgeAuthentication.Token;
            var replacement = token[0] == 'a' ? 'b' : 'a';
            Assert.IsFalse(BridgeAuthentication.IsAuthorized(replacement + token.Substring(1), null));
        }

        [Test]
        public void PrivateFile_InitialAndReplacementPermissionsAre0600()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) Assert.Ignore("POSIX permissions test");
            var path = Path.Combine(Path.GetTempPath(), "bridge-auth-" + Guid.NewGuid().ToString("N"));
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    EditorLockfile.WritePrivateFile(path, "private-token-" + i);
                    using var stat = Process.Start(new ProcessStartInfo("/usr/bin/stat",
                        (Application.platform == RuntimePlatform.OSXEditor ? "-f %Lp " : "-c %a ") + path)
                    { RedirectStandardOutput = true, UseShellExecute = false });
                    Assert.AreEqual("600", stat.StandardOutput.ReadToEnd().Trim());
                    stat.WaitForExit();
                    Assert.AreEqual(0, stat.ExitCode);
                    Assert.AreEqual("private-token-" + i, File.ReadAllText(path));
                    // Upgrading a legacy 0644 discovery file must not preserve its mode.
                    if (i == 0)
                    {
                        using var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", "644 " + path)
                        { UseShellExecute = false });
                        chmod.WaitForExit();
                        Assert.AreEqual(0, chmod.ExitCode);
                    }
                }
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void SettingsException_ResetsPreviousWildcardToLoopback()
        {
            var field = typeof(BridgeHost).GetField("bindAddress", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                field.SetValue(null, IPAddress.Any);
                LogAssert.Expect(LogType.Warning, new Regex("Project Settings load error: injected"));
                BridgeHost.ApplyProjectSettings(() => throw new InvalidOperationException("injected"));
                Assert.AreEqual(IPAddress.Loopback, field.GetValue(null));
            }
            finally { BridgeHost.Restart(); }
        }

        [Test]
        public void WildcardListener_WarnsExactlyOnce()
        {
            var warningCount = 0;
            Application.LogCallback countWarning = (message, stack, type) =>
            {
                if (type == LogType.Warning && message.Contains("TCP listener is bound to non-loopback"))
                    warningCount++;
            };
            Application.logMessageReceived += countWarning;
            try
            {
                typeof(BridgeHost).GetField("bindAddress", BindingFlags.NonPublic | BindingFlags.Static)
                    .SetValue(null, IPAddress.Any);
                typeof(BridgeHost).GetField("currentPort", BindingFlags.NonPublic | BindingFlags.Static)
                    .SetValue(null, 0);
                LogAssert.Expect(LogType.Warning, new Regex("TCP listener is bound to non-loopback address 0.0.0.0"));
                typeof(BridgeHost).GetMethod("StartTcpListenerOnCurrentEndpoint", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { false });
                Assert.AreEqual(1, warningCount);
            }
            finally
            {
                Application.logMessageReceived -= countWarning;
                BridgeHost.Restart();
            }
        }

        [Test]
        public void DiscoveryContent_HasPerInstance256BitToken()
        {
            var content = EditorLockfile.BuildContent(1, "/test", "127.0.0.1", 6400,
                6400, "version", "bridge", "ready", 1, 1);
            var token = content.Value<string>("authToken");
            Assert.IsNotNull(token, "Discovery must supply credentials to CLI and unityd");
            Assert.AreEqual(32, Convert.FromBase64String(token).Length);
        }

        [Test]
        public void LegacyPing_RequiresAuthentication()
        {
            var previous = Environment.GetEnvironmentVariable("UNITY_CLI_ALLOW_UNAUTHENTICATED");
            Environment.SetEnvironmentVariable("UNITY_CLI_ALLOW_UNAUTHENTICATED", null);
            try
            {
                var port = BridgeHost.StartOnEphemeralLoopbackPortForTesting();
                using var socket = new TcpClient();
                socket.Connect("127.0.0.1", port);
                socket.ReceiveTimeout = 5000;
                var stream = socket.GetStream();
                var payload = Encoding.UTF8.GetBytes("ping");
                var header = BitConverter.GetBytes(payload.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(header);
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
                var reader = new BinaryReader(stream);
                header = reader.ReadBytes(4);
                if (BitConverter.IsLittleEndian) Array.Reverse(header);
                var response = JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes(BitConverter.ToInt32(header, 0))));
                Assert.AreEqual("UNAUTHORIZED", response.Value<string>("code"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UNITY_CLI_ALLOW_UNAUTHENTICATED", previous);
                BridgeHost.Restart();
            }
        }
    }
}
