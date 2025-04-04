using UnityEngine;
using UnityEditor;
using WebSocketSharp;
using WebSocketSharp.Server;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // Added for JObject
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug; // Explicit Debug reference to avoid confusion

namespace BSMI021.MCPUnityBridge
{
    /// <summary>
    /// Handles the initialization and management of the WebSocket server
    /// when the Unity Editor loads.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPBridgeLoader
    {
        private static WebSocketServer wsServer;
        private const int DEFAULT_PORT = 8765; // As per RFC #4
        private const string LOG_PREFIX = "[MCPBridge-Loader] ";
        // Dictionary to track active connections by their ID
        private static readonly Dictionary<string, MCPBridgeService> activeConnections = new Dictionary<string, MCPBridgeService>();
        private static readonly object connectionLock = new object(); // Lock for thread safety
        private static bool isServerRunning = false;
        private static string pidFilePath;
        private static int currentPort;

        // Static constructor called by Unity Editor on load
        static MCPBridgeLoader()
        {
            // Set up PID file path in the temp directory
            pidFilePath = Path.Combine(Path.GetTempPath(), "mcp_unity_bridge_server.pid");
            
            // Check for and clean up any existing zombie servers
            CleanupZombieServer();

            // Register for domain reload
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
            
            // Initial server start
            StartServer();
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                // Check if port is already in use by any TCP listener
                IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
                
                foreach (var endpoint in tcpConnInfoArray)
                {
                    if (endpoint.Port == port)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX}Error checking port availability: {ex.Message}");
                return false;
            }
        }

