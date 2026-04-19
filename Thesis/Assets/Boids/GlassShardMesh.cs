using System.Collections.Generic;
using UnityEngine;

// Generates a mesh for one shard of a broken stained-glass window.
// FlockManager calls Initialize() right after Instantiate(); the mesh is built
// immediately so the MeshCollider is valid before physics runs.
// If used standalone (no FlockManager), Start() falls back to a random shard shape.
[RequireComponent(typeof(MeshFilter))]
public class GlassShardMesh : MonoBehaviour
{
    [Tooltip("Standalone size — only used when FlockManager has not called Initialize().")]
    [SerializeField] private float size = 1f;

    [Tooltip("Vertex jitter in standalone mode. Higher = more jagged.")]
    [SerializeField] [Range(0f, 0.25f)] private float jitter = 0.10f;

    [Tooltip("Seed for standalone shape variation. -1 uses instance ID.")]
    [SerializeField] private int seed = -1;

    private WindowFractureLayout.ShardData _shard;
    private float _windowWidth, _windowHeight;
    private bool _initialized;

    // XZ offset from the window center where this shard sits when the puzzle is assembled.
    public Vector2 HomeOffsetXZ => _shard.centroid;

    // Called by FlockManager immediately after Instantiate.
    public void Initialize(WindowFractureLayout.ShardData data, float windowWidth, float windowHeight)
    {
        _shard       = data;
        _windowWidth  = windowWidth;
        _windowHeight = windowHeight;
        _initialized  = true;
        ApplyMesh(BuildMeshFromShard());
    }

    private void Start()
    {
        if (!_initialized)
            ApplyMesh(BuildFallbackMesh());
    }

    private void ApplyMesh(Mesh mesh)
    {
        GetComponent<MeshFilter>().mesh = mesh;
        MeshCollider col = GetComponent<MeshCollider>();
        if (col != null)
            col.sharedMesh = mesh;
    }

    // Build a mesh whose UV coordinates map into the shared window texture.
    private Mesh BuildMeshFromShard()
    {
        Vector2[] verts2D = _shard.vertices;
        int n = verts2D.Length;

        Vector3[] verts = new Vector3[n];
        Vector2[] uvs   = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            verts[i] = new Vector3(verts2D[i].x, 0f, verts2D[i].y);
            // Map the vertex's window-space position into [0,1] UV space.
            Vector2 windowPos = verts2D[i] + _shard.centroid;
            uvs[i] = new Vector2(
                windowPos.x / _windowWidth  + 0.5f,
                windowPos.y / _windowHeight + 0.5f
            );
        }

