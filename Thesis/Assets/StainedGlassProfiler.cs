using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

public enum ConfigLabel { Static, Dynamic, Hybrid }

/// <summary>
/// Records per-frame GPU cost for three named rendering configurations.
/// Each run produces a CSV that graph_profiler.py converts into thesis figures
/// and a LaTeX table.
///
/// Workflow:
///   1. Set Configuration and BoidCount in the Inspector for this run.
///   2. Enter Play mode; let warmup complete (overlay turns green).
///   3. Press [I] to run an isolation measurement, then exit Play.
///   4. Repeat for each configuration (Static / Dynamic / Hybrid).
///   5. Run graph_profiler.py on all three CSVs.
/// </summary>
[AddComponentMenu("Profiling/Stained Glass Shader Profiler")]
public class StainedGlassProfiler : MonoBehaviour
{
    [Header("Experiment")]
    [Tooltip("Which rendering configuration is active in this scene.")]
    public ConfigLabel configuration = ConfigLabel.Hybrid;
    [Tooltip("Number of active boid agents. Set manually to match your BoidManager.")]
    public int boidCount = 500;

    [Header("Shader Filter")]
    public string shaderNameFilter = "StainedGlass";

    [Header("Sampling")]
    [Tooltip("Frames to skip before recording begins")]
    public int warmupFrames = 30;
    [Tooltip("Rolling average window size in frames")]
    public int rollingWindow = 120;

    [Header("Isolation Mode")]
    [Tooltip("Key to trigger one isolation measurement cycle")]
    public KeyCode isolationKey = KeyCode.I;
    [Tooltip("Frames to hold the shader disabled during isolation")]
    public int isolationHoldFrames = 60;

    [Header("Output")]
    public bool logToCSV = true;
    [Tooltip("Disable to hide the on-screen overlay")]
    public bool showOverlay = true;
    public string csvFilePrefix = "StainedGlassProfile";

    // ── Profiler recorders ────────────────────────────────────────────────────
    private ProfilerRecorder _gpuRecorder;
    private ProfilerRecorder _renderThreadRecorder;

    // ── Stats ─────────────────────────────────────────────────────────────────
    private readonly Queue<double> _gpuWindow = new Queue<double>();
    private double _windowSum;
    private double _allTimeMin = double.MaxValue;
    private double _allTimeMax = double.MinValue;
    private long _totalSamples;
    private int _frame;

    // ── Isolation state ───────────────────────────────────────────────────────
    private enum IsoState { Idle, BaselineWait, ShaderOff }
    private IsoState _isoState = IsoState.Idle;
    private int _isoFrameCounter;
    private double _isoBaseline;
    private double _isoShaderOff;
    private double _lastIsolationCost = double.NaN;
    private Renderer[] _targetRenderers;

    // ── CSV ───────────────────────────────────────────────────────────────────
    private StreamWriter _csv;
    private string _csvPath;

    // ── GUI ───────────────────────────────────────────────────────────────────
    private GUIStyle _boxStyle;
    private readonly StringBuilder _sb = new StringBuilder(512);

    // ─────────────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        _gpuRecorder          = ProfilerRecorder.StartNew(ProfilerCategory.Render,   "GPU Frame Time", 8);
        _renderThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread",  8);

        if (logToCSV) OpenCSV();

