using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CalabiYau.EditorTools
{
    /// <summary>
    /// Performs repeatable asset-reference checks and a Windows build after the
    /// Unity 2021 -> 2022 migration. Run from Tools/CalabiYau/Validate Unity 2022 Migration.
    /// </summary>
    public static class Unity2022MigrationVerifier
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string BuildDirectory = "Builds/Unity2022Migration";

        [MenuItem("Tools/CalabiYau/Validate Unity 2022 Migration")]
        public static void ValidateFromMenu()
        {
            ValidateAssets();
            Debug.Log("[MigrationCheck] Asset validation passed.");
        }

        public static void ValidateAndBuild()
        {
            try
            {
                ValidateAssets();
                BuildWindowsPlayer();
                Debug.Log("[MigrationCheck] Validation and Windows build passed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void ValidateAssets()
        {
            var failures = new List<string>();
            var warnings = new List<string>();
            var checkedGameObjects = 0;
            var checkedMaterials = 0;

            if (!File.Exists(SampleScenePath))
            {
                failures.Add($"Required scene is missing: {SampleScenePath}");
            }

            var enabledScenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    enabledScenes.Add(scene.path);
                }
            }

            if (!enabledScenes.Contains(SampleScenePath))
            {
                failures.Add($"Build Settings does not contain enabled scene: {SampleScenePath}");
            }

            foreach (var scenePath in enabledScenes)
            {
                if (!File.Exists(scenePath))
                {
                    failures.Add($"Enabled build scene is missing: {scenePath}");
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                {
                    checkedGameObjects += CheckHierarchyForMissingScripts(root, scenePath, failures);
                }
            }

            foreach (var prefabGuid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    checkedGameObjects += CheckHierarchyForMissingScripts(prefab, prefabPath, failures);
                }
            }

            foreach (var materialGuid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var materialPath = AssetDatabase.GUIDToAssetPath(materialGuid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    failures.Add($"Material could not be loaded: {materialPath}");
                    continue;
                }

                checkedMaterials++;
                if (material.shader == null)
                {
                    failures.Add($"Material has a missing shader: {materialPath}");
                }
                else if (!material.shader.isSupported)
                {
                    warnings.Add($"Shader is not supported by the active render pipeline: {materialPath} ({material.shader.name})");
                }
            }

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                failures.Add("No Scriptable Render Pipeline asset is active in Graphics Settings.");
            }

            foreach (var warning in warnings)
            {
                Debug.LogWarning($"[MigrationCheck] {warning}");
            }

            Debug.Log(
                $"[MigrationCheck] Checked {enabledScenes.Count} enabled scene(s), " +
                $"{checkedGameObjects} GameObject(s), {checkedMaterials} material(s); " +
                $"{warnings.Count} unsupported-shader warning(s).");

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Unity 2022 migration validation failed:\n- " + string.Join("\n- ", failures));
            }
        }

        private static int CheckHierarchyForMissingScripts(
            GameObject root,
            string assetPath,
            ICollection<string> failures)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                count++;
                var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                if (missingCount > 0)
                {
                    failures.Add(
                        $"{assetPath}: '{GetHierarchyPath(transform)}' has {missingCount} missing script(s).");
                }
            }

            return count;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private static void BuildWindowsPlayer()
        {
            Directory.CreateDirectory(BuildDirectory);
            var buildPath = Path.Combine(BuildDirectory, "CalabiYau.exe");
            var scenes = Array.FindAll(EditorBuildSettings.scenes, scene => scene.enabled);
            var scenePaths = Array.ConvertAll(scenes, scene => scene.path);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {report.summary.result}, " +
                    $"{report.summary.totalErrors} error(s), {report.summary.totalWarnings} warning(s).");
            }

            Debug.Log(
                $"[MigrationCheck] Windows build succeeded: {buildPath}, " +
                $"{report.summary.totalSize} bytes, {report.summary.totalTime}.");
        }
    }
}
