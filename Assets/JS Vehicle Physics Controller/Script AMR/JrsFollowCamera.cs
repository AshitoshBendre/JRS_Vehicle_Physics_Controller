using UnityEngine;

public class JrsFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset;

    public float horizontalSpringConstant = 15f;
    public float horizontalDampingConstant = 8f;

    [Range(0.0f, 1.0f)]
    [Tooltip("Scales the overall spring and damping force.")]
    public float effectMultiplier = 1.0f;

    [Header("First Person Settings")]
    [Tooltip("The maximum distance (in meters) the camera can drift from its exact offset.")]
    public float maxDriftDistance = 0.15f;

    private Vector3 velocity;
    void LateUpdate()
    {
        if (target == null) return;

        // 1. Find the target world position based on the offset
        Vector3 desiredPosition = target.TransformPoint(offset);

        // 2. Calculate horizontal spring forces
        Vector3 horizontalDisplacement = new Vector3(desiredPosition.x - transform.position.x, 0, desiredPosition.z - transform.position.z);
        Vector3 horizontalSpringForce = horizontalSpringConstant * horizontalDisplacement;
        Vector3 horizontalDampingForce = -horizontalDampingConstant * new Vector3(velocity.x, 0, velocity.z);

        // 3. Apply the slider multiplier to the total force
        Vector3 force = (horizontalSpringForce + horizontalDampingForce) * effectMultiplier;

        // 4. Apply physics to velocity and position (CHANGED: Using Time.deltaTime)
        velocity += force * Time.deltaTime;
        transform.position += new Vector3(velocity.x, 0, velocity.z) * Time.deltaTime;

        // 5. Lock the Y axis directly 
        float desiredCameraHeight = target.position.y + offset.y;
        transform.position = new Vector3(transform.position.x, desiredCameraHeight, transform.position.z);

        // 6. CLAMP THE DRIFT
        Vector3 currentHorizontalPosition = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 targetHorizontalPosition = new Vector3(desiredPosition.x, 0, desiredPosition.z);

        float distanceToTarget = Vector3.Distance(currentHorizontalPosition, targetHorizontalPosition);

        if (distanceToTarget > maxDriftDistance)
        {
            Vector3 directionToTarget = (currentHorizontalPosition - targetHorizontalPosition).normalized;
            Vector3 clampedPosition = targetHorizontalPosition + (directionToTarget * maxDriftDistance);

            transform.position = new Vector3(clampedPosition.x, desiredCameraHeight, clampedPosition.z);

            // NEW: Kill the velocity when hitting the clamp boundary so force doesn't build up!
            velocity = Vector3.zero;
        }

        // 7. Match rotation 
        transform.rotation = target.rotation;
    }
}