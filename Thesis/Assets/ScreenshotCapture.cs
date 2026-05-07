using System.Collections;
using System.IO;
using UnityEngine;

[AddComponentMenu("Profiling/Stained Glass Screenshot Capture")]
public class ScreenshotCapture : MonoBehaviour
{
    [Header("Shader")]
    [Tooltip("Which shader is active in this scene — used to label the PNG filename")]
    public ShaderMode shaderMode = ShaderMode.Static;

    [Header("Capture")]
    [Tooltip("Press this key to capture a screenshot")]
    public KeyCode captureKey = KeyCode.F9;
    [Tooltip("Extra frames to wait before capturing (let the GPU flush)")]
    public int settleFrames = 3;

    private int _setIndex = 0;
    private bool _capturing = false;

    void Start()
    {
        Debug.Log($"[ScreenshotCapture] Ready — press {captureKey} to capture. Shader={shaderMode}");
    }

    void Update()
    {
        if (Input.GetKeyDown(captureKey) && !_capturing)
            StartCoroutine(Capture());
    }

    IEnumerator Capture()
    {
        _capturing = true;
        Time.timeScale = 0f;

        for (int i = 0; i < settleFrames; i++)
            yield return new WaitForEndOfFrame();

        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();

        string path = Path.Combine(Application.dataPath, $"screenshot_{shaderMode}_{_setIndex:D3}.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);

        Debug.Log($"[ScreenshotCapture] Saved → {path}");

        Time.timeScale = 1f;
        _setIndex++;
        _capturing = false;

        Debug.Log($"[ScreenshotCapture] Press {captureKey} again to capture another state.");
    }
}