        private static void CleanupZombieServer()
        {
            try
            {
                if (File.Exists(pidFilePath))
                {
                    string[] pidInfo = File.ReadAllLines(pidFilePath);
                    if (pidInfo.Length >= 2 && int.TryParse(pidInfo[0], out int pid) && int.TryParse(pidInfo[1], out int port))
                    {
                        try
                        {
                            Process process = Process.GetProcessById(pid);
                            // If we get here, the process exists
                            Debug.LogWarning($"{LOG_PREFIX}Found zombie WebSocket server (PID: {pid}, Port: {port}). Attempting to kill it...");
                            process.Kill();
                            process.WaitForExit(3000); // Wait up to 3 seconds for the process to die
                        }
                        catch (ArgumentException)
                        {
                            // Process not found, which is fine
                            Debug.Log($"{LOG_PREFIX}No zombie process found with PID: {pid}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"{LOG_PREFIX}Error killing zombie process: {ex.Message}");
                        }
                    }
                    
                    // Clean up the PID file regardless of what happened above
                    try
                    {
                        File.Delete(pidFilePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"{LOG_PREFIX}Error deleting PID file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX}Error during zombie cleanup: {ex.Message}");
            }
        }

        private static void WritePidFile(int port)
        {
            try
            {
                // Write both the current process ID and port
                File.WriteAllText(pidFilePath, $"{Process.GetCurrentProcess().Id}\n{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX}Error writing PID file: {ex.Message}");
            }
        }

        public static void StartServer()
        {
            if (isServerRunning)
            {
                Debug.Log($"{LOG_PREFIX}Server is already running.");
                return;
            }

            try
            {
                currentPort = EditorPrefs.GetInt("MCPBridge_Port", DEFAULT_PORT);
                
                // Check if port is available
                if (!IsPortAvailable(currentPort))
                {
                    Debug.LogError($"{LOG_PREFIX}Port {currentPort} is already in use. Please change the port in Tools/MCP Bridge/Settings.");
                    return;
                }

                // Create and start the WebSocket server
                wsServer = new WebSocketServer($"ws://127.0.0.1:{currentPort}");
                wsServer.AddWebSocketService<MCPBridgeService>("/mcp");
                wsServer.Start();
                
                if (wsServer.IsListening)
                {
                    isServerRunning = true;
                    WritePidFile(currentPort); // Write PID file after successful start
                    Debug.Log($"{LOG_PREFIX}WebSocket server started on port {currentPort}");
                }
                else
                {
                    Debug.LogError($"{LOG_PREFIX}Failed to start WebSocket server on port {currentPort}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX}Error starting WebSocket server: {ex.Message}");
                isServerRunning = false;
            }
        }

        public static void StopServer()
        {
            if (!isServerRunning || wsServer == null)
            {
                return;
            }

            try
            {
                wsServer.Stop();
                wsServer = null;
                isServerRunning = false;
                
                // Clean up PID file
                try
                {
                    if (File.Exists(pidFilePath))
                    {
                        File.Delete(pidFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX}Error deleting PID file during shutdown: {ex.Message}");
                }
                
                Debug.Log($"{LOG_PREFIX}WebSocket server stopped");
                // Clear connections on stop
                lock (connectionLock)
                {
                    activeConnections.Clear();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX}Error stopping WebSocket server: {ex.Message}");
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            // Ensure server is stopped before reload
            StopServer();
        }

        private static void OnAfterAssemblyReload()
        {
            // Restart server after reload
            StartServer();
        }

        private static void OnEditorQuitting()
        {
            // Clean up when the editor is closing
            StopServer();
        }

        /// <summary>
        /// Static method to send a message to a specific client connection.
        /// Used by CommandHandler after processing a command on the main thread.
        /// </summary>
        public static void SendToClient(string connectionId, string jsonResponse)
        {
            MCPBridgeService serviceInstance = null;
            lock (connectionLock)
            {
                activeConnections.TryGetValue(connectionId, out serviceInstance);
            }

            if (serviceInstance != null)
            {
                // Use the SendResponse helper on the specific service instance
                // This ensures the message is sent over the correct WebSocket connection
                serviceInstance.SendResponse(jsonResponse);
            }
            else
            {
                Debug.LogWarning(LOG_PREFIX + $"Attempted to send response to non-existent or closed connection ID: {connectionId}");
            }
        }


        // The actual WebSocket service implementation
        public class MCPBridgeService : WebSocketBehavior
        {
            // Access Context property from WebSocketBehavior base class
            protected override void OnOpen()
            {
                // Accessing via the Context property, let's just log the ID for now
                Debug.Log(LOG_PREFIX + $"Client connected: {ID}"); // Simplified log
                // Track the connection
                lock (connectionLock)
                {
                    activeConnections[ID] = this;
                }
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                // This runs on a background thread from WebSocketSharp
                Debug.Log(LOG_PREFIX + $"Received raw message: {e.Data}");

                // Declare commandData and correlationId outside the try block for accessibility in catch blocks
                Dictionary<string, object> commandData = null;
                string correlationIdForCatch = null; // Renamed to avoid confusion with the one passed to RouteCommand

                try
                {
                    // Parse the incoming JSON message
                    // Expecting format: {"command": "command_name", "parameters": {...}, "correlationId": "..."}
                    commandData = JsonConvert.DeserializeObject<Dictionary<string, object>>(e.Data);

                    // Extract command (check commandData != null first)
                    if (commandData == null || !commandData.ContainsKey("command") || !(commandData["command"] is string command))
                    {
                        throw new JsonException("Invalid command format: Missing or invalid 'command' field.");
                    }

                    // Extract correlationId (optional but expected from our client) (check commandData != null first)
                    string correlationId = null; // This one is passed to RouteCommand
                    if (commandData != null && commandData.ContainsKey("correlationId") && commandData["correlationId"] is string idStr)
                    {
                        correlationId = idStr;
                        correlationIdForCatch = idStr; // Store for catch blocks
                    }
                    else
                    {
                        Debug.LogWarning(LOG_PREFIX + "Received command without a correlationId.");
                    }


                    // Extract parameters (might be null or not present)
                    Dictionary<string, object> parameters = null;
                    if (commandData.ContainsKey("parameters") && commandData["parameters"] is JObject paramsJObject) // Use JObject for flexibility
                    {
                        parameters = paramsJObject.ToObject<Dictionary<string, object>>();
                    }
                    else if (commandData.ContainsKey("parameters") && commandData["parameters"] is Dictionary<string, object> paramsDict)
                    {
                        parameters = paramsDict; // Handle if it's already a dictionary
                    }
                    else
                    {
                        parameters = new Dictionary<string, object>(); // Ensure parameters is not null
                    }


                    // Capture 'this' (the service instance) to pass to the main thread action
                    // This allows the CommandHandler to call Send() back on this specific connection
                    MCPBridgeService serviceInstance = this;

                    // Create the action to be executed on the main thread, passing correlationId
                    Action commandAction = () => CommandHandler.RouteCommand(command, parameters, correlationId, serviceInstance);

                    // Enqueue the action for main thread execution
                    MainThreadDispatcher.Enqueue(commandAction);
                }
                catch (JsonException jsonEx)
                {
                    // Use correlationIdForCatch which is accessible here
                    Debug.LogError(LOG_PREFIX + $"JSON Parsing Error: {jsonEx.Message}\nRaw Data: {e.Data}");
                    // Send error response immediately back if parsing fails, using the ID if available
                    // Use direct constructor for safety against caching issues
                    Send(JsonConvert.SerializeObject(new CommandResponse(false, null, null, $"Invalid JSON format: {jsonEx.Message}", correlationIdForCatch)));
                }
                catch (Exception ex)
                {
                    // Use correlationIdForCatch captured before/during the try block
                    Debug.LogError(LOG_PREFIX + $"Error processing message: {ex.Message}\nRaw Data: {e.Data}");
                    // Send generic error response immediately back
                    // Use direct constructor for safety against caching issues
                    Send(JsonConvert.SerializeObject(new CommandResponse(false, null, null, $"Error processing message: {ex.Message}", correlationIdForCatch)));
                }
            }

            // Helper method within the service instance to send responses
            public void SendResponse(string jsonResponse)
            {
                Send(jsonResponse);
            }


            protected override void OnClose(CloseEventArgs e)
            {
                Debug.Log(LOG_PREFIX + $"Client disconnected: {ID} - {e.Reason} (Code: {e.Code})");
                // Untrack the connection
                lock (connectionLock)
                {
                    activeConnections.Remove(ID);
                }
            }

            protected override void OnError(WebSocketSharp.ErrorEventArgs e)
            {
                Debug.LogError(LOG_PREFIX + $"WebSocket Error: {e.Message}");
            }
        }
    }
}
