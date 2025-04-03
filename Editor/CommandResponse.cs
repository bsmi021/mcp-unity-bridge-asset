using System;
using System.Collections.Generic;

namespace BSMI021.MCPUnityBridge
{
    /// <summary>
    /// Standard structure for responses sent back from the C# Unity Bridge to the MCP server.
    /// Designed to be serialized to JSON using Newtonsoft.Json.
    /// </summary>
    public class CommandResponse
    {
        /// <summary>
        /// REQUIRED: Indicates if the command execution on the Unity side was successful.
        /// </summary>
        public bool success;

        /// <summary>
        /// OPTIONAL: A human-readable status message (e.g., "Asset created successfully.").
        /// </summary>
        public string message;

        /// <summary>
        /// OPTIONAL: The actual data payload resulting from the command (e.g., List<string>, Dictionary<string, object>).
        /// Must be serializable by Newtonsoft.Json. Null if no data is returned or if an error occurred.
        /// </summary>
        public object data;

        /// <summary>
        /// OPTIONAL: If 'success' is false, this should contain a description of the error
        /// that occurred on the Unity side. Null otherwise.
        /// </summary>
        public string error;

        /// <summary>
        /// OPTIONAL: Used to correlate this response with the original request sent by the client.
        /// Should be populated with the correlationId received in the request payload.
        /// </summary>
        public string correlationId; // Ensure this field exists

        /// <summary>
        /// Convenience constructor with 5 arguments.
        /// </summary>
        public CommandResponse(bool success = false, string message = null, object data = null, string error = null, string correlationId = null) // Ensure 5 args
        {
            this.success = success;
            this.message = message;
            this.data = data;
            this.error = error;
            this.correlationId = correlationId; // Ensure assignment
        }

        // Static helper for creating success responses with correlationId
        public static CommandResponse Success(string message = null, object data = null, string correlationId = null) // Ensure 3 args
        {
            // Call the 5-argument constructor
            return new CommandResponse(true, message, data, null, correlationId);
        }

        // Static helper for creating error responses with correlationId
        public static CommandResponse Error(string errorMessage, string message = null, string correlationId = null) // Ensure 3 args
        {
            // Call the 5-argument constructor
            return new CommandResponse(false, message, null, errorMessage, correlationId);
        }
    }
}
