using UnityEngine;

public class TankMotor : MonoBehaviour
{
    private Rigidbody tankRigidbody;
    private float moveSpeed;
    private float rotateSpeed;

    public void Configure(Rigidbody targetRigidbody, float newMoveSpeed, float newRotateSpeed)
    {
        tankRigidbody = targetRigidbody;
        moveSpeed = newMoveSpeed;
        rotateSpeed = newRotateSpeed;
    }

    public void ApplyMovement(TankInputData inputData)
    {
        if (tankRigidbody == null)
        {
            return;
        }

        Vector3 movement = transform.forward * moveSpeed * inputData.MoveAxis;
        movement.y = tankRigidbody.velocity.y;
        tankRigidbody.velocity = movement;
    }

    public void ApplyBodyRotation(TankInputData inputData)
    {
        transform.Rotate(0, inputData.TurnAxis * rotateSpeed, 0);
    }
}
