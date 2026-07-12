using UnityEngine;

public class TankAim : MonoBehaviour
{
    private Transform tankTower;
    private Transform aimTransform;
    private LayerMask whatIsAimMask;
    private float towerRotationSpeed;

    public Vector3 CurrentAimPoint { get; private set; }

    public void Configure(Transform newTankTower, float newTowerRotationSpeed, Transform newAimTransform, LayerMask newAimMask)
    {
        tankTower = newTankTower;
        towerRotationSpeed = newTowerRotationSpeed;
        aimTransform = newAimTransform;
        whatIsAimMask = newAimMask;
    }

    public void UpdateAimPoint(Vector3 mouseScreenPosition)
    {
        if (Camera.main == null || aimTransform == null)
        {
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(mouseScreenPosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsAimMask))
        {
            float fixedY = aimTransform.position.y;
            CurrentAimPoint = new Vector3(hit.point.x, fixedY, hit.point.z);
            aimTransform.position = CurrentAimPoint;
        }
    }

    public void ApplyTowerRotation()
    {
        if (tankTower == null || aimTransform == null)
        {
            return;
        }

        Vector3 direction = aimTransform.position - tankTower.position;
        direction.y = 0;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        tankTower.rotation = Quaternion.RotateTowards(tankTower.rotation, targetRotation, towerRotationSpeed);
    }
}
