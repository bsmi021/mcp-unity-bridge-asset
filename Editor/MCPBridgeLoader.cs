using UnityEngine;
using UnityEditor;
using WebSocketSharp;
using WebSocketSharp.Server;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // Added for JObject
using System.Collections.Generic;

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

        // Static constructor called by Unity Editor on load
        static MCPBridgeLoader()
        {
            // Ensure cleanup happens when scripts are recompiled or editor quits
            EditorApplication.quitting += StopServer;
            // AssemblyReloadEvents.beforeAssemblyReload += StopServer; // Consider if needed

            StartServer();
        }

        public static void StartServer()
        {
            if (wsServer != null && wsServer.IsListening)
            {
                Debug.LogWarning(LOG_PREFIX + "Server already running.");
                return;
            }

            int port = EditorPrefs.GetInt("MCPBridge_Port", DEFAULT_PORT);
            Debug.Log(LOG_PREFIX + $"Attempting to start WebSocket server on port {port}...");

            try
            {
                // Using 127.0.0.1 to only allow local connections
                wsServer = new WebSocketServer($"ws://127.0.0.1:{port}");

                // Add the service/behavior that handles incoming connections/messages
                // We'll define MCPBridgeService later
                wsServer.AddWebSocketService<MCPBridgeService>("/mcp");

                wsServer.Start();

                if (wsServer.IsListening)
                {
                    Debug.Log(LOG_PREFIX + $"Successfully started. Listening on ws://127.0.0.1:{port}/mcp");
                }
                else
                {
                    Debug.LogError(LOG_PREFIX + "Server failed to start listening.");
                    wsServer = null; // Ensure server object is null if start failed
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Failed to start WebSocket server on port {port}. Error: {ex.Message}");
                Debug.LogError(LOG_PREFIX + $"Is port {port} already in use? Try changing it via Tools > MCP Bridge > Settings.");
                wsServer = null; // Ensure server object is null on exception
            }
        }

        public static void StopServer()
        {
            if (wsServer != null && wsServer.IsListening)
            {
                Debug.Log(LOG_PREFIX + "Stopping WebSocket server...");
                wsServer.Stop();
                Debug.Log(LOG_PREFIX + "Server stopped.");
            }
            wsServer = null;
            // Clear connections on stop
            lock (connectionLock)
            {
                activeConnections.Clear();
            }
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

            protected override void OnError(ErrorEventArgs e)
            {
                Debug.LogError(LOG_PREFIX + $"WebSocket Error: {e.Message}");
            }
        }
    }
}
