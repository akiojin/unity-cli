using System;
using System.IO;
using System.Net.Sockets;
using NUnit.Framework;
using UnityCliBridge.Models;
using BridgeHost = UnityCliBridge.Core.UnityCliBridge;

namespace UnityCliBridge.Tests.Editor
{
    [TestFixture]
    public class UnityCliBridgeHostConnectionTests
    {
        [Test]
        public void QuitEditor_DoesNotScheduleExitBeforeResponseIsSent()
        {
            var before = UnityEditor.EditorApplication.delayCall;
            try
            {
                var response = global::UnityCliBridge.Core.BridgeCommandRouter.Handle(new Command
                {
                    Id = "quit-response-order", Type = "quit_editor",
                    Parameters = new Newtonsoft.Json.Linq.JObject()
                }).GetAwaiter().GetResult();
                StringAssert.Contains("Unity Editor quitting", response);
                Assert.AreEqual(before, UnityEditor.EditorApplication.delayCall,
                    "Only the transport may schedule exit after flushing the response");
            }
            finally
            {
                UnityEditor.EditorApplication.delayCall = before;
            }
        }

        [SetUp]
        public void SetUp()
        {
            BridgeHost.ClearQueuedCommandsForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            BridgeHost.ClearQueuedCommandsForTesting();
        }

        [Test]
        public void IsExpectedDisconnect_ShouldReturnTrue_ForConnectionResetSocketException()
        {
            var ex = new SocketException((int)SocketError.ConnectionReset);

            Assert.IsTrue(BridgeHost.IsExpectedDisconnect(ex));
        }

        [Test]
        public void IsExpectedDisconnect_ShouldReturnTrue_ForIOExceptionWithConnectionResetInner()
        {
            var socketException = new SocketException((int)SocketError.ConnectionReset);
            var ioException = new IOException("peer reset", socketException);

            Assert.IsTrue(BridgeHost.IsExpectedDisconnect(ioException));
        }

        [Test]
        public void IsExpectedDisconnect_ShouldReturnFalse_ForUnexpectedException()
        {
            var ex = new InvalidOperationException("unexpected");

            Assert.IsFalse(BridgeHost.IsExpectedDisconnect(ex));
        }

        [Test]
        public void RemoveQueuedCommandsForClient_ShouldRemoveOnlyMatchingClientEntries()
        {
            var clientA = new TcpClient();
            var clientB = new TcpClient();

            try
            {
                BridgeHost.EnqueueCommandForTesting(new Command { Id = "a-1", Type = "ping" }, clientA);
                BridgeHost.EnqueueCommandForTesting(new Command { Id = "b-1", Type = "get_editor_info" }, clientB);
                BridgeHost.EnqueueCommandForTesting(new Command { Id = "a-2", Type = "ping" }, clientA);

                Assert.AreEqual(3, BridgeHost.GetQueuedCommandCountForTesting());

                var droppedA = BridgeHost.RemoveQueuedCommandsForClient(clientA);
                Assert.AreEqual(2, droppedA);
                Assert.AreEqual(1, BridgeHost.GetQueuedCommandCountForTesting());

                var droppedASecond = BridgeHost.RemoveQueuedCommandsForClient(clientA);
                Assert.AreEqual(0, droppedASecond);
                Assert.AreEqual(1, BridgeHost.GetQueuedCommandCountForTesting());

                var droppedB = BridgeHost.RemoveQueuedCommandsForClient(clientB);
                Assert.AreEqual(1, droppedB);
                Assert.AreEqual(0, BridgeHost.GetQueuedCommandCountForTesting());
            }
            finally
            {
                clientA.Dispose();
                clientB.Dispose();
            }
        }

        [Test]
        public void TryGetWritableStream_ShouldReturnFalse_ForDisposedClient()
        {
            var client = new TcpClient();
            client.Dispose();

            var success = BridgeHost.TryGetWritableStream(client, out var stream);

            Assert.IsFalse(success);
            Assert.IsNull(stream);
        }

        [Test]
        public void ShouldSkipStartupForProcessForTesting_ShouldSkipBatchWithoutOverride()
        {
            var result = BridgeHost.ShouldSkipStartupForProcessForTesting(
                isBatchMode: true,
                commandLine: "-batchmode -projectPath UnityCliBridge",
                allowBatchHostValue: null);

            Assert.IsTrue(result);
        }

        [Test]
        public void ShouldSkipStartupForProcessForTesting_ShouldAllowBatchWithOverride()
        {
            var result = BridgeHost.ShouldSkipStartupForProcessForTesting(
                isBatchMode: true,
                commandLine: "-batchmode -projectPath UnityCliBridge",
                allowBatchHostValue: "1");

            Assert.IsFalse(result);
        }

        [Test]
        public void ShouldSkipStartupForProcessForTesting_ShouldStillSkipRunTestsEvenWithOverride()
        {
            var result = BridgeHost.ShouldSkipStartupForProcessForTesting(
                isBatchMode: true,
                commandLine: "-batchmode -runTests -projectPath UnityCliBridge",
                allowBatchHostValue: "true");

            Assert.IsTrue(result);
        }

        [Test]
        public void ResolveHostAndPortForTesting_ShouldPreferEnvironmentValues()
        {
            var result = BridgeHost.ResolveHostAndPortForTesting(
                configuredHost: "localhost",
                configuredPort: 6400,
                envHostValue: "127.0.0.1",
                envPortValue: "6500",
                envPortOverrideValue: "6600");

            Assert.AreEqual("127.0.0.1", result.host);
            Assert.AreEqual(6600, result.port);
        }

        [Test]
        public void ResolveHostAndPortForTesting_ShouldFallbackToConfiguredValuesWhenEnvInvalid()
        {
            var result = BridgeHost.ResolveHostAndPortForTesting(
                configuredHost: "localhost",
                configuredPort: 6400,
                envHostValue: "",
                envPortValue: "invalid",
                envPortOverrideValue: null);

            Assert.AreEqual("localhost", result.host);
            Assert.AreEqual(6400, result.port);
        }
    }
}
