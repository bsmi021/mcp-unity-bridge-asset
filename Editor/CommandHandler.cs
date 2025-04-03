using System;
using System.Collections.Generic;
using UnityEngine; // For Debug.Log/Error
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp; // Added for WebSocketState enum
// Required for Scene Management
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;


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
                    case "manage_asset":
                        // Pass correlationId to the handler
                        response = HandleManageAsset(parameters, correlationId);
                        break;
                    case "manage_gameobject":
                        // Pass correlationId to the handler
                        response = HandleManageGameObject(parameters, correlationId);
                        break;
                    case "manage_scene":
                        // Pass correlationId to the handler
                        response = HandleManageScene(parameters, correlationId);
                        break;
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

        private static CommandResponse HandleManageAsset(Dictionary<string, object> parameters, string correlationId)
        {
            Debug.Log(LOG_PREFIX + $"Handling 'manage_asset' command (ID: {correlationId}).");

            // 1. Extract and validate the 'action' parameter
            if (!parameters.TryGetValue("action", out object actionObj) || !(actionObj is string action))
            {
                return CommandResponse.Error("Missing or invalid 'action' parameter for manage_asset.", correlationId: correlationId);
            }

            // 2. Extract common parameters (handle potential nulls/missing keys)
            parameters.TryGetValue("path", out object pathObj);
            string path = pathObj as string; // Will be null if missing or wrong type

            parameters.TryGetValue("destination_path", out object destPathObj);
            string destinationPath = destPathObj as string;

            parameters.TryGetValue("asset_type", out object assetTypeObj);
            string assetType = assetTypeObj as string;

            parameters.TryGetValue("force", out object forceObj);
            // Safely convert to bool, default to false if missing or invalid
            bool force = forceObj is bool b ? b : false;

            // Extract source GameObject identifier (for create_prefab)
            Dictionary<string, object> sourceGoIdentifierDict = GetNestedParams(parameters, "source_gameobject_identifier");


            // 3. Switch based on action
            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "create_folder":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' parameter for create_folder action.", correlationId: correlationId);
                        }
                        // Example: path = "Assets/MyNewFolder" or "Assets/Existing/SubFolder"
                        string parentPath = System.IO.Path.GetDirectoryName(path);
                        string newFolderName = System.IO.Path.GetFileName(path);

                        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(newFolderName))
                        {
                            return CommandResponse.Error($"Invalid 'path' format for create_folder: '{path}'. Must include parent and new folder name.", correlationId: correlationId);
                        }

                        // Check if parent exists
                        if (!AssetDatabase.IsValidFolder(parentPath))
                        {
                            return CommandResponse.Error($"Parent folder does not exist: '{parentPath}'", correlationId: correlationId);
                        }

                        // Check if folder already exists
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            // Decide behavior: error or success if exists? Let's return success.
                            return CommandResponse.Success($"Folder '{path}' already exists.", new Dictionary<string, object> { { "path", path } }, correlationId);
                        }

                        // Create the folder
                        string guid = AssetDatabase.CreateFolder(parentPath, newFolderName);
                        if (string.IsNullOrEmpty(guid))
                        {
                            // This might happen if there's an internal Unity error or permissions issue
                            return CommandResponse.Error($"Failed to create folder '{path}'. Reason unknown.", correlationId: correlationId);
                        }
                        Debug.Log(LOG_PREFIX + $"Created folder '{path}' with GUID: {guid}");
                        return CommandResponse.Success($"Folder '{path}' created successfully.", new Dictionary<string, object> { { "path", path }, { "guid", guid } }, correlationId);

                    // --- Add other action cases below ---
                    case "create_asset":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' parameter for create_asset action.", correlationId: correlationId);
                        }
                        if (string.IsNullOrEmpty(assetType))
                        {
                            return CommandResponse.Error("Missing 'asset_type' parameter for create_asset action.", correlationId: correlationId);
                        }

                        // Validate path format (basic check)
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(path).IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                        {
                            return CommandResponse.Error($"Invalid asset path format: '{path}'. Must start with 'Assets/' and contain valid characters.", correlationId: correlationId);
                        }

                        // Check if parent directory exists
                        string assetParentPath = System.IO.Path.GetDirectoryName(path);
                        if (!AssetDatabase.IsValidFolder(assetParentPath))
                        {
                            return CommandResponse.Error($"Parent directory does not exist: '{assetParentPath}'", correlationId: correlationId);
                        }

                        // Check if asset already exists
                        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                        {
                            return CommandResponse.Error($"Asset already exists at path: '{path}'", correlationId: correlationId);
                        }

                        // --- Create the actual asset object ---
                        // This is complex as it depends heavily on 'asset_type'.
                        // Needs a robust way to instantiate different types.
                        UnityEngine.Object newAsset = null;
                        try
                        {
                            // Example: Creating a Material (requires a shader)
                            if (assetType.Equals("Material", StringComparison.OrdinalIgnoreCase))
                            {
                                Shader defaultShader = Shader.Find("Standard"); // Or another appropriate default
                                if (defaultShader == null)
                                {
                                    return CommandResponse.Error("Could not find default 'Standard' shader to create Material.", correlationId: correlationId);
                                }
                                newAsset = new Material(defaultShader);
                            }
                            // TODO: Add cases for other common asset types (ScriptableObject, CSharpScript, etc.)
                            // Example for ScriptableObject (requires knowing the specific type):
                            // else if (assetType == "MyScriptableObjectType") {
                            //     newAsset = ScriptableObject.CreateInstance("MyScriptableObjectType");
                            // }
                            else
                            {
                                // If type is unknown or unsupported for direct creation
                                return CommandResponse.Error($"Asset type '{assetType}' cannot be created directly via this tool yet.", correlationId: correlationId);
                            }

                            if (newAsset == null)
                            {
                                // Should ideally be caught by the specific type creation logic
                                return CommandResponse.Error($"Failed to instantiate asset of type '{assetType}'.", correlationId: correlationId);
                            }

                            // Create the asset file on disk
                            AssetDatabase.CreateAsset(newAsset, path);
                            AssetDatabase.SaveAssets(); // Ensure it's written
                            AssetDatabase.Refresh();    // Make sure Unity recognizes it

                            // Verify creation
                            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                            {
                                // Creation might have silently failed
                                return CommandResponse.Error($"Failed to create asset at '{path}' after AssetDatabase.CreateAsset call.", correlationId: correlationId);
                            }

                            string createdGuid = AssetDatabase.AssetPathToGUID(path);
                            Debug.Log(LOG_PREFIX + $"Created asset '{path}' of type '{assetType}' with GUID: {createdGuid}");
                            return CommandResponse.Success($"Asset '{path}' created successfully.", new Dictionary<string, object> { { "path", path }, { "guid", createdGuid }, { "type", assetType } }, correlationId);

                        }
                        catch (Exception createEx)
                        {
                            Debug.LogError(LOG_PREFIX + $"Exception during asset creation for type '{assetType}' at path '{path}': {createEx.Message}\n{createEx.StackTrace}");
                            // Clean up partially created asset if possible (though CreateAsset might handle this)
                            if (newAsset != null) UnityEngine.Object.DestroyImmediate(newAsset, true); // Important for editor objects
                            AssetDatabase.DeleteAsset(path); // Attempt to remove the file if created
                            AssetDatabase.Refresh();
                            return CommandResponse.Error($"Error creating asset '{path}': {createEx.Message}", correlationId: correlationId);
                        }


                    case "delete":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' parameter for delete action.", correlationId: correlationId);
                        }

                        // Check if the asset/folder actually exists before attempting deletion
                        if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                        {
                            // If it doesn't exist, consider it a success (idempotency) or an error?
                            // Let's return success for idempotency.
                            return CommandResponse.Success($"Asset or folder at '{path}' does not exist. No action taken.", null, correlationId);
                        }

                        // Attempt deletion
                        if (AssetDatabase.DeleteAsset(path))
                        {
                            Debug.Log(LOG_PREFIX + $"Deleted asset or folder at '{path}'.");
                            AssetDatabase.Refresh(); // Ensure Unity updates
                            return CommandResponse.Success($"Asset or folder '{path}' deleted successfully.", new Dictionary<string, object> { { "path", path } }, correlationId);
                        }
                        else
                        {
                            // Deletion failed - could be permissions, internal error, or maybe it was already deleted?
                            // Check again if it exists to differentiate
                            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                            {
                                return CommandResponse.Success($"Asset or folder at '{path}' was likely already deleted.", new Dictionary<string, object> { { "path", path } }, correlationId);
                            }
                            else
                            {
                                return CommandResponse.Error($"Failed to delete asset or folder at '{path}'. Reason unknown (check Unity console/permissions).", correlationId: correlationId);
                            }
                        }

                    case "move":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' (source) parameter for move action.", correlationId: correlationId);
                        }
                        if (string.IsNullOrEmpty(destinationPath))
                        {
                            return CommandResponse.Error("Missing 'destination_path' parameter for move action.", correlationId: correlationId);
                        }

                        // Validate paths (basic checks)
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            return CommandResponse.Error($"Invalid source path format: '{path}'. Must start with 'Assets/'.", correlationId: correlationId);
                        }
                        if (!destinationPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            return CommandResponse.Error($"Invalid destination path format: '{destinationPath}'. Must start with 'Assets/'.", correlationId: correlationId);
                        }

                        // Check if source exists
                        if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                        {
                            return CommandResponse.Error($"Source asset or folder does not exist: '{path}'", correlationId: correlationId);
                        }

                        // Check if destination parent directory exists
                        string destParentPath = System.IO.Path.GetDirectoryName(destinationPath);
                        if (!AssetDatabase.IsValidFolder(destParentPath))
                        {
                            return CommandResponse.Error($"Destination directory does not exist: '{destParentPath}'", correlationId: correlationId);
                        }

                        // Attempt the move. MoveAsset returns an error string if it fails (e.g., destination exists).
                        string moveError = AssetDatabase.MoveAsset(path, destinationPath);

                        if (string.IsNullOrEmpty(moveError))
                        {
                            Debug.Log(LOG_PREFIX + $"Moved asset or folder from '{path}' to '{destinationPath}'.");
                            AssetDatabase.Refresh(); // Ensure Unity updates
                            return CommandResponse.Success($"Asset or folder moved successfully from '{path}' to '{destinationPath}'.", new Dictionary<string, object> { { "sourcePath", path }, { "destinationPath", destinationPath } }, correlationId);
                        }
                        else
                        {
                            // Move failed, return the error message from Unity
                            Debug.LogError(LOG_PREFIX + $"Failed to move asset from '{path}' to '{destinationPath}': {moveError}");
                            return CommandResponse.Error($"Failed to move asset: {moveError}", correlationId: correlationId);
                        }

                    case "rename":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' (source) parameter for rename action.", correlationId: correlationId);
                        }
                        if (string.IsNullOrEmpty(destinationPath))
                        {
                            // For rename, destination_path is the *new name*, not a full path.
                            return CommandResponse.Error("Missing 'destination_path' (new name) parameter for rename action.", correlationId: correlationId);
                        }

                        // Validate source path
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            return CommandResponse.Error($"Invalid source path format: '{path}'. Must start with 'Assets/'.", correlationId: correlationId);
                        }

                        // Extract the new name from destinationPath. RenameAsset expects just the name, not a path.
                        string newName = System.IO.Path.GetFileNameWithoutExtension(destinationPath);
                        string newExtension = System.IO.Path.GetExtension(destinationPath); // Keep original extension if not provided in new name
                        if (string.IsNullOrEmpty(newName))
                        {
                            return CommandResponse.Error($"Invalid 'destination_path' (new name) format: '{destinationPath}'. Could not extract a valid name.", correlationId: correlationId);
                        }
                        // If the provided destination path included an extension, use it. Otherwise, keep the original.
                        if (string.IsNullOrEmpty(newExtension))
                        {
                            newExtension = System.IO.Path.GetExtension(path); // Get extension from original path
                        }
                        newName += newExtension; // Append the extension


                        // Check if source exists
                        if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                        {
                            return CommandResponse.Error($"Source asset or folder does not exist: '{path}'", correlationId: correlationId);
                        }

                        // Check if an asset with the new name already exists in the same directory
                        string directory = System.IO.Path.GetDirectoryName(path);
                        string potentialNewPath = System.IO.Path.Combine(directory, newName).Replace("\\", "/"); // Ensure forward slashes
                        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(potentialNewPath) != null || AssetDatabase.IsValidFolder(potentialNewPath))
                        {
                            return CommandResponse.Error($"An asset or folder with the name '{newName}' already exists in '{directory}'.", correlationId: correlationId);
                        }


                        // Attempt the rename. RenameAsset returns an error string if it fails.
                        string renameError = AssetDatabase.RenameAsset(path, newName); // Pass only the new name

                        if (string.IsNullOrEmpty(renameError))
                        {
                            Debug.Log(LOG_PREFIX + $"Renamed asset or folder '{path}' to '{newName}'.");
                            AssetDatabase.Refresh(); // Ensure Unity updates
                            // Return the *new* path in the response data
                            string finalNewPath = AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(potentialNewPath)); // Get the definite path after rename
                            return CommandResponse.Success($"Asset or folder renamed successfully to '{newName}'.", new Dictionary<string, object> { { "oldPath", path }, { "newPath", finalNewPath ?? potentialNewPath } }, correlationId);
                        }
                        else
                        {
                            // Rename failed, return the error message from Unity
                            Debug.LogError(LOG_PREFIX + $"Failed to rename asset '{path}' to '{newName}': {renameError}");
                            return CommandResponse.Error($"Failed to rename asset: {renameError}", correlationId: correlationId);
                        }


                    case "import":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' (source file system path) parameter for import action.", correlationId: correlationId);
                        }
                        if (string.IsNullOrEmpty(destinationPath))
                        {
                            return CommandResponse.Error("Missing 'destination_path' (Unity project path) parameter for import action.", correlationId: correlationId);
                        }

                        // Validate destination path
                        if (!destinationPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            return CommandResponse.Error($"Invalid destination path format: '{destinationPath}'. Must start with 'Assets/'.", correlationId: correlationId);
                        }

                        // Check if source file exists
                        if (!System.IO.File.Exists(path))
                        {
                            return CommandResponse.Error($"Source file does not exist: '{path}'", correlationId: correlationId);
                        }

                        // Check if destination directory exists in Unity
                        string importDestDir = System.IO.Path.GetDirectoryName(destinationPath);
                        if (!AssetDatabase.IsValidFolder(importDestDir))
                        {
                            return CommandResponse.Error($"Destination directory does not exist in Unity project: '{importDestDir}'", correlationId: correlationId);
                        }

                        // Check if destination file already exists in Unity
                        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destinationPath) != null)
                        {
                            // TODO: Add overwrite behavior based on 'force' parameter? For now, error out.
                            if (!force)
                            {
                                return CommandResponse.Error($"Asset already exists at destination path: '{destinationPath}'. Use 'force: true' to overwrite (not implemented yet).", correlationId: correlationId);
                            }
                            else
                            {
                                // Overwrite logic (delete existing first) - Needs careful implementation
                                Debug.LogWarning(LOG_PREFIX + $"Force importing: Deleting existing asset at '{destinationPath}' before import.");
                                if (!AssetDatabase.DeleteAsset(destinationPath))
                                {
                                    return CommandResponse.Error($"Failed to delete existing asset at '{destinationPath}' during force import.", correlationId: correlationId);
                                }
                                AssetDatabase.Refresh(); // Ensure deletion is processed
                            }
                        }

                        // Perform the file copy
                        try
                        {
                            // Get the full system path for the destination within the Unity project
                            string fullDestinationPath = System.IO.Path.Combine(Application.dataPath, destinationPath.Substring("Assets/".Length));
                            // Ensure the directory exists on the filesystem just in case Unity's check wasn't enough
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullDestinationPath));

                            System.IO.File.Copy(path, fullDestinationPath, force); // Use 'force' for overwrite flag
                            Debug.Log(LOG_PREFIX + $"Copied file from '{path}' to '{fullDestinationPath}'.");
                        }
                        catch (System.IO.IOException ioEx)
                        {
                            // Catch specific IO errors like file already exists if 'force' is false
                            Debug.LogError(LOG_PREFIX + $"IO Error importing file to '{destinationPath}': {ioEx.Message}");
                            return CommandResponse.Error($"IO Error importing file: {ioEx.Message}", correlationId: correlationId);
                        }
                        catch (Exception copyEx)
                        {
                            Debug.LogError(LOG_PREFIX + $"Error copying file from '{path}' to '{destinationPath}': {copyEx.Message}\n{copyEx.StackTrace}");
                            return CommandResponse.Error($"Error copying file for import: {copyEx.Message}", correlationId: correlationId);
                        }

                        // Tell Unity to recognize the new asset
                        AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate); // Use ImportAsset to ensure it's processed correctly
                        AssetDatabase.Refresh(); // Refresh might be redundant after ImportAsset, but can help ensure consistency

                        // Verify import
                        UnityEngine.Object importedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destinationPath);
                        if (importedAsset == null)
                        {
                            return CommandResponse.Error($"Asset import failed or was not recognized by Unity at '{destinationPath}' after copy.", correlationId: correlationId);
                        }

                        string importedGuid = AssetDatabase.AssetPathToGUID(destinationPath);
                        Debug.Log(LOG_PREFIX + $"Imported asset '{destinationPath}' with GUID: {importedGuid}");
                        return CommandResponse.Success($"Asset imported successfully to '{destinationPath}'.", new Dictionary<string, object> { { "sourcePath", path }, { "destinationPath", destinationPath }, { "guid", importedGuid } }, correlationId);


                    case "get_info":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' parameter for get_info action.", correlationId: correlationId);
                        }

                        // Check if it's a folder first
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            string folderGuid = AssetDatabase.AssetPathToGUID(path);
                            var folderInfo = new Dictionary<string, object>
                            {
                                { "path", path },
                                { "type", "Folder" },
                                { "guid", folderGuid },
                                { "isFolder", true }
                            };
                            return CommandResponse.Success($"Information retrieved for folder '{path}'.", folderInfo, correlationId);
                        }

                        // Try loading as an asset
                        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset == null)
                        {
                            return CommandResponse.Error($"Asset or folder not found at path: '{path}'", correlationId: correlationId);
                        }

                        // Gather basic info
                        string assetGuid = AssetDatabase.AssetPathToGUID(path);
                        string assetActualType = asset.GetType().FullName; // Get the actual C# type name

                        var assetInfo = new Dictionary<string, object>
                        {
                            { "path", path },
                            { "type", assetActualType },
                            { "guid", assetGuid },
                            { "isFolder", false },
                            { "name", asset.name } // Asset name might differ from file name
                        };

                        // Try to get importer info (might be null for some asset types)
                        AssetImporter importer = AssetImporter.GetAtPath(path);
                        if (importer != null)
                        {
                            assetInfo.Add("importerType", importer.GetType().FullName);
                            // TODO: Could potentially serialize importer settings here if needed,
                            // but that can get complex. Example:
                            // assetInfo.Add("importerUserData", importer.userData);
                            // assetInfo.Add("importerAssetBundleName", importer.assetBundleName);
                        }

                        return CommandResponse.Success($"Information retrieved for asset '{path}'.", assetInfo, correlationId);


                    case "find_usages":
                        if (string.IsNullOrEmpty(path))
                        {
                            return CommandResponse.Error("Missing 'path' parameter for find_usages action.", correlationId: correlationId);
                        }

                        // Get the GUID of the target asset
                        string targetGuid = AssetDatabase.AssetPathToGUID(path);
                        if (string.IsNullOrEmpty(targetGuid))
                        {
                            // Check if it's a folder, which doesn't have direct dependencies in the same way
                            if (AssetDatabase.IsValidFolder(path))
                            {
                                return CommandResponse.Success($"'{path}' is a folder. Finding direct asset usages is not applicable.", new Dictionary<string, object> { { "path", path }, { "usages", new List<string>() } }, correlationId);
                            }
                            else
                            {
                                return CommandResponse.Error($"Asset or folder not found at path: '{path}'", correlationId: correlationId);
                            }
                        }

                        List<string> usagePaths = new List<string>();
                        // Search potentially relevant assets: Scenes and Prefabs are common places for dependencies.
                        // Add other types like ScriptableObjects if needed: "t:Scene t:Prefab t:ScriptableObject"
                        string[] searchGuids = AssetDatabase.FindAssets("t:Scene t:Prefab", null); // Search all folders

                        foreach (string searchGuid in searchGuids)
                        {
                            string assetPath = AssetDatabase.GUIDToAssetPath(searchGuid);
                            // Load the asset to check its dependencies
                            UnityEngine.Object[] dependencies = EditorUtility.CollectDependencies(new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) });

                            foreach (UnityEngine.Object dep in dependencies)
                            {
                                if (dep == null) continue;
                                string depPath = AssetDatabase.GetAssetPath(dep);
                                string depGuid = AssetDatabase.AssetPathToGUID(depPath);

                                // Check if the dependency GUID matches the target asset's GUID
                                if (!string.IsNullOrEmpty(depGuid) && depGuid.Equals(targetGuid))
                                {
                                    if (!usagePaths.Contains(assetPath)) // Avoid duplicates
                                    {
                                        usagePaths.Add(assetPath);
                                    }
                                    break; // Found usage in this asset, move to the next search result
                                }
                            }
                        }

                        Debug.Log(LOG_PREFIX + $"Found {usagePaths.Count} usage(s) for asset '{path}'.");
                        return CommandResponse.Success($"Found {usagePaths.Count} usage(s) for asset '{path}'.", new Dictionary<string, object> { { "path", path }, { "guid", targetGuid }, { "usages", usagePaths } }, correlationId);

                    case "create_prefab":
                        if (string.IsNullOrEmpty(path)) return CommandResponse.Error("Missing 'path' parameter for create_prefab action.", correlationId: correlationId);
                        if (!path.ToLowerInvariant().EndsWith(".prefab")) return CommandResponse.Error("Invalid 'path' for create_prefab action. Must end with '.prefab'.", correlationId: correlationId);
                        if (sourceGoIdentifierDict == null) return CommandResponse.Error("Missing 'source_gameobject_identifier' parameter for create_prefab action.", correlationId: correlationId);

                        // Find source GameObject
                        GameObject sourceGo = FindGameObjectByIdentifier(sourceGoIdentifierDict);
                        if (sourceGo == null)
                        {
                            string identifierDesc = sourceGoIdentifierDict.ContainsKey("instance_id") ? $"instance ID {sourceGoIdentifierDict["instance_id"]}"
                                                    : sourceGoIdentifierDict.ContainsKey("path") ? $"path '{sourceGoIdentifierDict["path"]}'"
                                                    : sourceGoIdentifierDict.ContainsKey("name") ? $"name '{sourceGoIdentifierDict["name"]}'"
                                                    : "the specified criteria";
                            return CommandResponse.Error($"Source GameObject not found matching {identifierDesc} for create_prefab action.", correlationId: correlationId);
                        }

                        // Validate prefab path
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            return CommandResponse.Error($"Invalid prefab path format: '{path}'. Must start with 'Assets/'.", correlationId: correlationId);
                        }
                        string prefabDir = System.IO.Path.GetDirectoryName(path);
                        if (!AssetDatabase.IsValidFolder(prefabDir))
                        {
                            return CommandResponse.Error($"Target directory does not exist: '{prefabDir}'", correlationId: correlationId);
                        }
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                        {
                            return CommandResponse.Error($"Prefab already exists at path: '{path}'", correlationId: correlationId);
                        }

                        // Create the prefab
                        try
                        {
                            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(sourceGo, path, InteractionMode.UserAction, out bool success);

                            if (success && prefabAsset != null)
                            {
                                Debug.Log(LOG_PREFIX + $"Created prefab '{path}' from GameObject '{sourceGo.name}'.");
                                string prefabGuid = AssetDatabase.AssetPathToGUID(path);
                                return CommandResponse.Success($"Prefab '{path}' created successfully.", new Dictionary<string, object> { { "path", path }, { "guid", prefabGuid } }, correlationId);
                            }
                            else
                            {
                                return CommandResponse.Error($"Failed to create prefab at '{path}'. PrefabUtility.SaveAsPrefabAssetAndConnect returned failure.", correlationId: correlationId);
                            }
                        }
                        catch (Exception prefabEx)
                        {
                             Debug.LogError(LOG_PREFIX + $"Exception during prefab creation for '{sourceGo.name}' at path '{path}': {prefabEx.Message}\n{prefabEx.StackTrace}");
                             return CommandResponse.Error($"Error creating prefab '{path}': {prefabEx.Message}", correlationId: correlationId);
                        }


                    default:
                        return CommandResponse.Error($"Unsupported action '{action}' for manage_asset.", correlationId: correlationId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Exception during manage_asset action '{action}': {ex.Message}\n{ex.StackTrace}");
                return CommandResponse.Error($"Error executing manage_asset action '{action}': {ex.Message}", correlationId: correlationId);
            }
        }

        #region GameObject Helpers

        // Helper to parse Vector3 from dictionary
        private static bool TryParseVector3(object vectorObj, out Vector3 result)
        {
            result = Vector3.zero;
            if (vectorObj is JObject jObj) // Check if it's a JObject from Newtonsoft
            {
                float x = jObj.Value<float?>("x") ?? 0f;
                float y = jObj.Value<float?>("y") ?? 0f;
                float z = jObj.Value<float?>("z") ?? 0f;
                result = new Vector3(x, y, z);
                return true;
            }
            else if (vectorObj is Dictionary<string, object> dict) // Fallback for simple dictionary
            {
                float x = dict.TryGetValue("x", out object xVal) && xVal is IConvertible ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) && yVal is IConvertible ? Convert.ToSingle(yVal) : 0f;
                float z = dict.TryGetValue("z", out object zVal) && zVal is IConvertible ? Convert.ToSingle(zVal) : 0f;
                result = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        // Helper to find GameObject based on identifier dictionary
        private static GameObject FindGameObjectByIdentifier(Dictionary<string, object> identifierDict)
        {
            if (identifierDict == null) return null;

            // Priority: Instance ID > Path > Name
            if (identifierDict.TryGetValue("instance_id", out object idObj) && idObj is IConvertible idConv)
            {
                try
                {
                    int instanceId = Convert.ToInt32(idConv);
                    // FindObjectFromInstanceID is deprecated, use EditorUtility.InstanceIDToObject
                    UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
                    return obj as GameObject; // Returns null if not found or not a GameObject
                }
                catch { /* Ignore conversion errors */ }
            }

            if (identifierDict.TryGetValue("path", out object pathObj) && pathObj is string path && !string.IsNullOrEmpty(path))
            {
                // GameObject.Find uses name search, not path. We need to traverse.
                // This is a simplified traversal, might need improvement for complex paths.
                string[] parts = path.Split('/');
                Transform current = null;
                foreach (string part in parts)
                {
                    if (current == null) // Find root object
                    {
                        // FindObjectsOfType is slow, Find may be sufficient if unique root name
                        // GameObject root = GameObject.Find(part);
                        // Safer: Iterate root objects
                        GameObject root = null;
                        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                        {
                            if (go.name == part)
                            {
                                root = go;
                                break;
                            }
                        }
                        if (root == null) return null; // Root part not found
                        current = root.transform;
                    }
                    else // Find child
                    {
                        Transform child = current.Find(part);
                        if (child == null) return null; // Child part not found
                        current = child;
                    }
                }
                return current?.gameObject;
            }

            if (identifierDict.TryGetValue("name", out object nameObj) && nameObj is string name && !string.IsNullOrEmpty(name))
            {
                return GameObject.Find(name); // Note: Finds only active objects, might need FindObjectsOfType for inactive
            }

            return null; // No valid identifier found or object not found
        }

        // Helper to parse nested dictionary parameters (like create_params)
        private static Dictionary<string, object> GetNestedParams(Dictionary<string, object> parentParams, string key)
        {
            if (parentParams.TryGetValue(key, out object nestedObj))
            {
                // Newtonsoft.Json often deserializes nested objects as JObject
                if (nestedObj is JObject jObj)
                {
                    return jObj.ToObject<Dictionary<string, object>>();
                }
                // Handle case where it might already be a dictionary (less likely with Newtonsoft default)
                else if (nestedObj is Dictionary<string, object> dict)
                {
                    return dict;
                }
            }
            return null;
        }


        #endregion

        private static CommandResponse HandleManageGameObject(Dictionary<string, object> parameters, string correlationId)
        {
            Debug.Log(LOG_PREFIX + $"Handling 'manage_gameobject' command (ID: {correlationId}).");

            // 1. Extract Action
            if (!parameters.TryGetValue("action", out object actionObj) || !(actionObj is string action))
            {
                return CommandResponse.Error("Missing or invalid 'action' parameter for manage_gameobject.", correlationId: correlationId);
            }

            // 2. Extract Optional Scene Path (TODO: Implement scene loading/switching if provided)
            parameters.TryGetValue("scene_path", out object scenePathObj);
            string scenePath = scenePathObj as string;
            if (!string.IsNullOrEmpty(scenePath))
            {
                Debug.LogWarning(LOG_PREFIX + $"Scene switching based on 'scene_path' ({scenePath}) is not yet implemented. Operating on active scene.");
                // TODO: Add logic to load scene 'scenePath' before proceeding
            }

            // 3. Extract Optional Identifier (required by most actions)
            Dictionary<string, object> identifierDict = GetNestedParams(parameters, "identifier");

            // 4. Extract Optional Action-Specific Params
            Dictionary<string, object> createParamsDict = GetNestedParams(parameters, "create_params");
            Dictionary<string, object> modifyParamsDict = GetNestedParams(parameters, "modify_params");
            Dictionary<string, object> componentParamsDict = GetNestedParams(parameters, "component_params");


            // 5. Switch based on action
            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "create":
                        if (createParamsDict == null)
                        {
                            return CommandResponse.Error("Missing 'create_params' for create action.", correlationId: correlationId);
                        }

                        // Extract create parameters
                        if (!createParamsDict.TryGetValue("type", out object typeObj) || !(typeObj is string createType))
                        {
                            return CommandResponse.Error("Missing or invalid 'type' in create_params.", correlationId: correlationId);
                        }

                        createParamsDict.TryGetValue("name", out object nameObj);
                        string goName = nameObj as string; // Can be null

                        createParamsDict.TryGetValue("primitive_type", out object primitiveTypeObj);
                        string primitiveType = primitiveTypeObj as string; // Required if createType is 'primitive'

                        createParamsDict.TryGetValue("prefab_path", out object prefabPathObj);
                        string prefabPath = prefabPathObj as string; // Required if createType is 'prefab'

                        Dictionary<string, object> parentIdentifierDict = GetNestedParams(createParamsDict, "parent_identifier");
                        GameObject parentGo = FindGameObjectByIdentifier(parentIdentifierDict); // Can be null for root

                        TryParseVector3(createParamsDict.GetValueOrDefault("position"), out Vector3 position);
                        TryParseVector3(createParamsDict.GetValueOrDefault("rotation"), out Vector3 rotation);
                        TryParseVector3(createParamsDict.GetValueOrDefault("scale"), out Vector3 scale);
                        bool hasScale = createParamsDict.ContainsKey("scale"); // Need to know if scale was explicitly provided

                        GameObject newGo = null;

                        // --- Instantiate based on type ---
                        switch (createType.ToLowerInvariant())
                        {
                            case "empty":
                                newGo = new GameObject(string.IsNullOrEmpty(goName) ? "GameObject" : goName);
                                break;

                            case "primitive":
                                if (string.IsNullOrEmpty(primitiveType)) return CommandResponse.Error("Missing 'primitive_type' for primitive creation.", correlationId: correlationId);
                                PrimitiveType pt;
                                if (!Enum.TryParse<PrimitiveType>(primitiveType, true, out pt))
                                {
                                    return CommandResponse.Error($"Invalid 'primitive_type': {primitiveType}", correlationId: correlationId);
                                }
                                newGo = GameObject.CreatePrimitive(pt);
                                if (!string.IsNullOrEmpty(goName)) newGo.name = goName;
                                break;

                            case "prefab":
                                if (string.IsNullOrEmpty(prefabPath)) return CommandResponse.Error("Missing 'prefab_path' for prefab creation.", correlationId: correlationId);
                                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                                if (prefab == null) return CommandResponse.Error($"Prefab not found at path: {prefabPath}", correlationId: correlationId);
                                // InstantiatePrefab preserves the prefab connection
                                newGo = PrefabUtility.InstantiatePrefab(prefab, parentGo?.transform) as GameObject;
                                if (newGo == null) return CommandResponse.Error($"Failed to instantiate prefab: {prefabPath}", correlationId: correlationId);
                                if (!string.IsNullOrEmpty(goName)) newGo.name = goName; // Override name if provided
                                break;

                            default:
                                return CommandResponse.Error($"Unsupported creation type: {createType}", correlationId: correlationId);
                        }

                        if (newGo == null) // Should be caught above, but safety check
                        {
                            return CommandResponse.Error("Failed to create GameObject.", correlationId: correlationId);
                        }

                        // --- Set Parent and Transform ---
                        // Set parent *before* setting local transform values if parent exists
                        if (parentGo != null && createType != "prefab") // Prefab instantiation already handles parent
                        {
                            newGo.transform.SetParent(parentGo.transform, false); // worldPositionStays = false
                        }

                        // Set local transform properties
                        newGo.transform.localPosition = position;
                        newGo.transform.localEulerAngles = rotation;
                        if (hasScale) // Only set scale if provided, otherwise use default
                        {
                             newGo.transform.localScale = scale;
                        } else if (createType == "empty") { // Default scale for empty GO is (1,1,1)
                             newGo.transform.localScale = Vector3.one;
                        } // Primitives and Prefabs have their own default scales

                        // Ensure the object is part of the scene hierarchy if created at root without a parent
                        if (newGo.transform.parent == null && parentGo == null) {
                             // This might not be strictly necessary if created in active scene, but good practice
                             // UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(newGo, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        }

                        // Mark scene as dirty? Maybe not necessary for simple creation.
                        // UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(newGo.scene);

                        Debug.Log(LOG_PREFIX + $"Created GameObject '{newGo.name}' (Instance ID: {newGo.GetInstanceID()})");
                        return CommandResponse.Success($"GameObject '{newGo.name}' created successfully.", new Dictionary<string, object> { { "name", newGo.name }, { "instance_id", newGo.GetInstanceID() } }, correlationId);


                    case "find":
                        if (identifierDict == null)
                        {
                            return CommandResponse.Error("Missing 'identifier' parameter for find action.", correlationId: correlationId);
                        }

                        // Use the helper to find the GameObject
                        GameObject foundGo = FindGameObjectByIdentifier(identifierDict);

                        if (foundGo == null)
                        {
                            // Construct a meaningful "not found" message based on the identifier provided
                            string identifierDesc = identifierDict.ContainsKey("instance_id") ? $"instance ID {identifierDict["instance_id"]}"
                                                    : identifierDict.ContainsKey("path") ? $"path '{identifierDict["path"]}'"
                                                    : identifierDict.ContainsKey("name") ? $"name '{identifierDict["name"]}'"
                                                    : "the specified criteria";
                            return CommandResponse.Error($"GameObject not found matching {identifierDesc}.", correlationId: correlationId);
                        }

                        // TODO: Implement find_multiple logic later if needed. For now, return the first found.

                        Debug.Log(LOG_PREFIX + $"Found GameObject '{foundGo.name}' (Instance ID: {foundGo.GetInstanceID()})");
                        // Return basic info about the found object
                        var foundData = new Dictionary<string, object>
                        {
                            { "name", foundGo.name },
                            { "instance_id", foundGo.GetInstanceID() },
                            // Could add tag, layer, path, etc. here if useful
                        };
                        return CommandResponse.Success($"GameObject '{foundGo.name}' found.", foundData, correlationId);


                    case "modify":
                        if (identifierDict == null) return CommandResponse.Error("Missing 'identifier' parameter for modify action.", correlationId: correlationId);
                        if (modifyParamsDict == null) return CommandResponse.Error("Missing 'modify_params' for modify action.", correlationId: correlationId);

                        // Find the target GameObject
                        GameObject targetGo = FindGameObjectByIdentifier(identifierDict);
                        if (targetGo == null)
                        {
                             string identifierDesc = identifierDict.ContainsKey("instance_id") ? $"instance ID {identifierDict["instance_id"]}"
                                                    : identifierDict.ContainsKey("path") ? $"path '{identifierDict["path"]}'"
                                                    : identifierDict.ContainsKey("name") ? $"name '{identifierDict["name"]}'"
                                                    : "the specified criteria";
                            return CommandResponse.Error($"Target GameObject not found matching {identifierDesc} for modify action.", correlationId: correlationId);
                        }

                        bool modified = false;

                        // Apply modifications
                        if (modifyParamsDict.TryGetValue("name", out object newNameObj) && newNameObj is string newNameStr && !string.IsNullOrEmpty(newNameStr))
                        {
                            targetGo.name = newNameStr;
                            modified = true;
                        }
                        if (modifyParamsDict.TryGetValue("tag", out object newTagObj) && newTagObj is string newTagStr) // Allow empty tag? Unity default is "Untagged"
                        {
                             try { targetGo.tag = newTagStr; modified = true; } catch (UnityException ex) { return CommandResponse.Error($"Failed to set tag: {ex.Message}", correlationId: correlationId); } // Catch if tag doesn't exist
                        }
                         if (modifyParamsDict.TryGetValue("layer", out object newLayerObj) && newLayerObj is IConvertible newLayerConv)
                        {
                             try { targetGo.layer = Convert.ToInt32(newLayerConv); modified = true; } catch { return CommandResponse.Error("Invalid 'layer' value.", correlationId: correlationId); }
                        }
                         if (modifyParamsDict.TryGetValue("active", out object newActiveObj) && newActiveObj is bool newActiveBool)
                        {
                            targetGo.SetActive(newActiveBool);
                            modified = true;
                        }

                        // Handle reparenting
                        if (modifyParamsDict.ContainsKey("parent_identifier")) // Key exists, even if value is null
                        {
                             Dictionary<string, object> newParentIdentifierDict = GetNestedParams(modifyParamsDict, "parent_identifier");
                             GameObject newParentGo = FindGameObjectByIdentifier(newParentIdentifierDict); // Will be null if identifier is null or not found

                             if (newParentIdentifierDict != null && newParentGo == null) {
                                 // An identifier was provided, but the parent wasn't found
                                 return CommandResponse.Error("Specified new parent GameObject not found.", correlationId: correlationId);
                             }
                             // Check for hierarchy loops
                             if (newParentGo != null && newParentGo.transform.IsChildOf(targetGo.transform)) {
                                 return CommandResponse.Error("Cannot parent GameObject to one of its own descendants.", correlationId: correlationId);
                             }

                             targetGo.transform.SetParent(newParentGo?.transform, true); // worldPositionStays = true for modify
                             modified = true;
                        }

                        // Apply transform changes (relative to new parent if reparented)
                        if (modifyParamsDict.TryGetValue("position", out object newPosObj) && TryParseVector3(newPosObj, out Vector3 newPosition))
                        {
                            targetGo.transform.localPosition = newPosition;
                            modified = true;
                        }
                        if (modifyParamsDict.TryGetValue("rotation", out object newRotObj) && TryParseVector3(newRotObj, out Vector3 newRotation))
                        {
                            targetGo.transform.localEulerAngles = newRotation;
                            modified = true;
                        }
                        if (modifyParamsDict.TryGetValue("scale", out object newScaleObj) && TryParseVector3(newScaleObj, out Vector3 newScale))
                        {
                            targetGo.transform.localScale = newScale;
                            modified = true;
                        }

                        if (!modified)
                        {
                             return CommandResponse.Error("No valid properties provided in 'modify_params'.", correlationId: correlationId);
                        }

                        // Mark scene dirty after modifications
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetGo.scene);

                        Debug.Log(LOG_PREFIX + $"Modified GameObject '{targetGo.name}' (Instance ID: {targetGo.GetInstanceID()})");
                         var modifiedData = new Dictionary<string, object>
                        {
                            { "name", targetGo.name },
                            { "instance_id", targetGo.GetInstanceID() },
                        };
                        return CommandResponse.Success($"GameObject '{targetGo.name}' modified successfully.", modifiedData, correlationId);


                    case "add_component":
                        if (identifierDict == null) return CommandResponse.Error("Missing 'identifier' parameter for add_component action.", correlationId: correlationId);
                        if (componentParamsDict == null) return CommandResponse.Error("Missing 'component_params' for add_component action.", correlationId: correlationId);

                        // Extract component type name
                        if (!componentParamsDict.TryGetValue("component_type_name", out object compTypeNameObj) || !(compTypeNameObj is string compTypeName) || string.IsNullOrEmpty(compTypeName))
                        {
                            return CommandResponse.Error("Missing or invalid 'component_type_name' in component_params.", correlationId: correlationId);
                        }

                        // Find the target GameObject
                        GameObject goToAddComp = FindGameObjectByIdentifier(identifierDict);
                        if (goToAddComp == null)
                        {
                             string identifierDesc = identifierDict.ContainsKey("instance_id") ? $"instance ID {identifierDict["instance_id"]}"
                                                    : identifierDict.ContainsKey("path") ? $"path '{identifierDict["path"]}'"
                                                    : identifierDict.ContainsKey("name") ? $"name '{identifierDict["name"]}'"
                                                    : "the specified criteria";
                            return CommandResponse.Error($"Target GameObject not found matching {identifierDesc} for add_component action.", correlationId: correlationId);
                        }

                        // Find the Type using reflection. This requires the fully qualified name.
                        // May need assembly qualification for user scripts (e.g., "MyNamespace.MyScript, Assembly-CSharp")
                        Type componentType = Type.GetType(compTypeName); // Basic search
                        if (componentType == null)
                        {
                            // Try searching loaded assemblies for better script support
                            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                componentType = assembly.GetType(compTypeName);
                                if (componentType != null) break;
                            }
                        }

                        if (componentType == null)
                        {
                            return CommandResponse.Error($"Component type '{compTypeName}' not found. Ensure it's a valid type name (including namespace) accessible in the current AppDomain.", correlationId: correlationId);
                        }

                        // Check if it's actually a Component type
                        if (!typeof(Component).IsAssignableFrom(componentType)) {
                             return CommandResponse.Error($"Type '{compTypeName}' is not a valid Component.", correlationId: correlationId);
                        }

                        // Check if component already exists (AddComponent doesn't add duplicates of the same type)
                        Component existingComponent = goToAddComp.GetComponent(componentType);
                        if (existingComponent != null) {
                             Debug.Log(LOG_PREFIX + $"Component '{compTypeName}' already exists on GameObject '{goToAddComp.name}'. No action taken.");
                             return CommandResponse.Success($"Component '{compTypeName}' already exists on GameObject '{goToAddComp.name}'.", new Dictionary<string, object> { { "name", goToAddComp.name }, { "instance_id", goToAddComp.GetInstanceID() }, { "component_type", compTypeName } }, correlationId);
                        }

                        // Add the component
                        Component addedComponent = goToAddComp.AddComponent(componentType);

                        if (addedComponent == null)
                        {
                            // This might happen if the component has specific requirements not met (e.g., [RequireComponent])
                            return CommandResponse.Error($"Failed to add component '{compTypeName}' to GameObject '{goToAddComp.name}'. Check for dependencies or other constraints.", correlationId: correlationId);
                        }

                        // Mark scene dirty
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(goToAddComp.scene);

                        Debug.Log(LOG_PREFIX + $"Added component '{compTypeName}' to GameObject '{goToAddComp.name}'.");
                        return CommandResponse.Success($"Component '{compTypeName}' added successfully.", new Dictionary<string, object> { { "name", goToAddComp.name }, { "instance_id", goToAddComp.GetInstanceID() }, { "component_type", compTypeName } }, correlationId);


                    case "remove_component":
                        if (identifierDict == null) return CommandResponse.Error("Missing 'identifier' parameter for remove_component action.", correlationId: correlationId);
                        if (componentParamsDict == null) return CommandResponse.Error("Missing 'component_params' for remove_component action.", correlationId: correlationId);

                        // Extract component type name
                        if (!componentParamsDict.TryGetValue("component_type_name", out object compTypeNameToRemoveObj) || !(compTypeNameToRemoveObj is string compTypeNameToRemove) || string.IsNullOrEmpty(compTypeNameToRemove))
                        {
                            return CommandResponse.Error("Missing or invalid 'component_type_name' in component_params.", correlationId: correlationId);
                        }

                        // Find the target GameObject
                        GameObject goToRemoveComp = FindGameObjectByIdentifier(identifierDict);
                        if (goToRemoveComp == null)
                        {
                             string identifierDesc = identifierDict.ContainsKey("instance_id") ? $"instance ID {identifierDict["instance_id"]}"
                                                    : identifierDict.ContainsKey("path") ? $"path '{identifierDict["path"]}'"
                                                    : identifierDict.ContainsKey("name") ? $"name '{identifierDict["name"]}'"
                                                    : "the specified criteria";
                            return CommandResponse.Error($"Target GameObject not found matching {identifierDesc} for remove_component action.", correlationId: correlationId);
                        }

                        // Find the Type using reflection
                        Type componentTypeToRemove = Type.GetType(compTypeNameToRemove);
                        if (componentTypeToRemove == null)
                        {
                            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                componentTypeToRemove = assembly.GetType(compTypeNameToRemove);
                                if (componentTypeToRemove != null) break;
                            }
                        }

                        if (componentTypeToRemove == null)
                        {
                            return CommandResponse.Error($"Component type '{compTypeNameToRemove}' not found.", correlationId: correlationId);
                        }
                         if (!typeof(Component).IsAssignableFrom(componentTypeToRemove)) {
                             return CommandResponse.Error($"Type '{compTypeNameToRemove}' is not a valid Component.", correlationId: correlationId);
                        }

                        // Find the component instance
                        Component componentToRemove = goToRemoveComp.GetComponent(componentTypeToRemove);

                        if (componentToRemove == null)
                        {
                            Debug.Log(LOG_PREFIX + $"Component '{compTypeNameToRemove}' not found on GameObject '{goToRemoveComp.name}'. No action taken.");
                            return CommandResponse.Success($"Component '{compTypeNameToRemove}' not found on GameObject '{goToRemoveComp.name}'.", new Dictionary<string, object> { { "name", goToRemoveComp.name }, { "instance_id", goToRemoveComp.GetInstanceID() }, { "component_type", compTypeNameToRemove } }, correlationId);
                        }

                        // Remove the component
                        // Use DestroyImmediate for Editor operations
                        UnityEngine.Object.DestroyImmediate(componentToRemove, true); // Allow destroying assets is true, though maybe not needed here

                        // Verify removal (check if it's null immediately after DestroyImmediate)
                        if (goToRemoveComp.GetComponent(componentTypeToRemove) != null) {
                             // This might happen if the component cannot be removed (e.g., Transform, or required by others)
                             return CommandResponse.Error($"Failed to remove component '{compTypeNameToRemove}' from GameObject '{goToRemoveComp.name}'. It might be required or removal failed.", correlationId: correlationId);
                        }

                        // Mark scene dirty
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(goToRemoveComp.scene);

                        Debug.Log(LOG_PREFIX + $"Removed component '{compTypeNameToRemove}' from GameObject '{goToRemoveComp.name}'.");
                        return CommandResponse.Success($"Component '{compTypeNameToRemove}' removed successfully.", new Dictionary<string, object> { { "name", goToRemoveComp.name }, { "instance_id", goToRemoveComp.GetInstanceID() }, { "component_type", compTypeNameToRemove } }, correlationId);


                    case "delete":
                        if (identifierDict == null) return CommandResponse.Error("Missing 'identifier' parameter for delete action.", correlationId: correlationId);

                        // Find the target GameObject
                        GameObject goToDelete = FindGameObjectByIdentifier(identifierDict);
                        if (goToDelete == null)
                        {
                             // If not found, consider it success (idempotency)
                             string identifierDesc = identifierDict.ContainsKey("instance_id") ? $"instance ID {identifierDict["instance_id"]}"
                                                    : identifierDict.ContainsKey("path") ? $"path '{identifierDict["path"]}'"
                                                    : identifierDict.ContainsKey("name") ? $"name '{identifierDict["name"]}'"
                                                    : "the specified criteria";
                             Debug.Log(LOG_PREFIX + $"GameObject not found matching {identifierDesc} for delete action. No action taken.");
                             return CommandResponse.Success($"GameObject not found matching {identifierDesc}. No action taken.", null, correlationId);
                        }

                        string deletedName = goToDelete.name;
                        int deletedInstanceId = goToDelete.GetInstanceID();
                        UnityEngine.SceneManagement.Scene scene = goToDelete.scene; // Get scene before destroying

                        // Destroy the GameObject immediately (for Editor context)
                        UnityEngine.Object.DestroyImmediate(goToDelete);

                        // Verify deletion (check if it's null immediately after DestroyImmediate)
                        // Note: InstanceIDToObject might still return something briefly? Relying on DestroyImmediate is usually sufficient.
                        // if (EditorUtility.InstanceIDToObject(deletedInstanceId) != null) {
                        //      return CommandResponse.Error($"Failed to delete GameObject '{deletedName}'.", correlationId: correlationId);
                        // }

                        // Mark scene dirty
                        if(scene.IsValid()) { // Check if scene is valid before marking dirty
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                        }

                        Debug.Log(LOG_PREFIX + $"Deleted GameObject '{deletedName}' (Instance ID: {deletedInstanceId}).");
                        return CommandResponse.Success($"GameObject '{deletedName}' deleted successfully.", new Dictionary<string, object> { { "name", deletedName }, { "instance_id", deletedInstanceId } }, correlationId);


                    default:
                        return CommandResponse.Error($"Unsupported action '{action}' for manage_gameobject.", correlationId: correlationId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Exception during manage_gameobject action '{action}': {ex.Message}\n{ex.StackTrace}");
                return CommandResponse.Error($"Error executing manage_gameobject action '{action}': {ex.Message}", correlationId: correlationId);
            }
        }

        private static CommandResponse HandleManageScene(Dictionary<string, object> parameters, string correlationId)
        {
            Debug.Log(LOG_PREFIX + $"Handling 'manage_scene' command (ID: {correlationId}).");

            // 1. Extract Action
            if (!parameters.TryGetValue("action", out object actionObj) || !(actionObj is string action))
            {
                return CommandResponse.Error("Missing or invalid 'action' parameter for manage_scene.", correlationId: correlationId);
            }

            // 2. Extract Optional Scene Path (required by most actions)
            parameters.TryGetValue("scene_path", out object scenePathObj);
            string scenePath = scenePathObj as string;

            // 3. Extract Optional Save Mode (for 'save' action)
            parameters.TryGetValue("save_mode", out object saveModeObj);
            string saveMode = saveModeObj as string ?? "save_current"; // Default to save_current

            // 4. Switch based on action
            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "load":
                        if (string.IsNullOrEmpty(scenePath)) return CommandResponse.Error("Missing 'scene_path' parameter for load action.", correlationId: correlationId);
                        if (!scenePath.EndsWith(".unity")) return CommandResponse.Error("Invalid scene path. Must end with '.unity'.", correlationId: correlationId);
                        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return CommandResponse.Error($"Scene asset not found at path: '{scenePath}'.", correlationId: correlationId);

                        // Prompt user to save current scene if dirty
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            return CommandResponse.Error("Scene load cancelled by user (unsaved changes).", correlationId: correlationId);
                        }

                        // Load the scene
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                        Debug.Log(LOG_PREFIX + $"Loaded scene: {scenePath}");
                        return CommandResponse.Success($"Scene '{scenePath}' loaded successfully.", new Dictionary<string, object> { { "loadedScenePath", scenePath } }, correlationId);

                    case "save":
                        Scene currentScene = SceneManager.GetActiveScene();
                        if (!currentScene.IsValid()) return CommandResponse.Error("No valid scene is currently active to save.", correlationId: correlationId);

                        bool saveResult;
                        string savedPath = currentScene.path; // Default to current path

                        if (saveMode.Equals("save_as", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(scenePath)) return CommandResponse.Error("Missing 'scene_path' parameter for save_as action.", correlationId: correlationId);
                            if (!scenePath.EndsWith(".unity")) return CommandResponse.Error("Invalid scene path for save_as. Must end with '.unity'.", correlationId: correlationId);
                            // Ensure directory exists? EditorSceneManager.SaveScene might handle this.
                            string saveAsDir = System.IO.Path.GetDirectoryName(scenePath);
                             if (!AssetDatabase.IsValidFolder(saveAsDir)) {
                                 // Attempt to create directory? Or error out? Let's error for now.
                                 return CommandResponse.Error($"Target directory does not exist: '{saveAsDir}'", correlationId: correlationId);
                             }

                            saveResult = EditorSceneManager.SaveScene(currentScene, scenePath, false); // Save a copy
                            if (saveResult) savedPath = scenePath;
                        }
                        else // save_current
                        {
                            if (string.IsNullOrEmpty(currentScene.path)) return CommandResponse.Error("Cannot 'save_current' on an untitled scene. Use 'save_as' with a 'scene_path'.", correlationId: correlationId);
                            saveResult = EditorSceneManager.SaveScene(currentScene); // Save existing
                        }

                        if (saveResult)
                        {
                            Debug.Log(LOG_PREFIX + $"Saved scene: {savedPath}");
                            return CommandResponse.Success($"Scene saved successfully to '{savedPath}'.", new Dictionary<string, object> { { "savedScenePath", savedPath } }, correlationId);
                        }
                        else
                        {
                            return CommandResponse.Error($"Failed to save scene to '{savedPath}'.", correlationId: correlationId);
                        }

                    case "create":
                         if (string.IsNullOrEmpty(scenePath)) return CommandResponse.Error("Missing 'scene_path' parameter for create action.", correlationId: correlationId);
                         if (!scenePath.EndsWith(".unity")) return CommandResponse.Error("Invalid scene path for create. Must end with '.unity'.", correlationId: correlationId);
                         if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null) return CommandResponse.Error($"Scene already exists at path: '{scenePath}'.", correlationId: correlationId);

                         // Ensure directory exists? NewScene might handle this, SaveScene below definitely needs it.
                         string createDir = System.IO.Path.GetDirectoryName(scenePath);
                         if (!AssetDatabase.IsValidFolder(createDir)) {
                             return CommandResponse.Error($"Target directory does not exist: '{createDir}'", correlationId: correlationId);
                         }

                         // Create a new empty scene
                         Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single); // Creates default camera/light
                         // Or use NewSceneSetup.EmptyScene for a truly empty scene

                         // Save the new scene to the specified path
                         if (EditorSceneManager.SaveScene(newScene, scenePath, false)) // Save as new
                         {
                             Debug.Log(LOG_PREFIX + $"Created and saved new scene: {scenePath}");
                             return CommandResponse.Success($"New scene created and saved successfully at '{scenePath}'.", new Dictionary<string, object> { { "createdScenePath", scenePath } }, correlationId);
                         }
                         else
                         {
                             // Clean up the unsaved new scene? Might not be necessary.
                             return CommandResponse.Error($"Failed to save newly created scene to '{scenePath}'.", correlationId: correlationId);
                         }

                    default:
                        return CommandResponse.Error($"Unsupported action '{action}' for manage_scene.", correlationId: correlationId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + $"Exception during manage_scene action '{action}': {ex.Message}\n{ex.StackTrace}");
                return CommandResponse.Error($"Error executing manage_scene action '{action}': {ex.Message}", correlationId: correlationId);
            }
        }

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
