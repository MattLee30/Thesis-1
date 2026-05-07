using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;


public class BoidsTest
{
    const string ScenePath        = "Assets/Scenes/BoidsTest.unity";
    const int    WarmupFrames     = 30;
    const float  RecordingSeconds = 30f;
    const int    TimeoutMs        = 90_000;

    static readonly Dictionary<ShaderMode, string> MatPaths = new()
    {
        { ShaderMode.Static,  "Assets/Static Glass + Photoshop/StainedGlassStatic.mat" },
        { ShaderMode.Dynamic, "Assets/Static Glass + Photoshop/DynamicShader.mat"       },
        { ShaderMode.Hybrid,  "Assets/Static Glass + Photoshop/HybridShader.mat"        },
    };

    static IEnumerable<TestCaseData> ProfileCases()
    {
        foreach (ShaderMode mode in new[] { ShaderMode.Static, ShaderMode.Dynamic, ShaderMode.Hybrid })
            foreach (int count in new[] { 100, 250, 500, 1000 })
                yield return new TestCaseData(mode, count).SetName($"{mode}_{count}boids");
    }

    [UnityTest, Timeout(TimeoutMs), TestCaseSource(nameof(ProfileCases))]
    public IEnumerator Profile(ShaderMode mode, int boidCount)
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var flock = Object.FindFirstObjectByType<FlockManager>();
        Assert.IsNotNull(flock, "FlockManager not found in scene");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPaths[mode]);
        Assert.IsNotNull(mat, $"Material not found at {MatPaths[mode]}");

        flock.Respawn(boidCount, mat);
        yield return null;

        var go      = new GameObject("Profiler");
        var profiler = go.AddComponent<StainedGlassProfiler>();
        profiler.boidCount         = boidCount;
        profiler.shaderMode        = mode;
        profiler.warmupFrames      = WarmupFrames;
        profiler.recordingDuration = RecordingSeconds;
        profiler.logToCSV          = true;
        profiler.showOverlay       = false;

        yield return new WaitUntil(() => profiler.IsDone);

        Assert.IsTrue(File.Exists(profiler.CsvPath),
            $"CSV not written: expected at {profiler.CsvPath}");
    }
}
