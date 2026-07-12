using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(TankInput))]
[RequireComponent(typeof(TankMotor))]
[RequireComponent(typeof(TankAim))]
[RequireComponent(typeof(TankWeapon))]
public class TankController : MonoBehaviour
{
    [Header("Gun data")]
    [SerializeField] private Transform gunPoint;
    [SerializeField] private float bulletSpeed;
    [SerializeField] private GameObject bulletPrefab;

    [Header("Movement data")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float rotateSpeed;

    [Header("Tower data")]
    [SerializeField] private Transform tankTower;
    [SerializeField] private float towerRotationSpeed;

    [Header("Aim data")]
    [SerializeField] private Transform aimTransform;
    [SerializeField] private LayerMask whatIsAimMask;

    [Header("Tank parts")]
    [SerializeField] private TankInput tankInput;
    [SerializeField] private TankMotor tankMotor;
    [SerializeField] private TankAim tankAim;
    [SerializeField] private TankWeapon tankWeapon;

    private TankInputData currentInput;
    public TankInputData CurrentInput => currentInput;

    private void Awake()
    {
        Rigidbody tankRigidbody = GetComponent<Rigidbody>();

        tankInput = GetTankPart(tankInput);
        tankMotor = GetTankPart(tankMotor);
        tankAim = GetTankPart(tankAim);
        tankWeapon = GetTankPart(tankWeapon);

        tankMotor.Configure(tankRigidbody, moveSpeed, rotateSpeed);
        tankAim.Configure(tankTower, towerRotationSpeed, aimTransform, whatIsAimMask);
        tankWeapon.Configure(gunPoint, bulletSpeed, bulletPrefab);
    }

    private void Update()
    {
        currentInput = tankInput.ReadInput();
        tankAim.UpdateAimPoint(currentInput.MouseScreenPosition);
        tankWeapon.TryFire(currentInput);
    }

    private void FixedUpdate()
    {
        tankMotor.ApplyMovement(currentInput);
        tankMotor.ApplyBodyRotation(currentInput);
        tankAim.ApplyTowerRotation();
    }

    private T GetTankPart<T>(T currentPart) where T : Component
    {
        if (currentPart != null)
        {
            return currentPart;
        }

        return GetComponent<T>();
    }
}
