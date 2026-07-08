using UnityEngine;

public class Player : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 100f;

    [Header("References")]
    public Rigidbody rb;

    void FixedUpdate()
    {
        float horizontal = Input.GetAxis("Horizontal");  // A/D 或 ←→
        float vertical   = Input.GetAxis("Vertical");    // W/S 或 ↑↓

        // 前进/后退
        Vector3 move = transform.forward * (vertical * moveSpeed);
        rb.velocity = new Vector3(move.x, rb.velocity.y, move.z);

        // 左右转向
        float turn = horizontal * rotateSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turn, 0f));
    }
}
