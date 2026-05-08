using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Captures screenshots during a StainedGlassProfiler recording session and
/// writes a companion CSV index mapping frame numbers to PNG filenames.
/// Run dssim_analysis.py on those PNGs + the source image to get per-frame DSSIM.
///
/// Attach to the same GameObject as StainedGlassProfiler.
/// </summary>
[AddComponentMenu("Profiling/DSSIM Capture")]
[RequireComponent(typeof(StainedGlassProfiler))]
public class DSSIMCapture : MonoBehaviour
{
    [Header("Capture Settings")]
    [Tooltip("Frames between screenshots during recording. Minimum 1.")]
    public int captureInterval = 30;

    [Tooltip("Camera to capture. Falls back to Camera.main.")]
    public Camera captureCamera;

    [Tooltip("Resolution scale relative to screen size (1 = native, 0.5 = half).")]
    [Range(0.1f, 1f)]
    public float resolutionScale = 0.5f;

    [Tooltip("Capture one final screenshot when the profiler finishes.")]
    public bool captureFinal = true;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private StainedGlassProfiler _profiler;
    private StreamWriter _indexCsv;
    private string _baseDir;
    private string _sessionPrefix;
    private int _captureCount;
    private int _framesSinceCapture;
    private bool _wasRecording;
    private bool _finalCaptured;
    private bool _indexClosed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _profiler = GetComponent<StainedGlassProfiler>();
        if (captureCamera == null)
            captureCamera = Camera.main;
    }

    void OnEnable()
    {
        _captureCount       = 0;
        _framesSinceCapture = 0;
        _wasRecording       = false;
        _finalCaptured      = false;
        _indexClosed        = false;
    }

    void OnDisable() => CloseIndex();

    void LateUpdate()
    {
        if (_profiler == null || _indexClosed) return;

        bool isRecording = !_profiler.IsDone && _profiler.CsvPath != null;

        // Transition into recording: open the index CSV
        if (isRecording && !_wasRecording)
        {
            OpenIndex();
            _wasRecording = true;
        }

        // Periodic capture while recording
        if (isRecording && _indexCsv != null)
        {
            _framesSinceCapture++;
            if (_framesSinceCapture >= Mathf.Max(1, captureInterval))
            {
                _framesSinceCapture = 0;
                StartCoroutine(CaptureFrame(Time.frameCount));
            }
        }

        // Final capture when profiler finishes
        if (_profiler.IsDone && !_finalCaptured && captureFinal && _indexCsv != null)
        {
            _finalCaptured = true;
            StartCoroutine(CaptureFrame(Time.frameCount, isFinal: true));
        }

        // Close once we are done and any final coroutine has been dispatched
        if (_profiler.IsDone && (!captureFinal || _finalCaptured))
            CloseIndex();
    }

    // ── Screenshot capture ────────────────────────────────────────────────────

    private IEnumerator CaptureFrame(int frameNumber, bool isFinal = false)
    {
        yield return new WaitForEndOfFrame();

        if (captureCamera == null || _indexCsv == null) yield break;

        int w = Mathf.Max(1, Mathf.RoundToInt(Screen.width  * resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * resolutionScale));

        // Render to an off-screen texture then read back CPU-side
        var rt   = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = captureCamera.targetTexture;
        captureCamera.targetTexture = rt;
        captureCamera.Render();
        captureCamera.targetTexture = prev;

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        string label   = isFinal ? "final" : _captureCount.ToString("D4");
        string pngName = $"{_sessionPrefix}_cap{label}_f{frameNumber}.png";
        string pngPath = Path.Combine(_baseDir, pngName);

        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        Destroy(tex);

        _indexCsv.WriteLine($"{frameNumber},{label},{pngName}");
        _indexCsv.Flush();

        if (!isFinal) _captureCount++;
        Debug.Log($"[DSSIMCapture] Saved {pngName}");
    }

    // ── Index CSV ──────────────────────────────────────────────────────────────

    private void OpenIndex()
    {
        _baseDir       = Path.GetDirectoryName(_profiler.CsvPath) ?? Application.dataPath;
        _sessionPrefix = Path.GetFileNameWithoutExtension(_profiler.CsvPath);

        string indexPath = Path.Combine(_baseDir, $"{_sessionPrefix}_screenshots.csv");
        _indexCsv = new StreamWriter(indexPath, append: false, Encoding.UTF8);
        _indexCsv.WriteLine("# DSSIMCapture screenshot index");
        _indexCsv.WriteLine($"# Session: {_sessionPrefix}");
        _indexCsv.WriteLine($"# CaptureInterval: {captureInterval}");
        _indexCsv.WriteLine($"# ResolutionScale: {resolutionScale}");
        _indexCsv.WriteLine("Frame,Label,Filename");
        Debug.Log($"[DSSIMCapture] Index → {indexPath}");
    }

    private void CloseIndex()
    {
        if (_indexCsv == null || _indexClosed) return;
        _indexCsv.Flush();
        _indexCsv.Close();
        _indexCsv   = null;
        _indexClosed = true;
        Debug.Log($"[DSSIMCapture] Index closed ({_captureCount} screenshots).");
    }
}