        // Fan triangulation from vertex 0 — valid for the convex polygons produced by the fracture.
        int[] tris = new int[(n - 2) * 3];
        for (int i = 0; i < n - 2; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        Mesh mesh = new Mesh { name = "GlassShard" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Standalone fallback (original behavior, unchanged) ────────────────────

    private static readonly Vector2[] BaseFallbackPerimeter =
    {
        new Vector2( 0.00f,  0.95f),
        new Vector2(-0.35f,  0.55f),
        new Vector2(-0.58f,  0.05f),
        new Vector2(-0.15f, -0.72f),
        new Vector2( 0.05f, -0.90f),
        new Vector2( 0.50f, -0.58f),
        new Vector2( 0.55f,  0.12f),
        new Vector2( 0.40f,  0.60f),
    };

    private Mesh BuildFallbackMesh()
    {
        int n = BaseFallbackPerimeter.Length;
        Vector2[] perimeter = new Vector2[n];

        Random.State saved = Random.state;
        Random.InitState(seed >= 0 ? seed : GetInstanceID());
        for (int i = 0; i < n; i++)
            perimeter[i] = BaseFallbackPerimeter[i] + Random.insideUnitCircle * jitter;
        Random.state = saved;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++)
            centroid += perimeter[i];
        centroid /= n;

        Vector3[] verts = new Vector3[n + 1];
        Vector2[] uvs   = new Vector2[n + 1];

        verts[0] = new Vector3(centroid.x * size, 0f, centroid.y * size);
        uvs[0]   = centroid * 0.5f + Vector2.one * 0.5f;

        for (int i = 0; i < n; i++)
        {
            verts[i + 1] = new Vector3(perimeter[i].x * size, 0f, perimeter[i].y * size);
            uvs[i + 1]   = perimeter[i] * 0.5f + Vector2.one * 0.5f;
        }

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

// ── Window fracture layout ────────────────────────────────────────────────────
// Recursively cuts a rectangle into `count` convex shards by slicing the largest
// remaining piece with a random chord between two non-adjacent boundary points.
// All shards tile the original window with zero gaps or overlaps.
public static class WindowFractureLayout
{
    public struct ShardData
    {
        // Mesh vertices in local space: the polygon centered at the origin.
        public Vector2[] vertices;
        // XZ offset from the window center where this shard sits when assembled.
        public Vector2 centroid;
    }

    public static ShardData[] Generate(int count, float width, float height, int seed)
    {
        var shards = new List<List<Vector2>>();
        shards.Add(new List<Vector2>
        {
            new Vector2(-width  * 0.5f, -height * 0.5f),
            new Vector2( width  * 0.5f, -height * 0.5f),
            new Vector2( width  * 0.5f,  height * 0.5f),
            new Vector2(-width  * 0.5f,  height * 0.5f),
        });

        var rng         = new System.Random(seed);
        int maxAttempts = count * 30;
        int attempts    = 0;

        while (shards.Count < count && attempts++ < maxAttempts)
        {
            int           idx  = LargestIndex(shards);
            List<Vector2> poly = shards[idx];
            int           n    = poly.Count;

            // Pick two boundary points on non-adjacent edges.
            int e1 = rng.Next(n);
            int e2 = n >= 4
                ? (e1 + 2 + rng.Next(n - 2)) % n
                : (e1 + 1 + rng.Next(2))      % n;

            // Keep cut points away from edge endpoints to avoid degenerate slivers.
            float t1 = 0.2f + (float)(rng.NextDouble() * 0.6);
            float t2 = 0.2f + (float)(rng.NextDouble() * 0.6);

            Vector2 p1 = Lerp2(poly[e1], poly[(e1 + 1) % n], t1);
            Vector2 p2 = Lerp2(poly[e2], poly[(e2 + 1) % n], t2);

            if (!Slice(poly, p1, p2, out List<Vector2> left, out List<Vector2> right))
                continue;

            shards.RemoveAt(idx);
            shards.Add(left);
            shards.Add(right);
        }

        // Should always reach `count`; trim as a safety net.
        while (shards.Count > count)
            shards.RemoveAt(shards.Count - 1);

        var result = new ShardData[shards.Count];
        for (int i = 0; i < shards.Count; i++)
        {
            Vector2[] poly = shards[i].ToArray();
            Vector2   c    = Centroid(poly);
            result[i] = new ShardData
            {
                vertices = Translate(poly, -c),
                centroid = c,
            };
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static int LargestIndex(List<List<Vector2>> shards)
    {
        int   best     = 0;
        float bestArea = Area(shards[0]);
        for (int i = 1; i < shards.Count; i++)
        {
            float a = Area(shards[i]);
            if (a > bestArea) { bestArea = a; best = i; }
        }
        return best;
    }

    static float Area(List<Vector2> poly)
    {
        float a = 0f;
        int   n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 v = poly[i], w = poly[(i + 1) % n];
            a += v.x * w.y - w.x * v.y;
        }
        return Mathf.Abs(a) * 0.5f;
    }

    // Splits poly with a line through p1→p2 into left and right halves.
    // Returns false if either half degenerates to fewer than 3 vertices.
    static bool Slice(List<Vector2> poly, Vector2 p1, Vector2 p2,
                      out List<Vector2> left, out List<Vector2> right)
    {
        left  = new List<Vector2>();
        right = new List<Vector2>();
        int     n   = poly.Count;
        Vector2 dir = new Vector2(p2.x - p1.x, p2.y - p1.y);

        for (int i = 0; i < n; i++)
        {
            Vector2 curr = poly[i];
            Vector2 next = poly[(i + 1) % n];
            float   sc   = Cross(dir, new Vector2(curr.x - p1.x, curr.y - p1.y));
            float   sn   = Cross(dir, new Vector2(next.x - p1.x, next.y - p1.y));

            if (sc >= 0f) left.Add(curr);
            if (sc <= 0f) right.Add(curr);

            if ((sc > 0f && sn < 0f) || (sc < 0f && sn > 0f))
            {
                float   t   = sc / (sc - sn);
                Vector2 hit = new Vector2(curr.x + t * (next.x - curr.x),
                                          curr.y + t * (next.y - curr.y));
                left.Add(hit);
                right.Add(hit);
            }
        }

        return left.Count >= 3 && right.Count >= 3;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // Signed-area centroid formula (exact for any simple polygon).
    static Vector2 Centroid(Vector2[] poly)
    {
        float area = 0f, cx = 0f, cy = 0f;
        int   n    = poly.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 a  = poly[i], b = poly[(i + 1) % n];
            float   cr = a.x * b.y - b.x * a.y;
            area += cr;
            cx   += (a.x + b.x) * cr;
            cy   += (a.y + b.y) * cr;
        }
        area *= 0.5f;
        if (Mathf.Abs(area) < 1e-6f)
        {
            Vector2 avg = Vector2.zero;
            for (int i = 0; i < n; i++) avg += poly[i];
            return avg / n;
        }
        return new Vector2(cx / (6f * area), cy / (6f * area));
    }

    static Vector2[] Translate(Vector2[] poly, Vector2 offset)
    {
        var result = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++)
            result[i] = new Vector2(poly[i].x + offset.x, poly[i].y + offset.y);
        return result;
    }

    static Vector2 Lerp2(Vector2 a, Vector2 b, float t) =>
        new Vector2(a.x + t * (b.x - a.x), a.y + t * (b.y - a.y));
}
