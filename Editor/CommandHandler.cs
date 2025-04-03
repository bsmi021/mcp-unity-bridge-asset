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
                    case "manage_asset":
                        // Pass correlationId to the handler
                        response = HandleManageAsset(parameters, correlationId);
                        break;
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
