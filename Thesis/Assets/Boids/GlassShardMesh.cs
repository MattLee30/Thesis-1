using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GlassShardMesh : MonoBehaviour
{
    [Header("Shard Shape")]
    [SerializeField, Range(5, 8)] private int vertexCount = 6;
    [SerializeField] private float baseRadius = 0.3f;
    [SerializeField, Range(0f, 1f)] private float irregularity = 0.55f;
    [SerializeField, Range(0f, 1f)] private float elongation = 0.5f;

    [Header("Visual")]
    [SerializeField] private Material glassMaterial; // assign a shared URP/Unlit transparent mat

    private static readonly int ColorID = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        GenerateShard();
    }

    private void GenerateShard()
    {
        // --- Shape parameters randomised once per boid ---
        int n = Random.Range(5, vertexCount + 1);
        float elongFactor = 1f + Random.Range(0f, elongation) * 2f; // stretch on one axis
        float elongAngle = Random.Range(0f, Mathf.PI);               // direction of stretch

        Vector3[] verts = new Vector3[n + 1]; // +1 for centre
        int[] tris = new int[n * 3];

        verts[0] = Vector3.zero; // centre vertex

        for (int i = 0; i < n; i++)
        {
            // Evenly spaced base angle with random jitter
            float baseAngle = (Mathf.PI * 2f * i) / n;
            float jitter = (Random.value - 0.5f) * (Mathf.PI * 2f / n) * irregularity;
            float angle = baseAngle + jitter;

            // Radius jitter
            float r = baseRadius * Random.Range(1f - irregularity, 1f);

            // Apply elongation along elongAngle
            float cosA = Mathf.Cos(angle - elongAngle);
            r *= Mathf.Lerp(1f, elongFactor * Mathf.Abs(cosA) + (1f - Mathf.Abs(cosA)), elongation);

            verts[i + 1] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
        }

        // Fan triangles from centre
        for (int i = 0; i < n; i++)
        {
            int ti = i * 3;
            tris[ti]     = 0;
            tris[ti + 1] = i + 1;
            tris[ti + 2] = (i + 1) % n + 1;
        }

        Mesh mesh = new Mesh();
        mesh.name = "GlassShard";
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (glassMaterial != null)
            mr.sharedMaterial = glassMaterial;

        // Per-instance colour via MaterialPropertyBlock — no material duplication
        _mpb = new MaterialPropertyBlock();
        Color c = Color.white;
        c.a = Random.Range(0.35f, 0.75f); // each shard slightly different opacity
        _mpb.SetColor(ColorID, c);
        mr.SetPropertyBlock(_mpb);
    }

    public void SetTint(Color flockColor)
    {
    if (_mpb == null) _mpb = new MaterialPropertyBlock();
    MeshRenderer mr = GetComponent<MeshRenderer>();
    mr.GetPropertyBlock(_mpb);

    // Preserve per-instance alpha, apply flock hue
    Color current = _mpb.GetColor(ColorID);
    flockColor.a = current.a;
    _mpb.SetColor(ColorID, flockColor);
    mr.SetPropertyBlock(_mpb);
    }
}