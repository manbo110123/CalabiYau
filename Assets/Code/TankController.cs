using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(TankInput))]
[RequireComponent(typeof(TankMotor))]
[RequireComponent(typeof(TankAim))]
[RequireComponent(typeof(TankWeapon))]
public class TankController : MonoBehaviour
{
    [Header("Control mode")]
    [SerializeField] private bool applyLocalMovement = true;
    [SerializeField] private bool applyLocalWeapon = true;

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
    public Vector3 CurrentAimPoint => tankAim != null ? tankAim.CurrentAimPoint : transform.position + transform.forward;
    public Vector3 CurrentFireOrigin => gunPoint != null ? gunPoint.position : transform.position + Vector3.up * 0.8f;
    public Vector3 CurrentFireDirection => gunPoint != null ? gunPoint.forward : transform.forward;

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

        if (applyLocalWeapon)
        {
            tankWeapon.TryFire(currentInput);
        }
    }

    private void FixedUpdate()
    {
        if (applyLocalMovement)
        {
            tankMotor.ApplyMovement(currentInput);
            tankMotor.ApplyBodyRotation(currentInput);
        }

        tankAim.ApplyTowerRotation();
    }

    public void SetLocalMovementEnabled(bool isEnabled)
    {
        applyLocalMovement = isEnabled;
    }

    public void SetLocalWeaponEnabled(bool isEnabled)
    {
        applyLocalWeapon = isEnabled;
    }

    public void SetLocalControlEnabled(bool isEnabled)
    {
        applyLocalMovement = isEnabled;
        applyLocalWeapon = isEnabled;
    }

    public void SetLocalMovementIgnoresPhysicsCollision(bool isEnabled)
    {
        if (tankMotor != null)
        {
            tankMotor.SetIgnorePhysicsCollision(isEnabled);
        }
    }

    public void SetNetworkPredictionMovementSpeeds(float moveMetersPerSecond, float turnDegreesPerSecond)
    {
        moveSpeed = moveMetersPerSecond;
        rotateSpeed = turnDegreesPerSecond * Time.fixedDeltaTime;

        if (tankMotor != null)
        {
            tankMotor.SetMovementSpeeds(moveSpeed, rotateSpeed);
        }
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
