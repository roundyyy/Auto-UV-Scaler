using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using AutoUVScaler.Scripts;

public class AutoUVScalerEditor : EditorWindow
{
    private GameObject rootObject;
    private string saveFolder = "Assets/Roundy/AutoUVScaler/Meshes";
    private static float tilingFactor = 1f;
    private static Vector2 uvOffset = Vector2.zero; // Added UV offset
    private static bool editModeEnabled = false;
    private static bool ignoreObjectRotation = true; // Default: ignore rotation (current behavior)
    private Vector3 originalScale;
    private Material defaultMaterial; // Added default material field
    private bool showInstructions = false; // Toggle for instructions

    // Runtime dictionaries for quicker access
    private static Dictionary<int, Mesh> originalMeshDict = new Dictionary<int, Mesh>();
    private static Dictionary<int, string> meshAssetPathDict = new Dictionary<int, string>();

    // Static flag to track if the editor is open
    private static bool isEditorOpen = false;

    private Vector2 scrollPosition = Vector2.zero;

    private static int selectedTilingRange = 2; // Default to index 2 (1.0 range)
    private static float[] tilingRanges = new float[] { 0.01f, 0.1f, 1f, 10f, 100f };

    // Static constructor to ensure scene view integration is always active

    [InitializeOnLoadMethod]
    static void Initialize()
    {
        // Register the scene GUI callback when Unity starts
        SceneView.duringSceneGui -= OnSceneGUIStatic;
        SceneView.duringSceneGui += OnSceneGUIStatic;

        // Load data on initialization
        LoadPersistentData();
    }

    [MenuItem("Tools/Roundy/Auto UV Scaler")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<AutoUVScalerEditor>("Auto UV Scaler");
        wnd.Show();
    }

    private void OnEnable()
    {
        isEditorOpen = true;
    }

    private void OnDisable()
    {
        isEditorOpen = false;
        SavePersistentData();
    }

