// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class StormSilkParityCaptureDriverTests
{
    private static async Task ExerciseInstancedScaleScene(ISilkGraphicsDevice device, string backend)
    {
        string stagePath = Path.Combine(EvidenceDirectory(), $"scale-{backend}.usda");
        string reportName = $"scale-{backend}-evidence.json";
        File.Delete(Path.Combine(EvidenceDirectory(), reportName));
        File.WriteAllText(stagePath, CreateInstancedScaleFixture(), new UTF8Encoding(false));
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(stagePath);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), source);
        using var capture = new SilkFrameCapturer(device);
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity);
        var timer = Stopwatch.StartNew();
        SilkFrameCaptureResult first = capture.Capture(session, 512, 512, RenderSettings.Default, 1, camera);
        long firstFrameMilliseconds = timer.ElapsedMilliseconds;
        SilkFrameCaptureResult repeated = capture.Capture(session, 512, 512, RenderSettings.Default, 1, camera);
        int visiblePixels = CountVisiblePixels(first.Rgba.Span);

        await Assert.That(first.RenderResult.Statistics.MeshCount).IsEqualTo(10_000);
        await Assert.That(first.RenderResult.DrawCount).IsEqualTo(1);
        await Assert.That(first.RenderResult.Statistics.GeometryBuilds).IsEqualTo(1UL);
        await Assert.That(visiblePixels).IsGreaterThan(10_000);
        await Assert.That(first.Rgba.Span.SequenceEqual(repeated.Rgba.Span)).IsTrue();
        await Assert.That(repeated.RenderResult.Statistics.GeometryBuilds)
            .IsEqualTo(first.RenderResult.Statistics.GeometryBuilds);
        await Assert.That(repeated.RenderResult.Statistics.BufferAllocationBytes)
            .IsEqualTo(first.RenderResult.Statistics.BufferAllocationBytes);
        await Assert.That(repeated.RenderResult.Statistics.TextureUploadBytes)
            .IsEqualTo(first.RenderResult.Statistics.TextureUploadBytes);

        await scheduler.EditAsync(
            static stage => stage.GetPrim("/World/Grid").SetActive(false), UsdStageInvalidationKind.Full);
        SilkFrameCaptureResult hidden = capture.Capture(session, 512, 512, RenderSettings.Default, 1, camera);
        await Assert.That(hidden.RenderResult.DrawCount).IsEqualTo(0);
        await Assert.That(CountVisiblePixels(hidden.Rgba.Span)).IsEqualTo(0);
        await Assert.That(first.Rgba.Span.SequenceEqual(hidden.Rgba.Span)).IsFalse();

        WriteJsonEvidence(reportName, new
        {
            schemaVersion = 2,
            status = "executed",
            id = "instanced-grid-10000",
            license = "MIT; deterministically authored by this repository, with no external asset dependency",
            backend,
            device = device.Capabilities,
            execution = ParityExecutionIdentity.Read(Environment.GetEnvironmentVariable),
            sourceIdentity = CreateSourceIdentity(),
            fixture = ParityEvidenceInputs.CreateTextIdentity(
                EvidenceDirectory(), [Path.GetFileName(stagePath)]).Files[0],
            instanceCount = 10_000,
            first.RenderResult.DrawCount,
            first.RenderResult.Statistics.GeometryBuilds,
            visiblePixels,
            hiddenPixels = CountVisiblePixels(hidden.Rgba.Span),
            firstFrameMilliseconds,
            timingPolicy = "Informational only; no wall-clock claim is made on shared software-rasterizer hosts",
            negativeControl = "Deactivating the scheduler-owned instancer retires its geometry and all visible pixels",
            firstHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first.Rgba.Span)),
            repeatedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(repeated.Rgba.Span)),
            hiddenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(hidden.Rgba.Span)),
        });
    }

    private static string CreateInstancedScaleFixture()
    {
        var text = new StringBuilder(
            """
            #usda 1.0
            (
                defaultPrim = "World"
                metersPerUnit = 1
                upAxis = "Y"
            )
            # Project-authored MIT fixture: a 100 by 100 grid, one shared triangle prototype.
            def Xform "World"
            {
                def PointInstancer "Grid"
                {
                    rel prototypes = </World/Prototypes/Tri>
            """);
        text.Append("\n        int[] protoIndices = [");
        for (int index = 0; index < 10_000; index++)
        {
            text.Append(index == 0 ? "0" : ", 0");
        }
        text.Append("]\n        point3f[] positions = [");
        for (int index = 0; index < 10_000; index++)
        {
            if (index != 0)
            {
                text.Append(", ");
            }
            double x = -0.89 + 0.018 * (index % 100);
            double y = -0.89 + 0.018 * (index / 100);
            text.Append(CultureInfo.InvariantCulture, $"({x:F4}, {y:F4}, 0.08)");
        }
        text.Append("]\n        float3[] scales = [");
        for (int index = 0; index < 10_000; index++)
        {
            text.Append(index == 0 ? "(0.008, 0.008, 1)" : ", (0.008, 0.008, 1)");
        }
        text.AppendLine("]");
        text.Append(
            """
                }
                def Scope "Prototypes"
                {
                    token visibility = "invisible"
                    def Mesh "Tri"
                    {
                        uniform bool doubleSided = 1
                        uniform token subdivisionScheme = "none"
                        point3f[] points = [(-1, -0.65, 0), (0.95, -0.20, 0), (-0.25, 1, 0)]
                        int[] faceVertexCounts = [3]
                        int[] faceVertexIndices = [0, 1, 2]
                        color3f[] primvars:displayColor = [(0.15, 0.45, 0.65)]
                        uniform token primvars:displayColor:interpolation = "constant"
                    }
                }
            }
            """);
        return text.Append('\n').ToString();
    }

    private static int CountVisiblePixels(ReadOnlySpan<byte> rgba)
    {
        int count = 0;
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            if (rgba[offset] != 0 || rgba[offset + 1] != 0 || rgba[offset + 2] != 0)
            {
                count++;
            }
        }
        return count;
    }
}
