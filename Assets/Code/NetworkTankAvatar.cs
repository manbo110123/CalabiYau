using UnityEngine;

public class NetworkTankAvatar : MonoBehaviour
{
    [Header("Tank parts")]
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Transform tankTower;
    [SerializeField] private Transform aimTransform;

    private Rigidbody tankRigidbody;
    private bool useNetworkAuthorityMode;
    private bool hasPendingServerState;
    private Vector3 pendingPosition;
    private Quaternion pendingRotation;
    private float pendingAimX;
    private float pendingAimZ;

    private void Awake()
    {
        if (bodyTransform == null)
        {
            bodyTransform = transform;
        }

        tankRigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!hasPendingServerState)
        {
            return;
        }

        hasPendingServerState = false;
        ApplyBodyState(pendingPosition, pendingRotation);
        ApplyAimState(pendingAimX, pendingAimZ);
    }

    public void SetNetworkAuthorityMode(bool isEnabled)
    {
        useNetworkAuthorityMode = isEnabled;

        if (tankRigidbody == null)
        {
            tankRigidbody = GetComponent<Rigidbody>();
        }

        if (tankRigidbody == null)
        {
            return;
        }

        tankRigidbody.isKinematic = isEnabled;
        tankRigidbody.velocity = Vector3.zero;
        tankRigidbody.angularVelocity = Vector3.zero;
    }

    public void ApplyServerState(float x, float y, float z, float bodyYaw, float aimX, float aimZ)
    {
        pendingPosition = new Vector3(x, y, z);
        pendingRotation = Quaternion.Euler(0f, bodyYaw, 0f);
        pendingAimX = aimX;
        pendingAimZ = aimZ;
        hasPendingServerState = true;
    }

    private void ApplyBodyState(Vector3 serverPosition, Quaternion serverRotation)
    {
        if (tankRigidbody != null && bodyTransform == transform)
        {
            if (useNetworkAuthorityMode)
            {
                tankRigidbody.MovePosition(serverPosition);
                tankRigidbody.MoveRotation(serverRotation);
            }
            else
            {
                tankRigidbody.position = serverPosition;
                tankRigidbody.rotation = serverRotation;
            }

            tankRigidbody.velocity = Vector3.zero;
            tankRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        bodyTransform.position = serverPosition;
        bodyTransform.rotation = serverRotation;
    }

    private void ApplyAimState(float aimX, float aimZ)
    {
        if (aimTransform != null)
        {
            float aimY = aimTransform.position.y;
            aimTransform.position = new Vector3(aimX, aimY, aimZ);
        }

        if (tankTower == null)
        {
            return;
        }

        Vector3 targetPoint = aimTransform != null
            ? aimTransform.position
            : new Vector3(aimX, tankTower.position.y, aimZ);

        Vector3 direction = targetPoint - tankTower.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        tankTower.rotation = Quaternion.LookRotation(direction);
    }
}
