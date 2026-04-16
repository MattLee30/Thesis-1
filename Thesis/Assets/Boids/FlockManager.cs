using System.Collections.Generic;
using UnityEngine;

public class FlockManager : MonoBehaviour
{
    public enum FlockState { Idle, Grouping, Engaging }

    [SerializeField] private BoidSettings settings;
    [SerializeField] private GameObject boidPrefab;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private List<BoidAgent> boids = new List<BoidAgent>();
    private List<BoidAgent> foreignBoids = new List<BoidAgent>();
    private Transform target;
    private SphereCollider aggroTrigger;
    private FlockState state = FlockState.Idle;

    public FlockType FlockType => settings.flockType;
    public BoidSettings Settings => settings;
    public IReadOnlyList<BoidAgent> Boids => boids;
    public int BoidCount => boids.Count;
    public Transform Target => target;
    public FlockState State => state;

    public float EffectiveBoundaryRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageBoundaryRadius > 0f)
                return settings.engageBoundaryRadius;
            return settings.boundaryRadius;
        }
    }

    public float EffectiveAvoidanceRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageAvoidanceRadius > 0f)
                return settings.engageAvoidanceRadius;
            return settings.avoidanceRadius;
        }
    }

    public void SetTarget(Transform t)
    {
        target = t;
        if (t != null)
            state = FlockState.Grouping;
    }

    public void ClearTarget()
    {
        target = null;
        state = FlockState.Idle;
    }

    public void SetForeignBoids(List<BoidAgent> foreign)
    {
        foreignBoids = foreign;
    }

    private void Awake()
    {
        if (settings != null && settings.aggroRadius > 0f)
        {
            aggroTrigger = gameObject.AddComponent<SphereCollider>();
            aggroTrigger.radius = settings.aggroRadius;
            aggroTrigger.isTrigger = true;

            if (GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    private void Start()
    {
        if (boidPrefab != null && settings != null)
            SpawnFlock();
    }

    private void Update()
    {
        if (target == null || settings == null)
            return;

        if (state == FlockState.Grouping)
        {
            // Check if enough boids are clustered to transition to Engaging
            float effectiveRadius = EffectiveBoundaryRadius;
            float radiusSqr = effectiveRadius * effectiveRadius;
            int clusteredCount = 0;

            for (int i = 0; i < boids.Count; i++)
            {
                Vector3 offset = boids[i].Position - transform.position;
                if (offset.sqrMagnitude <= radiusSqr)
                    clusteredCount++;
            }

            float fraction = boids.Count > 0 ? (float)clusteredCount / boids.Count : 1f;
            if (fraction >= settings.groupUpThreshold)
                state = FlockState.Engaging;

            return; // Don't advance while grouping
        }

        // Engaging — move toward target (existing arrival behavior)
        Vector3 direction = target.position - transform.position;
        float distance = direction.magnitude;

        if (distance < settings.targetStopDistance)
            return;

        float speed = settings.targetFollowSpeed;

        if (distance < settings.targetSlowDistance)
        {
            float t = (distance - settings.targetStopDistance) / (settings.targetSlowDistance - settings.targetStopDistance);
            speed *= Mathf.Clamp01(t);
        }

        transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (target == null && settings != null && other.CompareTag(settings.aggroTag))
            SetTarget(other.transform);
    }

    private void SpawnFlock()
    {
        for (int i = 0; i < settings.flockSize; i++)
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * settings.spawnRadius;
            Vector3 startVelocity = Random.onUnitSphere * settings.maxSpeed * 0.5f;

            GameObject boidObj = Instantiate(boidPrefab, spawnPos, Quaternion.LookRotation(startVelocity), transform);
            BoidAgent agent = boidObj.GetComponent<BoidAgent>();
            agent.settings = settings;
            agent.manager = this;
            agent.Initialize(startVelocity);
            ApplyFlockColor(agent);

            boids.Add(agent);
        }
    }

    public void AddBoids(List<BoidAgent> incoming)
    {
        for (int i = 0; i < incoming.Count; i++)
        {
            BoidAgent boid = incoming[i];
            boid.transform.SetParent(transform);
            boid.settings = settings;
            boid.manager = this;
            ApplyFlockColor(boid);
            boids.Add(boid);
        }
    }

    public List<BoidAgent> RemoveBoids(int count)
    {
        count = Mathf.Min(count, boids.Count);
        List<BoidAgent> removed = new List<BoidAgent>(count);

        // Remove from the end to avoid shifting
        int startIndex = boids.Count - count;
        for (int i = boids.Count - 1; i >= startIndex; i--)
        {
            removed.Add(boids[i]);
            boids.RemoveAt(i);
        }

        return removed;
    }

    public void InitializeFromSplit(BoidSettings sourceSettings, List<BoidAgent> splitBoids)
    {
        settings = sourceSettings;
        AddBoids(splitBoids);
    }

    public Vector3 GetFlockCenter()
    {
        if (boids.Count == 0)
            return transform.position;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < boids.Count; i++)
            center += boids[i].Position;

        return center / boids.Count;
    }

    private void ApplyFlockColor(BoidAgent boid)
    {
        Renderer renderer = boid.GetComponentInChildren<Renderer>();
        if (renderer != null)
            renderer.material.color = settings.flockColor;
    }

    private void LateUpdate()
    {
        float perceptionSqr = settings.perceptionRadius * settings.perceptionRadius;
        float effectiveAvoidance = EffectiveAvoidanceRadius;
        float avoidanceSqr = effectiveAvoidance * effectiveAvoidance;

        for (int i = 0; i < boids.Count; i++)
        {
            BoidAgent boid = boids[i];
            Vector3 separationHeading = Vector3.zero;
            Vector3 alignmentHeading = Vector3.zero;
            Vector3 cohesionCenter = Vector3.zero;
            int neighborCount = 0;

            for (int j = 0; j < boids.Count; j++)
            {
                if (i == j) continue;

                BoidAgent other = boids[j];
                Vector3 offset = other.Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < perceptionSqr)
                {
                    neighborCount++;
                    alignmentHeading += other.Velocity;
                    cohesionCenter += other.Position;

                    if (sqrDist < avoidanceSqr)
                    {
                        // Weight separation inversely by distance
                        separationHeading -= offset / Mathf.Max(offset.magnitude, 0.001f);
                    }
                }
            }

            if (neighborCount > 0)
            {
                alignmentHeading /= neighborCount;
                cohesionCenter = (cohesionCenter / neighborCount) - boid.Position;
            }

            // Cross-flock separation (separation only, no alignment/cohesion)
            Vector3 crossFlockSeparation = Vector3.zero;
            for (int f = 0; f < foreignBoids.Count; f++)
            {
                Vector3 offset = foreignBoids[f].Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < avoidanceSqr)
                {
                    crossFlockSeparation -= offset / Mathf.Max(offset.magnitude, 0.001f);
                }
            }

            boid.UpdateBoid(separationHeading, alignmentHeading, cohesionCenter, neighborCount, crossFlockSeparation);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || settings == null) return;

        // Boundary sphere
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, EffectiveBoundaryRadius);

        // Spawn area
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, settings.spawnRadius);

        // Aggro radius
        if (settings.aggroRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, settings.aggroRadius);
        }
    }
}