    private static void LoadPersistentData()
    {
        // Access the persistent data singleton
        var persistentData = AutoUVScalerData.Instance;

        // Populate runtime dictionaries from persistent data
        originalMeshDict.Clear();
        meshAssetPathDict.Clear();

        foreach (var mapping in persistentData.meshMappings)
        {
            int instanceID;
            if (int.TryParse(mapping.gameObjectID, out instanceID))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(mapping.originalMeshGUID);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    Mesh originalMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                    if (originalMesh != null)
                    {
                        originalMeshDict[instanceID] = originalMesh;
                        meshAssetPathDict[instanceID] = mapping.autoUVMeshAssetPath;
                    }
                }
            }
        }
    }

    private static void SavePersistentData()
    {
        // Access the persistent data singleton
        var persistentData = AutoUVScalerData.Instance;

        // Update persistent data from runtime dictionaries
        persistentData.meshMappings.Clear();

        foreach (var entry in originalMeshDict)
        {
            int instanceID = entry.Key;
            Mesh originalMesh = entry.Value;

            if (originalMesh != null)
            {
                string originalMeshPath = AssetDatabase.GetAssetPath(originalMesh);
                string originalMeshGUID = AssetDatabase.AssetPathToGUID(originalMeshPath);

                MeshMapping mapping = new MeshMapping
                {
                    gameObjectID = instanceID.ToString(),
                    originalMeshGUID = originalMeshGUID,
                    autoUVMeshAssetPath = meshAssetPathDict.ContainsKey(instanceID) ?
                                         meshAssetPathDict[instanceID] : ""
                };

                persistentData.meshMappings.Add(mapping);
            }
        }

        AutoUVScalerData.Save();
    }

    private static void RevertToOriginalMesh(GameObject obj)
    {
        if (obj == null) return;

        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        int instanceID = obj.GetInstanceID();

        if (originalMeshDict.ContainsKey(instanceID))
        {
            Mesh originalMesh = originalMeshDict[instanceID];
            if (originalMesh != null)
            {
                Undo.RecordObject(meshFilter, "Revert to Original Mesh");
                meshFilter.sharedMesh = originalMesh;

                Debug.Log($"Reverted {obj.name} to original mesh: {originalMesh.name}");
            }
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Auto UV Scaler", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Edit Mode toggle with different colors
        GUI.backgroundColor = editModeEnabled ? Color.green : Color.white;
        if (GUILayout.Button(editModeEnabled ? "AutoUV: ON" : "AutoUV: OFF"))
        {
            editModeEnabled = !editModeEnabled;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();

        rootObject = (GameObject)EditorGUILayout.ObjectField(
            "Root GameObject",
            rootObject,
            typeof(GameObject),
            true
        );

        EditorGUILayout.LabelField("Save Folder (under Assets/Roundy):");
        EditorGUILayout.BeginHorizontal();
        saveFolder = EditorGUILayout.TextField(saveFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Save Folder", "Assets", "");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                if (selectedFolder.StartsWith(Application.dataPath))
                {
                    string relPath = "Assets" + selectedFolder.Substring(Application.dataPath.Length);
                    saveFolder = relPath;
                }
                else
                {
                    Debug.LogWarning("Folder must be inside the project's Assets folder.");
                }
            }
        }
        EditorGUILayout.EndHorizontal();



        tilingFactor = EditorGUILayout.FloatField("Tiling Factor", tilingFactor);

        // UV Offset controls
        EditorGUILayout.LabelField("UV Offset:");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("X:", GUILayout.Width(15));
        float newXOffset = EditorGUILayout.Slider(uvOffset.x, 0f, 1f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Y:", GUILayout.Width(15));
        float newYOffset = EditorGUILayout.Slider(uvOffset.y, 0f, 1f);
        EditorGUILayout.EndHorizontal();

        // Update UV offset if changed
        if (newXOffset != uvOffset.x || newYOffset != uvOffset.y)
        {
            uvOffset = new Vector2(newXOffset, newYOffset);
            RefreshSelectedMesh();
        }

        // Add ignoreObjectRotation toggle
        bool newIgnoreRotation = EditorGUILayout.Toggle("Ignore Object Rotation", ignoreObjectRotation);
        if (newIgnoreRotation != ignoreObjectRotation)
        {
            ignoreObjectRotation = newIgnoreRotation;
            RefreshSelectedMesh();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate UVs Now"))
        {
            if (rootObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a valid root GameObject.", "OK");
                return;
            }

            EnsureSaveFolder();
            GenerateBoxUVs();
        }

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("Revert Selected to Original Mesh"))
        {
            GameObject selectedObj = Selection.activeGameObject;
            if (selectedObj != null)
            {
                RevertToOriginalMesh(selectedObj);
            }
            else
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject to revert.", "OK");
            }
        }

        if (GUILayout.Button("Revert All in Root to Original Meshes"))
        {
            if (rootObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a valid root GameObject.", "OK");
                return;
            }

            MeshFilter[] filters = rootObject.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in filters)
            {
                if (mf != null && mf.gameObject != null)
                {
                    RevertToOriginalMesh(mf.gameObject);
                }
            }

            Debug.Log("Reverted all meshes in root object to originals.");
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();

        // Primitive creation section
        EditorGUILayout.LabelField("Create Primitive:", EditorStyles.boldLabel);

        // Default material field
        defaultMaterial = (Material)EditorGUILayout.ObjectField(
            "Default Material",
            defaultMaterial,
            typeof(Material),
            false
        );

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Cube"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Cube);
        }
        if (GUILayout.Button("Sphere"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Sphere);
        }
        if (GUILayout.Button("Cylinder"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Cylinder);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Caps"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Capsule);
        }
        if (GUILayout.Button("Plane"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Plane);
        }
        if (GUILayout.Button("Quad"))
        {
            CreatePrimitiveWithMaterial(PrimitiveType.Quad);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Instructions button
        if (GUILayout.Button(showInstructions ? "Hide Instructions" : "Show Instructions"))
        {
            showInstructions = !showInstructions;
        }


        if (showInstructions)
        {
            // Store the current scroll position as a serialized field in your class
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200)); // Set a fixed height

            EditorGUILayout.HelpBox(
                "1) Assign a root object or parent object to process multiple objects at once.\n" +
                "2) Choose or create a save folder under Assets/.\n" +
                "3) Set default material for new primitives.\n" +
                "4) Set tiling factor and UV offset as desired.\n" +
                "5) Click 'Generate UVs Now'.\n\n" +
                "For each child with a MeshFilter, we:\n" +
                "- Create an optimized copy with auto-projected UVs\n" +
                "- Save it as a read-only .asset for better performance\n" +
                "- Update the MeshFilter to use the new mesh\n" +
                "\nAutoUV Mode Features:\n" +
                "- Toggle AutoUV Mode to enable real-time UV projection during scaling\n" +
                "- Scale objects normally and UVs will update automatically\n" +
                "- Adjust UV offset and tiling directly in the scene view\n" +
                "- Create primitives with your default material\n" +
                "- Use Revert button to restore original meshes",
                MessageType.Info
            );

            EditorGUILayout.EndScrollView();
        }
    }

    // Static version of OnSceneGUI that will be called by the static callback
    private static void OnSceneGUIStatic(SceneView sceneView)
    {
        // Create an instance of the shared functionality
        DrawSceneGUI(sceneView);

        // Only process scaling operations if edit mode is enabled
        if (!editModeEnabled) return;

        // Only process if we're using the scale tool
        if (Tools.current != Tool.Scale) return;

        // Get selected object
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null) return;

        MeshFilter meshFilter = selectedObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        // Store original state when starting to scale
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            Vector3 originalScale = selectedObject.transform.localScale;

            // Store original mesh if we haven't already
            int instanceID = selectedObject.GetInstanceID();
            if (!originalMeshDict.ContainsKey(instanceID))
            {
                originalMeshDict[instanceID] = meshFilter.sharedMesh;
                SavePersistentData(); // Save the reference immediately
            }
        }

        // Process scaling
        if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
        {
            RefreshSelectedMesh(selectedObject);
        }

        // Save asset changes when mouse is released
        if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
        {
            AssetDatabase.SaveAssets();
        }
    }

    // Instance version to maintain compatibility


    // Shared logic for drawing scene GUI
    private static void DrawSceneGUI(SceneView sceneView)
    {
        Handles.BeginGUI();

        // Position UI in bottom right corner
        float margin = 30f;
        float buttonWidth = 150f;
        float buttonHeight = 30f;
        float rightX = sceneView.position.width - buttonWidth - margin;
        float bottomY = sceneView.position.height - buttonHeight - margin;

        // AutoUV toggle button
        Rect buttonRect = new Rect(rightX, bottomY, buttonWidth, buttonHeight);
        GUI.backgroundColor = editModeEnabled ? Color.green : Color.white;
        if (GUI.Button(buttonRect, editModeEnabled ? "AutoUV: ON" : "AutoUV: OFF"))
        {
            editModeEnabled = !editModeEnabled;
            SceneView.RepaintAll();

            // If toggle is turned on, ensure persistent data is loaded
            if (editModeEnabled && originalMeshDict.Count == 0)
            {
                LoadPersistentData();
            }
        }
        GUI.backgroundColor = Color.white;

        // Only show additional controls if edit mode is enabled
        if (editModeEnabled)
        {
            float controlWidth = 150f;
            float controlHeight = 20f;
            float sliderHeight = 20f;
            float spacing = 5f;
            float currentY = bottomY - spacing - controlHeight;

            //lable color
            GUI.contentColor = Color.white;

            //slider color
            GUI.backgroundColor = Color.cyan;


            currentY -= controlHeight + spacing;
            Rect rangeDropdownLabelRect = new Rect(rightX, currentY, controlWidth - 45f, controlHeight);
            GUI.Label(rangeDropdownLabelRect, "Tiling Range:");

            Rect rangeDropdownRect = new Rect(rightX + controlWidth - 45f, currentY, 45f, controlHeight);
            string[] rangeOptions = new string[] { "0.01", "0.1", "1", "10", "100" };
            int newSelectedRange = EditorGUI.Popup(rangeDropdownRect, selectedTilingRange, rangeOptions);

            if (newSelectedRange != selectedTilingRange)
            {
                selectedTilingRange = newSelectedRange;
                // Adjust tiling factor to be within the new range if needed
                tilingFactor = Mathf.Clamp(tilingFactor, 0.001f, tilingRanges[selectedTilingRange]);
                RefreshSelectedMesh(Selection.activeGameObject);
                SceneView.RepaintAll();
            }

            // Tiling factor field - label and field on same line
            currentY -= controlHeight + spacing;
            Rect tilingLabelRect = new Rect(rightX, currentY, controlWidth - 45f, controlHeight);
            GUI.Label(tilingLabelRect, "Tiling Factor:");

            Rect tilingValueRect = new Rect(rightX + controlWidth - 40f, currentY, 40f, controlHeight);
            float newTiling = EditorGUI.FloatField(tilingValueRect, tilingFactor);

            currentY -= sliderHeight + spacing;
            Rect tilingSliderRect = new Rect(rightX, currentY, controlWidth, sliderHeight);
            newTiling = GUI.HorizontalSlider(tilingSliderRect, newTiling, 0.001f, tilingRanges[selectedTilingRange]);

            if (newTiling != tilingFactor)
            {
                tilingFactor = newTiling;
                RefreshSelectedMesh(Selection.activeGameObject);
                SceneView.RepaintAll();
            }

            // UV X Offset - label and field on same line
            currentY -= controlHeight + spacing;
            Rect uvXLabelRect = new Rect(rightX, currentY, controlWidth - 45f, controlHeight);
            GUI.Label(uvXLabelRect, "UV X Offset:");

            Rect uvXValueRect = new Rect(rightX + controlWidth - 40f, currentY, 40f, controlHeight);
            float newXOffset = EditorGUI.FloatField(uvXValueRect, uvOffset.x);

            currentY -= sliderHeight + spacing;
            Rect uvXSliderRect = new Rect(rightX, currentY, controlWidth, sliderHeight);
            newXOffset = GUI.HorizontalSlider(uvXSliderRect, newXOffset, 0f, 1f);

            // UV Y Offset - label and field on same line
            currentY -= controlHeight + spacing;
            Rect uvYLabelRect = new Rect(rightX, currentY, controlWidth - 45f, controlHeight);
            GUI.Label(uvYLabelRect, "UV Y Offset:");

            Rect uvYValueRect = new Rect(rightX + controlWidth - 40f, currentY, 40f, controlHeight);
            float newYOffset = EditorGUI.FloatField(uvYValueRect, uvOffset.y);

            currentY -= sliderHeight + spacing;
            Rect uvYSliderRect = new Rect(rightX, currentY, controlWidth, sliderHeight);
            newYOffset = GUI.HorizontalSlider(uvYSliderRect, newYOffset, 0f, 1f);

            // Check if UV offset changed
            if (newXOffset != uvOffset.x || newYOffset != uvOffset.y)
            {
                uvOffset = new Vector2(newXOffset, newYOffset);
                RefreshSelectedMesh(Selection.activeGameObject);
                SceneView.RepaintAll();
            }

            // Ignore Rotation toggle
            currentY -= buttonHeight + spacing;
            Rect rotationToggleRect = new Rect(rightX, currentY, controlWidth, buttonHeight);
            bool newIgnoreRotation = GUI.Toggle(rotationToggleRect, ignoreObjectRotation, "Ignore Object Rotation");
            if (newIgnoreRotation != ignoreObjectRotation)
            {
                ignoreObjectRotation = newIgnoreRotation;
                RefreshSelectedMesh(Selection.activeGameObject);
                SceneView.RepaintAll();
            }

            // Revert button
            currentY -= buttonHeight + spacing;
            Rect revertRect = new Rect(rightX, currentY, controlWidth, buttonHeight);
            GUI.backgroundColor = Color.yellow;
            if (GUI.Button(revertRect, "Revert to Original"))
            {
                GameObject selectedObj = Selection.activeGameObject;
                if (selectedObj != null)
                {
                    RevertToOriginalMesh(selectedObj);
                }
            }

            //refresh button
            currentY -= buttonHeight + spacing;
            Rect refreshRect = new Rect(rightX, currentY, controlWidth, buttonHeight);
            GUI.backgroundColor = Color.cyan;
            if (GUI.Button(refreshRect, "Refresh Selected"))
            {
                GameObject selectedObj = Selection.activeGameObject;
                if (selectedObj != null)
                {
                    RefreshSelectedMesh(selectedObj);
                }

            }


            GUI.backgroundColor = Color.white;



            float buttonSize = (controlWidth - spacing * 2) / 3; // 3 buttons per row

            // Row 1 of primitive buttons
            currentY -= buttonHeight + spacing;
            if (GUI.Button(new Rect(rightX, currentY, buttonSize, buttonHeight), "Cube"))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Cube);
            }
            if (GUI.Button(new Rect(rightX + buttonSize + spacing, currentY, buttonSize, buttonHeight), "Sph."))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Sphere);
            }
            if (GUI.Button(new Rect(rightX + 2 * (buttonSize + spacing), currentY, buttonSize, buttonHeight), "Cyl."))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Cylinder);
            }

            // Row 2 of primitive buttons
            currentY -= buttonHeight + spacing;
            if (GUI.Button(new Rect(rightX, currentY, buttonSize, buttonHeight), "Cap."))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Capsule);
            }
            if (GUI.Button(new Rect(rightX + buttonSize + spacing, currentY, buttonSize, buttonHeight), "Plane"))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Plane);
            }
            if (GUI.Button(new Rect(rightX + 2 * (buttonSize + spacing), currentY, buttonSize, buttonHeight), "Quad"))
            {
                var editor = GetWindow<AutoUVScalerEditor>();
                editor.CreatePrimitiveWithMaterial(PrimitiveType.Quad);
            }
            // Primitive creation buttons
            currentY -= controlHeight + spacing;
            Rect primitivesLabelRect = new Rect(rightX, currentY, controlWidth, controlHeight);
            GUI.Label(primitivesLabelRect, "Create Primitive:");
        }

        Handles.EndGUI();
    }

    // New method to refresh a selected mesh with current settings
    private static void RefreshSelectedMesh(GameObject selectedObject)
    {
        if (selectedObject == null) return;

        MeshFilter meshFilter = selectedObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        int instanceID = selectedObject.GetInstanceID();

        // Get the original mesh
        if (!originalMeshDict.ContainsKey(instanceID)) return;
        Mesh originalMesh = originalMeshDict[instanceID];

        // Check if we already have a saved asset for this object
        if (!meshAssetPathDict.ContainsKey(instanceID))
        {
            // First time editing - generate and save a new mesh asset
            Mesh newMesh = GenerateBoxProjectedMesh(meshFilter, originalMesh);

            // Save the mesh
            EnsureSaveFolder();
            string baseName = CleanupMeshName(originalMesh.name);
            string newMeshName = baseName + "_AutoUV_" + Random.Range(1000, 9999).ToString();
            string assetPath = Path.Combine(GetDefaultSaveFolder(), newMeshName + ".asset");
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(newMesh, assetPath);
            AssetDatabase.SaveAssets();

            // Store the asset path
            meshAssetPathDict[instanceID] = assetPath;
            SavePersistentData(); // Save update to persistent data

            // Apply to mesh filter
            Undo.RecordObject(meshFilter, "Update Mesh Filter");
            meshFilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

            Debug.Log($"Created and saved new AutoUV mesh: {assetPath}");
        }
        else
        {
            // Already has a saved asset - update it
            string assetPath = meshAssetPathDict[instanceID];
            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

            if (existingMesh != null)
            {
                // Generate updated mesh
                Mesh updatedMesh = GenerateBoxProjectedMesh(meshFilter, originalMesh);

                // Copy mesh data to the existing asset
                existingMesh.Clear();
                existingMesh.vertices = updatedMesh.vertices;
                existingMesh.triangles = updatedMesh.triangles;
                existingMesh.normals = updatedMesh.normals;
                existingMesh.uv = updatedMesh.uv;
                existingMesh.uv2 = updatedMesh.uv2;
                EditorUtility.SetDirty(existingMesh);

                // Apply to mesh filter
                Undo.RecordObject(meshFilter, "Update Mesh UVs");
                meshFilter.sharedMesh = existingMesh;
            }
        }
    }

    // Instance method that forwards to static method
    private void RefreshSelectedMesh()
    {
        RefreshSelectedMesh(Selection.activeGameObject);
    }

    // Modified primitive creation method to apply default material
    private void CreatePrimitiveWithMaterial(PrimitiveType type)
    {
        // Create primitive at origin with no rotation
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        // Apply default material if set
        if (defaultMaterial != null)
        {
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = defaultMaterial;
            }
        }

        // Select the newly created object
        Selection.activeGameObject = obj;

        // Focus on it in scene view
        SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log($"Created {type} at origin" + (defaultMaterial != null ? $" with material {defaultMaterial.name}" : ""));
    }

    // Static version to create primitives at origin - maintains backward compatibility


    private static Mesh GenerateBoxProjectedMesh(MeshFilter mf, Mesh sourceMesh = null)
    {
        // If no source mesh is provided, use the shared mesh
        if (sourceMesh == null)
        {
            sourceMesh = mf.sharedMesh;
        }

        Mesh workingMesh = new Mesh();
        workingMesh.name = CleanupMeshName(sourceMesh.name);

        // Get mesh data
        Vector3[] vertices = sourceMesh.vertices;
        int[] triangles = sourceMesh.triangles;
        Vector3[] normals = sourceMesh.normals;

        // New arrays for the split mesh
        List<Vector3> newVerts = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        List<int> newTris = new List<int>();

        // Process triangles in groups of 3
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Get average normal for this triangle
            Vector3 triNormal = (normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]]) / 3f;
            triNormal.Normalize();

            // Determine projection direction based on triangle normal and ignoreObjectRotation setting
            Direction dir;

            if (ignoreObjectRotation)
            {
                // Current behavior - use world-space normal
                Vector3 worldNormal = mf.transform.TransformDirection(triNormal);
                dir = GetCubeProjectionDirectionForNormal(worldNormal);
            }
            else
            {
                // New behavior - use local-space normal to respect object rotation
                dir = GetCubeProjectionDirectionForNormal(triNormal);
            }

            // Add vertices
            int baseIndex = newVerts.Count;
            for (int j = 0; j < 3; j++)
            {
                // Keep vertices in local space
                Vector3 localPos = vertices[triangles[i + j]];
                newVerts.Add(localPos);
                newNormals.Add(normals[triangles[i + j]]);

                // Calculate UVs based on ignoreObjectRotation setting
                Vector2 uv = Vector2.zero;

                if (ignoreObjectRotation)
                {
                    // Current behavior - use world position (ignores rotation)
                    Vector3 worldPos = mf.transform.TransformPoint(localPos);
                    switch (dir)
                    {
                        case Direction.Up:
                        case Direction.Down:
                            uv = new Vector2(worldPos.x, worldPos.z);
                            break;
                        case Direction.Left:
                        case Direction.Right:
                            uv = new Vector2(worldPos.z, worldPos.y);
                            break;
                        case Direction.Forward:
                        case Direction.Back:
                            uv = new Vector2(worldPos.x, worldPos.y);
                            break;
                    }
                }
                else
                {
                    // New behavior - respect object rotation
                    // Use local position but with scaling applied
                    Vector3 lossyScale = mf.transform.lossyScale;
                    Vector3 scaledLocalPos = new Vector3(
                        localPos.x * lossyScale.x,
                        localPos.y * lossyScale.y,
                        localPos.z * lossyScale.z
                    );

                    switch (dir)
                    {
                        case Direction.Up:
                        case Direction.Down:
                            uv = new Vector2(scaledLocalPos.x, scaledLocalPos.z);
                            break;
                        case Direction.Left:
                        case Direction.Right:
                            uv = new Vector2(scaledLocalPos.z, scaledLocalPos.y);
                            break;
                        case Direction.Forward:
                        case Direction.Back:
                            uv = new Vector2(scaledLocalPos.x, scaledLocalPos.y);
                            break;
                    }
                }
                uv *= tilingFactor;

                // Apply UV offset
                uv += uvOffset;

                newUVs.Add(uv);
            }

            // Add triangle indices
            newTris.Add(baseIndex);
            newTris.Add(baseIndex + 1);
            newTris.Add(baseIndex + 2);
        }

        // Apply to new mesh
        workingMesh.vertices = newVerts.ToArray();
        workingMesh.normals = newNormals.ToArray();
        workingMesh.triangles = newTris.ToArray();
        workingMesh.uv = newUVs.ToArray();
        workingMesh.uv2 = newUVs.ToArray();

        return workingMesh;
    }

    // Get default save folder for static context
    private static string GetDefaultSaveFolder()
    {
        string folder = "Assets/Roundy/AutoUVScaler/Meshes";
        EnsureSaveFolder(folder);
        return folder;
    }

    // Static version of EnsureSaveFolder
    private static void EnsureSaveFolder(string folder = null)
    {
        string folderPath = folder ?? "Assets/Roundy/AutoUVScaler/Meshes";

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            string parentDir = Path.GetDirectoryName(folderPath);
            string newFolder = Path.GetFileName(folderPath);

            // Make sure parent directory exists
            if (!AssetDatabase.IsValidFolder(parentDir))
            {
                string grandParentDir = Path.GetDirectoryName(parentDir);
                string parentFolderName = Path.GetFileName(parentDir);
                AssetDatabase.CreateFolder(grandParentDir, parentFolderName);
            }

            AssetDatabase.CreateFolder(parentDir, newFolder);
        }
    }

    // Instance version of EnsureSaveFolder
    private void EnsureSaveFolder()
    {
        if (!AssetDatabase.IsValidFolder(saveFolder))
        {
            string parentDir = Path.GetDirectoryName(saveFolder);
            string newFolder = Path.GetFileName(saveFolder);

            // Make sure parent directory exists
            if (!AssetDatabase.IsValidFolder(parentDir))
            {
                string grandParentDir = Path.GetDirectoryName(parentDir);
                string parentFolderName = Path.GetFileName(parentDir);
                AssetDatabase.CreateFolder(grandParentDir, parentFolderName);
            }

            AssetDatabase.CreateFolder(parentDir, newFolder);
        }
    }

    private static string CleanupMeshName(string originalName)
    {
        string cleanName = Regex.Replace(originalName, @"(_BoxUV.*|_AutoUV.*|_Copy.*|\d+$)", "");
        cleanName = Regex.Replace(cleanName, @"_{2,}", "_");
        cleanName = cleanName.TrimEnd('_');
        if (string.IsNullOrEmpty(cleanName))
            cleanName = "Mesh";
        return cleanName;
    }

    public enum Direction
    {
        Up,
        Down,
        Left,
        Right,
        Forward,
        Back
    }



    public static Direction GetCubeProjectionDirectionForNormal(Vector3 normal)
    {
        Direction uvDir = Direction.Up;
        float angle = Vector3.Angle(normal, Vector3.up);
        float newAngle = Vector3.Angle(normal, Vector3.down);
        if (newAngle < angle)
        {
            angle = newAngle;
            uvDir = Direction.Down;
        }
        newAngle = Vector3.Angle(normal, Vector3.left);
        if (newAngle < angle)
        {
            angle = newAngle;
            uvDir = Direction.Left;
        }
        newAngle = Vector3.Angle(normal, Vector3.right);
        if (newAngle < angle)
        {
            angle = newAngle;
            uvDir = Direction.Right;
        }
        newAngle = Vector3.Angle(normal, Vector3.forward);
        if (newAngle < angle)
        {
            angle = newAngle;
            uvDir = Direction.Forward;
        }
        newAngle = Vector3.Angle(normal, Vector3.back);
        if (newAngle < angle)
        {
            angle = newAngle;
            uvDir = Direction.Back;
        }
        return uvDir;
    }

    private void GenerateBoxUVs()
    {
        if (rootObject == null) return;

        MeshFilter[] filters = rootObject.GetComponentsInChildren<MeshFilter>(true);

        int createdCount = 0;
        foreach (MeshFilter mf in filters)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            int instanceID = mf.gameObject.GetInstanceID();

            // Store original mesh if we haven't already
            if (!originalMeshDict.ContainsKey(instanceID))
            {
                originalMeshDict[instanceID] = mf.sharedMesh;
            }

            // Generate new mesh with box projection
            Mesh workingMesh = GenerateBoxProjectedMesh(mf, originalMeshDict[instanceID]);

            // Save mesh
            string assetName = $"{workingMesh.name}_AutoUV_{createdCount:D2}";
            string fullPath = Path.Combine(saveFolder, assetName + ".asset");
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            AssetDatabase.CreateAsset(workingMesh, fullPath);
            workingMesh.UploadMeshData(true);

            // Store the asset path
            meshAssetPathDict[instanceID] = fullPath;

            // Update mesh filter
            Undo.RecordObject(mf, "Update Mesh Filter");
            mf.sharedMesh = workingMesh;

            createdCount++;
        }

        SavePersistentData(); // Save the data after batch processing
        AssetDatabase.SaveAssets();
        Debug.Log($"Auto UV projection complete. Created {createdCount} new optimized mesh asset(s).");
    }

    // Custom Editor for MeshFilter
    [CustomEditor(typeof(MeshFilter))]
    public class MeshFilterAutoUVEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            // Edit Mode button removed as in original
        }
    }
}