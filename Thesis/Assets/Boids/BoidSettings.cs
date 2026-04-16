using UnityEngine;

public enum FlockType { Melee, Ranged }

[CreateAssetMenu(fileName = "BoidSettings", menuName = "Boids/Boid Settings")]
public class BoidSettings : ScriptableObject
{
    [Header("Flock Identity")]
    public FlockType flockType;
    public Color flockColor = Color.white;

    [Header("Movement")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;
    public float maxSteerForce = 3f;

    [Header("Detection")]
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1f;

    [Header("Rule Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float crossFlockSeparationWeight = 2f;

    [Header("Obstacle Avoidance")]
    public float obstacleAvoidanceWeight = 10f;
    public float obstacleAvoidanceRadius = 1.5f;
    public LayerMask obstacleMask;

    [Header("Boundary")]
    public float boundaryRadius = 25f;
    public float boundaryTurnStrength = 5f;

    [Header("Spawning")]
    public int flockSize = 40;
    public float spawnRadius = 5f;

    [Header("Target Following")]
    public float targetFollowSpeed = 3f;
    public float targetStopDistance = 2f;
    public float targetSlowDistance = 8f;
    public float aggroRadius = 15f;
    public string aggroTag = "Player";

    [Header("Engagement")]
    public float targetSeekWeight = 0f;
    public float engageBoundaryRadius = -1f;
    public float engageAvoidanceRadius = -1f;
    public float targetKeepDistance = 3f;
    public float groupUpThreshold = 0.7f;
}
