#if UNITY_EDITOR
using Cinemachine;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the hidden Cinemachine 2.x Collider extension through Unity's Undo-aware editor API.
/// Cinemachine 2.10.7 marks this extension hidden from the normal Add Component search.
/// </summary>
public static class AddCinemachineColliderMenu
{
    private const string MenuPath = "Tools/3C/Add Camera Obstacle Avoidance To Selected FreeLook";

    [MenuItem(MenuPath, true)]
    private static bool CanAddCollider()
    {
        return Selection.activeGameObject != null
            && Selection.activeGameObject.GetComponent<CinemachineFreeLook>() != null;
    }

    [MenuItem(MenuPath)]
    private static void AddCollider()
    {
        GameObject cameraObject = Selection.activeGameObject;
        CinemachineCollider collider = cameraObject.GetComponent<CinemachineCollider>();

        if (collider == null)
        {
            collider = Undo.AddComponent<CinemachineCollider>(cameraObject);
            collider.m_CollideAgainst = Physics.DefaultRaycastLayers;
            collider.m_MinimumDistanceFromTarget = 0.1f;
            collider.m_AvoidObstacles = true;
            collider.m_CameraRadius = 0.2f;
            collider.m_Strategy = CinemachineCollider.ResolutionStrategy.PullCameraForward;
            collider.m_SmoothingTime = 0.1f;
            collider.m_Damping = 0.2f;
            collider.m_DampingWhenOccluded = 0.1f;
            EditorUtility.SetDirty(collider);
        }

        Selection.activeGameObject = cameraObject;
    }
}
#endif
