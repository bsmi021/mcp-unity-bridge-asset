using System;
using System.Collections.Generic;
using UnityEngine; // For Debug.Log/Error
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp; // Added for WebSocketState enum

namespace BSMI021.MCPUnityBridge
{
    /// <summary>
    /// Contains static methods responsible for handling specific commands received
    /// from the MCP server and executing the corresponding Unity Editor actions.
    /// Methods here are invoked by the MainThreadDispatcher and run on the main thread.
    /// </summary>
    public static class CommandHandler
    {
        private const string LOG_PREFIX = "[MCPBridge-Handler] ";

        /// <summary>
        /// Routes incoming command data to the appropriate handler method.
        /// This method would typically be called within the Action delegate
        /// created by the WebSocket service and executed by the MainThreadDispatcher.
        /// </summary>
        /// <param name="command">The command name (e.g., "ping", "manage_asset").</param>
        /// <param name="parameters">A dictionary containing the parameters for the command.</param>
        /// <param name="correlationId">The correlation ID passed from the client.</param>
        /// <param name="serviceInstance">The specific MCPBridgeService instance that received the command.</param>
        public static void RouteCommand(string command, Dictionary<string, object> parameters, string correlationId, MCPBridgeLoader.MCPBridgeService serviceInstance)
        {
            // No need to extract correlationId here anymore, it's passed as a parameter
            // string correlationId = null;
            // if (parameters != null && parameters.TryGetValue("correlationId", out object idObject) && idObject is string idStr)
            // {
            //     correlationId = idStr;
            //     // Optionally remove it from parameters passed to handlers if they shouldn't see it
            //     // parameters.Remove("correlationId");
            // }

            CommandResponse response;
            try
            {
                // TODO: Implement robust JSON parsing/deserialization of parameters here if needed,
                // potentially using Newtonsoft.Json if parameters are complex objects.
                // For now, assumes parameters are simple enough or handled within specific handlers.

                switch (command?.ToLowerInvariant())
                {
                    case "ping":
                        // Pass correlationId to the handler
                        response = HandlePing(parameters, correlationId);
                        break;

                    // --- Placeholder for future handlers ---
                    // case "manage_asset":
                    //     response = HandleManageAsset(parameters);
                    //     break;
                    // case "manage_gameobject":
                    //     response = HandleManageGameObject(parameters);
                    //     break;
                    // case "manage_scene":
                    //     response = HandleManageScene(parameters);
                    //     break;
                    // ... etc.

                    default:
                        Debug.LogWarning(LOG_PREFIX + $"Received unknown command: {command}");
                        // Use static helper again
                        response = CommandResponse.Error($"Unknown command: {command}", correlationId: correlationId);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Exception during command routing or execution for command '{command}': {ex.Message}\n{ex.StackTrace}");
                // Use static helper again
                response = CommandResponse.Error($"Internal bridge error processing command '{command}': {ex.Message}", correlationId: correlationId);
            }

            // Ensure correlationId is set on the final response before sending (Still useful)
            if (response != null) response.correlationId = correlationId;

            // Send the response back using the specific service instance
            SendResponse(response, serviceInstance);
        }


        /// <summary>
        /// Example handler for a simple 'ping' command.
        /// </summary>
        private static CommandResponse HandlePing(Dictionary<string, object> parameters, string correlationId)
        {
            Debug.Log(LOG_PREFIX + $"Handling 'ping' command (ID: {correlationId}).");
            // Optionally check parameters if ping expects any
            // object payload = parameters?.ContainsKey("payload") == true ? parameters["payload"] : null;
            // Use static helper again
            return CommandResponse.Success("Pong!", parameters, correlationId); // Echo parameters back in data for testing
        }

        // --- Placeholder stubs for future handlers ---

        // private static CommandResponse HandleManageAsset(Dictionary<string, object> parameters)
        // {
        //     // TODO: Implement AssetDatabase logic based on parameters["action"]
        //     Debug.Log(LOG_PREFIX + "Handling 'manage_asset' command (Not Implemented).");
        //     return CommandResponse.Error("manage_asset not implemented yet.");
        // }

        // ... other handler stubs ...


        /// <summary>
        /// Sends the CommandResponse back to the MCP server via the specific WebSocket connection.
        /// </summary>
        private static void SendResponse(CommandResponse response, MCPBridgeLoader.MCPBridgeService serviceInstance)
        {
            // Simplify check: Only ensure the service instance itself exists.
            // Let the underlying Send() method handle the connection state.
            if (serviceInstance == null)
            {
                Debug.LogWarning(LOG_PREFIX + "Cannot send response, service instance is null.");
                return;
            }

            try
            {
                // Use Newtonsoft.Json for reliable serialization
                // Use Formatting.None for compactness over the wire
                // Use NullValueHandling.Ignore to avoid sending null fields
                string jsonResponse = JsonConvert.SerializeObject(response, Formatting.None,
                     new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                Debug.Log(LOG_PREFIX + $"Sending response via service instance {serviceInstance.ID}: {jsonResponse}");

                // Use the SendResponse helper method added to MCPBridgeService
                serviceInstance.SendResponse(jsonResponse);

            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Failed to serialize or send response: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Removed manual JsonEscape and SimpleJsonSerializer methods as they are replaced by Newtonsoft.Json
    }
}
