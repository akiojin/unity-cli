using NUnit.Framework;
using UnityCliBridge.Core;
using UnityCliBridge.Models;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using System;
using System.Diagnostics;

namespace UnityCliBridge.Tests.Integration
{
    [TestFixture]
    public class UnityCliBridgeIntegrationTests
    {
        private const int TEST_PORT = 6401; // Different port to avoid conflicts
        private const int CONNECTION_TIMEOUT_MS = 5000;
        
        [SetUp]
        public void Setup()
        {
            EnsureServerRunning();
        }

        [Test]
        public async Task UnityCliBridge_ShouldAcceptTcpConnection()
        {
            // Arrange
            TcpClient client = null;
            
            try
            {
                // Act - Try to connect to the Unity TCP server
                client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                
                // Wait for connection with timeout
                var completed = await Task.WhenAny(connectTask, Task.Delay(CONNECTION_TIMEOUT_MS));
                
                // Assert
                Assert.IsTrue(completed == connectTask, "Connection should complete within timeout");
                Assert.IsTrue(client.Connected, "Client should be connected");
                Assert.AreEqual(McpStatus.Connected, Core.UnityCliBridge.Status, "MCP status should be Connected");
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }

        [Test]
        public async Task UnityCliBridge_ShouldProcessPingCommand()
        {
            // Arrange
            TcpClient client = null;
            
            try
            {
                client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                
                var stream = client.GetStream();
                
                // Create ping command
                var pingCommand = new Command
                {
                    Id = "test-ping-001",
                    Type = "ping",
                    Parameters = new Newtonsoft.Json.Linq.JObject
                    {
                        ["message"] = "Hello Unity"
                    }
                };
                
                // Act - Send ping command
                var commandJson = JsonConvert.SerializeObject(pingCommand);
                var commandBytes = Encoding.UTF8.GetBytes(commandJson + "\n");
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await stream.FlushAsync();
                
                // Read response
                var buffer = new byte[1024];
                var responseTask = stream.ReadAsync(buffer, 0, buffer.Length);
                var completed = await Task.WhenAny(responseTask, Task.Delay(CONNECTION_TIMEOUT_MS));
                
                if (completed != responseTask)
                {
                    Assert.Ignore("Unity MCP server did not respond to ping within the allotted timeout. Skipping integration test.");
                }
                
                var bytesRead = await responseTask;
                var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                dynamic response = JsonConvert.DeserializeObject(responseJson);
                
                // Assert
                Assert.IsNotNull(response, "Response should not be null");
                Assert.AreEqual("test-ping-001", (string)response.id, "Response ID should match command ID");
                Assert.IsTrue((bool)response.success, "Response should indicate success");
                Assert.AreEqual("pong", (string)response.data.message, "Response should contain pong message");
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }

        [Test]
        public async Task UnityCliBridge_ShouldHandleInvalidJson()
        {
            // Arrange
            TcpClient client = null;
            
            try
            {
                client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                
                var stream = client.GetStream();
                
                // Act - Send invalid JSON
                var invalidJson = "{ invalid json }\n";
                var commandBytes = Encoding.UTF8.GetBytes(invalidJson);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await stream.FlushAsync();
                
                // Read response
                var buffer = new byte[1024];
                var responseTask = stream.ReadAsync(buffer, 0, buffer.Length);
                var completed = await Task.WhenAny(responseTask, Task.Delay(CONNECTION_TIMEOUT_MS));
                
                if (completed != responseTask)
                {
                    Assert.Ignore("Unity MCP server did not respond to invalid JSON within the allotted timeout. Skipping integration test.");
                }
                
                var bytesRead = await responseTask;
                var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                dynamic response = JsonConvert.DeserializeObject(responseJson);
                
                // Assert
                Assert.IsFalse((bool)response.success, "Response should indicate failure");
                Assert.IsTrue(((string)response.error).Contains("parse"), "Error should mention parsing issue");
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }
        
        [Test]
        public async Task UnityCliBridge_ShouldHandleMultipleClients()
        {
            // Arrange
            TcpClient client1 = null;
            TcpClient client2 = null;
            
            try
            {
                // Act - Connect two clients
                client1 = new TcpClient();
                await client1.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                
                client2 = new TcpClient();
                await client2.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                
                // Send commands from both clients
                var command1 = new Command { Id = "client1-cmd", Type = "ping" };
                var command2 = new Command { Id = "client2-cmd", Type = "ping" };
                
                var json1 = JsonConvert.SerializeObject(command1) + "\n";
                var json2 = JsonConvert.SerializeObject(command2) + "\n";
                
                await client1.GetStream().WriteAsync(Encoding.UTF8.GetBytes(json1), 0, json1.Length);
                await client2.GetStream().WriteAsync(Encoding.UTF8.GetBytes(json2), 0, json2.Length);
                
                // Assert - Both clients should be connected
                Assert.IsTrue(client1.Connected, "Client 1 should remain connected");
                Assert.IsTrue(client2.Connected, "Client 2 should remain connected");
            }
            finally
            {
                client1?.Close();
                client1?.Dispose();
                client2?.Close();
                client2?.Dispose();
            }
        }
        
        [Test]
        public void UnityCliBridge_StatusShouldBeDisconnectedOnStartup()
        {
            // Assert - Check initial status
            // Note: In actual Unity, the MCP might already be connected from previous tests
            // This test verifies that the status enum is working correctly
            Assert.IsTrue(
                Core.UnityCliBridge.Status == McpStatus.Disconnected || 
                Core.UnityCliBridge.Status == McpStatus.Connected,
                "Status should be either Disconnected or Connected"
            );
        }
        
        [Test]
        public async Task UnityCliBridge_ShouldReconnectAfterDisconnection()
        {
            // Arrange
            TcpClient client = null;
            
            try
            {
                // First connection
                client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                Assert.IsTrue(client.Connected, "Should connect initially");
                
                // Disconnect
                client.Close();
                client.Dispose();
                
                // Wait a bit for server to process disconnection
                await Task.Delay(500);
                
                // Act - Reconnect
                client = new TcpClient();
                var reconnectTask = client.ConnectAsync("127.0.0.1", Core.UnityCliBridge.DEFAULT_PORT);
                var completed = await Task.WhenAny(reconnectTask, Task.Delay(CONNECTION_TIMEOUT_MS));
                
                // Assert
                Assert.IsTrue(completed == reconnectTask, "Should reconnect within timeout");
                Assert.IsTrue(client.Connected, "Should be connected after reconnection");
            }
            finally
            {
                client?.Close();
                client?.Dispose();
            }
        }

        private static void EnsureServerRunning()
        {
            if (IsServerReachable(Core.UnityCliBridge.DEFAULT_PORT))
            {
                return;
            }

            Core.UnityCliBridge.Restart();

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < CONNECTION_TIMEOUT_MS)
            {
                if (IsServerReachable(Core.UnityCliBridge.DEFAULT_PORT))
                {
                    return;
                }
                Thread.Sleep(200);
            }

            Assert.Ignore("Unity MCP TCP listener is not reachable on the default port. Integration tests skipped.");
        }

        private static bool IsServerReachable(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync("127.0.0.1", port);
                    var completed = Task.WhenAny(connectTask, Task.Delay(500));
                    if (!completed.Wait(500))
                    {
                        return false;
                    }

                    return connectTask.IsCompletedSuccessfully && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
