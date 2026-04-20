using UnityEngine;

// Configures FlockManager with an input image before shards are spawned.
// Awake() on any component runs before Start() on any component, so window
// dimensions and the shared shard material are ready before FlockManager.Start()
// calls SpawnFlock(). Attach to the same GameObject as FlockManager, or wire
// the flockManager reference manually.
public class ImageToGlassPipeline : MonoBehaviour
{
    [Header("Image Input")]
    [Tooltip("The source image. Each shard will display its corresponding fragment.")]
    [SerializeField] private Texture2D sourceImage;

    [Header("Window Config")]
    [Tooltip("Physical size (Unity units) of the window's longest edge.")]
    [SerializeField] private float windowScale = 10f;
    [SerializeField] private int   fractureSeed = 42;

    [Header("Material")]
    [Tooltip("Stained-glass material template. A shared instance is created with the source image applied.")]
    [SerializeField] private Material shardMaterialTemplate;

    [Header("References")]
    [SerializeField] private FlockManager flockManager;

    [Header("Background Plane (optional)")]
    [Tooltip("If assigned, scaled to window dimensions and shows the full source image.")]
    [SerializeField] private Renderer backgroundPlaneRenderer;

    // The shared material instance given to every shard.
    public Material SharedMaterial { get; private set; }
    public float    WindowWidth    { get; private set; }
    public float    WindowHeight   { get; private set; }

    private void Awake()
    {
        if (sourceImage == null)
        {
            Debug.LogWarning("ImageToGlassPipeline: no source image assigned — pipeline inactive.");
            return;
        }

        if (flockManager == null)
            flockManager = GetComponent<FlockManager>();

        // Derive window dimensions that preserve the image aspect ratio.
        float aspect = (float)sourceImage.width / sourceImage.height;
        WindowWidth  = aspect >= 1f ? windowScale          : windowScale * aspect;
        WindowHeight = aspect >= 1f ? windowScale / aspect : windowScale;

        if (flockManager != null)
            flockManager.SetWindowConfig(WindowWidth, WindowHeight, fractureSeed);

        // Build shared shard material with the source image.
        if (shardMaterialTemplate != null)
        {
            SharedMaterial = new Material(shardMaterialTemplate) { name = "ShardMat_" + sourceImage.name };
            ApplyImageToMaterial(SharedMaterial, sourceImage);

            if (flockManager != null)
                flockManager.SetShardMaterial(SharedMaterial);
        }

        // Scale + texture the optional background plane.
        if (backgroundPlaneRenderer != null)
        {
            Material bgMat = shardMaterialTemplate != null
                ? new Material(shardMaterialTemplate) { name = "BG_" + sourceImage.name }
                : new Material(backgroundPlaneRenderer.sharedMaterial);
            ApplyImageToMaterial(bgMat, sourceImage);
            backgroundPlaneRenderer.sharedMaterial = bgMat;
            backgroundPlaneRenderer.transform.localScale = new Vector3(WindowWidth, 1f, WindowHeight);
        }
    }

    // Swap the source image at runtime and refresh all materials.
    public void SetSourceImage(Texture2D tex)
    {
        if (tex == null) return;
        sourceImage = tex;

        if (SharedMaterial != null)
            ApplyImageToMaterial(SharedMaterial, tex);

        if (backgroundPlaneRenderer != null && backgroundPlaneRenderer.sharedMaterial != null)
            ApplyImageToMaterial(backgroundPlaneRenderer.sharedMaterial, tex);
    }

    private static void ApplyImageToMaterial(Material mat, Texture2D tex)
    {
        // Cover both the standard Unity slot and the custom "maintext" property
        // used by the StainedGlass shadergraphs.
        mat.mainTexture = tex;
        if (mat.HasProperty("maintext"))
            mat.SetTexture("maintext", tex);
    }
}
