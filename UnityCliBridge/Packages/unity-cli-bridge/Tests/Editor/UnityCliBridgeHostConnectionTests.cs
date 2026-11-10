using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NUnit.Framework;
using UnityCliBridge.Models;
using BridgeHost = UnityCliBridge.Core.UnityCliBridge;

namespace UnityCliBridge.Tests.Editor
{
    [TestFixture]
    public class UnityCliBridgeHostConnectionTests
    {
        private sealed class PausedContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
            public override void Post(SendOrPostCallback callback, object state)
                => callbacks.Enqueue(() => callback(state));
            public void Drain()
            {
                while (callbacks.TryDequeue(out var callback)) callback();
            }
        }

        private sealed class CommandQueueScope : IDisposable
        {
            private static FieldInfo Field(string name) => typeof(BridgeHost)
                .GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object sync = Field("queueLock").GetValue(null);
            private readonly FieldInfo pending = Field("pendingCommand");
            private readonly FieldInfo exiting = Field("exitRequested");
            private readonly FieldInfo processing = Field("isProcessingCommand");
            private readonly (Command, TcpClient, DateTime)[] savedCommands;
            private readonly Task savedPending;
            private readonly int savedExit;
            private readonly bool savedProcessing;
            public readonly Queue<(Command, TcpClient, DateTime)> Commands;
            public Task Pending { get => (Task)pending.GetValue(null); set => pending.SetValue(null, value); }
            public int ExitRequested { get => (int)exiting.GetValue(null); set => exiting.SetValue(null, value); }

            public CommandQueueScope()
            {
                Monitor.Enter(sync);
                Commands = (Queue<(Command, TcpClient, DateTime)>)Field("commandQueue").GetValue(null);
                savedCommands = Commands.ToArray();
                savedPending = Pending;
                savedExit = ExitRequested;
                savedProcessing = (bool)processing.GetValue(null);
                Commands.Clear();
                Pending = null;
                ExitRequested = 0;
                processing.SetValue(null, false);
            }

            public void Pump(Action exit, Func<Command, TcpClient, DateTime, Task> process)
                => typeof(BridgeHost).GetMethod("ProcessCommandQueueCore",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { exit, process });

            public void Dispose()
            {
                Commands.Clear();
                foreach (var command in savedCommands) Commands.Enqueue(command);
                Pending = savedPending;
                ExitRequested = savedExit;
                processing.SetValue(null, savedProcessing);
                Monitor.Exit(sync);
            }
        }

        [Test]
        public void QuitEditor_ExitsBeforeAnUnrelatedPendingCommandCompletes()
        {
            using var scope = new CommandQueueScope();
            var neverCompletes = new TaskCompletionSource<bool>();
            scope.Pending = neverCompletes.Task;
            scope.ExitRequested = 1;
            var exits = 0;
            scope.Pump(() => exits++, (command, client, time) => Task.CompletedTask);
            Assert.AreEqual(1, exits, "A flushed quit must not wait for unrelated pending work");
            Assert.IsFalse(neverCompletes.Task.IsCompleted);
            scope.Pump(() => exits++, (command, client, time) => Task.CompletedTask);
            Assert.AreEqual(1, exits, "Consume the exit request exactly once");
        }

        [Test]
        public void QuitEditor_StopsDrainingCommandsAfterSuccess()
        {
            using var scope = new CommandQueueScope();
            scope.Commands.Enqueue((new Command { Type = "quit_editor" }, null, DateTime.UtcNow));
            scope.Commands.Enqueue((new Command { Type = "get_editor_state" }, null, DateTime.UtcNow));
            var neverCompletes = new TaskCompletionSource<bool>();
            var processed = 0;
            var exits = 0;
            Task Process(Command command, TcpClient client, DateTime time)
            {
                processed++;
                if (command.Type != "quit_editor") return neverCompletes.Task;
                scope.ExitRequested = 1;
                return Task.CompletedTask;
            }
            scope.Pump(() => exits++, Process);
            Assert.AreEqual(1, processed, "Do not start another handler after quit succeeds");
            Assert.AreEqual(1, scope.Commands.Count);
            scope.Pump(() => exits++, Process);
            Assert.AreEqual(1, exits);
            Assert.AreEqual(1, processed);
        }

