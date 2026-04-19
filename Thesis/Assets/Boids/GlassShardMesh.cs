using UnityEngine;

// Generates an irregular glass-shard polygon mesh at runtime and assigns it to
// the MeshFilter (and MeshCollider if present) on this GameObject.
// Add this component to the boid prefab. Runs in Awake so the mesh is ready
// before any other component references it.
[RequireComponent(typeof(MeshFilter))]
public class GlassShardMesh : MonoBehaviour
{
    [Tooltip("Overall size of the shard in local units. Increase the prefab scale alongside this for world-space size.")]
    [SerializeField] private float size = 1f;

    [Tooltip("How much each vertex is randomly displaced from the base shape. Higher = more jagged.")]
    [SerializeField] [Range(0f, 0.25f)] private float jitter = 0.10f;

    [Tooltip("Seed for per-boid shape variation. -1 uses the instance ID so every boid differs.")]
    [SerializeField] private int seed = -1;

    // Base shard outline — 8 vertices ordered CCW in the XZ plane (Y = 0).
    // Designed to look like a roughly elongated, asymmetric glass shard.
    private static readonly Vector2[] BasePerimeter = new Vector2[]
    {
        new Vector2( 0.00f,  0.95f),   // tip (top)
        new Vector2(-0.35f,  0.55f),   // upper left
        new Vector2(-0.58f,  0.05f),   // left
        new Vector2(-0.15f, -0.72f),   // lower left
        new Vector2( 0.05f, -0.90f),   // tip (bottom)
        new Vector2( 0.50f, -0.58f),   // lower right
        new Vector2( 0.55f,  0.12f),   // right
        new Vector2( 0.40f,  0.60f),   // upper right
    };

    private void Awake()
    {
        Mesh shard = BuildMesh();

        GetComponent<MeshFilter>().mesh = shard;

        MeshCollider col = GetComponent<MeshCollider>();
        if (col != null)
            col.sharedMesh = shard;
    }

    private Mesh BuildMesh()
    {
        int n = BasePerimeter.Length;
        Vector2[] perimeter = new Vector2[n];

        // Apply per-instance jitter so every boid has a unique silhouette.
        Random.State savedState = Random.state;
        Random.InitState(seed >= 0 ? seed : GetInstanceID());
        for (int i = 0; i < n; i++)
            perimeter[i] = BasePerimeter[i] + Random.insideUnitCircle * jitter;
        Random.state = savedState;

        // Compute centroid for fan triangulation — works on any simple polygon.
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++)
            centroid += perimeter[i];
        centroid /= n;

        // Build vertex array: [0] = centroid, [1..n] = perimeter (in XZ plane, Y = 0).
        Vector3[] verts = new Vector3[n + 1];
        Vector2[] uvs   = new Vector2[n + 1];

        verts[0] = new Vector3(centroid.x * size, 0f, centroid.y * size);
        uvs[0]   = centroid * 0.5f + Vector2.one * 0.5f;

        for (int i = 0; i < n; i++)
        {
            verts[i + 1] = new Vector3(perimeter[i].x * size, 0f, perimeter[i].y * size);
            uvs[i + 1]   = perimeter[i] * 0.5f + Vector2.one * 0.5f;
        }

        // Fan triangles from centroid: (centroid, v_i, v_{i+1 mod n}).
        int[] tris = new int[n * 3];
        for (int i = 0; i < n; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i < n - 1 ? i + 2 : 1;
        }

        Mesh mesh = new Mesh { name = "GlassShard" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
