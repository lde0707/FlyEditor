using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyEditor.Editor
{
    /// <summary>Small editor shortcuts for the solo development workflow.</summary>
    public static class ProjectTools
    {
        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("Tools/FlyEditor/Open Main Scene", priority = 0)]
        public static void OpenMainScene()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(MainScenePath);
        }

        [MenuItem("Tools/FlyEditor/Open Save Data Folder", priority = 1)]
        public static void OpenSaveDataFolder()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        [MenuItem("Tools/FlyEditor/Validate Project", priority = 20)]
        public static void ValidateProject()
        {
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) != null,
                "Main scene is missing: " + MainScenePath);
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Require(scenes.Length > 0 && scenes[0].path == MainScenePath,
                "Main must be the first enabled scene in the shared build scene list.");
            foreach (var scene in scenes)
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null,
                    "Build scene is missing: " + scene.path);
            Require(EditorSettings.serializationMode == SerializationMode.ForceText,
                "Enable Force Text serialization in Project Settings > Editor.");
            Require(!EditorApplication.isCompiling, "Wait for script compilation to complete.");
            Debug.Log("[FlyEditor] Project validation passed.");
        }

        [MenuItem("Tools/FlyEditor/Build Windows Development", priority = 40)]
        public static void BuildWindowsDevelopment()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            ValidateProject();
            Require(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64), "Install Windows Build Support in Unity Hub.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "Builds", "Windows", "FlyEditor.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                    .Select(scene => scene.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Require(result.summary.result == BuildResult.Succeeded,
                "Windows development build failed: " + result.summary.result);
            Debug.Log("[FlyEditor] Windows development build succeeded: " + output);
        }

        // Batch verification opens the saved scene without modifying a developer's editor session.
        public static void ValidateBatch()
        {
            Require(Application.isBatchMode, "Run this entry point with -batchmode.");
            ValidateProject();
            var scene = EditorSceneManager.OpenScene(MainScenePath);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject) == 0, "Missing script on " + transform.name);
            Require(Camera.main != null, "Main scene needs an active MainCamera.");
            Debug.Log("[FlyEditor] Scene validation passed; no missing scripts.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FlyEditor] " + message);
        }
    }
}