        [Test]
        public void CommandResponse_CompletesWithoutPumpingPausedUnityContext()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var context = new PausedContext();
            var previous = SynchronizationContext.Current;
            try
            {
                using var receiver = new TcpClient();
                receiver.Connect((IPEndPoint)listener.LocalEndpoint);
                using var sender = listener.AcceptTcpClient();
                var stream = sender.GetStream();
                var gates = (ConditionalWeakTable<NetworkStream, SemaphoreSlim>)typeof(BridgeHost)
                    .GetField("SendGates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var gate = gates.GetValue(stream, _ => new SemaphoreSlim(1, 1));
                gate.Wait();
                Task pending;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    pending = (Task)typeof(BridgeHost).GetMethod("ProcessCommandInternal",
                        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] {
                        new Command { Id = "paused-context", Type = "get_editor_state",
                            Parameters = new Newtonsoft.Json.Linq.JObject() }, sender, DateTime.UtcNow });
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                    gate.Release();
                }
                Assert.IsTrue(pending.Wait(5000), "Sending must not need a Unity context update while paused");
                var header = new BinaryReader(receiver.GetStream()).ReadBytes(4);
                Assert.AreEqual(4, header.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(header);
                Assert.Greater(BitConverter.ToInt32(header, 0), 0);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                context.Drain();
                listener.Stop();
            }
        }

        [Test]
        public void QuitEditor_QueuesMainThreadExitOnlyAfterResponseIsSent()
        {
            var exitRequested = typeof(BridgeHost).GetField("exitRequested",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(exitRequested, "Exit must be queued for the main-thread update loop");
            var previous = (int)exitRequested.GetValue(null);
            var before = UnityEditor.EditorApplication.delayCall;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task pending = null;
            SemaphoreSlim gate = null;
            var gateHeld = false;
            try
            {
                exitRequested.SetValue(null, 0);
                using var receiver = new TcpClient();
                receiver.Connect((IPEndPoint)listener.LocalEndpoint);
                using var sender = listener.AcceptTcpClient();
                var gates = (ConditionalWeakTable<NetworkStream, SemaphoreSlim>)typeof(BridgeHost)
                    .GetField("SendGates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                gate = gates.GetValue(sender.GetStream(), _ => new SemaphoreSlim(1, 1));
                gate.Wait();
                gateHeld = true;
                pending = (Task)typeof(BridgeHost).GetMethod("ProcessCommandInternal",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] {
                    new Command { Id = "quit-flush-order", Type = "quit_editor",
                        Parameters = new Newtonsoft.Json.Linq.JObject { ["force"] = true } },
                    sender, DateTime.UtcNow });
                Assert.IsFalse(pending.IsCompleted, "The response write is blocked");
                Assert.AreEqual(0, exitRequested.GetValue(null), "Never exit before flushing success");
                gate.Release();
                gateHeld = false;
                Assert.IsTrue(pending.Wait(5000), "Sending must not need a Unity context update");
                var reader = new BinaryReader(receiver.GetStream());
                var header = reader.ReadBytes(4);
                Assert.AreEqual(4, header.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(header);
                var body = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(BitConverter.ToInt32(header, 0)));
                StringAssert.Contains("Unity Editor quitting", body);
                Assert.AreEqual(1, exitRequested.GetValue(null));
                Assert.AreEqual(before, UnityEditor.EditorApplication.delayCall,
                    "Background Editors must not depend on an inspector delayCall");
            }
            finally
            {
                if (gateHeld) gate.Release();
                pending?.Wait(5000);
                exitRequested.SetValue(null, previous);
                listener.Stop();
            }
        }

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

        [Test]
        public void QuitEditor_RefusesDirtySceneUnlessForceIsExplicit()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            var before = UnityEditor.EditorApplication.delayCall;
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                foreach (var force in new[] { false, true })
                {
                    var response = Newtonsoft.Json.Linq.JObject.Parse(
                        global::UnityCliBridge.Core.BridgeCommandRouter.Handle(new Command
                        {
                            Id = "quit-dirty", Type = "quit_editor",
                            Parameters = new Newtonsoft.Json.Linq.JObject { ["force"] = force }
                        }).GetAwaiter().GetResult());
                    Assert.AreEqual(force ? "success" : "error", response["status"]?.ToString());
                    if (!force)
                    {
                        Assert.AreEqual("UNSAVED_SCENES", response["code"]?.ToString());
                        Assert.IsNotEmpty(response["details"]?["scenes"] as Newtonsoft.Json.Linq.JArray);
                        Assert.IsTrue(scene.isDirty, "Refusal must not clear unsaved changes");
                    }
                    Assert.AreEqual(before, UnityEditor.EditorApplication.delayCall,
                        "The router must never schedule exit before the response is sent");
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
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
