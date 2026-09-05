using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BakeSelectedSkinnedMesh
{
    private const string OutputRoot = "Assets/Generated";
    private const string OutputFolder = "Assets/Generated/WeaponMeshes";

    [MenuItem("Tools/CalabiYau/Bake Selected Skinned Mesh")]
    private static void BakeSelected()
    {
        GameObject sourceObject = Selection.activeGameObject;
        SkinnedMeshRenderer sourceRenderer = sourceObject != null
            ? sourceObject.GetComponent<SkinnedMeshRenderer>()
            : null;

        if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
        {
            EditorUtility.DisplayDialog(
                "Bake Selected Skinned Mesh",
                "Select a GameObject with a Skinned Mesh Renderer first.",
                "OK");
            return;
        }

        EnsureOutputFolders();

        var bakedMesh = new Mesh
        {
            name = sourceObject.name + "_Baked"
        };
        sourceRenderer.BakeMesh(bakedMesh, false);

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{OutputFolder}/{bakedMesh.name}.asset");
        AssetDatabase.CreateAsset(bakedMesh, meshPath);

        var bakedObject = new GameObject(sourceObject.name + "_Static");
        Undo.RegisterCreatedObjectUndo(bakedObject, "Bake Selected Skinned Mesh");
        bakedObject.transform.SetParent(sourceObject.transform.parent, false);
        bakedObject.transform.localPosition = sourceObject.transform.localPosition;
        bakedObject.transform.localRotation = sourceObject.transform.localRotation;
        bakedObject.transform.localScale = sourceObject.transform.localScale;

        MeshFilter meshFilter = bakedObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = bakedMesh;

        MeshRenderer meshRenderer = bakedObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        meshRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        meshRenderer.receiveShadows = sourceRenderer.receiveShadows;
        meshRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        meshRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;

        Selection.activeGameObject = bakedObject;
        EditorGUIUtility.PingObject(bakedObject);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"Baked '{sourceObject.name}' to static mesh '{meshPath}' and created '{bakedObject.name}'.",
            bakedObject);
    }

    [MenuItem("Tools/CalabiYau/Bake Selected Skinned Mesh", true)]
    private static bool ValidateBakeSelected()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null
            && selected.GetComponent<SkinnedMeshRenderer>() != null;
    }

    private static void EnsureOutputFolders()
    {
        if (!AssetDatabase.IsValidFolder(OutputRoot))
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(OutputRoot));
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(OutputRoot, Path.GetFileName(OutputFolder));
        }
    }
}
