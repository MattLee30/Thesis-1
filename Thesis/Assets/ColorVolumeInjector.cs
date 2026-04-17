using UnityEngine;

public class ColorVolumeInjector : MonoBehaviour
{
    [SerializeField] private RenderTexture colorVolumeRT;
    [SerializeField] private Material stainedGlassMaterial;
    [SerializeField] private Material boidMaterial;

    private static readonly int ColorVolumeID = 
        Shader.PropertyToID("ColorVolumeRT");

    void Update()
    {
        if (stainedGlassMaterial != null)
            stainedGlassMaterial.SetTexture(ColorVolumeID, colorVolumeRT);

        if (boidMaterial != null)
            boidMaterial.SetTexture(ColorVolumeID, colorVolumeRT);
    }
}