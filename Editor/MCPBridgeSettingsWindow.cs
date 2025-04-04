using UnityEngine;
using UnityEditor;

namespace BSMI021.MCPUnityBridge
{
    /// <summary>
    /// Provides an Editor Window for configuring MCP Bridge settings, primarily the WebSocket port.
    /// </summary>
    public class MCPBridgeSettingsWindow : EditorWindow
    {
        private const string PORT_PREF_KEY = "MCPBridge_Port";
        private const int DEFAULT_PORT = 8765; // Must match MCPBridgeLoader
        private const string LOG_PREFIX = "[MCPBridge-Settings] ";

        private int currentPort;
        private int inputPort;

        [MenuItem("Tools/MCP Bridge/Settings")]
        public static void ShowWindow()
        {
            // Get existing open window or if none, make a new one:
            MCPBridgeSettingsWindow window = (MCPBridgeSettingsWindow)EditorWindow.GetWindow(typeof(MCPBridgeSettingsWindow));
            window.titleContent = new GUIContent("MCP Bridge Settings");
            window.minSize = new Vector2(300, 150);
            window.Show();
        }

        private void OnEnable()
        {
            // Load the saved port when the window is enabled
            currentPort = EditorPrefs.GetInt(PORT_PREF_KEY, DEFAULT_PORT);
            inputPort = currentPort; // Initialize input field with current value
        }

        void OnGUI()
        {
            GUILayout.Label("MCP Bridge WebSocket Port", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox($"The C# Bridge listens on this port (ws://127.0.0.1:PORT/mcp).\nEnsure your MCP Server connects to the same port.\nDefault: {DEFAULT_PORT}", MessageType.Info);

            GUILayout.Space(10);

            inputPort = EditorGUILayout.IntField("WebSocket Port", inputPort);

            // Basic validation (port range)
            if (inputPort < 1024 || inputPort > 65535)
            {
                EditorGUILayout.HelpBox("Port must be between 1024 and 65535.", MessageType.Warning);
                GUI.enabled = false; // Disable Apply button if port is invalid
            }
            else
            {
                GUI.enabled = true;
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Apply and Restart Bridge"))
            {
                if (inputPort != currentPort)
                {
                    Debug.Log(LOG_PREFIX + $"Applying new port: {inputPort}");
                    EditorPrefs.SetInt(PORT_PREF_KEY, inputPort);
                    currentPort = inputPort;
                    
                    // Stop the server first
                    MCPBridgeLoader.StopServer();
                    
                    // Small delay to ensure port is released
                    System.Threading.Thread.Sleep(100);
                    
                    // Start with new port
                    MCPBridgeLoader.StartServer();
                    
                    EditorUtility.DisplayDialog("MCP Bridge Settings", 
                        $"Port updated to {currentPort}. Bridge server restarted.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("MCP Bridge Settings", 
                        "Port is already set to this value.", "OK");
                }
            }

            GUI.enabled = true; // Re-enable GUI elements

            GUILayout.Space(5);
            if (GUILayout.Button("Reset to Default"))
            {
                if (currentPort != DEFAULT_PORT)
                {
                    inputPort = DEFAULT_PORT;
                    EditorPrefs.SetInt(PORT_PREF_KEY, DEFAULT_PORT);
                    currentPort = DEFAULT_PORT;
                    EditorUtility.DisplayDialog("MCP Bridge Settings", $"Port reset to default ({DEFAULT_PORT}). Restarting bridge server...", "OK");
                    MCPBridgeLoader.StopServer();
                    MCPBridgeLoader.StartServer();
                }
                else
                {
                    EditorUtility.DisplayDialog("MCP Bridge Settings", "Port is already set to the default value.", "OK");
                }
            }
        }
    }
}
