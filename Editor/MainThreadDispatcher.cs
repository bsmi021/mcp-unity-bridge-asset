using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine; // Required for Debug.Log/Error

namespace BSMI021.MCPUnityBridge
{
    /// <summary>
    /// Handles dispatching actions received from background threads (like WebSocket)
    /// onto the Unity main thread for safe execution of Unity APIs.
    /// Uses a ConcurrentQueue and the EditorApplication.update delegate.
    /// </summary>
    [InitializeOnLoad] // Ensure the update delegate is registered on load
    public static class MainThreadDispatcher
    {
        // Thread-safe queue to hold actions that need main thread execution
        private static readonly ConcurrentQueue<Action> executionQueue = new ConcurrentQueue<Action>();
        private const string LOG_PREFIX = "[MCPBridge-Dispatcher] ";

        static MainThreadDispatcher()
        {
            // Register our processing method with the editor's update loop
            EditorApplication.update -= ProcessQueue; // Ensure it's not added multiple times
            EditorApplication.update += ProcessQueue;
            // Debug.Log(LOG_PREFIX + "Registered main thread queue processor."); // Optional: Log registration
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread.
        /// Call this from background threads (e.g., WebSocket OnMessage).
        /// </summary>
        /// <param name="action">The action delegate to execute.</param>
        public static void Enqueue(Action action)
        {
            if (action == null)
            {
                Debug.LogError(LOG_PREFIX + "Attempted to enqueue a null action.");
                return;
            }
            executionQueue.Enqueue(action);
        }

        /// <summary>
        /// Processes the queued actions. This method is called by EditorApplication.update
        /// and therefore runs on the main thread.
        /// </summary>
        private static void ProcessQueue()
        {
            // Process all actions currently in the queue for this frame
            // Avoid potentially blocking the editor for too long by processing
            // only what's currently there, rather than looping indefinitely if
            // new items are added very rapidly during processing.
            while (executionQueue.TryDequeue(out Action action))
            {
                try
                {
                    // Debug.Log(LOG_PREFIX + "Executing action on main thread."); // Optional: Verbose logging
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    // Log exceptions that occur during the action's execution
                    Debug.LogError(LOG_PREFIX + $"Exception executing action on main thread: {ex.Message}\n{ex.StackTrace}");
                    // Note: We might need a way to report this specific error back
                    // via WebSocket if the action itself didn't handle its own errors
                    // and return a CommandResponse.Error. This depends on how the
                    // Action delegate is constructed in the OnMessage handler.
                }
            }
        }
    }
}
