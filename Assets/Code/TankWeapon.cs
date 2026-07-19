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
            FireFromGunPoint();
        }
    }

    public void PlayNetworkFire(Vector3 origin, Vector3 direction, float range)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = gunPoint != null ? gunPoint.forward : transform.forward;
        }

        Quaternion rotation = Quaternion.LookRotation(direction.normalized);
        SpawnBullet(origin, rotation, range);
    }

    private void FireFromGunPoint()
    {
        if (gunPoint == null || bulletPrefab == null)
        {
            return;
        }

        SpawnBullet(gunPoint.position, gunPoint.rotation, 0f);
    }

    private void SpawnBullet(Vector3 origin, Quaternion rotation, float range)
    {
        if (bulletPrefab == null)
        {
            return;
        }

        GameObject bullet = Instantiate(bulletPrefab, origin, rotation);
        Rigidbody bulletRigidbody = bullet.GetComponent<Rigidbody>();

        if (bulletRigidbody != null)
        {
            bulletRigidbody.velocity = rotation * Vector3.forward * bulletSpeed;
        }

        float safeBulletSpeed = Mathf.Max(1f, bulletSpeed);
        float lifetime = range > 0f ? Mathf.Clamp(range / safeBulletSpeed, 0.1f, 7f) : 7f;
        Destroy(bullet, lifetime);
    }
}
