using System;
using System.IO;
using System.Linq;
using GangaKiBagiya;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GangaKiBagiya.EditorTools
{
    public static class Phase1SceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private static readonly string[] BackgroundNames =
        {
            "sun", "clod back 2", "cloud 1", "cloud 2", "cloud 3", "cloud back 1"
        };
        private static readonly string[] RiverNames = { "fast river", "medium river", "slow river" };
        private static readonly string[] ObstacleNames =
        {
            "boat", "croc", "dirt", "diya", "dolphine", "fish 1", "fish 2", "flower", "plastic", "pollution"
        };

        [MenuItem("Ganga Ki Bagiya/Phase 1/Build Playable Scene")]
        public static void BuildScene()
        {
            SetupAnimatorControllers.GenerateAllControllers();

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
                throw new InvalidOperationException("SampleScene must contain a camera.");

            camera.orthographic = true;
            camera.orthographicSize = 3.2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            camera.backgroundColor = new Color(0.74f, 0.88f, 0.91f, 1f);
            EditorUtility.SetDirty(camera);

            Phase1GameController controller = camera.GetComponent<Phase1GameController>();
            if (controller == null)
                controller = Undo.AddComponent<Phase1GameController>(camera.gameObject);

            SerializedObject serialized = new SerializedObject(controller);
            SetPrefabArray(serialized.FindProperty("backgroundPrefabs"), "Assets/Models/background", BackgroundNames);
            // Cloud backs are translucent in the art; the 3D import is opaque, so they use the PNG sprites.
            SerializedProperty backgroundProp = serialized.FindProperty("backgroundPrefabs");
            backgroundProp.GetArrayElementAtIndex(1).objectReferenceValue = null;
            backgroundProp.GetArrayElementAtIndex(5).objectReferenceValue = null;
            SetPrefabArray(serialized.FindProperty("riverLanePrefabs"), "Assets/Models/water", RiverNames);
            SetPrefabArray(serialized.FindProperty("obstaclePrefabs"), "Assets/Models/obstacles", ObstacleNames);

            SetControllerArray(serialized.FindProperty("backgroundControllers"), "Assets/Models/AnimatorControllers", BackgroundNames);
            SetControllerArray(serialized.FindProperty("riverLaneControllers"), "Assets/Models/AnimatorControllers", RiverNames);
            SetControllerArray(serialized.FindProperty("obstacleControllers"), "Assets/Models/AnimatorControllers", ObstacleNames);

            SerializedProperty matkaProp = serialized.FindProperty("matkaPrefab");
            if (matkaProp != null)
            {
                GameObject matka = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/water/matka.fbx");
                if (matka == null)
                    matka = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/obstacles/matka.fbx");
                if (matka != null)
                    matkaProp.objectReferenceValue = matka;
            }

            SerializedProperty matkaControllerProp = serialized.FindProperty("matkaController");
            if (matkaControllerProp != null)
            {
                RuntimeAnimatorController matkaCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Models/AnimatorControllers/matka_Controller.controller");
                if (matkaCtrl != null)
                    matkaControllerProp.objectReferenceValue = matkaCtrl;
            }

            SetSpriteArray(serialized.FindProperty("backgroundSprites"), "Assets/background", BackgroundNames);
            SetSpriteArray(serialized.FindProperty("riverLaneSprites"), "Assets/water", RiverNames);
            SetSpriteArray(serialized.FindProperty("obstacleSprites"), "Assets/obstacles", ObstacleNames);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(entry => entry.path == ScenePath && entry.enabled))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the Phase 1 scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("Phase 1 scene configured with all animated 3D models, Animator Controllers, and fallback sprites.");
        }

        [MenuItem("Ganga Ki Bagiya/Phase 1/Build Android APK")]
        public static void BuildAndroidApk()
        {
            BuildScene();
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android build support is not installed or the target could not be selected.");

            string outputDirectory = "Builds/Android";
            Directory.CreateDirectory(outputDirectory);
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outputDirectory, "GangaKiBagiya-Phase1.apk"),
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Android build failed: " + report.summary.result + ". See the Unity build log for details.");
            Debug.Log("Phase 1 Android APK built at " + options.locationPathName + " (" + report.summary.totalSize + " bytes).");
        }

        private static void SetPrefabArray(SerializedProperty property, string directory, string[] names)
        {
            if (property == null) return;
            property.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                string path = directory + "/" + names[i] + ".fbx";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    string snakePath = directory + "/" + names[i].Replace(" ", "_") + ".fbx";
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(snakePath);
                }
                if (prefab == null)
                    Debug.LogWarning("Missing FBX model: " + path);
                property.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
            }
        }

        private static void SetControllerArray(SerializedProperty property, string directory, string[] names)
        {
            if (property == null) return;
            property.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                string path = directory + "/" + names[i] + "_Controller.controller";
                RuntimeAnimatorController ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (ctrl == null)
                {
                    string snakePath = directory + "/" + names[i].Replace(" ", "_") + "_Controller.controller";
                    ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(snakePath);
                }
                if (ctrl == null)
                    Debug.LogWarning("Missing Animator Controller: " + path);
                property.GetArrayElementAtIndex(i).objectReferenceValue = ctrl;
            }
        }

        private static void SetSpriteArray(SerializedProperty property, string directory, string[] names)
        {
            if (property == null) return;
            property.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                string path = directory + "/" + names[i] + ".png";
                Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null)
                    throw new InvalidOperationException("Missing or unimported sprite: " + path);
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }
        }
    }
}
