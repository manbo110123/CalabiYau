using UnityEngine;

public class TankMotor : MonoBehaviour
{
    private Rigidbody tankRigidbody;
    private float moveSpeed;
    private float rotateSpeed;
    private bool ignorePhysicsCollision;
    private bool hasOriginalKinematicState;
    private bool originalIsKinematic;

    public void Configure(Rigidbody targetRigidbody, float newMoveSpeed, float newRotateSpeed)
    {
        tankRigidbody = targetRigidbody;
        moveSpeed = newMoveSpeed;
        rotateSpeed = newRotateSpeed;

        if (tankRigidbody != null && !hasOriginalKinematicState)
        {
            originalIsKinematic = tankRigidbody.isKinematic;
            hasOriginalKinematicState = true;
        }
    }

    public void SetMovementSpeeds(float newMoveSpeed, float newRotateSpeed)
    {
        moveSpeed = newMoveSpeed;
        rotateSpeed = newRotateSpeed;
    }

    public void SetIgnorePhysicsCollision(bool isEnabled)
    {
        ignorePhysicsCollision = isEnabled;

        if (tankRigidbody == null)
        {
            return;
        }

        if (isEnabled)
        {
            tankRigidbody.isKinematic = true;
            tankRigidbody.velocity = Vector3.zero;
            tankRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        if (hasOriginalKinematicState)
        {
            tankRigidbody.isKinematic = originalIsKinematic;
        }
    }

    public void ApplyMovement(TankInputData inputData)
    {
        if (tankRigidbody == null)
        {
            return;
        }

        if (ignorePhysicsCollision)
        {
            Vector3 predictedMovement = transform.forward * moveSpeed * inputData.MoveAxis * Time.fixedDeltaTime;
            tankRigidbody.MovePosition(tankRigidbody.position + predictedMovement);
            return;
        }

        Vector3 movement = transform.forward * moveSpeed * inputData.MoveAxis;
        movement.y = tankRigidbody.velocity.y;
        tankRigidbody.velocity = movement;
    }

    public void ApplyBodyRotation(TankInputData inputData)
    {
        if (ignorePhysicsCollision && tankRigidbody != null)
        {
            Quaternion rotation = tankRigidbody.rotation * Quaternion.Euler(0f, inputData.TurnAxis * rotateSpeed, 0f);
            tankRigidbody.MoveRotation(rotation);
            return;
        }

        transform.Rotate(0, inputData.TurnAxis * rotateSpeed, 0);
    }
}
