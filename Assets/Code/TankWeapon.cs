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
        SpawnBullet(origin, rotation, range, true);
    }

    private void FireFromGunPoint()
    {
        if (gunPoint == null || bulletPrefab == null)
        {
            return;
        }

        SpawnBullet(gunPoint.position, gunPoint.rotation, 0f, false);
    }

    private void SpawnBullet(
        Vector3 origin,
        Quaternion rotation,
        float range,
        bool isNetworkPresentation)
    {
        if (bulletPrefab == null)
        {
            return;
        }

        GameObject bullet = Instantiate(bulletPrefab, origin, rotation);
        Rigidbody bulletRigidbody = bullet.GetComponent<Rigidbody>();

        if (isNetworkPresentation)
        {
            // Network fire is authoritative hitscan. This projectile is visual only:
            // each client may animate it, but it must not push local Rigidbody props.
            Collider[] colliders = bullet.GetComponentsInChildren<Collider>();

            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        if (bulletRigidbody != null)
        {
            if (isNetworkPresentation)
            {
                bulletRigidbody.detectCollisions = false;
                bulletRigidbody.useGravity = false;
            }

            bulletRigidbody.velocity = rotation * Vector3.forward * bulletSpeed;
        }

        float safeBulletSpeed = Mathf.Max(1f, bulletSpeed);
        float lifetime = isNetworkPresentation
            ? Mathf.Clamp(Mathf.Max(0f, range) / safeBulletSpeed, 0.01f, 7f)
            : 7f;
        Destroy(bullet, lifetime);
    }
}
