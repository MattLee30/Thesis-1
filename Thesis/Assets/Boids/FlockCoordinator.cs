using System.Collections.Generic;
using UnityEngine;

public class FlockCoordinator : MonoBehaviour
{
    [Header("Flock References")]
    [Tooltip("Leave empty to auto-discover all FlockManagers in the scene.")]
    [SerializeField] private List<FlockManager> flocks = new List<FlockManager>();

    [Header("Merge Settings")]
    [SerializeField] private float mergeProximity = 10f;
    [SerializeField] private bool autoMerge = true;

    [Header("Cross-Flock Avoidance")]
    [Tooltip("Max distance between flock centers before cross-flock avoidance is skipped.")]
    [SerializeField] private float crossFlockCullDistance = 60f;

    private void Start()
    {
        if (flocks.Count == 0)
            DiscoverFlocks();
    }

    private void Update()
    {
        DistributeForeignBoids();

        if (autoMerge)
            CheckAutoMerge();
    }

    private void DistributeForeignBoids()
    {
        float cullDistSqr = crossFlockCullDistance * crossFlockCullDistance;

        for (int i = 0; i < flocks.Count; i++)
        {
            FlockManager current = flocks[i];
            if (current == null || current.BoidCount == 0)
            {
                current?.SetForeignBoids(new List<BoidAgent>());
                continue;
            }

            Vector3 currentCenter = current.GetFlockCenter();
            List<BoidAgent> foreign = new List<BoidAgent>();

            for (int j = 0; j < flocks.Count; j++)
            {
                if (i == j) continue;

                FlockManager other = flocks[j];
                if (other == null || other.BoidCount == 0)
                    continue;

                // Same type — no cross-flock avoidance needed
                if (other.FlockType == current.FlockType)
                    continue;

                // Broad-phase: skip distant flocks
                Vector3 otherCenter = other.GetFlockCenter();
                if ((currentCenter - otherCenter).sqrMagnitude > cullDistSqr)
                    continue;

                IReadOnlyList<BoidAgent> otherBoids = other.Boids;
                for (int k = 0; k < otherBoids.Count; k++)
                    foreign.Add(otherBoids[k]);
            }

            current.SetForeignBoids(foreign);
        }
    }

    public void DiscoverFlocks()
    {
        flocks.Clear();
        flocks.AddRange(FindObjectsByType<FlockManager>(FindObjectsSortMode.None));
    }

    public void RegisterFlock(FlockManager flock)
    {
        if (!flocks.Contains(flock))
            flocks.Add(flock);
    }

    public void UnregisterFlock(FlockManager flock)
    {
        flocks.Remove(flock);
    }

    private void CheckAutoMerge()
    {
        float proximitySqr = mergeProximity * mergeProximity;

        for (int i = flocks.Count - 1; i >= 0; i--)
        {
            if (flocks[i] == null || flocks[i].BoidCount == 0)
                continue;

            for (int j = i - 1; j >= 0; j--)
            {
                if (flocks[j] == null || flocks[j].BoidCount == 0)
                    continue;

                if (flocks[i].FlockType != flocks[j].FlockType)
                    continue;

                Vector3 centerA = flocks[i].GetFlockCenter();
                Vector3 centerB = flocks[j].GetFlockCenter();

                if ((centerA - centerB).sqrMagnitude < proximitySqr)
                {
                    MergeFlocks(flocks[j], flocks[i]);
                    break; // flocks[i] was consumed, move to next i
                }
            }
        }
    }

    public void MergeFlocks(FlockManager receiver, FlockManager donor)
    {
        if (receiver.FlockType != donor.FlockType)
        {
            Debug.LogWarning($"Cannot merge flocks of different types: {receiver.FlockType} vs {donor.FlockType}");
            return;
        }

        List<BoidAgent> allBoids = donor.RemoveBoids(donor.BoidCount);
        receiver.AddBoids(allBoids);

        // Remove the now-empty donor from tracking and destroy its GameObject
        flocks.Remove(donor);
        Destroy(donor.gameObject);
    }

    public void SetFlockTarget(FlockManager flock, Transform target)
    {
        if (flock != null)
            flock.SetTarget(target);
    }

    public void SetAllFlocksTarget(Transform target)
    {
        for (int i = 0; i < flocks.Count; i++)
        {
            if (flocks[i] != null)
                flocks[i].SetTarget(target);
        }
    }

    public void SetFlockTargetByType(FlockType type, Transform target)
    {
        for (int i = 0; i < flocks.Count; i++)
        {
            if (flocks[i] != null && flocks[i].FlockType == type)
                flocks[i].SetTarget(target);
        }
    }

    public void ClearAllTargets()
    {
        for (int i = 0; i < flocks.Count; i++)
        {
            if (flocks[i] != null)
                flocks[i].ClearTarget();
        }
    }

    public FlockManager SplitFlock(FlockManager source)
    {
        if (source.BoidCount < 2)
        {
            Debug.LogWarning("Cannot split a flock with fewer than 2 boids.");
            return null;
        }

        int halfCount = source.BoidCount / 2;
        List<BoidAgent> splitBoids = source.RemoveBoids(halfCount);

        // Create a new FlockManager for the split group
        GameObject newFlockObj = new GameObject($"Flock_{source.FlockType}_Split");
        newFlockObj.transform.position = source.GetFlockCenter();
        FlockManager newFlock = newFlockObj.AddComponent<FlockManager>();

        // Copy settings via serialized field — use reflection-free approach
        // The new manager needs settings assigned; we use AddBoids which sets settings per-boid,
        // but we also need the manager-level settings for LateUpdate calculations.
        // Use a public initializer.
        newFlock.InitializeFromSplit(source.Settings, splitBoids);

        RegisterFlock(newFlock);
        return newFlock;
    }
}
