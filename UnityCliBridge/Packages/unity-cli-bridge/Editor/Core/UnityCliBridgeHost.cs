using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Models;
using UnityCliBridge.Helpers;
using UnityCliBridge.Logging;
using UnityCliBridge.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

namespace UnityCliBridge.Core
{
    /// <summary>
    /// Main Unity CLI Bridge class that handles TCP communication and command processing
    /// </summary>
    [InitializeOnLoad]
    public static class UnityCliBridge
    {
        private static TcpListener tcpListener;
        private static readonly Queue<(Command command, TcpClient client, DateTime enqueuedAtUtc)> commandQueue =
            new Queue<(Command, TcpClient, DateTime)>();
        private static readonly object queueLock = new object();
        private static CancellationTokenSource cancellationTokenSource;
        private static Task listenerTask;
        private static bool isProcessingCommand;
        private static int activeClientCount;
        
        
        private static BridgeStatus _status = BridgeStatus.NotConfigured;
        public static BridgeStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    BridgeLogger.Log($"Status changed to: {value}");
                }
            }
        }
        
        public const int DEFAULT_PORT = 6400;
        private static int currentPort = DEFAULT_PORT;
        // For logging only (what we bind/listen on)
        private static string currentHost = "localhost";
        private static IPAddress bindAddress = IPAddress.Any; // default: 0.0.0.0
        
        /// <summary>
        /// Static constructor - called when Unity loads
        /// </summary>
        static UnityCliBridge()
        {
            BridgeLogger.Log("Initializing...");
            EditorApplication.update += ProcessCommandQueue;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            
            // Load Project Settings and start the TCP listener
            TryLoadProjectSettingsAndApply();
            StartTcpListener();
        }

        

        /// <summary>
        /// Load Project Settings (Project Settings > Unity CLI Bridge) and apply host/port.
        /// </summary>
        private static void TryLoadProjectSettingsAndApply()
        {
            try
            {
                var settings = UnityCliBridgeProjectSettings.instance;
                var configuredHost = settings != null ? settings.ResolvedUnityHost : "localhost";
                var configuredPort = settings != null ? settings.ResolvedPort : DEFAULT_PORT;
                var resolved = ResolveHostAndPortForTesting(
                    configuredHost,
                    configuredPort,
                    Environment.GetEnvironmentVariable("UNITY_CLI_HOST"),
                    Environment.GetEnvironmentVariable("UNITY_CLI_PORT"),
                    Environment.GetEnvironmentVariable("UNITY_CLI_PORT_OVERRIDE"));

                var host = resolved.host;
                var port = resolved.port;

                currentHost = host;
                currentPort = port;
                bindAddress = ResolveBindAddress(host);

                BridgeLogger.Log($"Project Settings loaded: host={host}, bind={bindAddress}, port={currentPort}");
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning($"Project Settings load error: {ex.Message}. Using defaults.");
            }
        }

        internal static (string host, int port) ResolveHostAndPortForTesting(
            string configuredHost,
            int configuredPort,
            string envHostValue,
            string envPortValue,
            string envPortOverrideValue)
        {
            var host = string.IsNullOrWhiteSpace(envHostValue)
                ? (string.IsNullOrWhiteSpace(configuredHost) ? "localhost" : configuredHost.Trim())
                : envHostValue.Trim();

            var port = configuredPort > 0 && configuredPort < 65536 ? configuredPort : DEFAULT_PORT;
            var rawPort = !string.IsNullOrWhiteSpace(envPortOverrideValue) ? envPortOverrideValue : envPortValue;
            if (!string.IsNullOrWhiteSpace(rawPort) && int.TryParse(rawPort, out var parsedPort) && parsedPort > 0 && parsedPort < 65536)
            {
                port = parsedPort;
            }

            return (host, port);
        }

        private static bool ShouldSkipStartupForCurrentProcess()
        {
            var commandLine = Environment.CommandLine ?? string.Empty;
            var allowBatchHost = Environment.GetEnvironmentVariable("UNITY_CLI_ALLOW_BATCH_HOST");
            if (ShouldSkipStartupForProcessForTesting(Application.isBatchMode, commandLine, allowBatchHost))
            {
                BridgeLogger.Log("Skipping TCP listener startup in AssetImportWorker/batch/test process.");
                return true;
            }

            return false;
        }

        internal static bool ShouldSkipStartupForProcessForTesting(bool isBatchMode, string commandLine, string allowBatchHostValue)
        {
            commandLine ??= string.Empty;

            bool isWorkerOrTest =
                commandLine.IndexOf("AssetImportWorker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandLine.IndexOf("-adb2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandLine.IndexOf("-runTests", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isWorkerOrTest)
            {
                return true;
            }

            bool isBatch =
                isBatchMode ||
                commandLine.IndexOf("-batchMode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandLine.IndexOf("-batchmode", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isBatch)
            {
                return false;
            }

            bool allowBatchHost =
                string.Equals(allowBatchHostValue, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowBatchHostValue, "true", StringComparison.OrdinalIgnoreCase);

            return !allowBatchHost;
        }

        private static IPAddress ResolveBindAddress(string host)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return IPAddress.Loopback;
                }
                if (host == "*" || host == "0.0.0.0")
                {
                    return IPAddress.Any;
                }
                if (host == "::")
                {
                    return IPAddress.IPv6Any;
                }
                if (host == "::1")
                {
                    return IPAddress.IPv6Loopback;
                }
                if (IPAddress.TryParse(host, out var ip))
                {
                    return ip;
                }
                var addrs = Dns.GetHostAddresses(host);
                var ipv4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return ipv4 ?? addrs.FirstOrDefault() ?? IPAddress.Loopback;
            }
            catch
            {
                return IPAddress.Loopback;
            }
        }
        
        /// <summary>
        /// Starts the TCP listener on the configured port
        /// </summary>
        private static void StartTcpListener()
        {
            if (ShouldSkipStartupForCurrentProcess())
            {
                Status = BridgeStatus.NotConfigured;
                return;
            }

            try
            {
                if (tcpListener != null)
                {
                    StopTcpListener();
                }
                
                cancellationTokenSource = new CancellationTokenSource();
                tcpListener = new TcpListener(bindAddress, currentPort);
                tcpListener.Start();
                Interlocked.Exchange(ref activeClientCount, 0);
                
                Status = BridgeStatus.Disconnected;
                BridgeLogger.Log($"TCP listener binding on {bindAddress}:{currentPort} (host={currentHost})");
                
                // Start accepting connections asynchronously
                listenerTask = Task.Run(() => AcceptConnectionsAsync(cancellationTokenSource.Token));
            }
            catch (SocketException ex)
            {
                Status = BridgeStatus.Error;
                BridgeLogger.LogError($"Failed to start TCP listener on port {currentPort}: {ex.Message}");
                
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    BridgeLogger.LogError($"Port {currentPort} is already in use. Please ensure no other instance is running.");
                }
            }
            catch (Exception ex)
            {
                Status = BridgeStatus.Error;
                BridgeLogger.LogError($"Unexpected error starting TCP listener: {ex}");
            }
        }
        
        /// <summary>
        /// Stops the TCP listener
        /// </summary>
        private static void StopTcpListener()
        {
            try
            {
                cancellationTokenSource?.Cancel();
                tcpListener?.Stop();
                listenerTask?.Wait(TimeSpan.FromSeconds(1));
                
                tcpListener = null;
                cancellationTokenSource = null;
                listenerTask = null;
                Interlocked.Exchange(ref activeClientCount, 0);
                lock (queueLock)
                {
                    commandQueue.Clear();
                }
                BridgeCommandStats.ResetForTesting();
                
                Status = BridgeStatus.Disconnected;
                BridgeLogger.Log("TCP listener stopped");
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError($"Error stopping TCP listener: {ex}");
            }
        }
        
        /// <summary>
        /// Accepts incoming TCP connections asynchronously
        /// </summary>
        private static async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await AcceptClientAsync(tcpListener, cancellationToken);
                    if (tcpClient != null)
                    {
                        Interlocked.Increment(ref activeClientCount);
                        Status = BridgeStatus.Connected;
                        BridgeLogger.Log($"Client connected from {tcpClient.Client.RemoteEndPoint}");
                        
                        // Handle client in a separate task
                        _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken));
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        BridgeLogger.LogError($"Error accepting connection: {ex}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Accepts a client with cancellation support
        /// </summary>
        private static async Task<TcpClient> AcceptClientAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => listener.Stop()))
            {
                try
                {
                    return await listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Handles communication with a connected client
        /// </summary>
        private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var clientLabel = DescribeClient(client);
            try
            {
                client.ReceiveTimeout = 30000; // 30 second timeout
                client.SendTimeout = 30000;
                
                var buffer = new byte[4096];
                var stream = client.GetStream();
                var messageBuffer = new List<byte>();
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        // Client disconnected
                        break;
                    }
                    
                    // Add received bytes to message buffer
                    for (int i = 0; i < bytesRead; i++)
                    {
                        messageBuffer.Add(buffer[i]);
                    }
                    
                    // Process complete messages
                    while (messageBuffer.Count >= 4)
                    {
                        // Read message length (first 4 bytes, big-endian)
                        var lengthBytes = messageBuffer.GetRange(0, 4).ToArray();
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(lengthBytes);
                        }
                        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
                        
                        // Check if we have the complete message
                        if (messageBuffer.Count >= 4 + messageLength)
                        {
                            // Extract message
                            var messageBytes = messageBuffer.GetRange(4, messageLength).ToArray();
                            messageBuffer.RemoveRange(0, 4 + messageLength);
                            
                            var json = Encoding.UTF8.GetString(messageBytes);
                            // 受信ログは Unity コンソールには出力せず、コマンド処理キューのみを更新する。
                            
                            try
                            {
                                // Handle special ping command
                                if (json.Trim().ToLower() == "ping")
                                {
                                    var pongResponse = Response.Pong();
                                    if (!await TrySendFramedMessage(stream, pongResponse, cancellationToken))
                                    {
                                        break;
                                    }
                                    continue;
                                }
                                
                                // Parse command
                                var command = JsonConvert.DeserializeObject<Command>(json);
                                if (command != null)
                                {
                                    // Queue command for processing on main thread
                                    lock (queueLock)
                                    {
                                        commandQueue.Enqueue((command, client, DateTime.UtcNow));
                                    }
                                }
                                else
                                {
                                    var errorResponse = Response.ErrorResult("Invalid command format", "PARSE_ERROR", null);
                                    if (!await TrySendFramedMessage(stream, errorResponse, cancellationToken))
                                    {
                                        break;
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                var errorResponse = Response.ErrorResult($"JSON parsing error: {ex.Message}", "JSON_ERROR", null);
                                if (!await TrySendFramedMessage(stream, errorResponse, cancellationToken))
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Not enough data yet, wait for more
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested && !IsExpectedDisconnect(ex))
                {
                    BridgeLogger.LogError($"Client handler error: {ex}");
                }
            }
            finally
            {
                var dropped = RemoveQueuedCommandsForClient(client);
                if (dropped > 0)
                {
                    BridgeLogger.LogWarning($"Dropped {dropped} queued command(s) for disconnected client {clientLabel}");
                }

                client?.Close();
                var remainingClients = Interlocked.Decrement(ref activeClientCount);
                if (remainingClients < 0)
                {
                    Interlocked.Exchange(ref activeClientCount, 0);
                    remainingClients = 0;
                }
                if (remainingClients == 0)
                {
                    Status = BridgeStatus.Disconnected;
                }
                BridgeLogger.Log($"Client disconnected: {clientLabel}");
            }
        }
        
        /// <summary>
        /// Sends a framed message over the stream
        /// </summary>
        private static async Task<bool> TrySendFramedMessage(NetworkStream stream, string message, CancellationToken cancellationToken)
        {
            if (stream == null || !stream.CanWrite)
            {
                return false;
            }

            try
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);
                await stream.WriteAsync(lengthBytes, 0, 4, cancellationToken);
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return true;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested || IsExpectedDisconnect(ex))
            {
                return false;
            }
            catch (Exception ex)
            {
                try { BridgeLogger.LogError($"Send error: {ex}"); } catch { }
                return false;
            }
        }
        
        /// <summary>
        /// Processes queued commands on the Unity main thread.
        /// Drains all queued commands within a single frame for lower latency.
        /// </summary>
        private static async void ProcessCommandQueue()
        {
            if (isProcessingCommand) return;
            isProcessingCommand = true;
            try
            {
                while (true)
                {
                    (Command command, TcpClient client, DateTime enqueuedAtUtc) item;
                    lock (queueLock)
                    {
                        if (commandQueue.Count == 0) break;
                        item = commandQueue.Dequeue();
                    }
                    await ProcessCommandInternal(item.command, item.client, item.enqueuedAtUtc);
                }
            }
            finally
            {
                isProcessingCommand = false;
            }
        }
        
        /// <summary>
        /// Processes a single command
        /// </summary>
        private static async Task ProcessCommandInternal(Command command, TcpClient client, DateTime enqueuedAtUtc)
        {
            NetworkStream responseStream = null;
            var commandType = command?.Type ?? "(null)";
            using var statsScope = BridgeCommandStats.BeginCommand(commandType, enqueuedAtUtc);
            try
            {
                if (!TryGetWritableStream(client, out responseStream))
                {
                    var commandId = command?.Id ?? "(unknown)";
                    BridgeLogger.LogWarning($"Skipping command {commandId}:{commandType} because client stream is not writable");
                    return;
                }

                string response;
                
                // During Play Mode, restrict heavy commands per policy to keep the bridge responsive
                if (Application.isPlaying && !PlayModeCommandPolicy.IsAllowed(command.Type))
                {
                    var state = new {
                        isPlaying = Application.isPlaying,
                        isCompiling = EditorApplication.isCompiling,
                        isUpdating = EditorApplication.isUpdating
                    };
                    response = Response.ErrorResult(command.Id, $"Command '{command.Type}' is blocked during Play Mode", "PLAY_MODE_BLOCKED", state);
                    response = PrepareCommandResponseForStats(response, out _);
                    var sendStopwatch = Stopwatch.StartNew();
                    await TrySendFramedMessage(responseStream, response, CancellationToken.None);
                    sendStopwatch.Stop();
                    BridgeCommandStats.RecordStageDuration("response_send_ms", sendStopwatch.Elapsed.TotalMilliseconds);
                    statsScope.Complete(false, Encoding.UTF8.GetByteCount(response));
                    return;
                }

                var warnings = PlayModeChangeWarningHelper.GetWarnings(command.Type, command.Parameters);
                var handlerStopwatch = Stopwatch.StartNew();

                response = await BridgeCommandRouter.Handle(command);

                if (warnings != null && response != null)
                {
                    response = Response.AppendWarnings(response, warnings);
                }
                handlerStopwatch.Stop();
                BridgeCommandStats.RecordStageDuration("handler_ms", handlerStopwatch.Elapsed.TotalMilliseconds);
                response = PrepareCommandResponseForStats(response, out var responseIsError);

                // Send response
                var responseWriteStopwatch = Stopwatch.StartNew();
                await TrySendFramedMessage(responseStream, response, CancellationToken.None);
                responseWriteStopwatch.Stop();
                BridgeCommandStats.RecordStageDuration("response_send_ms", responseWriteStopwatch.Elapsed.TotalMilliseconds);
                statsScope.Complete(!responseIsError, Encoding.UTF8.GetByteCount(response));
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError($"Error processing command {command}: {ex}");
                
                try
                {
                    if (responseStream != null)
                    {
                        var errorResponse = Response.ErrorResult(
                            command.Id,
                            $"Internal error: {ex.Message}", 
                            "INTERNAL_ERROR",
                            new { 
                                commandType = command.Type,
                                stackTrace = ex.StackTrace
                            }
                        );
                        errorResponse = PrepareCommandResponseForStats(errorResponse, out _);
                        var responseWriteStopwatch = Stopwatch.StartNew();
                        await TrySendFramedMessage(responseStream, errorResponse, CancellationToken.None);
                        responseWriteStopwatch.Stop();
                        BridgeCommandStats.RecordStageDuration("response_send_ms", responseWriteStopwatch.Elapsed.TotalMilliseconds);
                        statsScope.Complete(false, Encoding.UTF8.GetByteCount(errorResponse));
                    }
                }
                catch
                {
                    // Best effort - ignore errors when sending error response
                }
            }
            finally
            {
            }
        }

        private static string PrepareCommandResponseForStats(string response, out bool isError)
        {
            var sanitized = response;
            var timings = default(Dictionary<string, double>);
            var extractedIsError = false;

            if (!string.IsNullOrWhiteSpace(response))
            {
                try
                {
                    var root = JObject.Parse(response);
                    extractedIsError =
                        string.Equals(root["status"]?.ToString(), "error", StringComparison.OrdinalIgnoreCase) ||
                        (root["success"]?.Type == JTokenType.Boolean && !(root["success"]?.Value<bool>() ?? true));

                    var payload = root["result"] as JObject ?? root["data"] as JObject;
                    if (payload?["_commandStats"] is JObject stats)
                    {
                        timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        foreach (var property in stats.Properties())
                        {
                            if (TryExtractTiming(property.Value, out var timing))
                            {
                                timings[property.Name] = timing;
                            }
                        }

                        payload.Remove("_commandStats");
                        sanitized = root.ToString(Formatting.None);
                    }
                }
                catch
                {
                    // Ignore malformed responses and fall back to the original payload.
                }
            }

            if (timings != null)
            {
                foreach (var timing in timings)
                {
                    BridgeCommandStats.RecordStageDuration(timing.Key, timing.Value);
                }
            }

            isError = extractedIsError;
            return sanitized;
        }

        private static bool TryExtractTiming(JToken token, out double value)
        {
            value = 0;
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return true;
            }

            if (token.Type == JTokenType.String)
            {
                return double.TryParse(token.Value<string>(), out value);
            }

            return false;
        }

        internal static bool TryGetWritableStream(TcpClient client, out NetworkStream stream)
        {
            stream = null;
            if (client == null)
            {
                return false;
            }

            try
            {
                stream = client.GetStream();
                return stream != null && stream.CanWrite;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        internal static bool IsExpectedDisconnect(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex is OperationCanceledException)
            {
                return true;
            }

            if (ex is ObjectDisposedException)
            {
                return true;
            }

            if (ex is SocketException socketException)
            {
                switch (socketException.SocketErrorCode)
                {
                    case SocketError.ConnectionReset:
                    case SocketError.ConnectionAborted:
                    case SocketError.Shutdown:
                    case SocketError.NotConnected:
                    case SocketError.OperationAborted:
                    case SocketError.Interrupted:
                        return true;
                }

                return false;
            }

            if (ex is IOException ioException && ioException.InnerException != null)
            {
                return IsExpectedDisconnect(ioException.InnerException);
            }

            return ex.InnerException != null && IsExpectedDisconnect(ex.InnerException);
        }

        internal static int RemoveQueuedCommandsForClient(TcpClient client)
        {
            if (client == null)
            {
                return 0;
            }

            lock (queueLock)
            {
                if (commandQueue.Count == 0)
                {
                    return 0;
                }

                var dropped = 0;
                var retained = new Queue<(Command command, TcpClient client, DateTime enqueuedAtUtc)>(commandQueue.Count);

                while (commandQueue.Count > 0)
                {
                    var item = commandQueue.Dequeue();
                    if (ReferenceEquals(item.client, client))
                    {
                        dropped++;
                        continue;
                    }

                    retained.Enqueue(item);
                }

                while (retained.Count > 0)
                {
                    commandQueue.Enqueue(retained.Dequeue());
                }

                return dropped;
            }
        }

        internal static void EnqueueCommandForTesting(Command command, TcpClient client)
        {
            if (command == null || client == null)
            {
                return;
            }

            lock (queueLock)
            {
                commandQueue.Enqueue((command, client, DateTime.UtcNow));
            }
        }

        internal static int GetQueuedCommandCountForTesting()
        {
            lock (queueLock)
            {
                return commandQueue.Count;
            }
        }

        internal static void ClearQueuedCommandsForTesting()
        {
            lock (queueLock)
            {
                commandQueue.Clear();
            }

            BridgeCommandStats.ResetForTesting();
        }

        private static string DescribeClient(TcpClient client)
        {
            if (client == null)
            {
                return "unknown";
            }

            try
            {
                var endpoint = client.Client?.RemoteEndPoint;
                return endpoint?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
        
        /// <summary>
        /// Shuts down the Unity CLI Bridge system
        /// </summary>
        private static void Shutdown()
        {
            BridgeLogger.Log("Shutting down...");
            StopTcpListener();
            EditorApplication.update -= ProcessCommandQueue;
            EditorApplication.quitting -= Shutdown;
        }

        /// <summary>
        /// Starts the TCP listener.
        /// </summary>
        public static void Start()
        {
            BridgeLogger.Log("Starting...");
            TryLoadProjectSettingsAndApply();
            StartTcpListener();
        }

        /// <summary>
        /// Stops the TCP listener without unregistering editor callbacks.
        /// </summary>
        public static void Stop()
        {
            BridgeLogger.Log("Stopping...");
            StopTcpListener();
            Status = BridgeStatus.NotConfigured;
        }

        /// <summary>
        /// Restarts the TCP listener
        /// </summary>
        public static void Restart()
        {
            BridgeLogger.Log("Restarting...");
            TryLoadProjectSettingsAndApply();
            StopTcpListener();
            StartTcpListener();
        }
        
        /// <summary>
        /// Changes the listening port and restarts
        /// </summary>
        public static void ChangePort(int newPort)
        {
            if (newPort < 1024 || newPort > 65535)
            {
                BridgeLogger.LogError($"Invalid port number: {newPort}. Must be between 1024 and 65535.");
                return;
            }
            
            currentPort = newPort;
            Restart();
        }
    }
}
