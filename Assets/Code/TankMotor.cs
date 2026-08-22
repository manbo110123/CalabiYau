using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;
using UnityEngine;

public class TankMotor : MonoBehaviour
{
    private Rigidbody tankRigidbody;
    private float moveSpeed;
    private float rotateSpeed;
    private bool ignorePhysicsCollision;
    private bool hasOriginalKinematicState;
    private bool originalIsKinematic;
    private TankWorldCollision2D collisionWorld;

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

    public void ApplyMovementAndRotation(TankInputData inputData)
    {
        if (!ignorePhysicsCollision)
        {
            ApplyMovement(inputData);
            ApplyBodyRotation(inputData);
            return;
        }

        if (tankRigidbody == null)
        {
            return;
        }

        if (collisionWorld == null)
        {
            collisionWorld = TrainingCollisionMap2D.CreateResolver();
        }

        Vector3 rigidbodyPosition = tankRigidbody.position;
        TankPose2D start = new TankPose2D(
            new Vec2D(rigidbodyPosition.x, rigidbodyPosition.z),
            tankRigidbody.rotation.eulerAngles.y * Mathf.Deg2Rad);

        // A reconciliation correction can briefly place presentation between two legal
        // poses. Let the pending correction finish instead of throwing from an invalid
        // transient pose; normal prediction steps always start and end collision-valid.
        if (!collisionWorld.IsPoseValid(start))
        {
            return;
        }

        float safeFixedDeltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        TankMoveResult2D movement = TankCommandSimulation2D.Simulate(
            collisionWorld,
            start,
            inputData.MoveAxis,
            inputData.TurnAxis,
            moveSpeed,
            rotateSpeed / safeFixedDeltaTime,
            safeFixedDeltaTime);
        Vector3 resolvedPosition = new Vector3(
            movement.Pose.Position.X,
            rigidbodyPosition.y,
            movement.Pose.Position.Y);
        Quaternion resolvedRotation = Quaternion.Euler(
            0f,
            movement.Pose.GameplayYawRadians * Mathf.Rad2Deg,
            0f);
        tankRigidbody.MovePosition(resolvedPosition);
        tankRigidbody.MoveRotation(resolvedRotation);
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
