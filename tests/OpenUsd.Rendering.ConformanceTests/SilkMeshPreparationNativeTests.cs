// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkMeshPreparationNativeTests
{
    private const string Mesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "none"
            point3f[] points = [(0,0,0),(1,0,0),(1,1,0),(0,1,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [(0,0,1)] (interpolation = "constant")
            texCoord2f[] primvars:st = [(0,0),(1,0),(1,1),(0,1)] (interpolation = "faceVarying")
        }
        """;

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RejectedGpuPreparationReplaysRetirementsBeforeAnotherNativeSync(
        SilkGraphicsBackend backend, bool bufferBudget)
    {
        string geometry = Mesh + "\n" + Mesh.Replace("\"Mesh\"", "\"Removed\"", StringComparison.Ordinal);
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, source, new SilkPreparationLimits(1_000_000, 1_000_000));
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        if (bufferBudget)
        {
            budget.ConfigureDevice(device);
        }
        using var renderer = new SilkMeshRenderer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        using OpenUsdSilkPage initial = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(initial);
        byte[] before = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        using ISilkGraphicsBuffer? pressure = bufferBudget
            ? device.CreateBuffer(checked((nuint)(budget.MaximumBytes - budget.Usage.ReservedBytes - 1)),
                SilkBufferUsage.Upload) : null;
        ulong revision = renderer.Scene.Revision;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points",
            [new(0.5f, 0, 0), new(1.5f, 0, 0), new(1.5f, 1, 0), new(0.5f, 1, 0)]));
        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Removed"));
        using OpenUsdSilkPage update = session.Sync(32, 32, camera: camera);
        device.RefuseBufferAfter = bufferBudget ? null : 2;
        if (bufferBudget)
        {
            await Assert.That(() => renderer.ApplyPage(update)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(budget.MaximumBytes - 1);
        }
        else
        {
            await Assert.That(() => renderer.ApplyPage(update)).Throws<InvalidOperationException>();
        }
        device.RefuseBufferAfter = null;
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        byte[] afterRefusal = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(afterRefusal.AsSpan().SequenceEqual(before)).IsTrue();
        update.Dispose();
        device.RefuseBufferAfter = bufferBudget ? null : 2;
        if (bufferBudget)
        {
            await Assert.That(() => session.Sync(32, 32, camera: camera))
                .Throws<SilkGpuBufferBudgetExceededException>();
        }
        else
        {
            await Assert.That(() => session.Sync(32, 32, camera: camera)).Throws<InvalidOperationException>();
        }
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        device.RefuseBufferAfter = null;
        pressure?.Dispose();
        using OpenUsdSilkPage retry = session.Sync(32, 32, camera: camera);
        await Assert.That(retry.Revision).IsEqualTo(update.Revision + 1);
        await Assert.That(MeshValues(retry)).IsEqualTo(string.Empty);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(update.Revision);
        renderer.ApplyPage(retry);
        await Assert.That(renderer.Scene.MeshesByPath.ContainsKey(("/Removed", 0))).IsFalse();
        byte[] afterRetry = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(afterRetry.AsSpan().SequenceEqual(before)).IsFalse();
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var referenceCapturer = new SilkFrameCapturer(device);
        SilkFrameCaptureResult expected = referenceCapturer.Capture(
            reference, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(afterRetry.AsSpan().SequenceEqual(expected.Rgba.Span)).IsTrue();
        using OpenUsdSilkPage quiet = session.Sync(32, 32, camera: camera);
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    public async Task OutOfOrderRejectedPagesRefuseFurtherNativeDeltas()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage first = session.Sync(16, 16);
        using OpenUsdSilkPage second = session.Sync(16, 16);
        await Assert.That(() => first.RegisterReplay(static () => { })).Throws<InvalidOperationException>();
        InvalidOperationException? error = await Assert.That(() => session.Sync(16, 16))
            .Throws<InvalidOperationException>();
        await Assert.That(error!.Message).Contains("Recreate the session");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task DisposingARejectedPageConsumerCannotSilentlyDiscardItsNativeDelta(SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage page = session.Sync(16, 16);
        device.RefuseBufferAfter = 0;
        await Assert.That(() => renderer.ApplyPage(page)).Throws<InvalidOperationException>();
        renderer.Dispose();
        InvalidOperationException? failure = await Assert.That(() => session.Sync(16, 16))
            .Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("Recreate the session");
    }

    [Test]
    public async Task PageReplayRunsOutsideTheSessionLockAndStopsNativeProgressUntilSuccessful()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage first = session.Sync(16, 16);
        int attempts = 0;
        Action replay = () =>
        {
            if (!Task.Run(() => session.HasSynchronized).Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Page replay ran while the native-session lock was held.");
            }
            if (++attempts < 3)
            {
                throw new InvalidOperationException("Controlled page replay refusal.");
            }
        };
        first.RegisterReplay(replay);
        await Assert.That(() => session.Sync(16, 16)).Throws<InvalidOperationException>();
        await Assert.That(() => session.Sync(16, 16)).Throws<InvalidOperationException>();
        using OpenUsdSilkPage next = session.Sync(16, 16);
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(next.Revision).IsEqualTo(first.Revision + 1);
        using OpenUsdSilkPage quiet = session.Sync(16, 16);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SessionCeilingsBoundPathAndSharedStageLegacyAndExplicitCalls(bool limitPage, bool sharedStage)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        var limits = new SilkPreparationLimits(limitPage ? 1_000_000ul : 1ul, limitPage ? 1 : 1_000_000);
        using OpenUsdSilkSession session = sharedStage
            ? OpenUsdSilkRuntime.Create(fixture.Plugins, source, limits)
            : OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.Path, limits);
        await Assert.That(session.PreparationLimits).IsSameReferenceAs(limits);
        await Assert.That(() => session.Sync(16, 16)).Throws<OpenUsdSilkException>();
        await Assert.That(() => session.Sync(16, 16, new SilkSceneIngestionOptions(RenderPurpose.Default, "full")))
            .Throws<OpenUsdSilkException>();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1_000_000))).Throws<OpenUsdSilkException>();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task LegacySessionPagesKeepExactUsageAndTighterRequestsRestoreToTheSessionCeiling()
    {
        using Fixture fixture = Create(Mesh);
        var limits = new SilkPreparationLimits(1_000_000, 1_000_000);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.Path, limits);
        using OpenUsdSilkPage initial = session.Sync(16, 16);
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;
        string original = MeshValues(initial);
        await Assert.That(reserved).IsGreaterThan(0ul);
        await Assert.That(initial.MeshPreparationUsage.MaximumReservedBytes).IsEqualTo(1_000_000ul);
        await Assert.That(() => session.Sync(16, 16, Options(reserved - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage exact = session.Sync(16, 16, Options(reserved));
        await Assert.That(exact.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(reserved);
        await Assert.That(MeshValues(exact)).IsEqualTo(original);
        using OpenUsdSilkPage restored = session.Sync(16, 16);
        await Assert.That(restored.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(1_000_000ul);
        await Assert.That(MeshValues(restored)).IsEqualTo(original);
        await Assert.That(initial.MeshPreparationUsage.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(MeshValues(initial)).IsEqualTo(original);
        await Assert.That(() => session.Sync(16, 16, complexity: RenderComplexity.High))
            .Throws<NotSupportedException>();
        await Assert.That(() => session.Sync(16, 16, drawMode: RenderDrawMode.Wireframe))
            .Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConfiguredSessionCapturesLegacyPixelsAndRecoversWithoutRelaxingCeilings(
        SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, fixture.Path, new SilkPreparationLimits(4096, 100_000));
        using OpenUsdSilkSession reference = fixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var capturer = new SilkFrameCapturer(device);
        using var referenceCapturer = new SilkFrameCapturer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        SilkFrameCaptureResult expected = referenceCapturer.CaptureWithDepth(
            reference, 32, 32, RenderSettings.Default, camera: camera);
        SilkFrameCaptureResult initial = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(initial.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
        await Assert.That(() => capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, PageOptions(1), camera: camera)).Throws<OpenUsdSilkException>();
        SilkFrameCaptureResult recovered = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(recovered.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
        RenderDiagnostic admitted = recovered.Diagnostics.Entries.Single(
            static entry => entry.Code == "HDSILK_PREPARATION_ADMISSION");
        await Assert.That(admitted.Message).Contains("limit=4096");
        await Assert.That(admitted.Message).Contains("ceiling=100000");
        await Assert.That(admitted.Severity).IsEqualTo(RenderDiagnosticSeverity.Information);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task CaptureRefusalRecoversIdenticalPixelsOnTheSameRetainedRenderer(
        SilkGraphicsBackend backend, bool limitPage)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var capturer = new SilkFrameCapturer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        SilkFrameCaptureResult before = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, Options(4096), camera: camera);
        byte[] pixels = before.Rgba.ToArray();
        await Assert.That(() => capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, limitPage ? PageOptions(1) : Options(1), camera: camera))
            .Throws<OpenUsdSilkException>();
        SilkFrameCaptureResult after = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, Options(4096), camera: camera);
        await Assert.That(after.Rgba.Span.SequenceEqual(pixels)).IsTrue();
        await Assert.That(before.Rgba.Span.SequenceEqual(pixels)).IsTrue();
        int colors = Enumerable.Range(0, pixels.Length / 4)
            .Select(index => Convert.ToHexString(pixels.AsSpan(index * 4, 3))).Distinct().Count();
        await Assert.That(colors).IsGreaterThan(1);
    }

    [Test]
    public async Task CommandPageBoundaryRefusesTheWholeUpdateAndPreservesExactRetryValues()
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage first = session.Sync(16, 16, PageOptions(1_000_000));
        int length = first.ByteLength;
        string before = MeshValues(first);
        await Assert.That(length).IsGreaterThan(1);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [new(0, 0, 2), new(1, 0, 2), new(1, 1, 2), new(0, 1, 2)]));
        OpenUsdSilkException? refusal = null;
        try
        {
            using OpenUsdSilkPage rejected = session.Sync(16, 16, PageOptions(length - 1));
        }
        catch (OpenUsdSilkException exception)
        {
            refusal = exception;
        }
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Message).Contains("hdSilk command page refused");
        await Assert.That(MeshValues(first)).IsEqualTo(before);
        using OpenUsdSilkPage retry = session.Sync(16, 16, PageOptions(length));
        await Assert.That(retry.ByteLength).IsEqualTo(length);
        await Assert.That(MeshValues(retry)).IsNotEqualTo(before);
        using OpenUsdSilkSession referenceSession = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage reference = referenceSession.Sync(16, 16, Options(1_000_000));
        await Assert.That(MeshValues(retry)).IsEqualTo(MeshValues(reference));
        using OpenUsdSilkPage quiet = session.Sync(16, 16, PageOptions(length + 1));
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(quiet.ByteLength).IsLessThan(length);
        await Assert.That(MeshValues(retry)).IsEqualTo(MeshValues(reference));
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task EvenAnEmptySceneMustFitItsCompleteFrameCommand()
    {
        using Fixture fixture = Create(string.Empty);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage reference = session.Sync(16, 16, PageOptions(1_000_000));
        int length = reference.ByteLength;
        await Assert.That(() => session.Sync(16, 16, PageOptions(length - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage exact = session.Sync(16, 16, PageOptions(length));
        await Assert.That(exact.ByteLength).IsEqualTo(length);
        await Assert.That(exact.CommandCount).IsEqualTo(reference.CommandCount);
        await Assert.That(exact.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VersionOneAndLegacySyncDoNotInheritTheOptionalPageCeiling()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage versionOne = session.Sync(16, 16, Options(1_000_000));
        await Assert.That(MeshValues(versionOne).Length).IsGreaterThan(0);
        await Assert.That(versionOne.MeshPreparationUsage).IsNotNull();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage legacy = session.Sync(16, 16);
        await Assert.That(legacy.MeshPreparationUsage).IsNull();
        await Assert.That(MeshValues(legacy)).IsEqualTo(MeshValues(versionOne));
    }

    [Test]
    public async Task EmptyGeometryAndFullWidthLimitsKeepExactAccounting()
    {
        using Fixture empty = Create(string.Empty);
        using OpenUsdSilkSession emptySession = empty.Open();
        using OpenUsdSilkPage emptyPage = emptySession.Sync(16, 16, Options(1));
        await Assert.That(emptyPage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(emptyPage.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(0ul);
        using Fixture mesh = Create(Mesh);
        using OpenUsdSilkSession meshSession = mesh.Open();
        using OpenUsdSilkPage meshPage = meshSession.Sync(16, 16, Options(ulong.MaxValue));
        await Assert.That(meshPage.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(meshPage.MeshPreparationUsage.ReservedBytes).IsGreaterThan(0ul);
    }

    [Test]
    public async Task ExactReservationBoundaryRejectsOneByteLessBeforePublishing()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage legacy = session.Sync(16, 16,
            new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full"));
        using OpenUsdSilkPage first = session.Sync(16, 16, Options(1_000_000));
        SilkMeshPreparationUsage usage = first.MeshPreparationUsage ??
            throw new InvalidOperationException("The native request omitted its reservation accounting.");
        await Assert.That(usage.ReservedBytes).IsGreaterThan(0ul);
        await Assert.That(usage.PeakReservedBytes).IsEqualTo(usage.ReservedBytes);
        string original = MeshValues(first);
        await Assert.That(original).IsEqualTo(MeshValues(legacy));
        await Assert.That(() => session.Sync(16, 16, Options(usage.ReservedBytes - 1)))
            .Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(usage.ReservedBytes));
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(usage.ReservedBytes);
        await Assert.That(MeshValues(retry)).IsEqualTo(original);
        await Assert.That(MeshValues(first)).IsEqualTo(original);
        using OpenUsdSilkPage quiet = session.Sync(16, 16, Options(usage.ReservedBytes));
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(quiet.MeshPreparationUsage!.ReservedBytes).IsEqualTo(usage.ReservedBytes);
    }

    [Test]
    public async Task FailedGrowthPreservesTheOldPageAndReleasesReservationsForRetry()
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(4096));
        string original = MeshValues(initial);
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", Enumerable.Repeat(new UsdVec3f(2, 3, 4), 1024).ToArray()));
        await Assert.That(() => session.Sync(16, 16, Options(4096))).Throws<OpenUsdSilkException>();
        await Assert.That(MeshValues(initial)).IsEqualTo(original);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)]));
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(4096));
        await Assert.That(MeshValues(retry)).IsEqualTo(original);
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(retry.MeshPreparationUsage.PeakReservedBytes).IsLessThanOrEqualTo(4096ul);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task IndependentMeshesAggregateAndInstanceReferencesShareOneLease()
    {
        using Fixture single = Create(Mesh);
        using OpenUsdSilkSession one = single.Open();
        using OpenUsdSilkPage onePage = one.Sync(16, 16, Options(1_000_000));
        ulong cost = onePage.MeshPreparationUsage!.ReservedBytes;
        using Fixture pair = Create(Mesh + "\n" + Mesh.Replace("\"Mesh\"", "\"Other\"", StringComparison.Ordinal));
        using OpenUsdSilkSession two = pair.Open();
        await Assert.That(() => two.Sync(16, 16, Options(cost * 2 - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage twoPage = two.Sync(16, 16, Options(cost * 2));
        await Assert.That(twoPage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost * 2);

        using Fixture instances = Create("""
            def PointInstancer "Instances" {
                rel prototypes = </Instances/Mesh>
                int[] protoIndices = [0,0,0]
                point3f[] positions = [(0,0,0),(2,0,0),(4,0,0)]
            """ + "\n" + Mesh + "\n}");
        using OpenUsdSilkSession instanced = instances.Open();
        using OpenUsdSilkPage instancePage = instanced.Sync(16, 16, Options(cost));
        await Assert.That(instancePage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        int references = 0;
        using (SilkCommandEnumerator commands = instancePage.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert &&
                    commands.Current.AsMeshUpsert().PointCount == 0)
                {
                    references++;
                }
            }
        }
        await Assert.That(references).IsEqualTo(2);
    }

    [Test]
    public async Task LegacySyncRestoresItsUnboundedProfileAfterABudgetRefusal()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage legacy = session.Sync(16, 16);
        await Assert.That(legacy.MeshPreparationUsage).IsNull();
        await Assert.That(MeshValues(legacy).Length).IsGreaterThan(0);
        using OpenUsdSilkPage bounded = session.Sync(16, 16, Options(4096));
        await Assert.That(bounded.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
    }

    [Test]
    [Arguments("Points")]
    [Arguments("BasisCurves")]
    [Arguments("Volume")]
    [Arguments("SkelRoot")]
    public async Task UnaccountedGeometryProfilesAreRefusedInsteadOfOmitted(string type)
    {
        string geometry = type switch
        {
            "Points" => "def Points \"Other\" {\npoint3f[] points = [(0,0,0)]\n}",
            "BasisCurves" => """
                def BasisCurves "Other" {
                    point3f[] points = [(0,0,0),(1,0,0)]
                    int[] curveVertexCounts = [2]
                    uniform token type = "linear"
                }
                """,
            _ => $"def {type} \"Other\" {{}}"
        };
        using Fixture fixture = Create(Mesh + "\n" + geometry);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(4096))).Throws<OpenUsdSilkException>();
    }

    [Test]
    public async Task RefinementAndAlternateDrawModesCannotBypassThePreparationProfile()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(4096), complexity: RenderComplexity.Medium))
            .Throws<NotSupportedException>();
        await Assert.That(() => session.Sync(16, 16, Options(4096), drawMode: RenderDrawMode.Wireframe))
            .Throws<NotSupportedException>();
        using OpenUsdSilkPage accepted = session.Sync(16, 16, Options(4096));
        await Assert.That(accepted.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
    }

    private static SilkSceneIngestionOptions Options(ulong maximum) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", maximum);

    private static SilkSceneIngestionOptions PageOptions(int maximumPageBytes) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", 1_000_000, maximumPageBytes);

    private static string MeshValues(OpenUsdSilkPage page)
    {
        var values = new List<string>();
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MeshUpsert)
            {
                continue;
            }
            SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
            values.Add(mesh.Path);
            for (int point = 0; point < mesh.PointCount; point++)
            {
                for (int component = 0; component < 3; component++)
                {
                    values.Add(BitConverter.SingleToInt32Bits(mesh.GetPointComponent(point, component)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                values.Add(mesh.GetPointOrigin(point).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            for (int index = 0; index < mesh.IndexCount; index++)
            {
                values.Add(mesh.GetIndex(index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                values.Add(mesh.GetCornerEdge(index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                values.Add(mesh.GetTriangleSubprim(index / 3).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            for (int index = 0; index < mesh.AttributeCount; index++)
            {
                SilkMeshAttributeEntry attribute = mesh.GetAttribute(index);
                values.Add(attribute.Name);
                for (int element = 0; element < attribute.ElementCount; element++)
                {
                    for (int component = 0; component < attribute.ComponentCount; component++)
                    {
                        values.Add(BitConverter.SingleToInt32Bits(attribute.GetComponent(element, component))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
        }
        return string.Join(",", values);
    }

    private static Fixture Create(string geometry)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrEmpty(plugins))
        {
            if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
            {
                throw new InvalidOperationException(
                    "The native mesh preparation profile requires matched plugin inputs.");
            }
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a runtime with the mesh preparation extension.");
        }
        string root = Directory.CreateTempSubdirectory("silk-mesh-preparation-").FullName;
        string path = System.IO.Path.Combine(root, "scene.usda");
        File.WriteAllText(path, "#usda 1.0\n" + geometry);
        return new Fixture(root, path, plugins);
    }

    private sealed class Fixture(string root, string path, string plugins) : IDisposable
    {
        internal string Path { get; } = path;
        internal string Plugins { get; } = plugins;
        internal OpenUsdSilkSession Open() => OpenUsdSilkRuntime.Create(Plugins, Path);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
