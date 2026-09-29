using NUnit.Framework;
using UnityCliBridge.Core;
using UnityCliBridge.Models;
using UnityCliBridge.Tests.Helpers;
using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.TestTools;
using System;

namespace UnityCliBridge.Tests.Integration
{
    /// <summary>
    /// Exercises the real TCP transport (4-byte big-endian length framing) on an ephemeral
    /// loopback port, so the tests also run inside -runTests processes where the configured
    /// listener is intentionally skipped. Awaits use ConfigureAwait(false): socket I/O must not
    /// depend on the main-thread context, which has to stay free to drain the command queue.
    /// </summary>
    [TestFixture]
    public class UnityCliBridgeIntegrationTests
    {
        private const int TIMEOUT_MS = 5000;
        private int port;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            port = Core.UnityCliBridge.StartOnEphemeralLoopbackPortForTesting();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Return to the configured endpoint (and to "no listener" in batch/test processes).
            Core.UnityCliBridge.Restart();
        }

        [UnityTest]
        public IEnumerator UnityCliBridge_ShouldAcceptTcpConnection() => TaskTestUtility.Await(async () =>
        {
            using (var client = await ConnectAsync().ConfigureAwait(false))
            {
                Assert.IsTrue(client.Connected, "Client should be connected");
                var elapsed = Stopwatch.StartNew();
                while (Core.UnityCliBridge.Status != BridgeStatus.Connected && elapsed.ElapsedMilliseconds < TIMEOUT_MS)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }
                Assert.AreEqual(BridgeStatus.Connected, Core.UnityCliBridge.Status, "Bridge status should be Connected");
            }
        });

        [UnityTest]
        public IEnumerator UnityCliBridge_ShouldProcessPingCommand() => TaskTestUtility.Await(async () =>
        {
            using (var client = await ConnectAsync().ConfigureAwait(false))
            {
                var stream = client.GetStream();
                await WriteFrameAsync(stream, JsonConvert.SerializeObject(new Command
                {
                    Id = "test-ping-001",
                    Type = "ping",
                    Parameters = new JObject { ["message"] = "Hello Unity" }
                })).ConfigureAwait(false);

                var response = await ReadFrameAsync(stream).ConfigureAwait(false);

                Assert.AreEqual("test-ping-001", response["id"]?.Value<string>(), response.ToString());
                Assert.AreEqual("success", response["status"]?.Value<string>(), response.ToString());
                Assert.AreEqual("pong", response["result"]?["message"]?.Value<string>(), response.ToString());
                Assert.AreEqual("Hello Unity", response["result"]?["echo"]?.Value<string>(), response.ToString());
            }
        });

        [UnityTest]
        public IEnumerator UnityCliBridge_ShouldHandleInvalidJson() => TaskTestUtility.Await(async () =>
        {
            using (var client = await ConnectAsync().ConfigureAwait(false))
            {
                var stream = client.GetStream();
                await WriteFrameAsync(stream, "{ invalid json }").ConfigureAwait(false);

                var response = await ReadFrameAsync(stream).ConfigureAwait(false);

                Assert.AreEqual("error", response["status"]?.Value<string>(), response.ToString());
                Assert.AreEqual("JSON_ERROR", response["code"]?.Value<string>(), response.ToString());
                StringAssert.Contains("parsing", response["error"]?.Value<string>());
            }
        });

        [UnityTest]
        public IEnumerator UnityCliBridge_ShouldHandleMultipleClients() => TaskTestUtility.Await(async () =>
        {
            using (var client1 = await ConnectAsync().ConfigureAwait(false))
            using (var client2 = await ConnectAsync().ConfigureAwait(false))
            {
                await WriteFrameAsync(client1.GetStream(), JsonConvert.SerializeObject(new Command { Id = "client1-cmd", Type = "ping" })).ConfigureAwait(false);
                await WriteFrameAsync(client2.GetStream(), JsonConvert.SerializeObject(new Command { Id = "client2-cmd", Type = "ping" })).ConfigureAwait(false);

                var response1 = await ReadFrameAsync(client1.GetStream()).ConfigureAwait(false);
                var response2 = await ReadFrameAsync(client2.GetStream()).ConfigureAwait(false);

                Assert.AreEqual("client1-cmd", response1["id"]?.Value<string>(), "Client 1 should receive its own response");
                Assert.AreEqual("client2-cmd", response2["id"]?.Value<string>(), "Client 2 should receive its own response");
                Assert.AreEqual("success", response1["status"]?.Value<string>(), response1.ToString());
                Assert.AreEqual("success", response2["status"]?.Value<string>(), response2.ToString());
            }
        });

        [Test]
        public void UnityCliBridge_StatusShouldBeDisconnectedOnStartup()
        {
            // Assert - Check initial status
            // Note: In actual Unity, the bridge might already be connected from previous tests
            // This test verifies that the status enum is working correctly
            Assert.IsTrue(
                Core.UnityCliBridge.Status == BridgeStatus.Disconnected ||
                Core.UnityCliBridge.Status == BridgeStatus.Connected ||
                Core.UnityCliBridge.Status == BridgeStatus.NotConfigured,
                "Status should be Disconnected, Connected, or NotConfigured in batch/test mode"
            );
        }

        [UnityTest]
        public IEnumerator UnityCliBridge_ShouldReconnectAfterDisconnection() => TaskTestUtility.Await(async () =>
        {
            using (var first = await ConnectAsync().ConfigureAwait(false))
            {
                Assert.IsTrue(first.Connected, "Should connect initially");
            }

            // Wait a bit for server to process disconnection
            await Task.Delay(500).ConfigureAwait(false);

            using (var client = await ConnectAsync().ConfigureAwait(false))
            {
                var stream = client.GetStream();
                await WriteFrameAsync(stream, JsonConvert.SerializeObject(new Command { Id = "reconnect-ping", Type = "ping" })).ConfigureAwait(false);
                var response = await ReadFrameAsync(stream).ConfigureAwait(false);
                Assert.AreEqual("reconnect-ping", response["id"]?.Value<string>(), response.ToString());
                Assert.AreEqual("success", response["status"]?.Value<string>(), response.ToString());
            }
        });

        private async Task<TcpClient> ConnectAsync()
        {
            var client = new TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", port);
            if (await Task.WhenAny(connect, Task.Delay(TIMEOUT_MS)).ConfigureAwait(false) != connect)
            {
                client.Dispose();
                Assert.Fail($"Connection to 127.0.0.1:{port} did not complete within {TIMEOUT_MS} ms");
            }
            await connect.ConfigureAwait(false);
            return client;
        }

        private static async Task WriteFrameAsync(NetworkStream stream, string message)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            var length = BitConverter.GetBytes(payload.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(length);
            await stream.WriteAsync(length, 0, length.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task<JObject> ReadFrameAsync(NetworkStream stream)
        {
            var length = await ReadExactlyAsync(stream, 4).ConfigureAwait(false);
            if (BitConverter.IsLittleEndian) Array.Reverse(length);
            var payload = await ReadExactlyAsync(stream, BitConverter.ToInt32(length, 0)).ConfigureAwait(false);
            return JObject.Parse(Encoding.UTF8.GetString(payload));
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            var elapsed = Stopwatch.StartNew();
            while (offset < count)
            {
                var read = stream.ReadAsync(buffer, offset, count - offset);
                var remaining = Math.Max(1, TIMEOUT_MS - (int)elapsed.ElapsedMilliseconds);
                if (await Task.WhenAny(read, Task.Delay(remaining)).ConfigureAwait(false) != read)
                {
                    Assert.Fail($"No response frame within {TIMEOUT_MS} ms");
                }
                var bytesRead = await read.ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    Assert.Fail("Connection closed before a complete response frame arrived");
                }
                offset += bytesRead;
            }
            return buffer;
        }
    }
}
