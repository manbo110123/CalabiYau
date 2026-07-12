using UnityEngine;

public class TankWeapon : MonoBehaviour
{
    private Transform gunPoint;
    private float bulletSpeed;
    private GameObject bulletPrefab;

    public void Configure(Transform newGunPoint, float newBulletSpeed, GameObject newBulletPrefab)
    {
        gunPoint = newGunPoint;
        bulletSpeed = newBulletSpeed;
        bulletPrefab = newBulletPrefab;
    }

    public void TryFire(TankInputData inputData)
    {
        if (inputData.FirePressed)
        {
            Fire();
        }
    }

    private void Fire()
    {
        if (gunPoint == null || bulletPrefab == null)
        {
            return;
        }

        GameObject bullet = Instantiate(bulletPrefab, gunPoint.position, gunPoint.rotation);
        Rigidbody bulletRigidbody = bullet.GetComponent<Rigidbody>();

        if (bulletRigidbody != null)
        {
            bulletRigidbody.velocity = gunPoint.forward * bulletSpeed;
        }

        Destroy(bullet, 7);
    }
}
