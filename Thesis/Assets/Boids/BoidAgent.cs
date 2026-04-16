using UnityEngine;

public class BoidAgent : MonoBehaviour
{
    [HideInInspector] public BoidSettings settings;
    [HideInInspector] public FlockManager manager;

    private Vector3 velocity;
    private Transform cachedTransform;

    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;
    public FlockType FlockType => settings.flockType;

    private void Awake()
    {
        cachedTransform = transform;
    }

    public void Initialize(Vector3 startVelocity)
    {
        velocity = startVelocity;
    }

    public void UpdateBoid(Vector3 separationHeading, Vector3 alignmentHeading, Vector3 cohesionCenter, int neighborCount, Vector3 crossFlockSeparation)
    {
        Vector3 acceleration = Vector3.zero;

        // Apply the three core flocking rules
        if (neighborCount > 0)
        {
            acceleration += SteerTowards(separationHeading) * settings.separationWeight;
            acceleration += SteerTowards(alignmentHeading) * settings.alignmentWeight;
            acceleration += SteerTowards(cohesionCenter) * settings.cohesionWeight;
        }

        // Cross-flock avoidance (independent of same-flock neighbors)
        acceleration += SteerTowards(crossFlockSeparation) * settings.crossFlockSeparationWeight;

        // Boundary steering
        acceleration += ComputeBoundarySteer();

        // Target-seek steering (per-boid, only when Engaging)
        if (manager.Target != null && manager.State == FlockManager.FlockState.Engaging && settings.targetSeekWeight > 0f)
        {
            Vector3 targetOffset = manager.Target.position - cachedTransform.position;
            float targetDist = targetOffset.magnitude;

            if (targetDist > settings.targetKeepDistance)
                acceleration += SteerTowards(targetOffset) * settings.targetSeekWeight;
            else
                acceleration += SteerTowards(-targetOffset) * settings.targetSeekWeight;
        }

        // Obstacle avoidance
        acceleration += ComputeObstacleAvoidance();

        // Apply acceleration to velocity
        velocity += acceleration * Time.deltaTime;

        // Clamp speed between min and max
        float speed = velocity.magnitude;
        if (speed > 0f)
        {
            speed = Mathf.Clamp(speed, settings.minSpeed, settings.maxSpeed);
            velocity = velocity.normalized * speed;
        }

        // Move and orient
        cachedTransform.position += velocity * Time.deltaTime;
        if (velocity.sqrMagnitude > 0.001f)
        {
            cachedTransform.forward = velocity.normalized;
        }
    }

    private Vector3 SteerTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return Vector3.zero;

        Vector3 steer = direction.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude(steer, settings.maxSteerForce);
    }

    private Vector3 ComputeBoundarySteer()
    {
        Vector3 managerPos = manager.transform.position;
        Vector3 offset = cachedTransform.position - managerPos;
        float distance = offset.magnitude;
        float boundaryRadius = manager.EffectiveBoundaryRadius;

        if (distance < boundaryRadius)
            return Vector3.zero;

        // Steer back toward center, strength proportional to how far past the boundary
        float overshoot = distance - boundaryRadius;
        Vector3 directionToCenter = -offset.normalized;
        float strength = overshoot * settings.boundaryTurnStrength;
        float maxBoundaryForce = settings.obstacleAvoidanceWeight * settings.maxSteerForce;
        strength = Mathf.Min(strength, maxBoundaryForce);
        return directionToCenter * strength;
    }

    private Vector3 ComputeObstacleAvoidance()
    {
        if (settings.obstacleMask == 0)
            return Vector3.zero;

        Vector3 forward = cachedTransform.forward;

        // Check if there's an obstacle ahead
        if (!Physics.SphereCast(cachedTransform.position, settings.obstacleAvoidanceRadius, forward,
                out RaycastHit hit, settings.perceptionRadius, settings.obstacleMask))
        {
            return Vector3.zero;
        }

        // Find the first unobstructed direction
        Vector3[] dirs = BoidHelper.Directions;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = cachedTransform.TransformDirection(dirs[i]);
            if (!Physics.SphereCast(cachedTransform.position, settings.obstacleAvoidanceRadius, worldDir,
                    out RaycastHit _, settings.perceptionRadius, settings.obstacleMask))
            {
                return SteerTowards(worldDir) * settings.obstacleAvoidanceWeight;
            }
        }

        // All directions blocked — steer away from the hit
        return SteerTowards(-hit.normal) * settings.obstacleAvoidanceWeight;
    }
}