        Debug.Log($"[StainedGlassProfiler] Config={configuration} Boids={boidCount}. " +
                  $"Press [{isolationKey}] for isolation measurement.");
    }

    void OnDisable()
    {
        _gpuRecorder.Dispose();
        _renderThreadRecorder.Dispose();
        CloseCSV();
    }

    void Update()
    {
        _frame++;
        if (_frame < warmupFrames) return;

        double gpuMs = SampleRecorder(_gpuRecorder);

        if (gpuMs > 0)
        {
            _gpuWindow.Enqueue(gpuMs);
            _windowSum += gpuMs;
            if (_gpuWindow.Count > rollingWindow)
                _windowSum -= _gpuWindow.Dequeue();

            _allTimeMin = System.Math.Min(_allTimeMin, gpuMs);
            _allTimeMax = System.Math.Max(_allTimeMax, gpuMs);
            _totalSamples++;

            if (logToCSV && _csv != null)
            {
                double rtMs = SampleRecorder(_renderThreadRecorder);
                int isoActive = _isoState != IsoState.Idle ? 1 : 0;
                _csv.WriteLine($"{Time.frameCount},{gpuMs:F3},{rtMs:F3},{boidCount},{configuration},{isoActive}");
            }
        }

        if (Input.GetKeyDown(isolationKey) && _isoState == IsoState.Idle)
            StartIsolation();

        TickIsolation(gpuMs);
    }

    // ── Isolation logic ───────────────────────────────────────────────────────

    void StartIsolation()
    {
        _targetRenderers = FindStainedGlassRenderers();
        if (_targetRenderers.Length == 0)
        {
            Debug.LogWarning($"[StainedGlassProfiler] No renderers found matching '{shaderNameFilter}'.");
            return;
        }
        Debug.Log($"[StainedGlassProfiler] Isolation started — {_targetRenderers.Length} renderer(s).");
        _isoState = IsoState.BaselineWait;
        _isoFrameCounter = 0;
        _isoBaseline = 0;
        _isoShaderOff = 0;
    }

    void TickIsolation(double gpuMs)
    {
        if (_isoState == IsoState.Idle) return;
        _isoFrameCounter++;

        switch (_isoState)
        {
            case IsoState.BaselineWait:
                _isoBaseline += gpuMs;
                if (_isoFrameCounter >= isolationHoldFrames / 2)
                {
                    _isoBaseline /= (isolationHoldFrames / 2.0);
                    SetStainedGlassEnabled(false);
                    _isoFrameCounter = 0;
                    _isoState = IsoState.ShaderOff;
                }
                break;

            case IsoState.ShaderOff:
                _isoShaderOff += gpuMs;
                if (_isoFrameCounter >= isolationHoldFrames)
                {
                    _isoShaderOff /= (double)isolationHoldFrames;
                    SetStainedGlassEnabled(true);
                    _lastIsolationCost = _isoBaseline - _isoShaderOff;

                    Debug.Log($"[StainedGlassProfiler] Isolation — baseline: {_isoBaseline:F3} ms | " +
                              $"shader off: {_isoShaderOff:F3} ms | cost: {_lastIsolationCost:F3} ms");

                    if (logToCSV && _csv != null)
                        _csv.WriteLine($"# ISOLATION,baseline_ms={_isoBaseline:F3}," +
                                       $"shaderOff_ms={_isoShaderOff:F3},cost_ms={_lastIsolationCost:F3}," +
                                       $"config={configuration},boids={boidCount}");

                    _isoState = IsoState.Idle;
                }
                break;
        }
    }

    Renderer[] FindStainedGlassRenderers()
    {
        var results = new List<Renderer>();
        foreach (var r in FindObjectsOfType<Renderer>())
            foreach (var mat in r.sharedMaterials)
                if (mat != null && mat.shader != null &&
                    mat.shader.name.IndexOf(shaderNameFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(r);
                    break;
                }
        return results.ToArray();
    }

    void SetStainedGlassEnabled(bool enabled)
    {
        if (_targetRenderers == null) return;
        foreach (var r in _targetRenderers)
            if (r != null) r.enabled = enabled;
    }

    // ── Profiler helper ───────────────────────────────────────────────────────

    static double SampleRecorder(ProfilerRecorder recorder)
    {
        if (!recorder.Valid || recorder.Count == 0) return 0;
        long sum = 0;
        for (int i = 0; i < recorder.Count; i++)
            sum += recorder.GetSample(i).Value;
        return (sum / (double)recorder.Count) * 1e-6; // nanoseconds → milliseconds
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    void OpenCSV()
    {
        string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _csvPath = Path.Combine(Application.dataPath, $"{csvFilePrefix}_{ts}.csv");
        _csv = new StreamWriter(_csvPath, false, Encoding.UTF8);

        // Header block — parsed by graph_profiler.py to populate figure captions and the LaTeX table
        _csv.WriteLine("# StainedGlass Shader Profiler");
        _csv.WriteLine($"# Configuration: {configuration}");
        _csv.WriteLine($"# BoidCount: {boidCount}");
        _csv.WriteLine($"# GPU: {SystemInfo.graphicsDeviceName}");
        _csv.WriteLine($"# CPU: {SystemInfo.processorType}");
        _csv.WriteLine($"# RAM_MB: {SystemInfo.systemMemorySize}");
        _csv.WriteLine($"# UnityVersion: {Application.unityVersion}");
        _csv.WriteLine($"# Recorded: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _csv.WriteLine("Frame,GPU_ms,RenderThread_ms,BoidCount,Configuration,IsolationActive");

        Debug.Log($"[StainedGlassProfiler] CSV → {_csvPath}");
    }

    void CloseCSV()
    {
        if (_csv == null) return;
        _csv.Flush();
        _csv.Close();
        _csv = null;
        Debug.Log($"[StainedGlassProfiler] CSV saved → {_csvPath}");
    }

    // ── Overlay ───────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!showOverlay) return;

        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 13,
                alignment = TextAnchor.UpperLeft,
                richText  = true,
                padding   = new RectOffset(12, 12, 10, 10)
            };
            _boxStyle.normal.textColor = Color.white;
        }

        _sb.Clear();

        if (_frame < warmupFrames)
        {
            _sb.AppendLine($"<b>StainedGlass Profiler</b>  warming up {_frame}/{warmupFrames}");
            _sb.AppendLine($"Config: <b>{configuration}</b>   Boids: <b>{boidCount}</b>");
        }
        else
        {
            double avg = _gpuWindow.Count > 0 ? _windowSum / _gpuWindow.Count : 0;
            double min = _allTimeMin == double.MaxValue ? 0 : _allTimeMin;
            double max = _allTimeMax == double.MinValue ? 0 : _allTimeMax;

            _sb.AppendLine("<b>═══ StainedGlass Shader Profiler ═══</b>");
            _sb.AppendLine($"<color=lime>Config: <b>{configuration}</b>   Boids: <b>{boidCount}</b></color>");
            _sb.AppendLine($"GPU  avg  <b>{avg:F2} ms</b>  ({(avg > 0 ? 1000.0 / avg : 0):F0} fps equiv.)");
            _sb.AppendLine($"GPU  min  {min:F2} ms   max  {max:F2} ms");
            _sb.AppendLine($"Samples: {_totalSamples}   Window: {_gpuWindow.Count}/{rollingWindow}");

            if (_isoState != IsoState.Idle)
            {
                string phase = _isoState == IsoState.BaselineWait ? "measuring baseline..." : "shader OFF...";
                _sb.AppendLine($"<color=yellow>Isolation: {phase} ({_isoFrameCounter}f)</color>");
            }
            else if (!double.IsNaN(_lastIsolationCost))
            {
                _sb.AppendLine($"<color=cyan>Shader cost ≈ <b>{_lastIsolationCost:F2} ms</b>  (press {isolationKey} to re-run)</color>");
            }
            else
            {
                _sb.AppendLine($"<color=grey>Press [{isolationKey}] to isolate shader cost</color>");
            }

            if (logToCSV && _csvPath != null)
                _sb.AppendLine($"<color=grey>{Path.GetFileName(_csvPath)}</color>");
        }

        float w = 400, h = 175;
        GUI.Box(new Rect(10, 10, w, h), _sb.ToString(), _boxStyle);
    }
}
