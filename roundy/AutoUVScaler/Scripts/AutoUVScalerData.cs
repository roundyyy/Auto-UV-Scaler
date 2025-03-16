#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

namespace AutoUVScaler.Scripts
{
    [Serializable]
    public class MeshMapping
    {
        public string gameObjectID;
        public string originalMeshGUID;
        public string autoUVMeshAssetPath;
    }

    public class AutoUVScalerData : ScriptableObject
    {
        public List<MeshMapping> meshMappings = new List<MeshMapping>();

        private static AutoUVScalerData _instance;
        private static string dataAssetPath = "Assets/Roundy/AutoUVScaler/Scripts/AutoUVScalerData.asset";

        public static AutoUVScalerData Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<AutoUVScalerData>(dataAssetPath);

                    if (_instance == null)
                    {
                        // Create folders if needed
                        if (!AssetDatabase.IsValidFolder("Assets/Roundy/AutoUVScaler/Scripts"))
                        {
                            if (!AssetDatabase.IsValidFolder("Assets/Roundy/AutoUVScaler"))
                            {
                                AssetDatabase.CreateFolder("Assets/Roundy/", "AutoUVScaler");
                            }
                            AssetDatabase.CreateFolder("Assets/Roundy/AutoUVScaler", "Scripts");
                        }

                        _instance = ScriptableObject.CreateInstance<AutoUVScalerData>();
                        AssetDatabase.CreateAsset(_instance, dataAssetPath);
                        AssetDatabase.SaveAssets();
                    }
                }

                return _instance;
            }
        }

        public static void Save()
        {
            if (_instance != null)
            {
                EditorUtility.SetDirty(_instance);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif