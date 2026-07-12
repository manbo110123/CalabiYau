using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class Player : MonoBehaviour
{
    private Rigidbody rb;

    [Header("Gun data")]
    [SerializeField] private Transform gunPoint;
    [SerializeField] private float bulletSpeed;
    [SerializeField] private GameObject bulletPrefab;

    [Header("Movement data")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float rotateSpeed;

    private float verticalInput;
    private float horizontalInput;

    [Header("Tower data")]
    [SerializeField] private Transform tankTower;
    [SerializeField] private float towerRotationSpeed;

    [Header("Aim data")]
    [SerializeField] private Transform aimTransform;
    [SerializeField] private LayerMask whatIsAimMask;

    
    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }


    private void Update()
    {
        UpdateAim();
        CheckInput();
    }

    void FixedUpdate()
    {

        ApplyMovement();
        ApplyBodyRotation();
        ApplyTowerRotation();
    }

   
    private void UpdateAim()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsAimMask))
        {
            float fixedY = aimTransform.position.y;
            aimTransform.position = new Vector3(hit.point.x, fixedY, hit.point.z);
        }
    }


    private void ApplyMovement()
    {
        // 2. FixedUpdate只处理刚体物理，直接复用缓存输入
         Vector3 movement = transform.forward * moveSpeed * verticalInput;
         movement.y=rb.velocity.y;
         rb.velocity = movement;
    }

    private void ApplyBodyRotation()
    {
        transform.Rotate(0, horizontalInput * rotateSpeed, 0);
    }

    private void ApplyTowerRotation()
    {
        Vector3 direction = aimTransform.position - tankTower.position;
        direction.y = 0;

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        tankTower.rotation = Quaternion.RotateTowards(tankTower.rotation, targetRotation, towerRotationSpeed);
    }

    private void CheckInput()
    {
        if (Input.GetKeyDown(KeyCode.Mouse0))
            Shoot();
        // 1. Update只做输入读取+预处理
        verticalInput = Input.GetAxis("Vertical");
        horizontalInput = Input.GetAxis("Horizontal");

        // 后退时反转左右转向
        if (verticalInput < 0)
        {
            horizontalInput = -horizontalInput;
        }
    }

    void Shoot()
    {
        GameObject bullet = Instantiate(bulletPrefab, gunPoint.position, gunPoint.rotation);
        bullet.GetComponent<Rigidbody>().velocity = gunPoint.forward * bulletSpeed;

        Destroy(bullet, 7);
    }
}
