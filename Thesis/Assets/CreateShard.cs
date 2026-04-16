using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class CreateShard : MonoBehaviour
{
    [Header("Shard Shape")]
    [Tooltip("Maximum XZ displacement applied to each vertex (world-space units).")]
    public float edgeJitter = 0.15f;

    [Range(0f, 0.8f)]
    public float axisScaleVariance = 0.25f;

    public float yRotationRange = 180f;

    [Header("Thickness / Bevel")]
    [Tooltip("Random vertical (Y) noise added to each vertex — gives a subtle warped-glass look.")]
    public float verticalWarp = 0.01f;

    public int seed = -1;

    void Awake()
    {
        RandomizeMesh();
    }

    public void RandomizeMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) return;

        Mesh original = mf.sharedMesh;
        if (original == null) return;

        Mesh shard = Instantiate(original);
        shard.name = original.name + "_shard";

        System.Random rng = (seed == -1)
            ? new System.Random()
            : new System.Random(seed);

        float scaleX = 1f + (float)(rng.NextDouble() * 2 - 1) * axisScaleVariance;
        float scaleZ = 1f + (float)(rng.NextDouble() * 2 - 1) * axisScaleVariance;

        float yRot = (float)(rng.NextDouble() * 2 - 1) * yRotationRange;
        transform.Rotate(0f, yRot, 0f, Space.Self);

        Vector3[] verts = shard.vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].x *= scaleX;
            verts[i].z *= scaleZ;

            float jx = (float)(rng.NextDouble() * 2 - 1) * edgeJitter;
            float jz = (float)(rng.NextDouble() * 2 - 1) * edgeJitter;
            verts[i].x += jx;
            verts[i].z += jz;

            float jy = (float)(rng.NextDouble() * 2 - 1) * verticalWarp;
            verts[i].y += jy;
        }

        shard.vertices = verts;
        shard.RecalculateBounds();
        shard.RecalculateNormals();
        shard.RecalculateTangents();

        mf.mesh = shard;

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc != null)
        {
            mc.sharedMesh = null; 
            mc.sharedMesh = shard;
        }
    }
}