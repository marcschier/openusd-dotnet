// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Real shared-stage hdSilk publication, retained linking, and offscreen pixel evidence.
/// These are ABI24/session6 candidates, not a substitute for a matched native runtime.
/// </summary>
[NotInParallel]
public sealed class SilkDirectLightNativeConformanceTests
{
    private const int Size = 64;
    private const int Capacity = 128;
    private const int ExtraTag = 1001;
    private const byte ClearGreen = 32;
    private const uint PageAbi = 24;
    private const uint SessionAbi = 6;
    private const int FrameBufferSize = 15296;
    private const string RequiredVariable = "OPENUSD_DIRECT_LIGHT_EXECUTION_REQUIRED";
    private const string HardwareVariable = "OPENUSD_DIRECT_LIGHT_HARDWARE_REQUIRED";
    private const string PluginVariable = "OPENUSD_TEST_PLUGIN_PATH";
    private const string SwiftShaderVariable = "OPENUSD_REQUIRE_SWIFTSHADER";
    private const string LeftTarget = "/World/Left";
    private const string RightTarget = "/World/Right";
    private const string ExtraPath = "/World/Filtered/Extra";
    private const string Includes = "collection:lightLink:includes";
    private const string IncludeRoot = "collection:lightLink:includeRoot";

    // Independent wire oracle for OPENUSD_SILK_FRAME_LIGHTING_HAS_AUTHORED_DIRECT_LIGHTS.
    private const uint HasAuthoredDirectLights = 0x1;
    private const int FlagsOffset = 540;
    private const int EnvironmentAuthoredOffset = 15020;

    // Neither field is modified. The page has no public byte accessor; inspecting its
    // real managed-owned copy is necessary to assert the raw flags at byte 540.
    // Reading the already-bound frame buffer avoids RequireFrameBuffer changing cache
    // keys after the render whose pixels we are measuring.
    private static readonly FieldInfo PageDataField =
        typeof(OpenUsdSilkPage).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The real OpenUsdSilkPage byte-copy seam changed.");
    private static readonly FieldInfo FrameBufferField =
        typeof(SilkSceneGpuResources).GetField("_frameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The rendered frame-buffer observation seam changed.");

    private static readonly CameraState Camera = new(
        Matrix4x4.Identity,
        new Matrix4x4(
            0.5f, 0, 0, 0,
            0, 0.5f, 0, 0,
            0, 0, -0.2f, 0,
            0, 0, -1.2f, 1));

