using UnityEngine;
using UnityEngine.UI;

public class AutoAssign : MonoBehaviour
{
    public Image mainImage;
    public Image normalImage;
    public Image distortionImage;
    
    void Start()
    {
        AssignTexturesToMaterial();
    }

    void AssignTexturesToMaterial()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("AutoAssign: No Renderer component found on " + gameObject.name);
            return;
        }
        
        Material material = renderer.material;
        
        // Assign textures from images if they're set
        if (mainImage != null && mainImage.sprite != null)
            material.SetTexture("maintext", mainImage.sprite.texture);
        
        if (normalImage != null && normalImage.sprite != null)
            material.SetTexture("normaltext", normalImage.sprite.texture);
        
        if (distortionImage != null && distortionImage.sprite != null)
            material.SetTexture("distortiontext", distortionImage.sprite.texture);
        
        Debug.Log("Textures assigned to material on " + gameObject.name);
    }
}
