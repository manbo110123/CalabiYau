using UnityEngine;

/// <summary>
/// Marks a scene Rigidbody that belongs to the versioned authoritative static map.
/// Offline play keeps the authored Rigidbody behavior. Network-authoritative play
/// resets it to the authored pose and freezes it so local PhysX cannot diverge from
/// the standalone server's immutable collision map.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class NetworkStaticMapBody2D : MonoBehaviour
{
    [SerializeField] private int colliderId;

    private Rigidbody cachedRigidbody;
    private Vector3 authoredPosition;
    private Quaternion authoredRotation;
    private bool authoredIsKinematic;
    private bool authoredUseGravity;
    private bool hasCapturedAuthoredState;
    private bool isNetworkStatic;

    public int ColliderId => colliderId;
    public bool IsNetworkStatic => isNetworkStatic;

    private void Awake()
    {
        CaptureAuthoredState();
    }

    public static int SetNetworkModeForAll(bool enabled)
    {
        NetworkStaticMapBody2D[] bodies = FindObjectsOfType<NetworkStaticMapBody2D>();

        for (int index = 0; index < bodies.Length; index++)
        {
            bodies[index].SetNetworkStaticMode(enabled);
        }

        return bodies.Length;
    }

    public void SetNetworkStaticMode(bool enabled)
    {
        CaptureAuthoredState();

        if (cachedRigidbody == null)
        {
            return;
        }

        if (enabled)
        {
            cachedRigidbody.isKinematic = true;
            cachedRigidbody.useGravity = false;
            ResetToAuthoredPose();
            isNetworkStatic = true;
            return;
        }

        if (!isNetworkStatic)
        {
            return;
        }

        // Reset while still kinematic, then restore the scene-authored offline mode.
        ResetToAuthoredPose();
        cachedRigidbody.useGravity = authoredUseGravity;
        cachedRigidbody.isKinematic = authoredIsKinematic;
        isNetworkStatic = false;
    }

    private void CaptureAuthoredState()
    {
        if (hasCapturedAuthoredState)
        {
            return;
        }

        cachedRigidbody = GetComponent<Rigidbody>();

        if (cachedRigidbody == null)
        {
            return;
        }

        authoredPosition = cachedRigidbody.position;
        authoredRotation = cachedRigidbody.rotation;
        authoredIsKinematic = cachedRigidbody.isKinematic;
        authoredUseGravity = cachedRigidbody.useGravity;
        hasCapturedAuthoredState = true;
    }

    private void ResetToAuthoredPose()
    {
        cachedRigidbody.velocity = Vector3.zero;
        cachedRigidbody.angularVelocity = Vector3.zero;
        cachedRigidbody.position = authoredPosition;
        cachedRigidbody.rotation = authoredRotation;
        cachedRigidbody.Sleep();
    }

    private void OnValidate()
    {
        if (colliderId < 0)
        {
            colliderId = 0;
        }
    }
}
