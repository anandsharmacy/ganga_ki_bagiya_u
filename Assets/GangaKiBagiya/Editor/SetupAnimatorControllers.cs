using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GangaKiBagiya.EditorTools
{
    public static class SetupAnimatorControllers
    {
        private const string ControllerDir = "Assets/Models/AnimatorControllers";

        [MenuItem("Ganga Ki Bagiya/Phase 1/Setup All Animator Controllers")]
        public static void GenerateAllControllers()
        {
            if (!Directory.Exists(ControllerDir))
                Directory.CreateDirectory(ControllerDir);

            string[] searchDirs = { "Assets/Models/background", "Assets/Models/water", "Assets/Models/obstacles" };
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", searchDirs);

            Debug.Log($"Found {fbxGuids.Length} models to configure with Animator Controllers.");

            foreach (string guid in fbxGuids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;

                ConfigureModelImport(fbxPath);
                CreateControllerForModel(fbxPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Successfully created all Animator Controllers in " + ControllerDir);
        }

        private static void ConfigureModelImport(string fbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
                return;

            bool needsReimport = false;

            // Ensure animation is imported
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                needsReimport = true;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    string clipName = clips[i].name.ToLowerInvariant();
                    // Oneshot clips
                    bool isOneShot = clipName.Contains("snap") || clipName.Contains("bloom") || clipName.Contains("collect");
                    if (!isOneShot)
                    {
                        if (!clips[i].loopTime)
                        {
                            clips[i].loopTime = true;
                            needsReimport = true;
                        }
                    }
                }
                importer.clipAnimations = clips;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        private static void CreateControllerForModel(string fbxPath)
        {
            string modelName = Path.GetFileNameWithoutExtension(fbxPath);
            string controllerPath = $"{ControllerDir}/{modelName}_Controller.controller";

            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            AnimationClip[] clips = allAssets.OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();

            if (clips.Length == 0)
            {
                Debug.LogWarning($"No animation clips found in {fbxPath}");
                return;
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

            // Remove existing states to prevent duplicates
            ChildAnimatorState[] existingStates = rootStateMachine.states;
            foreach (ChildAnimatorState s in existingStates)
            {
                rootStateMachine.RemoveState(s.state);
            }

            AnimatorState defaultState = null;
            var oneShots = new System.Collections.Generic.List<AnimatorState>();

            foreach (AnimationClip clip in clips)
            {
                string cName = clip.name.ToLowerInvariant();
                AnimatorState state = rootStateMachine.AddState(clip.name);
                state.motion = clip;

                bool isOneShot = cName.Contains("snap") || cName.Contains("bloom") || cName.Contains("collect");
                if (!isOneShot && defaultState == null)
                {
                    defaultState = state;
                }
                else if (!isOneShot)
                {
                    Debug.LogWarning($"{fbxPath}: extra looping clip '{clip.name}' is unreachable; export one loop per model.");
                }
                else if (isOneShot)
                {
                    // Add trigger parameter if not already existing
                    string paramName = cName.Contains("snap") ? "Snap" :
                                      cName.Contains("bloom") ? "Bloom" : "Collect";

                    if (!controller.parameters.Any(p => p.name == paramName))
                    {
                        controller.AddParameter(paramName, AnimatorControllerParameterType.Trigger);
                    }

                    // Transition from AnyState to this one-shot state
                    AnimatorStateTransition trans = rootStateMachine.AddAnyStateTransition(state);
                    trans.AddCondition(AnimatorConditionMode.If, 0, paramName);
                    trans.hasExitTime = false;
                    trans.duration = 0.1f;
                    trans.canTransitionToSelf = false;
                    oneShots.Add(state);
                }
            }

            if (defaultState != null)
            {
                rootStateMachine.defaultState = defaultState;
                foreach (AnimatorState shot in oneShots)
                {
                    AnimatorStateTransition exit = shot.AddTransition(defaultState);
                    exit.hasExitTime = true;
                    exit.exitTime = 1f;
                    exit.duration = 0.1f;
                }
            }
            else if (rootStateMachine.states.Length > 0)
            {
                rootStateMachine.defaultState = rootStateMachine.states[0].state;
            }

            EditorUtility.SetDirty(controller);
        }
    }
}
