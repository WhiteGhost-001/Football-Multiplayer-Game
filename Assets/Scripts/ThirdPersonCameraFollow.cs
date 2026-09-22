using UnityEngine;

/// <summary>
/// Follows the player from behind. The offset is applied relative to the
/// player's facing, so "behind" always tracks which way the player is looking
/// (this fixes the old world-space offset that put the guest's camera in front).
/// Also provides the shared mouse-to-ground helper used so the character turns
/// to face whatever point the cursor points at.
/// </summary>
public class ThirdPersonCameraFollow : MonoBehaviour
{
    [Tooltip("The player transform to follow")]
    public Transform target;

    [Tooltip("Local-space offset relative to the player's facing (behind = -Z).")]
    public Vector3 offset = new Vector3(0f, 4f, -6f);

    [Tooltip("When true the offset rotates with the player's facing; when false it stays fixed in world space.")]
    public bool followBehind = true;

    [Tooltip("Height above the target that the camera looks at.")]
    public float lookAtHeight = 1.2f;

    [Tooltip("Time it takes for the camera to catch up")]
    public float smoothTime = 0.12f;

    // Shake
    private float shakeDuration = 0f;
    private float shakeIntensity = 0f;

    private Vector3 velocity = Vector3.zero;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = followBehind
            ? target.position + YawOnly(target.rotation) * offset
            : target.position + offset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        // Apply shake
        if (shakeDuration > 0)
        {
            transform.position += Random.insideUnitSphere * shakeIntensity;
            shakeDuration -= Time.deltaTime;
        }

        Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
        Vector3 euler = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up).eulerAngles;
        euler.z = 0f; // keep the camera level
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(euler), Time.deltaTime * 12f);
    }

    public void SnapToTarget()
    {
        if (target == null) return;

        transform.position = followBehind
            ? target.position + YawOnly(target.rotation) * offset
            : target.position + offset;

        Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
        Vector3 euler = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up).eulerAngles;
        euler.z = 0f;
        transform.rotation = Quaternion.Euler(euler);
    }

    private static Quaternion YawOnly(Quaternion rotation)
    {
        Vector3 e = rotation.eulerAngles;
        return Quaternion.Euler(0f, e.y, 0f);
    }

    /// <summary>
    /// Converts the mouse cursor position into a point on the horizontal plane at
    /// groundY (the pitch surface is world y=0). Returns false if the mouse ray
    /// doesn't hit the plane.
    /// </summary>
    public static bool MouseToGroundPoint(float groundY, out Vector3 point)
    {
        Camera cam = Camera.main;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        if (ground.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Trigger a camera shake effect (e.g. on goal).
    /// </summary>
    public void TriggerShake(float duration, float intensity)
    {
        shakeDuration = duration;
        shakeIntensity = intensity;
    }
}