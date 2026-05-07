using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

public enum ShaderMode { Static, Dynamic, Hybrid }

[AddComponentMenu("Profiling/Stained Glass Shader Profiler")]
public class StainedGlassProfiler : MonoBehaviour
{
    [Header("Experiment")]
    public int boidCount = 500;
    public ShaderMode shaderMode = ShaderMode.Static;

    [Header("Timing")]
    [Tooltip("Frames to skip before recording begins")]
    public int warmupFrames = 60;
    [Tooltip("Seconds to record GPU data")]
    public float recordingDuration = 30f;
    [Tooltip("Rolling average window size in frames (display only)")]
    public int rollingWindow = 120;

    [Header("Output")]
    public bool logToCSV = true;
    public bool showOverlay = true;

    public bool IsDone => _phase == Phase.Done;
    public string CsvPath => _csvPath;

    // ── Profiler recorders ────────────────────────────────────────────────────
    private ProfilerRecorder _gpuRecorder;
    private ProfilerRecorder _renderThreadRecorder;

    // ── Phase state ───────────────────────────────────────────────────────────
    private enum Phase { Warmup, Recording, Done }
    private Phase _phase = Phase.Warmup;
    private int _frame;
    private float _phaseStart;
    private float _elapsed;

    // per-phase accumulators
    private double _phaseGpuSum;
    private long   _phaseSamples;

    // final average
    private double _recordingAvg;

    // rolling window for overlay display
    private readonly Queue<double> _gpuWindow = new Queue<double>();
    private double _windowSum;
    private long   _totalSamples;

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

        _phase = Phase.Warmup;
        _frame = 0;

        Debug.Log($"[StainedGlassProfiler] Shader={shaderMode}  Boids={boidCount}  " +
                  $"Warmup={warmupFrames}frames  Recording={recordingDuration}s");
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

        if (_phase == Phase.Warmup)
        {
            if (_frame >= warmupFrames) EnterPhase(Phase.Recording);
            return;
        }

        if (_phase == Phase.Done) return;

        _elapsed = Time.realtimeSinceStartup - _phaseStart;

        double gpuMs = SampleRecorder(_gpuRecorder);

        if (gpuMs > 0)
        {
            _gpuWindow.Enqueue(gpuMs);
            _windowSum += gpuMs;
            if (_gpuWindow.Count > rollingWindow)
                _windowSum -= _gpuWindow.Dequeue();
            _totalSamples++;
        }

        if (_elapsed >= recordingDuration)
        {
            FinishPhase();
            return;
        }

        if (gpuMs > 0)
        {
            _phaseGpuSum += gpuMs;
            _phaseSamples++;

            if (logToCSV && _csv != null)
            {
                double rtMs = SampleRecorder(_renderThreadRecorder);
                _csv.WriteLine($"{Time.frameCount},{_elapsed:F4},{gpuMs:F3},{rtMs:F3},{boidCount},{shaderMode}");
            }
        }
    }

    // ── Phase transitions ─────────────────────────────────────────────────────

    void EnterPhase(Phase next)
    {
        _phase        = next;
        _phaseStart   = Time.realtimeSinceStartup;
        _phaseGpuSum  = 0;
        _phaseSamples = 0;

        switch (next)
        {
            case Phase.Recording:
                if (logToCSV) OpenCSV();
                Debug.Log("[StainedGlassProfiler] Warmup done — recording.");
                break;
            case Phase.Done:
                WriteSummary();
                CloseCSV();
                Debug.Log("[StainedGlassProfiler] Recording complete.");
                break;
        }
    }

    void FinishPhase()
    {
        _recordingAvg = _phaseSamples > 0 ? _phaseGpuSum / _phaseSamples : 0;
        Debug.Log($"[StainedGlassProfiler] {shaderMode} avg = {_recordingAvg:F3} ms  ({_phaseSamples} samples)");
        EnterPhase(Phase.Done);
    }

    void WriteSummary()
    {
        if (_csv == null) return;
        _csv.WriteLine($"# SUMMARY,shader={shaderMode},avg_gpu_ms={_recordingAvg:F3},boids={boidCount}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static double SampleRecorder(ProfilerRecorder recorder)
    {
        if (!recorder.Valid || recorder.Count == 0) return 0;
        long sum = 0;
        for (int i = 0; i < recorder.Count; i++)
            sum += recorder.GetSample(i).Value;
        return (sum / (double)recorder.Count) * 1e-6;
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    void OpenCSV()
    {
        string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _csvPath = Path.Combine(Application.dataPath, $"{shaderMode}{boidCount}_{ts}.csv");
        _csv = new StreamWriter(_csvPath, false, Encoding.UTF8);

        _csv.WriteLine("# StainedGlass Shader Profiler");
        _csv.WriteLine($"# ShaderMode: {shaderMode}");
        _csv.WriteLine($"# BoidCount: {boidCount}");
        _csv.WriteLine($"# RecordingDuration_s: {recordingDuration}");
        _csv.WriteLine($"# GPU: {SystemInfo.graphicsDeviceName}");
        _csv.WriteLine($"# CPU: {SystemInfo.processorType}");
        _csv.WriteLine($"# RAM_MB: {SystemInfo.systemMemorySize}");
        _csv.WriteLine($"# UnityVersion: {Application.unityVersion}");
        _csv.WriteLine($"# Recorded: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _csv.WriteLine("Frame,PhaseTime_s,GPU_ms,RenderThread_ms,BoidCount,ShaderMode");

        Debug.Log($"[StainedGlassProfiler] CSV → {_csvPath}");
    }

    void CloseCSV()
    {
        if (_csv == null) return;
        _csv.Flush();
        _csv.Close();
        _csv = null;
        if (_csvPath != null)
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
        _sb.AppendLine("<b>═══ StainedGlass Shader Profiler ═══</b>");
        _sb.AppendLine($"Shader: <b>{shaderMode}</b>   Boids: <b>{boidCount}</b>");

        switch (_phase)
        {
            case Phase.Warmup:
                _sb.AppendLine($"<color=yellow>Warming up…  {_frame} / {warmupFrames} frames</color>");
                break;

            case Phase.Done:
                _sb.AppendLine("<color=cyan><b>Complete</b></color>");
                _sb.AppendLine($"Avg GPU:   <b>{_recordingAvg:F2} ms</b>");
                _sb.AppendLine($"Samples:   {_totalSamples}");
                if (_csvPath != null) _sb.AppendLine($"<color=grey>{Path.GetFileName(_csvPath)}</color>");
                break;

            default:
            {
                float remaining = recordingDuration - _elapsed;
                double dispAvg  = _gpuWindow.Count > 0 ? _windowSum / _gpuWindow.Count : 0;

                _sb.AppendLine($"<color=lime><b>Recording</b>  {_elapsed:F1}s / {recordingDuration}s  ({remaining:F1}s left)</color>");
                _sb.AppendLine($"GPU avg  <b>{dispAvg:F2} ms</b>  ({(dispAvg > 0 ? 1000.0 / dispAvg : 0):F0} fps equiv.)");
                _sb.AppendLine($"Samples: {_totalSamples}");
                if (logToCSV && _csvPath != null)
                    _sb.AppendLine($"<color=grey>{Path.GetFileName(_csvPath)}</color>");
                break;
            }
        }

        float w = 380, h = 160;
        GUI.Box(new Rect(10, 10, w, h), _sb.ToString(), _boxStyle);
    }
}