    // A nonblack RGB clear makes a black, authored-dark receiver distinguishable
    // from a geometry gap without pretending opaque alpha is geometry coverage.
    private static readonly SilkMeshRenderOptions Options = new(
        new SilkColor(0, ClearGreen / 255f, 0, 1), 1)
    {
        OutputTransform = RenderOutputTransform.Identity
    };

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(97)]
    [Arguments(128)]
    public async Task NativeLightCountPartitions(int count)
    {
        RunEvidence result = Exercise(SilkGraphicsBackend.D3D12, Scenario.Count, count);
        CaptureEvidence initial = result.Captures["initial"];
        await AssertRun(result);
        await AssertPublished(initial, count, Enumerable.Range(1, count));
        await Assert.That(initial.Page.FrameCommands).IsEqualTo(1);
        await Assert.That(initial.Page.MeshUpserts.Length).IsEqualTo(2);
        await Assert.That(initial.State.Meshes.Length).IsEqualTo(2);
        await Assert.That(initial.State.FrameLights.Length).IsEqualTo(Capacity);
        await Assert.That(initial.Page.Frame!.Flags & HasAuthoredDirectLights)
            .IsEqualTo(count == 0 ? 0u : HasAuthoredDirectLights);
        await Assert.That(initial.DrawCount).IsGreaterThan(0);
        await AssertGeometry(initial.State.Pixels);
        foreach (SilkFrameLight light in initial.Frame.Lights)
        {
            await Assert.That(light.Type).IsEqualTo(1u);
            await Assert.That(light.Color.X).IsEqualTo(0f);
            await Assert.That(light.Color.Y).IsEqualTo(0f);
            await Assert.That(light.Color.Z).IsGreaterThan(0f);
            await Assert.That(Exposed(light)).IsGreaterThan(0f);
            await Assert.That(light.ShadowEnabled).IsEqualTo(0u);
        }
        await AssertSteady(initial, result.Captures["steady"]);
    }

    [Test]
    [Arguments("inherited-hidden")]
    [Arguments("intensity-zero")]
    [Arguments("all-color-zero")]
    [Arguments("diffuse-and-specular-zero")]
    [Arguments("exposed-float-zero")]
    [Arguments("exposed-rounding-zero")]
    public async Task OnlyMeaningfulNativeLightsConsumeCapacity(string filter)
    {
        RunEvidence result = Exercise(
            SilkGraphicsBackend.D3D12, Scenario.Filter, Capacity, filter);
        CaptureEvidence capture = result.Captures["initial"];
        await AssertRun(result);
        await AssertPublished(capture, Capacity, Enumerable.Range(1, Capacity));
        await Assert.That(capture.Page.FrameCommands).IsEqualTo(1);
        await Assert.That(capture.Frame.Flags & HasAuthoredDirectLights)
            .IsEqualTo(HasAuthoredDirectLights);
        await Assert.That(capture.Frame.Lights.Any(light => Tag(light) == ExtraTag)).IsFalse();
        await Assert.That(capture.Frame.Lights.All(light =>
            light.Color.Z > 0 && Exposed(light) > 0 &&
            (light.Diffuse > 0 || light.Specular > 0))).IsTrue();
        await Assert.That(capture.State.LinkUnsupported).IsEqualTo(0u);
        await Assert.That(capture.State.Meshes.Length).IsEqualTo(2);
        await AssertGeometry(capture.State.Pixels);
        await AssertSteady(capture, result.Captures["steady"]);
    }

    [Test]
    [Arguments("distant", "ordinary")]
    [Arguments("sphere", "ordinary")]
    [Arguments("rect", "ordinary")]
    [Arguments("disk", "ordinary")]
    [Arguments("cylinder", "ordinary")]
    [Arguments("distant", "diffuse-only")]
    [Arguments("sphere", "specular-only")]
    [Arguments("rect", "red-only")]
    [Arguments("disk", "green-only")]
    [Arguments("cylinder", "blue-only")]
    [Arguments("rect", "exposure")]
    [Arguments("distant", "near-zero")]
    [Arguments("distant", "subnormal")]
    public async Task NativeFramePreservesMeaningfulLightShapeAndControls(string shape, string control)
    {
        RunEvidence result = Exercise(
            SilkGraphicsBackend.D3D12, Scenario.Controls, 1, control, shape);
        CaptureEvidence capture = result.Captures["initial"];
        ShapeSpec expectedShape = Shape(shape);
        ControlSpec expectedControl = Control(control);
        await AssertRun(result);
        await AssertPublished(capture, 1, [1]);
        SilkFrameLight light = capture.Frame.Lights.Single();
        await Assert.That(light.Type).IsEqualTo(expectedShape.Type);
        await Assert.That(light.ShapeX).IsEqualTo(expectedShape.ShapeX);
        await Assert.That(light.ShapeY).IsEqualTo(expectedShape.ShapeY);
        await Assert.That(light.Radius).IsEqualTo(expectedShape.Radius);
        await Assert.That(light.Color).IsEqualTo(expectedControl.Color);
        await Assert.That(light.Intensity).IsEqualTo(expectedControl.Intensity);
        await Assert.That(light.Exposure).IsEqualTo(expectedControl.Exposure);
        await Assert.That(light.Diffuse).IsEqualTo(expectedControl.Diffuse);
        await Assert.That(light.Specular).IsEqualTo(expectedControl.Specular);
        await Assert.That(light.ShadowEnabled).IsEqualTo(0u);
        await Assert.That(light.Transform).IsEqualTo(ControlTransform);
        await Assert.That(capture.State.FrameLights[0]).IsEqualTo(light);

        // This is readback of the buffer bound by ApplyAndRender, not a second
        // independently constructed frame or a call to a test-only writer.
        byte[] bytes = capture.State.FrameBytes;
        await AssertVector(bytes, 224, new Vector4(1, 2, 3, expectedShape.Type));
        await AssertVector(bytes, 2272, new Vector4(0, 0, 1, expectedShape.Radius));
        await AssertVector(bytes, 4320, new Vector4(expectedControl.Color, expectedControl.Exposed));
        await AssertVector(bytes, 6368, new Vector4(
            expectedControl.Diffuse, expectedControl.Specular, 0, 0));
        await AssertVector(bytes, 8416, new Vector4(0, 1, 0, expectedShape.ShapeX));
        await AssertVector(bytes, 10464, new Vector4(-1, 0, 0, expectedShape.ShapeY));
        await Assert.That(ReadSingle(bytes, 4332)).IsGreaterThan(0f)
            .Because("Both the -126 normal and -149 subnormal exposure rows must survive selection and float packing.");
        await AssertSteady(capture, result.Captures["steady"]);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task AuthoredDarkAndNoAuthoredLightsHaveDistinctFallbacks(SilkGraphicsBackend backend)
    {
        RunEvidence result = Exercise(backend, Scenario.Dark, 0);
        CaptureEvidence none = result.Captures["initial"];
        CaptureEvidence dark = result.Captures["changed"];
        CaptureEvidence restored = result.Captures["restored"];
        await AssertRun(result);
        await AssertPublished(none, 0, []);
        await AssertPublished(dark, 0, []);
        await AssertPublished(restored, 0, []);
        await Assert.That(none.Frame.Flags & HasAuthoredDirectLights).IsEqualTo(0u);
        await Assert.That(dark.Frame.Flags & HasAuthoredDirectLights).IsEqualTo(HasAuthoredDirectLights);
        await Assert.That(restored.Frame.Flags & HasAuthoredDirectLights).IsEqualTo(0u);
        await Assert.That(ReadSingle(none.State.FrameBytes, EnvironmentAuthoredOffset)).IsEqualTo(0f);
        await Assert.That(ReadSingle(dark.State.FrameBytes, EnvironmentAuthoredOffset)).IsEqualTo(1f);
        await Assert.That(ReadSingle(restored.State.FrameBytes, EnvironmentAuthoredOffset)).IsEqualTo(0f);
        await Assert.That(none.DrawCount).IsGreaterThan(0);
        await Assert.That(dark.DrawCount).IsEqualTo(none.DrawCount);
        await AssertGeometry(none.State.Pixels);
        await AssertGeometry(dark.State.Pixels);
        await AssertCoverageEqual(none.State.Pixels, dark.State.Pixels);
        foreach ((int x, int y) in InteriorSamples())
        {
            await Assert.That(Channel(none.State.Pixels, x, y, 0)).IsGreaterThan((byte)20);
            await Assert.That(Channel(none.State.Pixels, x, y, 1)).IsGreaterThan((byte)20);
            await Assert.That(Channel(none.State.Pixels, x, y, 2)).IsGreaterThan((byte)20);
            await Assert.That(Channel(dark.State.Pixels, x, y, 0)).IsEqualTo((byte)0);
            await Assert.That(Channel(dark.State.Pixels, x, y, 1)).IsEqualTo((byte)0);
            await Assert.That(Channel(dark.State.Pixels, x, y, 2)).IsEqualTo((byte)0);
        }
        await Assert.That(restored.State.Pixels.SequenceEqual(none.State.Pixels)).IsTrue();
        await Assert.That(restored.State.FrameBytes.SequenceEqual(none.State.FrameBytes)).IsTrue();
        await AssertGeometryUnchanged(none, dark);
        await AssertSteady(restored, result.Captures["steady"]);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task NativeOverflowRefusesPageAndRecoversOnSameSession(SilkGraphicsBackend backend)
    {
        RunEvidence result = Exercise(backend, Scenario.Overflow, Capacity);
        CaptureEvidence before = result.Captures["before"];
        CaptureEvidence recovered = result.Captures["restored"];
        FailureEvidence failure = result.Failure
            ?? throw new InvalidOperationException("The overflow exercise did not return failure evidence.");
        await AssertRun(result);
        await AssertPublished(result.Captures["initial"], Capacity, Enumerable.Range(1, Capacity));
        await AssertPublished(before, Capacity, Enumerable.Range(1, Capacity));
        await AssertGeometryUnchanged(result.Captures["initial"], before);
        await AssertHighMembership(before, result.HighTag, Capacity, included: true, instances: false);
        await Assert.That(IndexOf(before.Frame, result.HighTag)).IsEqualTo(127);
        await AssertOverflow(failure, synchronizedBefore: true);
        await Assert.That(failure.ApplyCountBefore).IsEqualTo(2);
        await AssertStateEqual(before.State, failure.UnappliedState!);
        await AssertStateEqual(before.State, failure.RerenderedState!);
        await Assert.That(failure.RerenderDrawCount).IsEqualTo(before.DrawCount);
        await AssertPublished(recovered, Capacity, Enumerable.Range(1, Capacity));
        await AssertHighMembership(recovered, result.HighTag, Capacity, included: true, instances: false);
        await Assert.That(recovered.Frame.Lights.Any(light => Tag(light) == ExtraTag)).IsFalse();
        await Assert.That(recovered.State.Pixels.SequenceEqual(before.State.Pixels)).IsTrue();
        await Assert.That(recovered.State.FrameLights.SequenceEqual(before.State.FrameLights)).IsTrue();
        await AssertGeometryUnchanged(before, recovered, republishedOnRecovery: true);
        await AssertSteady(recovered, result.Captures["steady"]);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task NativeFirstOverflowDoesNotMarkSessionSynchronizedAndCanRetry(
        SilkGraphicsBackend backend)
    {
        RunEvidence result = Exercise(backend, Scenario.FirstOverflow, Capacity);
        FailureEvidence failure = result.Failure
            ?? throw new InvalidOperationException("The first-sync overflow evidence is absent.");
        await AssertRun(result);
        await AssertOverflow(failure, synchronizedBefore: false);
        await Assert.That(failure.ApplyCountBefore).IsEqualTo(0);
        await Assert.That(failure.UnappliedState is null).IsTrue();
        await Assert.That(failure.RerenderedState is null).IsTrue();
        CaptureEvidence recovered = result.Captures["restored"];
        await AssertPublished(recovered, Capacity, Enumerable.Range(1, Capacity));
        await Assert.That(recovered.Page.MeshUpserts.Length).IsEqualTo(2)
            .Because("The successful first publication must still deliver the geometry.");
        await Assert.That(recovered.DrawCount).IsGreaterThan(0);
        await AssertGeometry(recovered.State.Pixels);
        await AssertSteady(recovered, result.Captures["steady"]);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 97)]
    [Arguments(SilkGraphicsBackend.D3D12, 128)]
    [Arguments(SilkGraphicsBackend.Vulkan, 97)]
    [Arguments(SilkGraphicsBackend.Vulkan, 128)]
    public async Task NativeHighLightOnlyChangesLinkedMeshPixels(SilkGraphicsBackend backend, int count)
    {
        RunEvidence result = Exercise(backend, Scenario.HighMesh, count);
        await AssertHighRemovalAndRestoration(result, count, instances: false);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 97)]
    [Arguments(SilkGraphicsBackend.D3D12, 128)]
    [Arguments(SilkGraphicsBackend.Vulkan, 97)]
    [Arguments(SilkGraphicsBackend.Vulkan, 128)]
    public async Task NativeHighLightOnlyChangesLinkedInstancePixels(SilkGraphicsBackend backend, int count)
    {
        RunEvidence result = Exercise(backend, Scenario.HighInstance, count);
        await AssertHighRemovalAndRestoration(result, count, instances: true);
    }

    [Test]
    [Arguments(97, false)]
    [Arguments(128, false)]
    [Arguments(97, true)]
    [Arguments(128, true)]
    public async Task NativeHighLightLinkingSurvivesRemovalAndRestorationOnHardware(int count, bool instances)
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable(HardwareVariable)))
        {
            Skip.Test($"Set {HardwareVariable}=1 to require the actual D3D12 hardware profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RunEvidence result = Exercise(
            SilkGraphicsBackend.D3D12, instances ? Scenario.HighInstance : Scenario.HighMesh,
            count, hardware: true);
        await Assert.That(result.Device.IsSoftware).IsFalse();
        await AssertHighRemovalAndRestoration(result, count, instances);
    }

    [Test]
    public async Task RequiredHardwareProfileRefusesSoftwareFallback()
    {
        var profile = new ExecutionProfile(SilkGraphicsBackend.D3D12, true, false, D3D12Hardware: true);
        var software = new DeviceEvidence(
            SilkGraphicsBackend.D3D12, "Microsoft Basic Render Driver", "Direct3D 12", true, true);
        await Assert.That(() => CheckDevice(profile, software)).Throws<InvalidOperationException>()
            .WithMessageContaining("hardware");
    }

    [Test]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public async Task NativeOverflowRecoveryRetiresDepartedLightLinksAndShadows(bool withEnvironment, int refusals)
    {
        SilkGraphicsBackend backend = OperatingSystem.IsWindows()
            ? SilkGraphicsBackend.D3D12
            : SilkGraphicsBackend.Vulkan;
        var profile = new ExecutionProfile(
            backend, IsEnabled(Environment.GetEnvironmentVariable(RequiredVariable)), false);
        string? plugins = Environment.GetEnvironmentVariable(PluginVariable);
        CheckPrerequisites(profile, plugins, Directory.Exists(plugins), SupportedOs(backend));
        string root = Directory.CreateTempSubdirectory("silk-recovery-retirement-").FullName;
        string path = Path.Combine(root, "scene.usda");
        string original = BuildScene(Scenario.Overflow, Capacity, "", "distant");
        if (withEnvironment)
        {
            original += """

                def DomeLight "Environment" {
                    asset inputs:texture:file = @environment.ppm@
                    token inputs:texture:format = "latlong"
                    float inputs:intensity = 1
                }
                """;
        }
        try
        {
            await File.WriteAllTextAsync(path, original);
            if (withEnvironment)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "environment.ppm"), "P3\n1 1\n255\n80 120 180\n");
            }
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins!, source);
            var scene = new SilkSceneState();
            int departedTag;
            using (OpenUsdSilkPage initial = session.Sync(Size, Size, camera: Camera))
            {
                PageEvidence page = ReadPage(initial);
                FrameEvidence frame = page.Frame ??
                    throw new InvalidDataException("The initial native frame is absent.");
                await Assert.That(frame.LightCount).IsEqualTo((uint)Capacity);
                departedTag = Tag(frame.Lights[^1]);
                _ = scene.Apply(initial);
            }
            await scheduler.InvokeAsync(stage =>
            {
                UsdPrim light = stage.GetPrim(LightPath(departedTag));
                light.SetBool(IncludeRoot, false);
                light.SetRelationshipTargets(Includes, [LeftTarget]);
                light.SetBool("inputs:shadow:enable", true);
            });
            using (OpenUsdSilkPage linked = session.Sync(Size, Size, camera: Camera))
            {
                _ = scene.Apply(linked);
            }
            await Assert.That(scene.LightLinks.HasLinks).IsTrue();
            await Assert.That(scene.Shadows.HasShadows).IsTrue();
            await Assert.That(scene.Frame.LightCount).IsEqualTo((uint)Capacity);
            await Assert.That(scene.Environments.Count).IsEqualTo(withEnvironment ? 1 : 0);
            ulong retainedRevision = scene.Revision;

            await scheduler.InvokeAsync(stage =>
                stage.GetPrim(ExtraPath).SetColor3f("inputs:color", new UsdVec3f(0, 0, 0.001f)));
            for (int attempt = 0; attempt < refusals; attempt++)
            {
                OpenUsdSilkException? refusal = null;
                try
                {
                    using OpenUsdSilkPage unexpected = session.Sync(Size, Size, camera: Camera);
                }
                catch (OpenUsdSilkException exception)
                {
                    refusal = exception;
                }
                await Assert.That(refusal).IsNotNull();
                await Assert.That(scene.Revision).IsEqualTo(retainedRevision);
                await Assert.That(scene.LightLinks.HasLinks).IsTrue();
                await Assert.That(scene.Shadows.HasShadows).IsTrue();
                await Assert.That(scene.Environments.Count).IsEqualTo(withEnvironment ? 1 : 0);
            }

            await scheduler.InvokeAsync(stage =>
            {
                stage.GetPrim(LightPath(departedTag)).SetColor3f("inputs:color", new UsdVec3f(0, 0, 0));
                if (withEnvironment)
                {
                    stage.RemovePrim("/Environment");
                }
            });
            bool linkRetirement = false;
            bool shadowRetirement = false;
            bool environmentRetirement = false;
            using (OpenUsdSilkPage recovered = session.Sync(Size, Size, camera: Camera))
            {
                PageEvidence page = ReadPage(recovered);
                FrameEvidence frame = page.Frame ??
                    throw new InvalidDataException("The recovered native frame is absent.");
                await Assert.That(frame.LightCount).IsEqualTo((uint)Capacity);
                await Assert.That(frame.Lights.Select(Tag).Contains(departedTag)).IsFalse();
                await Assert.That(frame.Lights.Select(Tag).Contains(ExtraTag)).IsTrue();
                linkRetirement = page.LinkCommands == 1 && page.Links?.Rows.Length == 0;
                using (SilkCommandEnumerator commands = recovered.GetEnumerator())
                {
                    while (commands.MoveNext())
                    {
                        shadowRetirement |= commands.Current.Type == SilkCommandType.Shadow;
                        environmentRetirement |= commands.Current.Type == SilkCommandType.EnvironmentRemove;
                    }
                }
                Console.WriteLine(
                    $"Recovery commands: links={linkRetirement}, shadows={shadowRetirement}, " +
                    $"environment={environmentRetirement}, refusals={refusals}.");
                await Assert.That(environmentRetirement).IsEqualTo(withEnvironment);
                _ = scene.Apply(recovered);
            }
            Console.WriteLine(
                $"Recovery retirement: linkCommand={linkRetirement}, shadowCommand={shadowRetirement}, " +
                $"retainedLinks={scene.LightLinks.HasLinks}, retainedShadows={scene.Shadows.HasShadows}.");
            await Assert.That(linkRetirement).IsTrue()
                .Because("A same-count recovery must explicitly retire the departed light's restricted links.");
            await Assert.That(shadowRetirement).IsTrue();
            await Assert.That(scene.LightLinks.HasLinks).IsFalse();
            await Assert.That(scene.Shadows.HasShadows).IsFalse();
            await Assert.That(scene.Environments.Count).IsEqualTo(0);
            using (OpenUsdSilkPage steady = session.Sync(Size, Size, camera: Camera))
            {
                _ = scene.Apply(steady);
            }
            await Assert.That(scene.LightLinks.HasLinks).IsFalse();
            await Assert.That(scene.Shadows.HasShadows).IsFalse();
            await Assert.That(scene.Environments.Count).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "remove-link")]
    [Arguments(SilkGraphicsBackend.Vulkan, "remove-link")]
    [Arguments(SilkGraphicsBackend.D3D12, "remove-lower-light")]
    [Arguments(SilkGraphicsBackend.Vulkan, "remove-lower-light")]
    public async Task NativeLinkRemovalAndLightReorderRefreshRetainedMasks(
        SilkGraphicsBackend backend, string operation)
    {
        bool reindex = operation == "remove-lower-light";
        RunEvidence result = Exercise(
            backend, reindex ? Scenario.Reindex : Scenario.LinkRemoval, Capacity);
        CaptureEvidence before = result.Captures["before"];
        CaptureEvidence changed = result.Captures["changed"];
        CaptureEvidence restored = result.Captures["restored"];
        await AssertRun(result);
        await AssertPublished(before, Capacity, Enumerable.Range(1, Capacity));
        await AssertPublished(restored, Capacity, Enumerable.Range(1, Capacity));
        await AssertGeometryUnchanged(result.Captures["initial"], before);
        await Assert.That(IndexOf(before.Frame, result.HighTag)).IsEqualTo(127);
        await AssertHighMembership(before, result.HighTag, Capacity, true, false, result.SacrificialTag);
        await AssertHighMembership(
            changed, result.HighTag, reindex ? 127 : 128, reindex, false);
        await AssertHighMembership(restored, result.HighTag, Capacity, true, false, result.SacrificialTag);
        await Assert.That(changed.Page.LinkCommands).IsEqualTo(1);
        await Assert.That(changed.State.Revisions.Links).IsGreaterThan(before.State.Revisions.Links);
        await Assert.That(restored.State.Revisions.Links).IsGreaterThan(changed.State.Revisions.Links);
        await AssertGeometryUnchanged(before, changed);
        await AssertGeometryUnchanged(before, restored);
        await AssertCoverageEqual(before.State.Pixels, changed.State.Pixels);
        if (reindex)
        {
            await Assert.That(changed.Page.FrameCommands).IsEqualTo(1);
            await Assert.That(restored.Page.FrameCommands).IsEqualTo(1);
            await Assert.That(IndexOf(before.Frame, result.SacrificialTag)).IsEqualTo(0);
            await Assert.That(IndexOf(changed.Frame, result.SacrificialTag)).IsEqualTo(-1);
            int remappedHighIndex = IndexOf(changed.Frame, result.HighTag);
            await Assert.That(remappedHighIndex).IsGreaterThanOrEqualTo(0);
            await Assert.That(remappedHighIndex).IsLessThan(127)
                .Because("Removing a lower slot from 128 entries forces the actual slot-127 identity to remap.");
            await Assert.That(changed.State.Revisions.Frame).IsGreaterThan(before.State.Revisions.Frame);
            await Assert.That(changed.State.Pixels.SequenceEqual(before.State.Pixels)).IsTrue()
                .Because("The removed blue light was meaningful but excluded from both receivers.");
            await AssertPublished(changed, 127,
                Enumerable.Range(1, Capacity).Where(tag => tag != result.SacrificialTag));
        }
        else
        {
            await AssertPublished(changed, Capacity, Enumerable.Range(1, Capacity));
            await Assert.That(changed.State.Revisions.Frame).IsEqualTo(before.State.Revisions.Frame);
            await Assert.That(changed.Frame.Lights.SequenceEqual(before.Frame.Lights)).IsTrue();
            await AssertOnlyLeftRedChanges(before.State.Pixels, changed.State.Pixels);
        }
        await Assert.That(restored.State.Pixels.SequenceEqual(before.State.Pixels)).IsTrue();
        await AssertSteady(restored, result.Captures["steady"]);
    }

    [Test]
    [Arguments("missing-plugin")]
    [Arguments("missing-plugin-directory")]
    [Arguments("unsupported-os")]
    [Arguments("unsupported-backend")]
    [Arguments("cross-backend")]
    [Arguments("warp-not-software")]
    [Arguments("warp-wrong-identity")]
    [Arguments("selected-swiftshader")]
    [Arguments("missing-device-capability")]
    public async Task RequiredProfileRejectsMissingPrerequisites(string reason)
    {
        // Explicit local inputs to exactly the gates used by Exercise; no environment
        // mutation, fake native exports, or fabricated ABI execution.
        SilkGraphicsBackend backend = reason.StartsWith("warp", StringComparison.Ordinal)
            ? SilkGraphicsBackend.D3D12
            : SilkGraphicsBackend.Vulkan;
        var profile = new ExecutionProfile(backend, Required: true, RequireSwiftShader: true);
        string? plugins = reason == "missing-plugin" ? null : "matched-runtime/plugins";
        bool exists = reason != "missing-plugin-directory";
        bool supportedOs = reason != "unsupported-os";
        if (reason == "unsupported-backend")
        {
            profile = profile with { Backend = SilkGraphicsBackend.Metal };
        }
        var device = new DeviceEvidence(
            reason == "cross-backend" ? SilkGraphicsBackend.D3D12 : backend,
            backend == SilkGraphicsBackend.D3D12 ? "Microsoft Basic Render Driver" : "SwiftShader Device",
            "explicit gate input",
            IsSoftware: reason != "warp-not-software",
            SupportsCompute: reason != "missing-device-capability");
        if (reason is "warp-wrong-identity" or "selected-swiftshader")
        {
            device = device with { Name = "a different selected device" };
        }

        Exception? failure = null;
        try
        {
            CheckPrerequisites(profile, plugins, exists, supportedOs);
            CheckDevice(profile, device);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await AssertActionableRequiredFailure(failure, profile.Backend);
        await Assert.That(failure!.Message.Contains(
            reason switch
            {
                "missing-plugin" => "plugin",
                "missing-plugin-directory" => "directory",
                "unsupported-os" => "operating system",
                "unsupported-backend" => "backend",
                "cross-backend" => "selected",
                "warp-not-software" or "warp-wrong-identity" => "WARP",
                "selected-swiftshader" => "SwiftShader",
                _ => "compute"
            }, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task RequiredProfileTurnsExistingHelperSkipIntoActionableFailure()
    {
        var required = new ExecutionProfile(SilkGraphicsBackend.Vulkan, true, true);
        Exception? original = null;
        Exception? failure = null;
        try
        {
            Unavailable(required with { Required = false }, "explicit optional helper-skip input");
        }
        catch (Exception helperSkip)
        {
            original = helperSkip;
            try
            {
                // The same catch/translation used for CreateDevice and runtime setup.
                Unavailable(required, "the selected backend helper declined execution", helperSkip);
            }
            catch (Exception requiredFailure)
            {
                failure = requiredFailure;
            }
        }
        await Assert.That(original?.GetType().Name).IsEqualTo("SkipTestException");
        await AssertActionableRequiredFailure(failure, SilkGraphicsBackend.Vulkan);
        await Assert.That(ReferenceEquals(failure!.InnerException, original)).IsTrue();
        await Assert.That(failure.Message.Contains("helper", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task OptionalProfileReportsAnExplicitPrerequisiteSkipReason()
    {
        Exception? failure = null;
        try
        {
            CheckPrerequisites(
                new ExecutionProfile(SilkGraphicsBackend.Vulkan, false, false),
                plugins: null, pluginDirectoryExists: false, supportedOs: true);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await Assert.That(failure?.GetType().Name).IsEqualTo("SkipTestException");
        await Assert.That(failure!.Message.Contains(PluginVariable, StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains("optional", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    [Arguments("1", true)]
    [Arguments("true", true)]
    [Arguments("TRUE", true)]
    [Arguments("0", false)]
    [Arguments("false", false)]
    [Arguments("", false)]
    [Arguments(null, false)]
    public async Task RequiredProfileParsesExplicitSwitchValues(string? value, bool required)
    {
        bool parsed = IsEnabled(value);
        Exception? failure = null;
        try
        {
            CheckPrerequisites(
                new ExecutionProfile(SilkGraphicsBackend.Vulkan, parsed, false),
                plugins: null, pluginDirectoryExists: false, supportedOs: true);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await Assert.That(parsed).IsEqualTo(required);
        await Assert.That(failure?.GetType().Name)
            .IsEqualTo(required ? nameof(InvalidOperationException) : "SkipTestException");
        await Assert.That(failure!.Message.Contains(RequiredVariable, StringComparison.Ordinal)).IsTrue();
    }

    private static RunEvidence Exercise(
        SilkGraphicsBackend backend, Scenario scenario, int count,
        string detail = "", string shape = "distant", bool hardware = false)
    {
        var profile = new ExecutionProfile(
            backend,
            hardware || IsEnabled(Environment.GetEnvironmentVariable(RequiredVariable)),
            IsEnabled(Environment.GetEnvironmentVariable(SwiftShaderVariable)),
            D3D12Hardware: hardware);
        string? plugins = Environment.GetEnvironmentVariable(PluginVariable);
        CheckPrerequisites(profile, plugins, Directory.Exists(plugins), SupportedOs(backend));
        string scene = BuildScene(scenario, count, detail, shape);
        byte[] original = Encoding.UTF8.GetBytes(scene);
        string parent = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") is { Length: > 0 } workRoot
            ? Path.GetFullPath(workRoot)
            : Path.GetTempPath();
        string root = Directory.CreateDirectory(
            Path.Combine(parent, $"silk-direct-light-{Guid.NewGuid():N}")).FullName;
        string path = Path.Combine(root, "scene.usda");
        bool instances = scenario == Scenario.HighInstance;
        UsdStageScheduler? scheduler = null;
        RunEvidence? result = null;
        bool initialized = false;
        bool sourceUnchanged = false;
        try
        {
            File.WriteAllBytes(path, original);
            scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source =
                scheduler.AcquireRenderSourceAsync().AsTask().GetAwaiter().GetResult();
            using ISilkGraphicsDevice device = CreateDevice(profile);
            var actualDevice = new DeviceEvidence(
                device.Backend, device.Capabilities.DeviceName, device.Capabilities.ApiVersion,
                device.Capabilities.IsSoftware, device.Capabilities.SupportsCompute);
            CheckDevice(profile, actualDevice);
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins!, source);
            using var renderer = new SilkMeshRenderer(device);
            using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
                Size, Size, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(Size, Size));
            initialized = true;

            // All evidence below is owned data. No stage-bound prim or native/GPU owner
            // escapes to an async assertion, and every edit uses this scheduler.
            bool authoredInstances = !instances || scheduler.InvokeAsync(stage =>
                stage.GetPrim(LeftTarget).IsInstanceable() &&
                stage.GetPrim(RightTarget).IsInstanceable() &&
                stage.GetPrim(LeftTarget + "/Geometry").Exists() &&
                stage.GetPrim(RightTarget + "/Geometry").Exists())
                .AsTask().GetAwaiter().GetResult();
            bool hasExtra = scenario is Scenario.Filter or Scenario.Overflow or Scenario.FirstOverflow;
            bool extraAuthored = !hasExtra || scheduler.InvokeAsync(stage =>
                stage.GetPrim(ExtraPath).Exists()).AsTask().GetAwaiter().GetResult();
            var captures = new Dictionary<string, CaptureEvidence>(StringComparer.Ordinal);
            var resourceIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            FrameEvidence? currentFrame = null;
            LinkEvidence currentLinks = new(0, 0, 0, []);
            int applies = 0;
            int highTag = 0;
            int sacrificialTag = 0;
            FailureEvidence? failure = null;
            bool initiallySynchronized = session.HasSynchronized;

            CaptureEvidence capture(string label)
            {
                // Exactly one visible Sync. Enumerate and render this very same page.
                using OpenUsdSilkPage page = session.Sync(Size, Size, camera: Camera);
                PageEvidence publication = ReadPage(page);
                currentFrame = publication.Frame ?? currentFrame
                    ?? throw new InvalidDataException("The first native page omitted its frame.");
                currentLinks = publication.Links ?? currentLinks;
                SilkMeshRenderResult rendered = renderer.ApplyAndRender(page, color, depth, Options);
                applies++;
                StateEvidence state = ReadState(renderer, color, resourceIds);
                var capture = new CaptureEvidence(
                    publication, currentFrame, currentLinks, state,
                    session.HasSynchronized, rendered.DrawCount);
                captures.Add(label, capture);
                return capture;
            }

            FailureEvidence refuseOverflow(bool rerender)
            {
                int applyCountBefore = applies;
                bool synchronizedBefore = session.HasSynchronized;
                OpenUsdSilkException? error = null;
                OpenUsdSilkPage? unexpectedPage = null;
                try
                {
                    unexpectedPage = session.Sync(Size, Size, camera: Camera);
                }
                catch (OpenUsdSilkException exception)
                {
                    error = exception;
                }
                bool delivered = unexpectedPage is not null;
                unexpectedPage?.Dispose(); // Never apply even an incorrectly delivered overflow page.
                StateEvidence? unapplied = rerender ? ReadState(renderer, color, resourceIds) : null;
                int draws = 0;
                StateEvidence? rerendered = null;
                if (rerender)
                {
                    // No Sync or Apply: prove the retained scene still renders, not just
                    // that an untouched color texture kept stale bytes.
                    draws = renderer.Render(color, depth, Options).DrawCount;
                    rerendered = ReadState(renderer, color, resourceIds);
                }
                return new FailureEvidence(
                    error is not null, error is null ? 0 : (int)error.Status, error?.Message ?? "",
                    delivered, synchronizedBefore, session.HasSynchronized,
                    applyCountBefore, applies, unapplied, rerendered, draws);
            }

            if (scenario == Scenario.FirstOverflow)
            {
                failure = refuseOverflow(rerender: false);
                scheduler.InvokeAsync(stage =>
                    stage.GetPrim(ExtraPath).SetColor3f("inputs:color", new UsdVec3f(0, 0, 0)))
                    .AsTask().GetAwaiter().GetResult();
                _ = capture("restored");
                _ = capture("steady");
            }
            else
            {
                CaptureEvidence initial = capture("initial");
                if (scenario is Scenario.Count or Scenario.Filter or Scenario.Controls)
                {
                    _ = capture("steady");
                }
                else if (scenario == Scenario.Dark)
                {
                    scheduler.InvokeAsync(stage =>
                        RestoreDistant(stage, ExtraPath, ExtraTag, Vector3.Zero, restricted: false))
                        .AsTask().GetAwaiter().GetResult();
                    _ = capture("changed");
                    scheduler.InvokeAsync(stage => stage.RemovePrim(ExtraPath))
                        .AsTask().GetAwaiter().GetResult();
                    _ = capture("restored");
                    _ = capture("steady");
                }
                else
                {
                    if (initial.Frame.LightCount != count)
                    {
                        throw new InvalidDataException(
                            $"Native selection returned {initial.Frame.LightCount}, " +
                            $"expected {count} meaningful lights.");
                    }
                    // Translation is harmless for a distant light and identifies its
                    // authored prim. Do not assume lexical/name/native traversal order.
                    highTag = Tag(initial.Frame.Lights[count - 1]);
                    sacrificialTag = scenario == Scenario.Reindex ? Tag(initial.Frame.Lights[0]) : 0;
                    Dictionary<int, Vector3> colors = CalibratedColors(
                        initial.Frame, highTag, sacrificialTag);
                    scheduler.InvokeAsync(stage =>
                    {
                        foreach ((int tag, Vector3 value) in colors)
                        {
                            stage.GetPrim(LightPath(tag)).SetColor3f(
                                "inputs:color", new UsdVec3f(value.X, value.Y, value.Z));
                        }
                        UsdPrim high = stage.GetPrim(LightPath(highTag));
                        high.SetBool(IncludeRoot, false);
                        high.SetRelationshipTargets(Includes, [LeftTarget]);
                        if (sacrificialTag != 0)
                        {
                            UsdPrim lower = stage.GetPrim(LightPath(sacrificialTag));
                            lower.SetBool(IncludeRoot, false);
                            lower.ClearRelationshipTargets(Includes);
                        }
                    }).AsTask().GetAwaiter().GetResult();
                    CaptureEvidence before = capture("before");
                    if (IndexOf(before.Frame, highTag) != count - 1)
                    {
                        throw new InvalidDataException(
                            $"The selected authored light {LightPath(highTag)} did not remain in required " +
                            $"native slot {count - 1} before the pixel experiment. " +
                            $"Observed: {LightOrder(before.Frame)}.");
                    }
                    if (scenario == Scenario.Overflow)
                    {
                        scheduler.InvokeAsync(stage =>
                            stage.GetPrim(ExtraPath).SetColor3f("inputs:color", new UsdVec3f(0.2f, 0, 0)))
                            .AsTask().GetAwaiter().GetResult();
                        failure = refuseOverflow(rerender: true);
                        scheduler.InvokeAsync(stage =>
                            stage.GetPrim(ExtraPath).SetColor3f("inputs:color", new UsdVec3f(0, 0, 0)))
                            .AsTask().GetAwaiter().GetResult();
                    }
                    else if (scenario == Scenario.LinkRemoval)
                    {
                        scheduler.InvokeAsync(stage =>
                            stage.GetPrim(LightPath(highTag)).ClearRelationshipTargets(Includes))
                            .AsTask().GetAwaiter().GetResult();
                        _ = capture("changed");
                        scheduler.InvokeAsync(stage =>
                            stage.GetPrim(LightPath(highTag)).SetRelationshipTargets(Includes, [LeftTarget]))
                            .AsTask().GetAwaiter().GetResult();
                    }
                    else
                    {
                        int removedTag = sacrificialTag == 0 ? highTag : sacrificialTag;
                        scheduler.InvokeAsync(stage => stage.RemovePrim(LightPath(removedTag)))
                            .AsTask().GetAwaiter().GetResult();
                        _ = capture("changed");
                        scheduler.InvokeAsync(stage =>
                            RestoreDistant(stage, LightPath(removedTag), removedTag, colors[removedTag],
                                restricted: true, linkedToLeft: removedTag == highTag))
                            .AsTask().GetAwaiter().GetResult();
                    }
                    _ = capture("restored");
                    _ = capture("steady");
                }
            }
            result = new RunEvidence(
                profile, actualDevice, captures, failure, initiallySynchronized,
                session.HasSynchronized, authoredInstances, extraAuthored,
                highTag, sacrificialTag, SourceUnchanged: false);
        }
        catch (Exception exception) when (profile.Required || !initialized)
        {
            // This includes the existing CreateDevice helper's SkipTestException.
            // Required execution never converts missing plugins/backend/ABI into a skip.
            Unavailable(profile,
                $"native/offscreen execution could not complete: {exception.GetType().Name}: {exception.Message}",
                exception);
            throw;
        }
        finally
        {
            try
            {
                // Try-block using declarations have already disposed depth/color,
                // renderer, session, device, and source, in that order.
                if (scheduler is not null)
                {
                    scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                sourceUnchanged = File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(original);
            }
            finally
            {
                // Only the uniquely owned child, never OPENUSD_TEST_WORK_ROOT.
                Directory.Delete(root, recursive: true);
            }
        }
        return (result ?? throw new InvalidOperationException("Native exercise returned no evidence.")) with
        {
            SourceUnchanged = sourceUnchanged
        };
    }

    private static PageEvidence ReadPage(OpenUsdSilkPage page)
    {
        byte[] raw = ((byte[]?)PageDataField.GetValue(page)
            ?? throw new InvalidDataException("A live native page did not own its byte copy.")).ToArray();
        FrameEvidence? frame = null;
        LinkEvidence? links = null;
        var upserts = new List<MeshIdentity>();
        var removes = new List<(string Path, int InstanceIndex)>();
        int frames = 0;
        int linkCommands = 0;
        int offset = 0;
        int commandsSeen = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            SilkCommand command = commands.Current;
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset + 4)));
            ReadOnlySpan<byte> wire = raw.AsSpan(offset, length);
            if (BinaryPrimitives.ReadUInt32LittleEndian(wire) != (uint)command.Type)
            {
                throw new InvalidDataException("Typed and raw native page command order disagree.");
            }
            if (command.Type == SilkCommandType.Frame)
            {
                SilkFrameCommand native = command.AsFrame();
                bool hasLighting = length is 23096 or 23368;
                var lights = new SilkFrameLight[checked((int)native.LightCount)];
                for (int light = 0; light < lights.Length; light++)
                {
                    lights[light] = SilkFrameLight.CopyFrom(native, light);
                }
                frame = new FrameEvidence(
                    native.LightCount,
                    hasLighting ? BinaryPrimitives.ReadUInt32LittleEndian(wire[536..]) : 0u,
                    hasLighting ? BinaryPrimitives.ReadUInt32LittleEndian(wire[FlagsOffset..]) : 0u,
                    hasLighting ? BinaryPrimitives.ReadUInt32LittleEndian(wire[544..]) : 0u,
                    hasLighting ? BinaryPrimitives.ReadUInt32LittleEndian(wire[548..]) : 0u,
                    native.DomeCount, length, lights);
                frames++;
            }
            else if (command.Type == SilkCommandType.LightLink)
            {
                SilkLightLinkCommand native = command.AsLightLink();
                var rows = new List<LinkRow>();
                SilkLightLinkCommand.Enumerator entries = native.GetEnumerator();
                while (entries.MoveNext())
                {
                    SilkLightLinkEntry entry = entries.Current;
                    rows.Add(new LinkRow(entry.Path, entry.InstanceIndex,
                        new Masks(entry.LightMask, entry.ShadowMask, entry.DomeMask)));
                }
                links = new LinkEvidence(
                    native.LightCount, native.DomeCount, (uint)native.UnsupportedFeatures, rows.ToArray());
                linkCommands++;
            }
            else if (command.Type == SilkCommandType.MeshUpsert)
            {
                SilkMeshUpsertCommand mesh = command.AsMeshUpsert();
                var transform = new double[16];
                for (int element = 0; element < transform.Length; element++)
                {
                    transform[element] = mesh.GetTransformElement(element);
                }
                upserts.Add(new MeshIdentity(
                    mesh.Path, mesh.InstanceIndex, mesh.InstancerPath, mesh.InstancerContext.ToArray(),
                    transform, mesh.PointCount, mesh.IndexCount));
            }
            else if (command.Type == SilkCommandType.MeshRemove)
            {
                SilkMeshRemoveCommand mesh = command.AsMeshRemove();
                removes.Add((mesh.Path, mesh.InstanceIndex));
            }
            offset += length;
            commandsSeen++;
        }
        if (offset != raw.Length || commandsSeen != page.CommandCount)
        {
            throw new InvalidDataException("The native command byte/count envelope was not consumed exactly.");
        }
        return new PageEvidence(page.AbiVersion, page.Revision, page.CommandCount,
            frames, linkCommands, frame, links, upserts.ToArray(), removes.ToArray());
    }

    private static StateEvidence ReadState(
        SilkMeshRenderer renderer, ISilkGraphicsTexture color, Dictionary<object, int> resourceIds)
    {
        int resourceId(object resource)
        {
            if (!resourceIds.TryGetValue(resource, out int id))
            {
                id = resourceIds.Count + 1;
                resourceIds.Add(resource, id);
            }
            return id;
        }
        SilkSceneState scene = renderer.Scene;
        var buffer = (ISilkGraphicsBuffer?)FrameBufferField.GetValue(renderer.GpuResources)
            ?? throw new InvalidOperationException("ApplyAndRender did not bind a real frame buffer.");
        var frameBytes = new byte[checked((int)buffer.Size)];
        buffer.ReadbackForTesting(frameBytes);
        var pixels = new byte[Size * Size * 4];
        color.ReadbackForTesting(pixels);
        var meshes = new List<RetainedMesh>();
        foreach (SilkMeshData mesh in scene.Meshes.Values.OrderBy(
            static mesh => mesh.Transform.Span[12]))
        {
            SilkLightLinkMasks resolved = scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex);
            bool indexed = scene.MeshesByPath.TryGetValue((mesh.Path, mesh.InstanceIndex), out SilkMeshData? lookup)
                && ReferenceEquals(mesh, lookup);
            bool gpuPresent = renderer.GpuResources.Meshes.TryGetValue(mesh.Id, out SilkMeshGpuResource? gpuMesh);
            meshes.Add(new RetainedMesh(
                new MeshIdentity(mesh.Path, mesh.InstanceIndex, mesh.InstancerPath,
                    mesh.InstancerContext.ToArray(), mesh.Transform.ToArray(),
                    mesh.Points.Length / 3, mesh.Indices.Length),
                new Masks(resolved.LightMask, resolved.ShadowMask, resolved.DomeMask),
                indexed, gpuPresent ? resourceId(gpuMesh!) : 0,
                mesh.Points.ToArray(), mesh.Indices.ToArray(), mesh.TopologyRevision));
        }
        return new StateEvidence(
            scene.Frame.LightCount, scene.Frame.Lights.ToArray(), scene.Frame.View.ToArray(),
            scene.Frame.Projection.ToArray(), scene.Frame.AmbientLight, scene.Frame.DomeCount,
            scene.LightLinks.LightCount, scene.LightLinks.DomeCount, (uint)scene.LightLinks.UnsupportedFeatures,
            scene.LightLinks.Count, meshes.ToArray(),
            new Revisions(scene.Revision, scene.Frame.Revision, scene.LightLinks.Revision,
                scene.GeometryRevision, scene.MaterialRevision, scene.EnvironmentRevision,
                scene.DeformationRevision, scene.Shadows.Revision, renderer.GpuResources.Revision),
            renderer.GpuResources.Statistics, resourceId(buffer), frameBytes, pixels);
    }

    private static Dictionary<int, Vector3> CalibratedColors(
        FrameEvidence initial, int highTag, int sacrificialTag)
    {
        int blueReceiverCount = initial.Lights.Length - 1 - (sacrificialTag == 0 ? 0 : 1);
        var colors = new Dictionary<int, Vector3>();
        foreach (SilkFrameLight light in initial.Lights)
        {
            int tag = Tag(light);
            if (tag < 1 || tag > initial.Lights.Length || light.Type != 1 || colors.ContainsKey(tag))
            {
                throw new InvalidDataException($"Unrecognized/duplicate authored light tag in {LightOrder(initial)}.");
            }
            float exposed = Exposed(light);
            if (!float.IsFinite(exposed) || exposed <= 0)
            {
                throw new InvalidDataException($"Meaningful light {tag} has nonpositive/nonfinite exposed intensity.");
            }
            // UsdLux schema defaults are deliberately not hardcoded (DistantLight
            // defaults can differ from LightAPI). The actual published scale sets
            // unclipped radiance, while every filler remains meaningfully nonzero.
            Vector3 color = tag == highTag
                ? new Vector3(0.35f / exposed, 0, 0)
                : new Vector3(0, 0, (0.18f / blueReceiverCount) / exposed);
            if (!float.IsFinite(color.X + color.Z) || color.X + color.Z <= 0)
            {
                throw new InvalidDataException("The authored radiance calibration was not finite and positive.");
            }
            colors.Add(tag, color);
        }
        return colors;
    }

    private static void RestoreDistant(
        UsdStage stage, string path, int tag, Vector3 color,
        bool restricted, bool linkedToLeft = false)
    {
        UsdPrim light = stage.DefinePrim(path, "DistantLight");
        light.SetColor3f("inputs:color", new UsdVec3f(color.X, color.Y, color.Z));
        light.SetBool("inputs:shadow:enable", false);
        light.SetMatrix4d("xformOp:transform", new UsdMatrix4d(
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, tag, 0, 0, 1));
        light.SetTokenArray("xformOpOrder", ["xformOp:transform"]);
        light.SetBool(IncludeRoot, !restricted);
        if (linkedToLeft)
        {
            light.SetRelationshipTargets(Includes, [LeftTarget]);
        }
        else
        {
            light.ClearRelationshipTargets(Includes);
        }
        // Intensity/exposure/diffuse/specular and expandPrims remain schema defaults
        // both in the original generated distant light and this restored prim.
    }

    private static string BuildScene(Scenario scenario, int count, string detail, string shape)
    {
        var usd = new StringBuilder("#usda 1.0\n");
        bool instances = scenario == Scenario.HighInstance;
        if (instances)
        {
            // A class is not a third drawable receiver. Both def Xforms below
            // reference this one prototype and are genuinely instanceable.
            usd.AppendLine("class Xform \"ReceiverTemplate\" {");
            AppendGeometry(usd);
            usd.AppendLine("}");
        }
        usd.AppendLine("def Xform \"World\" {");
        AppendReceiver(usd, "Left", -1, instances);
        AppendReceiver(usd, "Right", 1, instances);
        usd.AppendLine("def Xform \"Lights\" {");
        for (int tag = 1; tag <= count; tag++)
        {
            if (scenario == Scenario.Controls)
            {
                AppendControlledLight(usd, Shape(shape), Control(detail));
            }
            else
            {
                AppendDistant(usd, LightName(tag), tag, "color3f inputs:color = (0, 0, 0.001)");
            }
        }
        usd.AppendLine("}");
        if (scenario is Scenario.Filter or Scenario.Overflow or Scenario.FirstOverflow)
        {
            usd.AppendLine("def Xform \"Filtered\" {");
            if (detail == "inherited-hidden")
            {
                usd.AppendLine("token visibility = \"invisible\"");
            }
            string attributes = scenario == Scenario.FirstOverflow
                ? "color3f inputs:color = (0.2, 0, 0)"
                : scenario == Scenario.Overflow
                    ? "color3f inputs:color = (0, 0, 0)"
                    : FilterAttributes(detail);
            AppendDistant(usd, "Extra", ExtraTag, attributes);
            usd.AppendLine("}");
        }
        usd.AppendLine("}");
        return usd.ToString();
    }

    private static void AppendReceiver(StringBuilder usd, string name, int x, bool instance)
    {
        usd.AppendLine(instance
            ? $"def Xform \"{name}\" (\n    prepend references = </ReceiverTemplate>\n    instanceable = true\n) {{"
            : $"def Xform \"{name}\" {{");
        usd.AppendLine(FormattableString.Invariant(
            $"matrix4d xformOp:transform = ((1,0,0,0),(0,1,0,0),(0,0,1,0),({x},0,0,1))"));
        usd.AppendLine("uniform token[] xformOpOrder = [\"xformOp:transform\"]");
        if (!instance)
        {
            AppendGeometry(usd);
        }
        usd.AppendLine("}");
    }

    private static void AppendGeometry(StringBuilder usd) => usd.AppendLine("""
        def Mesh "Geometry" {
            uniform token subdivisionScheme = "none"
            bool doubleSided = true
            point3f[] points = [(-0.65,-0.75,-4),(0.65,-0.75,-4),(0.65,0.75,-4),(-0.65,0.75,-4)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [(0,0,1)] (interpolation = "constant")
            color3f[] primvars:displayColor = [(1,1,1)] (interpolation = "constant")
        }
        """);

    private static void AppendDistant(StringBuilder usd, string name, int tag, string attributes)
    {
        usd.AppendLine(CultureInfo.InvariantCulture, $"def DistantLight \"{name}\" {{");
        usd.AppendLine(attributes);
        usd.AppendLine("bool inputs:shadow:enable = false");
        usd.AppendLine("token visibility = \"inherited\"");
        usd.AppendLine(FormattableString.Invariant(
            $"matrix4d xformOp:transform = ((1,0,0,0),(0,1,0,0),(0,0,1,0),({tag},0,0,1))"));
        usd.AppendLine("uniform token[] xformOpOrder = [\"xformOp:transform\"]");
        usd.AppendLine("}");
    }

    private static string FilterAttributes(string filter) => filter switch
    {
        "inherited-hidden" => "float inputs:intensity = 1\ncolor3f inputs:color = (0.2,0.3,0.4)",
        "intensity-zero" => "float inputs:intensity = 0\ncolor3f inputs:color = (0.2,0.3,0.4)",
        "all-color-zero" => "float inputs:intensity = 1\ncolor3f inputs:color = (0,0,0)",
        "diffuse-and-specular-zero" =>
            "float inputs:intensity = 1\ncolor3f inputs:color = (0.2,0.3,0.4)\n" +
            "float inputs:diffuse = 0\nfloat inputs:specular = 0",
        // 2^-200 is finite in double but rounds to float zero. The separate
        // near-zero positive control uses 2^-126, the smallest normal float.
        "exposed-float-zero" =>
            "float inputs:intensity = 1\nfloat inputs:exposure = -200\ncolor3f inputs:color = (0.2,0.3,0.4)",
        // At the immediately adjacent integral exposure below the smallest
        // positive float, half of float.Epsilon rounds to zero (ties to even).
        "exposed-rounding-zero" =>
            "float inputs:intensity = 1\nfloat inputs:exposure = -150\ncolor3f inputs:color = (1,1,1)",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown independent filter.")
    };

    private static void AppendControlledLight(StringBuilder usd, ShapeSpec shape, ControlSpec control)
    {
        usd.AppendLine(CultureInfo.InvariantCulture, $"def {shape.Schema} \"L0001\" {{");
        usd.AppendLine(shape.Attributes);
        usd.AppendLine(FormattableString.Invariant($"""
            float inputs:intensity = {control.Intensity}
            float inputs:exposure = {control.Exposure}
            float inputs:diffuse = {control.Diffuse}
            float inputs:specular = {control.Specular}
            color3f inputs:color = ({control.Color.X},{control.Color.Y},{control.Color.Z})
            bool inputs:shadow:enable = false
            matrix4d xformOp:transform = ((0,2,0,0),(-3,0,0,0),(0,0,4,0),(1,2,3,1))
            uniform token[] xformOpOrder = ["xformOp:transform"]
            """));
        usd.AppendLine("}");
    }

    private static readonly Matrix4x4 ControlTransform = new(
        0, 2, 0, 0, -3, 0, 0, 0, 0, 0, 4, 0, 1, 2, 3, 1);

    // Preserve the existing adapter: disk radius is not shapeX, distant angle
    // is not projected, and shapes without a radius retain the 0.5 default.
    private static ShapeSpec Shape(string shape) => shape switch
    {
        "distant" => new(1, "DistantLight", "float inputs:angle = 0.75", 0, 0, 0.5f),
        "sphere" => new(2, "SphereLight", "float inputs:radius = 0.375", 0, 0, 0.375f),
        "rect" => new(3, "RectLight", "float inputs:width = 2.5\nfloat inputs:height = 1.5", 2.5f, 1.5f, 0.5f),
        "disk" => new(4, "DiskLight", "float inputs:radius = 0.375", 0, 0, 0.375f),
        "cylinder" => new(5, "CylinderLight",
            "float inputs:length = 1.75\nfloat inputs:radius = 0.375", 1.75f, 0, 0.375f),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown light shape.")
    };

    private static ControlSpec Control(string control) => control switch
    {
        "ordinary" => new(new Vector3(0.2f, 0.3f, 0.4f), 0.5f, 0, 0.75f, 0.25f, 0.5f),
        "diffuse-only" => new(new Vector3(0.2f, 0.3f, 0.4f), 0.5f, 0, 0.75f, 0, 0.5f),
        "specular-only" => new(new Vector3(0.2f, 0.3f, 0.4f), 0.5f, 0, 0, 0.25f, 0.5f),
        "red-only" => new(new Vector3(0.2f, 0, 0), 0.5f, 0, 0.75f, 0.25f, 0.5f),
        "green-only" => new(new Vector3(0, 0.3f, 0), 0.5f, 0, 0.75f, 0.25f, 0.5f),
        "blue-only" => new(new Vector3(0, 0, 0.4f), 0.5f, 0, 0.75f, 0.25f, 0.5f),
        "exposure" => new(new Vector3(0.2f, 0.3f, 0.4f), 0.5f, 2, 0.75f, 0.25f, 2),
        "near-zero" => new(Vector3.One, 1, -126, 1, 1, 1.17549435E-38f),
        "subnormal" => new(Vector3.One, 1, -149, 1, 1, float.Epsilon),
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Unknown meaningful control.")
    };

    private static bool IsEnabled(string? value) =>
        value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool SupportedOs(SilkGraphicsBackend backend) =>
        backend == SilkGraphicsBackend.D3D12
            ? OperatingSystem.IsWindows()
            : backend == SilkGraphicsBackend.Vulkan && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    private static void CheckPrerequisites(
        ExecutionProfile profile, string? plugins, bool pluginDirectoryExists, bool supportedOs)
    {
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Unavailable(profile, "the matched native plugin configuration is missing");
        }
        if (!pluginDirectoryExists)
        {
            Unavailable(profile, "the configured matched native plugin directory does not exist");
        }
        if (profile.Backend is not (SilkGraphicsBackend.D3D12 or SilkGraphicsBackend.Vulkan))
        {
            Unavailable(profile, "only the explicitly selected D3D12 or Vulkan backend is allowed");
        }
        if (profile.D3D12Hardware && profile.Backend != SilkGraphicsBackend.D3D12)
        {
            Unavailable(profile, "the hardware profile requires the D3D12 backend");
        }
        if (!supportedOs)
        {
            Unavailable(profile, "the selected backend is unavailable on this operating system");
        }
    }

    private static void CheckDevice(ExecutionProfile profile, DeviceEvidence device)
    {
        if (device.Backend != profile.Backend)
        {
            Unavailable(profile,
                $"selected {device.Backend}, not the requested {profile.Backend}; cross-backend fallback is forbidden");
        }
        if (string.IsNullOrWhiteSpace(device.Name) || string.IsNullOrWhiteSpace(device.ApiVersion))
        {
            Unavailable(profile, "the actual device identity/API version is missing");
        }
        if (!device.SupportsCompute)
        {
            Unavailable(profile, $"the actual selected device '{device.Name}' lacks the compute capability");
        }
        if (profile.Backend == SilkGraphicsBackend.D3D12 && profile.D3D12Hardware && device.IsSoftware)
        {
            Unavailable(profile, $"D3D12 must really use hardware; actual device='{device.Name}', software=true");
        }
        if (profile.Backend == SilkGraphicsBackend.D3D12 && !profile.D3D12Hardware &&
            (!device.IsSoftware || !IsWarpName(device.Name)))
        {
            Unavailable(profile,
                $"D3D12 must really use WARP; actual device='{device.Name}', software={device.IsSoftware}");
        }
        if (profile.Backend == SilkGraphicsBackend.Vulkan && profile.RequireSwiftShader &&
            (!device.IsSoftware || !device.Name.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
        {
            Unavailable(profile,
                $"selected Vulkan runtime must be SwiftShader; " +
                $"actual device='{device.Name}', software={device.IsSoftware}");
        }
    }

    private static ISilkGraphicsDevice CreateDevice(ExecutionProfile profile)
    {
        if (!profile.D3D12Hardware)
        {
            return SilkDepthCaptureConformance.CreateDevice(profile.Backend);
        }
        if (!OperatingSystem.IsWindows() || profile.Backend != SilkGraphicsBackend.D3D12)
        {
            throw new PlatformNotSupportedException("The D3D12 hardware profile requires Windows.");
        }
        return D3D12SilkGraphicsDevice.Create(useWarp: false);
    }

    private static bool IsWarpName(string name) =>
        name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("WARP", StringComparison.OrdinalIgnoreCase);

    private static void Unavailable(ExecutionProfile profile, string reason, Exception? inner = null)
    {
        string message =
            $"{RequiredVariable}={(profile.Required ? "required" : "optional")}: {profile.Backend}: {reason}. " +
            $"Expected hdSilk page ABI {PageAbi}/session ABI {SessionAbi}. " +
            $"Set {PluginVariable} to the exact matched staged plugin directory and activate that root's native " +
            $"library search paths; supply {profile.Backend} offscreen execution " +
            $"(D3D12 WARP on Windows; Vulkan ICD via VK_DRIVER_FILES/VK_ICD_FILENAMES, " +
            $"{SwiftShaderVariable}=1 when SwiftShader is selected). No cross-backend fallback.";
        if (profile.Required)
        {
            throw new InvalidOperationException(message, inner);
        }
        Skip.Test(message);
        throw new InvalidOperationException("Skip.Test returned unexpectedly.", inner);
    }

    private static async Task AssertRun(RunEvidence result)
    {
        await Assert.That(result.SourceUnchanged).IsTrue()
            .Because("Edits must stay in the exact live stage; the original generated file is never saved.");
        await Assert.That(result.InitiallySynchronized).IsFalse();
        await Assert.That(result.FinallySynchronized).IsTrue();
        await Assert.That(result.ExtraAuthoredVerified).IsTrue()
            .Because("A filtered/overflow extra must really exist on the live native stage, " +
                "not just in generated text.");
        uint requiredSessionAbi = OpenUsdSilkRuntime.RequiredSilkSessionAbiVersion;
        await Assert.That(requiredSessionAbi).IsEqualTo(SessionAbi)
            .Because("Successful real Create must have validated session ABI 6, not an older managed requirement.");
        await Assert.That(result.Device.Backend).IsEqualTo(result.Profile.Backend);
        await Assert.That(result.Device.Name.Length).IsGreaterThan(0);
        await Assert.That(result.Device.ApiVersion.Length).IsGreaterThan(0);
        await Assert.That(result.Device.SupportsCompute).IsTrue();
        if (result.Profile.Backend == SilkGraphicsBackend.D3D12)
        {
            await Assert.That(result.Device.IsSoftware).IsEqualTo(!result.Profile.D3D12Hardware);
            await Assert.That(IsWarpName(result.Device.Name)).IsEqualTo(!result.Profile.D3D12Hardware);
        }
        if (result.Profile.Backend == SilkGraphicsBackend.Vulkan && result.Profile.RequireSwiftShader)
        {
            await Assert.That(result.Device.IsSoftware).IsTrue();
            await Assert.That(result.Device.Name.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        Console.WriteLine(
            $"Native direct-light evidence: backend={result.Device.Backend}, device='{result.Device.Name}', " +
            $"api='{result.Device.ApiVersion}', software={result.Device.IsSoftware}, " +
            $"compute={result.Device.SupportsCompute}, sourceUnchanged={result.SourceUnchanged}.");
        foreach ((string label, CaptureEvidence capture) in result.Captures)
        {
            Console.WriteLine(
                $"{label}: pageABI={capture.Page.Abi}, revision={capture.Page.Revision}, " +
                $"commands={capture.Page.CommandCount}, frameCommands={capture.Page.FrameCommands}, " +
                $"linkCommands={capture.Page.LinkCommands}, lights={capture.Frame.LightCount}, " +
                $"flags=0x{capture.Frame.Flags:X}, highTag={result.HighTag}, " +
                $"actualHighIndex={IndexOf(capture.Frame, result.HighTag)}, " +
                $"left={Pixel(capture.State.Pixels, 16, 32)}, right={Pixel(capture.State.Pixels, 48, 32)}.");
            foreach (RetainedMesh mesh in capture.State.Meshes)
            {
                Console.WriteLine(
                    $"  {Describe(mesh.Identity)}; " +
                    $"lightMask=0x{mesh.Masks.Light:X32}, shadowMask=0x{mesh.Masks.Shadow:X32}, " +
                    $"domeMask=0x{mesh.Masks.Dome:X}, resourceIdentity={mesh.ResourceId}.");
            }
            await Assert.That(capture.Page.Abi).IsEqualTo(PageAbi);
            await Assert.That(capture.Synchronized).IsTrue();
            await Assert.That(capture.State.FrameBytes.Length).IsEqualTo(FrameBufferSize);
            await Assert.That(capture.State.Revisions.Scene).IsEqualTo(capture.Page.Revision);
            await Assert.That(capture.Frame.RawLightCount).IsEqualTo(capture.Frame.LightCount);
            await Assert.That(capture.Frame.Reserved544).IsEqualTo(0u);
            await Assert.That(capture.Frame.Reserved548).IsEqualTo(0u);
            await Assert.That(capture.Frame.WireSize is 272 or 536 or 23096 or 23368).IsTrue();
            if (capture.Frame.WireSize is 272 or 536)
            {
                await Assert.That(capture.Frame.LightCount).IsEqualTo(0u);
                await Assert.That(capture.Frame.Flags).IsEqualTo(0u);
            }
            await Assert.That(capture.Frame.DomeCount).IsEqualTo(0u);
            await Assert.That(capture.State.Ambient).IsEqualTo(Vector4.Zero);
            await Assert.That(capture.State.DomeCount).IsEqualTo(0u);
            await Assert.That(capture.State.LinkUnsupported).IsEqualTo(0u);
            await Assert.That(capture.Links.Unsupported).IsEqualTo(0u);
            await Assert.That(capture.State.LightCount).IsEqualTo(capture.Frame.LightCount);
            await Assert.That(capture.State.FrameLights.Take((int)capture.Frame.LightCount)
                .SequenceEqual(capture.Frame.Lights)).IsTrue();
            await Assert.That(ReadSingle(capture.State.FrameBytes, 220))
                .IsEqualTo((float)capture.Frame.LightCount);
            await AssertNativeMeshesMatchRetained(capture);
        }
    }

    private static async Task AssertPublished(
        CaptureEvidence capture, int count, IEnumerable<int> expectedTags)
    {
        await Assert.That(capture.Frame.LightCount).IsEqualTo((uint)count);
        await Assert.That(capture.Frame.Lights.Length).IsEqualTo(count);
        await Assert.That(capture.Frame.Lights.Select(Tag).Distinct().Count()).IsEqualTo(count);
        await Assert.That(capture.Frame.Lights.Select(Tag).Order().SequenceEqual(expectedTags.Order())).IsTrue()
            .Because($"Actual native light ordering: {LightOrder(capture.Frame)}.");
        await Assert.That(capture.State.LightCount).IsEqualTo((uint)count);
        for (int light = count; light < capture.State.FrameLights.Length; light++)
        {
            await Assert.That(capture.State.FrameLights[light].Type).IsEqualTo(0u);
            await AssertVector(capture.State.FrameBytes, 4320 + light * 16, Vector4.Zero);
        }
    }

    private static async Task AssertHighRemovalAndRestoration(
        RunEvidence result, int count, bool instances)
    {
        CaptureEvidence initial = result.Captures["initial"];
        CaptureEvidence before = result.Captures["before"];
        CaptureEvidence removed = result.Captures["changed"];
        CaptureEvidence restored = result.Captures["restored"];
        await AssertRun(result);
        await Assert.That(result.AuthoredInstancesVerified).IsTrue();
        await AssertPublished(initial, count, Enumerable.Range(1, count));
        await AssertPublished(before, count, Enumerable.Range(1, count));
        await AssertGeometryUnchanged(initial, before);
        await Assert.That(Tag(initial.Frame.Lights[count - 1])).IsEqualTo(result.HighTag);
        await Assert.That(initial.Page.MeshUpserts.Length).IsEqualTo(2);
        await Assert.That(initial.Page.FrameCommands).IsEqualTo(1);
        await Assert.That(before.Page.FrameCommands).IsEqualTo(1);
        await Assert.That(before.Page.LinkCommands).IsEqualTo(1);
        await Assert.That(IndexOf(before.Frame, result.HighTag)).IsEqualTo(count - 1);
        await AssertHighMembership(before, result.HighTag, count, true, instances);
        await AssertPublished(removed, count - 1,
            Enumerable.Range(1, count).Where(tag => tag != result.HighTag));
        await Assert.That(removed.Page.FrameCommands).IsEqualTo(1);
        await Assert.That(IndexOf(removed.Frame, result.HighTag)).IsEqualTo(-1);
        await Assert.That(removed.State.FrameLights[count - 1].Type).IsEqualTo(0u);
        await AssertHighMembership(removed, result.HighTag, count - 1, false, instances);
        await AssertPublished(restored, count, Enumerable.Range(1, count));
        await Assert.That(restored.Page.FrameCommands).IsEqualTo(1);
        await AssertHighMembership(restored, result.HighTag, count, true, instances);
        int restoredIndex = IndexOf(restored.Frame, result.HighTag);
        await Assert.That(restoredIndex).IsGreaterThanOrEqualTo(0);
        // A restored native light may occupy a different slot. Compare identity/value
        // and actual remapped memberships, not a fictional native sort guarantee.
        await Assert.That(restored.Frame.Lights[restoredIndex])
            .IsEqualTo(before.Frame.Lights[count - 1]);
        await AssertOnlyLeftRedChanges(before.State.Pixels, removed.State.Pixels);
        await Assert.That(restored.State.Pixels.SequenceEqual(before.State.Pixels)).IsTrue();
        await AssertCoverageEqual(before.State.Pixels, restored.State.Pixels);
        await Assert.That(removed.State.Revisions.Frame).IsGreaterThan(before.State.Revisions.Frame);
        await Assert.That(restored.State.Revisions.Frame).IsGreaterThan(removed.State.Revisions.Frame);
        await AssertGeometryUnchanged(before, removed);
        await AssertGeometryUnchanged(before, restored);
        if (instances)
        {
            await AssertNativeInstanceMembership(initial);
            await AssertNativeInstanceMembership(before);
            await AssertNativeInstanceMembership(removed);
            await AssertNativeInstanceMembership(restored);
        }
        await AssertSteady(restored, result.Captures["steady"]);
        WritePixelEvidence(result, count, instances);
    }

    private static void WritePixelEvidence(RunEvidence result, int count, bool instances)
    {
        string? evidenceRoot = Environment.GetEnvironmentVariable("OPENUSD_DIRECT_LIGHT_EVIDENCE_ROOT");
        if (string.IsNullOrWhiteSpace(evidenceRoot))
        {
            return;
        }
        string kind = instances ? "instances" : "meshes";
        string root = Directory.CreateDirectory(Path.Combine(
            Path.GetFullPath(evidenceRoot),
            string.Create(CultureInfo.InvariantCulture, $"{result.Device.Backend}-{count}-{kind}-{Guid.NewGuid():N}")))
            .FullName;
        foreach ((string label, CaptureEvidence capture) in result.Captures)
        {
            using (var image = new FileStream(
                Path.Combine(root, $"{label}.png"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                _ = PngRgba8Writer.Write(image, Size, Size, capture.State.Pixels, 1024 * 1024);
            }
            File.WriteAllBytes(Path.Combine(root, $"{label}.frame.bin"), capture.State.FrameBytes);
        }
        var receipt = new
        {
            Test = !result.Device.IsSoftware && result.Device.Backend == SilkGraphicsBackend.D3D12
                ? nameof(NativeHighLightLinkingSurvivesRemovalAndRestorationOnHardware)
                : instances
                    ? nameof(NativeHighLightOnlyChangesLinkedInstancePixels)
                    : nameof(NativeHighLightOnlyChangesLinkedMeshPixels),
            Backend = result.Device.Backend.ToString(),
            result.Device.Name,
            result.Device.ApiVersion,
            result.Device.IsSoftware,
            result.SourceUnchanged,
            InitialLightCount = count,
            Width = Size,
            Height = Size,
            Captures = result.Captures.Select(entry => new
            {
                Label = entry.Key,
                entry.Value.Page.Abi,
                entry.Value.Page.Revision,
                entry.Value.Frame.LightCount,
                HighIndex = IndexOf(entry.Value.Frame, result.HighTag),
                PixelsSha256 = Convert.ToHexString(SHA256.HashData(entry.Value.State.Pixels)),
                FrameSha256 = Convert.ToHexString(SHA256.HashData(entry.Value.State.FrameBytes)),
                Meshes = entry.Value.State.Meshes.Select(mesh => new
                {
                    mesh.Identity.Path,
                    mesh.Identity.InstanceIndex,
                    LightMask = mesh.Masks.Light.ToString("X32", CultureInfo.InvariantCulture),
                    ShadowMask = mesh.Masks.Shadow.ToString("X32", CultureInfo.InvariantCulture),
                    DomeMask = mesh.Masks.Dome
                })
            })
        };
        File.WriteAllText(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(receipt));
        Console.WriteLine($"Validated native pixel evidence: {root}");
    }

    private static async Task AssertHighMembership(
        CaptureEvidence capture, int highTag, int count, bool included, bool instances,
        int sacrificialTag = 0)
    {
        await Assert.That(capture.Frame.LightCount).IsEqualTo((uint)count);
        await Assert.That(capture.Frame.Lights.Select(Tag).Distinct().Count()).IsEqualTo(count);
        await Assert.That(capture.State.Meshes.Length).IsEqualTo(2);
        UInt128 active = ActiveBits(count);
        int high = IndexOf(capture.Frame, highTag);
        int sacrificial = sacrificialTag == 0 ? -1 : IndexOf(capture.Frame, sacrificialTag);
        UInt128 highBit = high < 0 ? UInt128.Zero : UInt128.One << high;
        UInt128 sacrificialBit = sacrificial < 0 ? UInt128.Zero : UInt128.One << sacrificial;
        foreach (RetainedMesh mesh in capture.State.Meshes)
        {
            bool left = IsLeft(mesh.Identity);
            UInt128 expected = active & ~sacrificialBit;
            if (!left || !included)
            {
                expected &= ~highBit;
            }
            await Assert.That(mesh.Masks.Light & active).IsEqualTo(expected)
                .Because($"{Describe(mesh.Identity)}; actual high={high}, tag={highTag}, masks={mesh.Masks}.");
            await Assert.That(mesh.Masks.Shadow & active).IsEqualTo(active)
                .Because("Light-link exclusions must not implicitly become shadow-link exclusions.");
            Masks pageResolved = Resolve(capture.Links, mesh.Identity);
            await Assert.That(pageResolved.Light & active).IsEqualTo(expected);
            await Assert.That(pageResolved.Shadow & active).IsEqualTo(active);
            await Assert.That(mesh.Masks.Light & active).IsEqualTo(pageResolved.Light & active);
            await Assert.That(mesh.Masks.Dome & 0xffu).IsEqualTo(pageResolved.Dome & 0xffu);
            if (expected != active)
            {
                await Assert.That(capture.Links.Rows.Any(row =>
                    row.Path == mesh.Identity.Path &&
                    (row.InstanceIndex == mesh.Identity.InstanceIndex ||
                     row.InstanceIndex == SilkLightLinkCommand.AllInstances))).IsTrue()
                    .Because("An exclusion must exist on the observed native sparse table, " +
                        "not only in an expected mask.");
            }
        }
        if (capture.Links.Rows.Length > 0)
        {
            await Assert.That(capture.Links.LightCount).IsEqualTo((uint)count);
            await Assert.That(capture.State.LinkLightCount).IsEqualTo((uint)count);
        }
        if (high >= 0)
        {
            SilkFrameLight selected = capture.Frame.Lights[high];
            await Assert.That(selected.Type).IsEqualTo(1u);
            await Assert.That(selected.Color.Y).IsEqualTo(0f);
            await Assert.That(selected.Color.Z).IsEqualTo(0f);
            await Assert.That(MathF.Abs(selected.Color.X * Exposed(selected) - 0.35f)).IsLessThan(0.00001f);
        }
        foreach (SilkFrameLight filler in capture.Frame.Lights.Where(light => Tag(light) != highTag))
        {
            await Assert.That(filler.Color.X).IsEqualTo(0f);
            await Assert.That(filler.Color.Y).IsEqualTo(0f);
            await Assert.That(filler.Color.Z * Exposed(filler)).IsGreaterThan(0f);
        }
        await AssertGeometry(capture.State.Pixels);
        foreach ((int x, int y) in InteriorSamples())
        {
            bool redExpected = x < Size / 2 && included && high >= 0;
            byte red = Channel(capture.State.Pixels, x, y, 0);
            byte blue = Channel(capture.State.Pixels, x, y, 2);
            await Assert.That(redExpected ? red > 15 && red < 240 : red <= 1).IsTrue()
                .Because($"Expected red={redExpected} at ({x},{y}); {Pixel(capture.State.Pixels, x, y)}.");
            await Assert.That(blue).IsGreaterThan((byte)5);
            await Assert.That(blue).IsLessThan((byte)240);
            await Assert.That(Channel(capture.State.Pixels, x, y, 1)).IsEqualTo((byte)0);
        }
        if (instances)
        {
            await AssertNativeInstanceMembership(capture);
        }
        else
        {
            foreach (RetainedMesh mesh in capture.State.Meshes)
            {
                await Assert.That(mesh.Identity.InstancerPath).IsEqualTo(string.Empty);
                await Assert.That(mesh.Identity.Context.Length).IsEqualTo(0);
                await Assert.That(mesh.Identity.Path)
                    .IsEqualTo((IsLeft(mesh.Identity) ? LeftTarget : RightTarget) + "/Geometry");
            }
        }
    }

    private static async Task AssertNativeMeshesMatchRetained(CaptureEvidence capture)
    {
        foreach (MeshIdentity command in capture.Page.MeshUpserts)
        {
            RetainedMesh retained = capture.State.Meshes.Single(mesh =>
                mesh.Identity.Path == command.Path && mesh.Identity.InstanceIndex == command.InstanceIndex);
            await Assert.That(retained.IndexedByObservedIdentity).IsTrue();
            await Assert.That(retained.ResourceId).IsGreaterThan(0);
            await Assert.That(retained.Identity.InstancerPath).IsEqualTo(command.InstancerPath);
            await Assert.That(retained.Identity.Context.SequenceEqual(command.Context)).IsTrue();
            await Assert.That(retained.Identity.Transform.SequenceEqual(command.Transform)).IsTrue();
            if (command.PointCount > 0)
            {
                await Assert.That(retained.Identity.PointCount).IsEqualTo(command.PointCount);
                await Assert.That(retained.Identity.IndexCount).IsEqualTo(command.IndexCount);
            }
        }
    }

    private static async Task AssertNativeInstanceMembership(CaptureEvidence capture)
    {
        RetainedMesh[] meshes = capture.State.Meshes;
        await Assert.That(meshes.Length).IsEqualTo(2);
        await Assert.That(meshes.Select(mesh => (mesh.Identity.Path, mesh.Identity.InstanceIndex))
            .Distinct().Count()).IsEqualTo(2);
        await Assert.That(meshes.Select(mesh => DescribeContext(mesh.Identity)).Distinct().Count())
            .IsEqualTo(2)
            .Because("Two ordinary meshes or a duplicated native instance " +
                "must not stand in for the authored references.");
        foreach (RetainedMesh mesh in meshes)
        {
            MeshIdentity identity = mesh.Identity;
            await Assert.That(identity.InstancerPath.Length).IsGreaterThan(0).Because(Describe(identity));
            await Assert.That(identity.Context.Length).IsGreaterThan(0).Because(Describe(identity));
            await Assert.That(identity.Context.All(entry =>
                entry.InstancerPath.StartsWith('/') && entry.InstanceIndex >= 0)).IsTrue();
            await Assert.That(identity.Context[^1].InstancerPath).IsEqualTo(identity.InstancerPath);
            await Assert.That(mesh.IndexedByObservedIdentity).IsTrue();
            await Assert.That(identity.PointCount).IsEqualTo(4);
            await Assert.That(identity.IndexCount).IsEqualTo(6);
            await Assert.That(identity.Transform[12]).IsEqualTo(IsLeft(identity) ? -1d : 1d);
            await Assert.That(identity.Transform[13]).IsEqualTo(0d);
            await Assert.That(identity.Transform[14]).IsEqualTo(0d);
        }
        await Assert.That(meshes[0].Points.SequenceEqual(meshes[1].Points)).IsTrue();
        await Assert.That(meshes[0].Indices.SequenceEqual(meshes[1].Indices)).IsTrue();
        // The authoritative native tuple/context identifies each retained instance.
        // Its actual -1/+1 transform associates that tuple with the corresponding
        // authored reference Xform. Never reinterpret InstanceIndex as a USD ordinal
        // or use InstanceId's diagnostic hash as a collection target.
    }

    private static async Task AssertOnlyLeftRedChanges(byte[] before, byte[] removed)
    {
        await AssertCoverageEqual(before, removed);
        await AssertGeometry(before);
        await AssertGeometry(removed);
        foreach ((int x, int y) in InteriorSamples())
        {
            byte previousRed = Channel(before, x, y, 0);
            byte removedRed = Channel(removed, x, y, 0);
            if (x < Size / 2)
            {
                await Assert.That((int)previousRed - removedRed).IsGreaterThan(15)
                    .Because($"Linked interior ({x},{y}): {Pixel(before, x, y)} -> {Pixel(removed, x, y)}.");
                await Assert.That(removedRed).IsLessThanOrEqualTo((byte)1);
            }
            else
            {
                await Assert.That(previousRed).IsEqualTo(removedRed);
            }
            await Assert.That(Channel(before, x, y, 2)).IsEqualTo(Channel(removed, x, y, 2));
            await Assert.That(Channel(removed, x, y, 2)).IsGreaterThan((byte)5);
            await Assert.That(Channel(before, x, y, 1)).IsEqualTo((byte)0);
            await Assert.That(Channel(removed, x, y, 1)).IsEqualTo((byte)0);
        }
        // Stronger than a center probe: every excluded/reference pixel, including its
        // edges, and every left-side background pixel must remain byte-identical.
        var referenceBefore = new List<byte>();
        var referenceRemoved = new List<byte>();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (x >= Size / 2 || IsClear(before, x, y))
                {
                    for (int component = 0; component < 4; component++)
                    {
                        referenceBefore.Add(Channel(before, x, y, component));
                        referenceRemoved.Add(Channel(removed, x, y, component));
                    }
                }
            }
        }
        await Assert.That(referenceBefore.Count).IsGreaterThan(Size * Size * 2);
        await Assert.That(referenceRemoved.SequenceEqual(referenceBefore)).IsTrue()
            .Because("Every byte of the excluded half and every left-side geometry gap must stay unchanged.");
    }

    private static async Task AssertGeometry(byte[] pixels)
    {
        await Assert.That(pixels.Length).IsEqualTo(Size * Size * 4);
        int covered = Coverage(pixels).Count(static value => value);
        await Assert.That(covered).IsGreaterThan(500);
        await Assert.That(covered).IsLessThan(1500);
        foreach ((int x, int y) in InteriorSamples())
        {
            await Assert.That(IsClear(pixels, x, y)).IsFalse().Because(Pixel(pixels, x, y));
        }
        foreach (int x in new[] { 2, 31, 32, 61 })
        {
            foreach (int y in new[] { 2, 24, 32, 40, 61 })
            {
                await Assert.That(Channel(pixels, x, y, 0)).IsEqualTo((byte)0);
                await Assert.That(Channel(pixels, x, y, 1)).IsEqualTo(ClearGreen);
                await Assert.That(Channel(pixels, x, y, 2)).IsEqualTo((byte)0);
                await Assert.That(Channel(pixels, x, y, 3)).IsEqualTo(byte.MaxValue);
            }
        }
        foreach (int y in new[] { 2, 12, 51, 61 })
        {
            foreach (int x in new[] { 10, 16, 22, 42, 48, 54 })
            {
                await Assert.That(IsClear(pixels, x, y)).IsTrue().Because(Pixel(pixels, x, y));
            }
        }
    }

    private static async Task AssertCoverageEqual(byte[] first, byte[] second)
    {
        await Assert.That(Coverage(second).SequenceEqual(Coverage(first))).IsTrue()
            .Because("RGB geometry silhouettes, not just alpha/counts, must stay fixed.");
        await Assert.That(Coverage(first).Count(static value => value)).IsGreaterThan(500);
    }

    private static async Task AssertGeometryUnchanged(
        CaptureEvidence before, CaptureEvidence after, bool republishedOnRecovery = false)
    {
        await Assert.That(after.Page.MeshRemoves.Length).IsEqualTo(0);
        if (republishedOnRecovery)
        {
            // The existing session recovery repopulates imaging after a refused
            // sync. Replayed, identical geometry may advance its revision, but
            // must neither change its values/pixels nor rebuild its GPU payload.
            await Assert.That(after.Page.MeshUpserts.Length).IsEqualTo(before.State.Meshes.Length);
            await Assert.That(after.State.Revisions.Geometry)
                .IsEqualTo(before.State.Revisions.Geometry + (ulong)after.Page.MeshUpserts.Length);
        }
        else
        {
            await Assert.That(after.State.Revisions.Geometry).IsEqualTo(before.State.Revisions.Geometry);
        }
        await Assert.That(after.State.Revisions.Material).IsEqualTo(before.State.Revisions.Material);
        await Assert.That(after.State.Revisions.Environment).IsEqualTo(before.State.Revisions.Environment);
        await Assert.That(after.State.Revisions.Deformation).IsEqualTo(before.State.Revisions.Deformation);
        await Assert.That(after.State.Statistics.GeometryBuilds).IsEqualTo(before.State.Statistics.GeometryBuilds);
        await Assert.That(after.State.Statistics.VertexUploads).IsEqualTo(before.State.Statistics.VertexUploads);
        await Assert.That(after.State.Statistics.IndexUploads).IsEqualTo(before.State.Statistics.IndexUploads);
        await Assert.That(after.State.FrameResourceId).IsEqualTo(before.State.FrameResourceId);
        await AssertMeshesEqual(before.State.Meshes, after.State.Meshes, compareMasks: false);
    }

    private static async Task AssertSteady(CaptureEvidence before, CaptureEvidence steady)
    {
        await Assert.That(steady.Page.MeshUpserts.Length).IsEqualTo(0);
        await Assert.That(steady.Page.MeshRemoves.Length).IsEqualTo(0);
        await Assert.That(steady.Page.LinkCommands).IsEqualTo(0);
        await Assert.That(steady.State.Revisions.Frame).IsEqualTo(before.State.Revisions.Frame);
        await Assert.That(steady.State.Revisions.Links).IsEqualTo(before.State.Revisions.Links);
        await Assert.That(steady.State.Revisions.Shadows).IsEqualTo(before.State.Revisions.Shadows);
        await Assert.That(steady.State.Revisions.Gpu).IsEqualTo(before.State.Revisions.Gpu);
        await Assert.That(steady.State.FrameLights.SequenceEqual(before.State.FrameLights)).IsTrue();
        await Assert.That(steady.State.FrameBytes.SequenceEqual(before.State.FrameBytes)).IsTrue();
        await Assert.That(steady.State.Pixels.SequenceEqual(before.State.Pixels)).IsTrue();
        await Assert.That(steady.State.Statistics).IsEqualTo(before.State.Statistics)
            .Because("An unchanged native sync must not upload buffers or rebuild resources.");
        await AssertMeshesEqual(before.State.Meshes, steady.State.Meshes, compareMasks: true);
        await AssertGeometryUnchanged(before, steady);
    }

    private static async Task AssertMeshesEqual(
        RetainedMesh[] before, RetainedMesh[] after, bool compareMasks)
    {
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (int index = 0; index < before.Length; index++)
        {
            await Assert.That(after[index].Identity.Path).IsEqualTo(before[index].Identity.Path);
            await Assert.That(after[index].Identity.InstanceIndex).IsEqualTo(before[index].Identity.InstanceIndex);
            await Assert.That(after[index].Identity.InstancerPath).IsEqualTo(before[index].Identity.InstancerPath);
            await Assert.That(after[index].Identity.Context.SequenceEqual(before[index].Identity.Context)).IsTrue();
            await Assert.That(after[index].Identity.Transform.SequenceEqual(before[index].Identity.Transform)).IsTrue();
            await Assert.That(after[index].Points.SequenceEqual(before[index].Points)).IsTrue();
            await Assert.That(after[index].Indices.SequenceEqual(before[index].Indices)).IsTrue();
            await Assert.That(after[index].TopologyRevision).IsEqualTo(before[index].TopologyRevision);
            await Assert.That(after[index].ResourceId).IsEqualTo(before[index].ResourceId);
            await Assert.That(after[index].IndexedByObservedIdentity).IsTrue();
            if (compareMasks)
            {
                await Assert.That(after[index].Masks).IsEqualTo(before[index].Masks);
            }
        }
    }

    private static async Task AssertOverflow(FailureEvidence failure, bool synchronizedBefore)
    {
        await Assert.That(failure.IsSilkException).IsTrue();
        await Assert.That(failure.Status).IsNotEqualTo(0)
            .Because("Do not guess an overflow enum: require the actual non-OK native status.");
        await Assert.That(failure.Message.Contains("129", StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains("128", StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains("light", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(failure.PageDelivered).IsFalse();
        await Assert.That(failure.SynchronizedBefore).IsEqualTo(synchronizedBefore);
        await Assert.That(failure.SynchronizedAfter).IsEqualTo(synchronizedBefore);
        await Assert.That(failure.ApplyCountAfter).IsEqualTo(failure.ApplyCountBefore);
    }

    private static async Task AssertStateEqual(StateEvidence before, StateEvidence after)
    {
        await Assert.That(after.Revisions).IsEqualTo(before.Revisions);
        await Assert.That(after.LightCount).IsEqualTo(before.LightCount);
        await Assert.That(after.FrameLights.SequenceEqual(before.FrameLights)).IsTrue();
        await Assert.That(after.View.SequenceEqual(before.View)).IsTrue();
        await Assert.That(after.Projection.SequenceEqual(before.Projection)).IsTrue();
        await Assert.That(after.Ambient).IsEqualTo(before.Ambient);
        await Assert.That(after.DomeCount).IsEqualTo(before.DomeCount);
        await Assert.That(after.LinkLightCount).IsEqualTo(before.LinkLightCount);
        await Assert.That(after.LinkDomeCount).IsEqualTo(before.LinkDomeCount);
        await Assert.That(after.LinkUnsupported).IsEqualTo(before.LinkUnsupported);
        await Assert.That(after.LinkRowCount).IsEqualTo(before.LinkRowCount);
        await Assert.That(after.Statistics).IsEqualTo(before.Statistics);
        await Assert.That(after.FrameResourceId).IsEqualTo(before.FrameResourceId);
        await Assert.That(after.FrameBytes.SequenceEqual(before.FrameBytes)).IsTrue();
        await Assert.That(after.Pixels.SequenceEqual(before.Pixels)).IsTrue();
        await AssertMeshesEqual(before.Meshes, after.Meshes, compareMasks: true);
    }

    private static async Task AssertActionableRequiredFailure(Exception? failure, SilkGraphicsBackend backend)
    {
        await Assert.That(failure is InvalidOperationException).IsTrue();
        string message = failure?.Message ?? string.Empty;
        await Assert.That(message.Contains(RequiredVariable, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(backend.ToString(), StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("page ABI 24/session ABI 6", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(PluginVariable, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("staged", StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure?.GetType().Name == "SkipTestException").IsFalse();
    }

    private static async Task AssertVector(byte[] bytes, int offset, Vector4 expected)
    {
        await Assert.That(ReadSingle(bytes, offset)).IsEqualTo(expected.X);
        await Assert.That(ReadSingle(bytes, offset + 4)).IsEqualTo(expected.Y);
        await Assert.That(ReadSingle(bytes, offset + 8)).IsEqualTo(expected.Z);
        await Assert.That(ReadSingle(bytes, offset + 12)).IsEqualTo(expected.W);
    }

    private static Masks Resolve(LinkEvidence links, MeshIdentity mesh)
    {
        LinkRow? exact = links.Rows.FirstOrDefault(row =>
            row.Path == mesh.Path && row.InstanceIndex == mesh.InstanceIndex);
        LinkRow? path = links.Rows.FirstOrDefault(row =>
            row.Path == mesh.Path && row.InstanceIndex == SilkLightLinkCommand.AllInstances);
        return exact?.Masks ?? path?.Masks ?? new Masks(UInt128.MaxValue, UInt128.MaxValue, 0xff);
    }

    private static UInt128 ActiveBits(int count) =>
        count == Capacity ? UInt128.MaxValue : (UInt128.One << count) - UInt128.One;
    private static int IndexOf(FrameEvidence frame, int tag) =>
        Array.FindIndex(frame.Lights, light => Tag(light) == tag);
    private static int Tag(SilkFrameLight light)
    {
        float value = light.Transform.M41;
        int tag = checked((int)value);
        return value == tag ? tag : throw new InvalidDataException("A native light translation tag changed.");
    }
    private static float Exposed(SilkFrameLight light) => light.Intensity * MathF.Pow(2, light.Exposure);
    private static string LightName(int tag) => "L" + tag.ToString("D4", CultureInfo.InvariantCulture);
    private static string LightPath(int tag) => "/World/Lights/" + LightName(tag);
    private static string LightOrder(FrameEvidence frame) =>
        string.Join(", ", frame.Lights.Select((light, index) => $"{index}:{Tag(light)}"));
    private static bool IsLeft(MeshIdentity mesh) => mesh.Transform[12] < 0;
    private static string DescribeContext(MeshIdentity mesh) =>
        string.Join(" > ", mesh.Context.Select(entry => $"{entry.InstancerPath}[{entry.InstanceIndex}]"));
    private static string Describe(MeshIdentity mesh) =>
        $"{mesh.Path}[{mesh.InstanceIndex}], instancer={mesh.InstancerPath}, " +
        $"context={DescribeContext(mesh)}, x={mesh.Transform[12]}";
    private static float ReadSingle(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, sizeof(float)));
    private static byte Channel(byte[] pixels, int x, int y, int component) =>
        pixels[((y * Size + x) * 4) + component];
    private static string Pixel(byte[] pixels, int x, int y) =>
        $"({x},{y}) rgba({Channel(pixels, x, y, 0)},{Channel(pixels, x, y, 1)}," +
        $"{Channel(pixels, x, y, 2)},{Channel(pixels, x, y, 3)})";
    private static bool IsClear(byte[] pixels, int x, int y) =>
        Channel(pixels, x, y, 0) == 0 &&
        Channel(pixels, x, y, 1) == ClearGreen &&
        Channel(pixels, x, y, 2) == 0;
    private static bool[] Coverage(byte[] pixels)
    {
        var coverage = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                coverage[y * Size + x] = !IsClear(pixels, x, y);
            }
        }
        return coverage;
    }
    private static IEnumerable<(int X, int Y)> InteriorSamples()
    {
        foreach (int x in new[] { 10, 16, 22, 42, 48, 54 })
        {
            foreach (int y in new[] { 24, 32, 40 })
            {
                yield return (x, y);
            }
        }
    }

    private enum Scenario
    {
        Count, Filter, Controls, Dark, Overflow, FirstOverflow,
        HighMesh, HighInstance, LinkRemoval, Reindex
    }

    private sealed record ExecutionProfile(
        SilkGraphicsBackend Backend, bool Required, bool RequireSwiftShader, bool D3D12Hardware = false);
    private sealed record DeviceEvidence(
        SilkGraphicsBackend Backend, string Name, string ApiVersion, bool IsSoftware, bool SupportsCompute);
    private sealed record ShapeSpec(
        uint Type, string Schema, string Attributes, float ShapeX, float ShapeY, float Radius);
    private sealed record ControlSpec(
        Vector3 Color, float Intensity, float Exposure, float Diffuse, float Specular, float Exposed);
    private sealed record FrameEvidence(
        uint LightCount, uint RawLightCount, uint Flags, uint Reserved544, uint Reserved548,
        uint DomeCount, int WireSize, SilkFrameLight[] Lights);
    private readonly record struct Masks(UInt128 Light, UInt128 Shadow, uint Dome);
    private sealed record LinkRow(string Path, int InstanceIndex, Masks Masks);
    private sealed record LinkEvidence(uint LightCount, uint DomeCount, uint Unsupported, LinkRow[] Rows);
    private sealed record MeshIdentity(
        string Path, int InstanceIndex, string InstancerPath, SilkInstancerContextEntry[] Context,
        double[] Transform, int PointCount, int IndexCount);
    private sealed record RetainedMesh(
        MeshIdentity Identity, Masks Masks, bool IndexedByObservedIdentity, int ResourceId,
        float[] Points, uint[] Indices, ulong TopologyRevision);
    private sealed record PageEvidence(
        uint Abi, ulong Revision, uint CommandCount, int FrameCommands, int LinkCommands,
        FrameEvidence? Frame, LinkEvidence? Links, MeshIdentity[] MeshUpserts,
        (string Path, int InstanceIndex)[] MeshRemoves);
    private readonly record struct Revisions(
        ulong Scene, ulong Frame, ulong Links, ulong Geometry, ulong Material, ulong Environment,
        ulong Deformation, ulong Shadows, ulong Gpu);
    private sealed record StateEvidence(
        uint LightCount, SilkFrameLight[] FrameLights, double[] View, double[] Projection,
        Vector4 Ambient, uint DomeCount, uint LinkLightCount, uint LinkDomeCount,
        uint LinkUnsupported, int LinkRowCount, RetainedMesh[] Meshes, Revisions Revisions,
        SilkSceneGpuStatistics Statistics, int FrameResourceId, byte[] FrameBytes, byte[] Pixels);
    private sealed record CaptureEvidence(
        PageEvidence Page, FrameEvidence Frame, LinkEvidence Links, StateEvidence State,
        bool Synchronized, int DrawCount);
    private sealed record FailureEvidence(
        bool IsSilkException, int Status, string Message, bool PageDelivered,
        bool SynchronizedBefore, bool SynchronizedAfter, int ApplyCountBefore, int ApplyCountAfter,
        StateEvidence? UnappliedState, StateEvidence? RerenderedState, int RerenderDrawCount);
    private sealed record RunEvidence(
        ExecutionProfile Profile, DeviceEvidence Device, Dictionary<string, CaptureEvidence> Captures,
        FailureEvidence? Failure, bool InitiallySynchronized, bool FinallySynchronized,
        bool AuthoredInstancesVerified, bool ExtraAuthoredVerified,
        int HighTag, int SacrificialTag, bool SourceUnchanged);
}
