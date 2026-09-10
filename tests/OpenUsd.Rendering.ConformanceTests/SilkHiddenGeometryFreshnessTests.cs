// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkHiddenGeometryFreshnessTests
{
    [Test]
    [Arguments("color")]
    [Arguments("normals")]
    [Arguments("topology")]
    [Arguments("ancestor")]
    [Arguments("delete")]
    [Arguments("redefine")]
    [Arguments("instances")]
    public Task HiddenEditsRepopulateCurrentHydraDataOnD3D12(string change) =>
        VerifyAsync(change, SilkGraphicsBackend.D3D12);

    [Test]
    [Arguments("color")]
    [Arguments("normals")]
    [Arguments("topology")]
    [Arguments("ancestor")]
    [Arguments("delete")]
    [Arguments("redefine")]
    [Arguments("instances")]
    public Task HiddenEditsRepopulateCurrentHydraDataOnVulkan(string change) =>
        VerifyAsync(change, SilkGraphicsBackend.Vulkan);

    private static async Task VerifyAsync(string change, SilkGraphicsBackend backend)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        Result result = Exercise(change, backend, plugins);
        await Assert.That(ColoredPixels(result.Initial)).IsGreaterThan(0);
        await Assert.That(ColoredPixels(result.Hidden)).IsEqualTo(0);
        await Assert.That(result.Fresh.SequenceEqual(result.Initial)).IsFalse();
        await Assert.That(result.Restored.SequenceEqual(result.Fresh)).IsTrue();
        await Assert.That(result.SourceUnchanged).IsTrue();
        await Assert.That(ColoredPixels(result.Restored))
            .IsEqualTo(ColoredPixels(result.Fresh));
        if (change == "delete")
        {
            await Assert.That(ColoredPixels(result.Restored)).IsEqualTo(0);
        }
        else
        {
            await Assert.That(ColoredPixels(result.Restored)).IsGreaterThan(0);
        }
    }

    private static Result Exercise(string change, SilkGraphicsBackend backend, string plugins)
    {
        string root = Directory.CreateTempSubdirectory("silk-hidden-current-").FullName;
        string path = Path.Combine(root, "scene.usda");
        string scene = change == "instances" ? Scene + Instances : Scene;
        UsdStageScheduler? scheduler = null;
        try
        {
            File.WriteAllText(path, scene);
            scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = scheduler.AcquireRenderSourceAsync().AsTask().GetAwaiter().GetResult();
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, source);
            using var capturer = new SilkFrameCapturer(device);
            var filter = new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render, "full");
            byte[] initial = Capture(capturer, session);
            _ = Capture(capturer, session, filter);
            scheduler.InvokeAsync(stage => Mutate(stage, change)).AsTask().GetAwaiter().GetResult();
            byte[] hidden = Capture(capturer, session, filter);
            byte[] restored = Capture(capturer, session);
            using OpenUsdSilkSession freshSession = OpenUsdSilkRuntime.Create(plugins, source);
            using var freshCapturer = new SilkFrameCapturer(device);
            byte[] fresh = Capture(freshCapturer, freshSession);
            return new Result(initial, hidden, restored, fresh, File.ReadAllText(path) == scene);
        }
        finally
        {
            try
            {
                if (scheduler is not null)
                {
                    scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] Capture(
        SilkFrameCapturer capturer, OpenUsdSilkSession session, SilkSceneIngestionOptions? filter = null)
    {
        var camera = new CameraState(Matrix4x4.Identity, new Matrix4x4(
            0.5f, 0, 0, 0, 0, 0.5f, 0, 0, 0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
        SilkFrameCaptureResult frame = filter is null
            ? capturer.CaptureWithHdrColor(session, 32, 32, RenderSettings.PresentationDefault, camera: camera)
            : capturer.CaptureWithHdrColor(session, 32, 32, RenderSettings.PresentationDefault,
                filter, camera: camera);
        return (frame.HdrColor ?? throw new InvalidOperationException("The current HDR plane is absent."))
            .Rgba16Float.ToArray();
    }

    private static void Mutate(UsdStage stage, string change)
    {
        const string path = "/World/Hidden/Geometry";
        UsdPrim mesh = stage.GetPrim(path);
        switch (change)
        {
            case "color":
                mesh.SetColor3fArray("primvars:displayColor", [new UsdVec3f(0, 1, 0)]);
                break;
            case "normals":
                mesh.SetVec3fArray("normals", [new UsdVec3f(0.70710677f, 0, 0.70710677f)]);
                break;
            case "topology":
                mesh.SetInt32Array("faceVertexCounts", [3]);
                mesh.SetInt32Array("faceVertexIndices", [0, 1, 2]);
                break;
            case "ancestor":
                stage.GetPrim("/World").SetMatrix4d("xformOp:transform", new UsdMatrix4d(
                    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1));
                break;
            case "delete":
                stage.RemovePrim(path);
                break;
            case "redefine":
                stage.RemovePrim(path);
                UsdPrim cube = stage.DefinePrim(path, "Cube");
                cube.SetDouble("size", 1);
                cube.SetMatrix4d("xformOp:transform", new UsdMatrix4d(
                    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, -4, 1));
                cube.SetTokenArray("xformOpOrder", ["xformOp:transform"]);
                break;
            case "instances":
                UsdPrim instancer = stage.GetPrim("/Instancer");
                instancer.SetInt32Array("protoIndices", [0]);
                instancer.SetVec3fArray("positions", [new UsdVec3f(0.8f, 0, 0)]);
                break;
            default:
                throw new ArgumentException("Unknown hidden-geometry mutation.", nameof(change));
        }
    }

    private static int ColoredPixels(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(bytes);
        int count = 0;
        for (int index = 0; index < values.Length; index += 4)
        {
            if (values[index] != (Half)0 || values[index + 1] != (Half)0 || values[index + 2] != (Half)0)
            {
                count++;
            }
        }
        return count;
    }

    private sealed record Result(
        byte[] Initial, byte[] Hidden, byte[] Restored, byte[] Fresh, bool SourceUnchanged);

    private const string Scene = """
        #usda 1.0
        def Xform "World" {
            matrix4d xformOp:transform = ((1,0,0,0), (0,1,0,0), (0,0,1,0), (0,0,0,1))
            uniform token[] xformOpOrder = ["xformOp:transform"]
            def Xform "Hidden" {
                uniform token purpose = "proxy"
                def Mesh "Geometry" {
                    uniform token subdivisionScheme = "none"
                    bool doubleSided = true
                    point3f[] points = [(-0.75,-0.75,-4), (0.75,-0.75,-4), (0.75,0.75,-4), (-0.75,0.75,-4)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0,1,2,3]
                    normal3f[] normals = [(0,0,1)] (interpolation = "constant")
                    color3f[] primvars:displayColor = [(1,0,0)] (interpolation = "constant")
                }
            }
        }

        """;

    private const string Instances = """
        def PointInstancer "Instancer" {
            uniform token purpose = "proxy"
            rel prototypes = </World/Hidden/Geometry>
            int[] protoIndices = [0,0]
            point3f[] positions = [(-0.8,0,0), (0.8,0,0)]
        }
        """;
}
