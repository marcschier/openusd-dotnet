// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using OpenUsd.Geom;
using OpenUsd.Render;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;

namespace OpenUsd.Physics.Tests;

public sealed partial class UsdPhysicsTransportTests
{
    [Test]
    [Arguments("capacity")]
    [Arguments("time")]
    [Arguments("stage")]
    [Arguments("range")]
    [Arguments("loop")]
    [Arguments("pose")]
    [Arguments("orientation")]
    [Arguments("overflow")]
    [Arguments("source")]
    [Arguments("deformation-window")]
    [Arguments("deformation-values")]
    [Arguments("deformation-domain")]
    public async Task RecordingPreflightRefusesInvalidInputWithoutSimulationOrFiles(string mode)
    {
        string root = Directory.CreateTempSubdirectory("openusd-recording-preflight-").FullName;
        var world = new FakePhysicsWorld(1);
        bool deforming = mode.StartsWith("deformation-", StringComparison.Ordinal);
        world.DeformationCapacity = deforming ? 1 : 0;
        world.DeformationVertexCapacity = deforming ? 2 : 0;
        world.OnFrameCopied = frame =>
        {
            if (mode == "pose")
            {
                frame.BodyBuffer[0] = frame.BodyBuffer[0] with { Position = new UsdVec3d(double.NaN, 0, 0) };
            }
            if (mode == "orientation")
            {
                frame.BodyBuffer[0] = frame.BodyBuffer[0] with { Orientation = default };
            }
            frame.HasOverflow = mode == "overflow";
            if (deforming)
            {
                frame.DeformationBuffer[0] = new UsdPhysicsDeformation(
                    new UsdPhysicsObjectId(90, UsdPhysicsObjectKind.Deformable),
                    mode == "deformation-domain" ? (UsdPhysicsDeformationKind)999 : UsdPhysicsDeformationKind.Surface,
                    mode == "deformation-window" ? 1 : 0, 2, false);
                frame.DeformationVertexBuffer[0] =
                    new UsdVec3d(mode == "deformation-values" ? double.MaxValue : 0, 0, 0);
                frame.DeformationVertexBuffer[1] = new UsdVec3d(1, 0, 0);
                frame.SetDeformationCounts(1, 2, false);
            }
        };
        await using UsdPhysicsTransport transport = CreateTransport(
            world, new FakePhysicsClock(), CreateOptions(loop: mode == "loop"));
        await RunAsync(transport, transport.BuildAsync());
        PhysicsRenderBindingTable bindings = RecordingBindings(transport);
        RenderDiskJobRequest original = RecordingRequest(root, mode == "range" ? 61 : 2, transport);
        StageRenderState[] states = original.Frames.ToArray();
        if (mode == "time")
        {
            states[1] = states[1].WithTime(new StageTime(0.123));
        }
        if (mode == "stage")
        {
            states[1] = states[1].WithStage(new StageIdentity("different.usda"));
        }
        var request = new RenderDiskJobRequest(original.OutputDirectory, states);
        var source = new RecordingSource(request.OutputDirectory)
        {
            SourceChanged = mode == "source"
        };
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, bindings,
                new PhysicsRenderCapacities(mode == "capacity" ? 0 : 1, deforming ? 1 : 0, deforming ? 2 : 0),
                source, new PhysicsRenderRecordingContext("invalid", 8)));
            await PumpRecordingAsync(transport, job);
            await Assert.That(await CaptureAsync(transport, job)).IsNotNull();
            await Assert.That(world.Counter).IsEqualTo(0ul);
            await Assert.That(source.Positions.Count).IsEqualTo(0);
            await Assert.That(source.Clears).IsEqualTo(0);
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("capture")]
    [Arguments("ack-revision")]
    [Arguments("ack-count")]
    [Arguments("cancel")]
    [Arguments("reset")]
    [Arguments("source-revision")]
    [Arguments("binding-revision")]
    [Arguments("cleanup")]
    [Arguments("capture-and-cleanup")]
    public async Task InterruptedRecordingClearsOverridesAndNeverPublishesAPartialJob(string mode)
    {
        string root = Directory.CreateTempSubdirectory("openusd-recording-interrupted-").FullName;
        var world = new FakePhysicsWorld(1);
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        PhysicsRenderBindingTable bindings = RecordingBindings(transport);
        RenderDiskJobRequest request = RecordingRequest(root, 2, transport);
        var source = new RecordingSource(request.OutputDirectory);
        using var cancellation = new CancellationTokenSource();
        var captureFailure = new InvalidOperationException("capture failed");
        var cleanupFailure = new IOException("cleanup failed");
        source.ClearFailure = mode is "cleanup" or "capture-and-cleanup" ? cleanupFailure : null;
        source.OnCapture = frame =>
        {
            if (frame.Identity.StepIndex != 2)
            {
                return;
            }
            switch (mode)
            {
                case "capture":
                case "capture-and-cleanup":
                    throw captureFailure;
                case "ack-revision":
                    source.WrongRevision = true;
                    break;
                case "ack-count":
                    source.DropBody = true;
                    break;
                case "cancel":
                    cancellation.Cancel();
                    break;
                case "reset":
                    transport.ResetAsync().GetAwaiter().GetResult();
                    break;
                case "source-revision":
                    source.SourceChanged = true;
                    break;
                case "binding-revision":
                    bindings.Clear();
                    break;
            }
        };
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, bindings, new PhysicsRenderCapacities(1), source,
                new PhysicsRenderRecordingContext("interrupted", 8), cancellationToken: cancellation.Token));
            await PumpRecordingAsync(transport, job);
            Exception? failure = await CaptureAsync(transport, job);
            await Assert.That(failure).IsNotNull();
            await Assert.That(source.Clears).IsEqualTo(1);
            await Assert.That(source.PublishedDuringClear).IsFalse();
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
            if (mode == "capture-and-cleanup")
            {
                await Assert.That(failure).IsTypeOf<AggregateException>();
                var aggregate = (AggregateException)failure!;
                await Assert.That(aggregate.Flatten().InnerExceptions).Contains(captureFailure);
                await Assert.That(aggregate.Flatten().InnerExceptions).Contains(cleanupFailure);
            }
            if (mode == "cleanup")
            {
                await Assert.That(failure).IsSameReferenceAs(cleanupFailure);
            }
            if (mode == "cancel")
            {
                await Assert.That(failure is OperationCanceledException).IsTrue();
            }
            var recovered = new RecordingSource(request.OutputDirectory);
            RenderDiskJobRequest retry = RecordingRequest(root, 1, transport);
            PhysicsRenderBindingTable recoveredBindings = RecordingBindings(transport);
            Task<RenderDiskJobResult> retryJob = StartRecording(() => PhysicsRenderRecording.Execute(
                retry, transport, recoveredBindings, new PhysicsRenderCapacities(1), recovered,
                new PhysicsRenderRecordingContext("recovered", 8)));
            await PumpRecordingAsync(transport, retryJob);
            await Assert.That((await retryJob).Frames.Count).IsEqualTo(1);
            await Assert.That(recovered.Clears).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RecordingRefusesMissingBindingsBeforeAdvancingPhysics()
    {
        string root = Directory.CreateTempSubdirectory("openusd-recording-bindings-").FullName;
        var world = new FakePhysicsWorld(1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());
        RenderDiskJobRequest request = RecordingRequest(root, 1, transport);
        var source = new RecordingSource(request.OutputDirectory);
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, new PhysicsRenderBindingTable(1), new PhysicsRenderCapacities(1),
                source, new PhysicsRenderRecordingContext("unbound", 8)));
            await PumpRecordingAsync(transport, job);
            await Assert.That(async () => await job).Throws<InvalidDataException>();
            await Assert.That(world.Counter).IsEqualTo(0ul);
            await Assert.That(source.Positions.Count).IsEqualTo(0);
            await Assert.That(source.Clears).IsEqualTo(0);
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RecordingProjectsWholeDeformationsFromTheExactLeasedFrame()
    {
        string root = Directory.CreateTempSubdirectory("openusd-recording-deformation-").FullName;
        var world = new FakePhysicsWorld(1)
        {
            DeformationCapacity = 1,
            DeformationVertexCapacity = 2
        };
        world.OnFrameCopied = frame =>
        {
            frame.DeformationBuffer[0] = new UsdPhysicsDeformation(
                new UsdPhysicsObjectId(90, UsdPhysicsObjectKind.Deformable),
                UsdPhysicsDeformationKind.Surface, 0, 2, false);
            frame.DeformationVertexBuffer[0] = new UsdVec3d(0, world.Counter, 0);
            frame.DeformationVertexBuffer[1] = new UsdVec3d(1, world.Counter, 0);
            frame.SetDeformationCounts(1, 2, false);
        };
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());
        PhysicsRenderBindingTable bindings = RecordingBindings(transport);
        RenderDiskJobRequest request = RecordingRequest(root, 2, transport);
        var source = new RecordingSource(request.OutputDirectory);
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, bindings, new PhysicsRenderCapacities(1, 1, 2), source,
                new PhysicsRenderRecordingContext("cloth", 8)));
            await PumpRecordingAsync(transport, job);
            RenderDiskJobResult result = await job;
            float[] first = [0, 1, 0, 1, 1, 0];
            float[] second = [0, 2, 0, 1, 2, 0];
            await Assert.That(source.Deformations[0].SequenceEqual(first)).IsTrue();
            await Assert.That(source.Deformations[1].SequenceEqual(second)).IsTrue();
            await Assert.That(result.Frames[1].Simulation!.DeformationCount).IsEqualTo(1);
            await Assert.That(source.Clears).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RecordingStepsCapturesAndPublishesOnlyMatchingCompleteFrames()
    {
        string root = Directory.CreateTempSubdirectory("openusd-physics-recording-").FullName;
        var world = new FakePhysicsWorld(2);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());
        PhysicsRenderBindingTable bindings = RecordingBindings(transport);
        var states = new StageRenderState[3];
        for (int index = 0; index < states.Length; index++)
        {
            states[index] = StageRenderState.Create(new StageIdentity("recording.usda"))
                .WithTime(new StageTime(transport.Timeline.ToTimeCode((index + 1) * StepSeconds)))
                .WithViewport(new ViewportDimensions(1, 1));
        }
        var request = new RenderDiskJobRequest(Path.Combine(root, "recording"), states);
        var source = new RecordingSource(request.OutputDirectory);
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, bindings, new PhysicsRenderCapacities(2), source,
                new PhysicsRenderRecordingContext("falling-prop", 8, "review-v1")));
            await PumpRecordingAsync(transport, job);
            RenderDiskJobResult result = await job;
            await Assert.That(world.Counter).IsEqualTo(3ul);
            double[] expectedPositions = [1, 2, 3];
            await Assert.That(source.Positions.SequenceEqual(expectedPositions)).IsTrue();
            await Assert.That(source.Clears).IsEqualTo(1);
            await Assert.That(source.PublishedDuringClear).IsFalse();
            await Assert.That(result.Frames.Count).IsEqualTo(3);
            Guid epoch = result.Frames[0].Simulation!.Epoch;
            for (int index = 0; index < 3; index++)
            {
                RenderSimulationFrameIdentity identity = result.Frames[index].Simulation!;
                await Assert.That(identity.Epoch).IsEqualTo(epoch);
                await Assert.That(identity.StepIndex).IsEqualTo((ulong)index + 1);
                await Assert.That(identity.PublicationRevision).IsEqualTo((ulong)index + 2);
                await Assert.That(identity.BodyCount).IsEqualTo(2);
                await Assert.That(identity.SourceRevision).IsEqualTo(8ul);
                await Assert.That(identity.ScenarioIdentity).IsEqualTo("falling-prop");
                await Assert.That(identity.ReviewIdentity).IsEqualTo("review-v1");
            }
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            await Assert.That(manifest.RootElement.GetProperty("frames")[2]
                .GetProperty("simulation").GetProperty("stepIndex").GetUInt64()).IsEqualTo(3ul);
            Task<UsdPhysicsFrameLease> next = transport.StepAndAcquireFrameAsync(1);
            transport.Pump();
            using UsdPhysicsFrameLease frame = await next;
            await Assert.That(frame.Frame.StepIndex).IsEqualTo(4ul);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PhysicsRenderBindingTable RecordingBindings(UsdPhysicsTransport transport)
    {
        if (!transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease))
        {
            throw new InvalidOperationException("The built test world published no frame.");
        }
        using (lease)
        {
            var bindings = new PhysicsRenderBindingTable(lease.Frame.BodyCount + lease.Frame.DeformationCount);
            for (int index = 0; index < lease.Frame.BodyCount; index++)
            {
                _ = bindings.TryBind(new PhysicsRenderObjectId(lease.Frame.Bodies[index].Id.Value), $"/Body{index}");
            }
            for (int index = 0; index < lease.Frame.DeformationCount; index++)
            {
                _ = bindings.TryBind(new PhysicsRenderObjectId(lease.Frame.Deformations[index].Id.Value),
                    $"/Deform{index}");
            }
            return bindings;
        }
    }

    [Test]
    [Arguments("primId", "int", "the primId identity plane")]
    [Arguments("instanceId", "int", "the instanceId identity plane")]
    [Arguments("normal", "normal3f", "do not capture the normal plane")]
    public async Task RecordingRefusesIdentityPlanesWithoutSimulationOrFiles(
        string plane, string dataType, string refusal)
    {
        string root = Directory.CreateTempSubdirectory("openusd-recording-identity-").FullName;
        var world = new FakePhysicsWorld(1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), CreateOptions());
        await RunAsync(transport, transport.BuildAsync());
        PhysicsRenderBindingTable bindings = RecordingBindings(transport);
        var color = new UsdRenderVariableSpecification("/Render/Color", "half4", "color", "raw", []);
        var identity = new UsdRenderVariableSpecification("/Render/Identity", dataType, plane, "raw", []);
        var product = new RenderProductRequest(new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "identity.exr", "raster", "/Camera",
                1, 1, 1, "expandAperture", new UsdVec2f(20, 20), new UsdVec4f(0, 0, 1, 1),
                true, true, [0, 1], [])],
            [color, identity], ["default", "render"], ["full"], "", []), 0);
        var optics = new UsdGeomCameraState(
            UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, 10, 0);
        var frames = new RenderPreparedFrame[2];
        for (int index = 0; index < frames.Length; index++)
        {
            frames[index] = product.PrepareFrame(UsdMatrix4d.Identity, optics,
                transport.Timeline.ToTimeCode((transport.Status.StepIndex + (ulong)index + 1) * StepSeconds),
                RenderCameraFrameSettings.Default);
        }
        RenderDiskJobRequest request = new RenderProductJobPlan(
            new StageIdentity("recording.usda"), frames, RenderSettings.Default)
            .CreateJob(Path.Combine(root, "recording"));
        var source = new RecordingSource(request.OutputDirectory);
        try
        {
            Task<RenderDiskJobResult> job = StartRecording(() => PhysicsRenderRecording.Execute(
                request, transport, bindings, new PhysicsRenderCapacities(1, 0, 0),
                source, new PhysicsRenderRecordingContext("identity", 8)));
            await PumpRecordingAsync(transport, job);
            Exception? failure = await CaptureAsync(transport, job);
            await Assert.That(failure).IsTypeOf<NotSupportedException>();
            await Assert.That(failure!.Message).Contains(refusal);
            await Assert.That(world.Counter).IsEqualTo(0ul);
            await Assert.That(source.Positions.Count).IsEqualTo(0);
            await Assert.That(Directory.Exists(request.OutputDirectory)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RenderDiskJobRequest RecordingRequest(string root, int count, UsdPhysicsTransport transport)
    {
        var states = new StageRenderState[count];
        for (int index = 0; index < count; index++)
        {
            states[index] = StageRenderState.Create(new StageIdentity("recording.usda"))
                .WithTime(new StageTime(transport.Timeline.ToTimeCode(
                    (transport.Status.StepIndex + (ulong)index + 1) * StepSeconds)))
                .WithViewport(new ViewportDimensions(1, 1));
        }
        return new RenderDiskJobRequest(Path.Combine(root, "recording"), states);
    }

    // The recording blocks while the test pumps; neither may monopolize the thread pool its continuations need.
    private static Task<RenderDiskJobResult> StartRecording(Func<RenderDiskJobResult> execute) =>
        Task.Factory.StartNew(
            execute, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static async Task PumpRecordingAsync(UsdPhysicsTransport transport, Task job)
    {
        long deadline = Environment.TickCount64 + 10_000;
        while (!job.IsCompleted)
        {
            transport.Pump();
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("The deterministic recording did not complete.");
            }
            await Task.Yield();
        }
    }

    private sealed class RecordingSource(string destination) : IPhysicsRenderRecordingSource
    {
        internal List<double> Positions { get; } = [];
        internal List<float[]> Deformations { get; } = [];
        internal int Clears { get; private set; }
        internal bool PublishedDuringClear { get; private set; }
        internal Action<PhysicsRenderRecordingFrame>? OnCapture { get; set; }
        internal Exception? ClearFailure { get; set; }
        internal bool WrongRevision { get; set; }
        internal bool DropBody { get; set; }
        internal bool SourceChanged { get; set; }

        public void Validate(
            RenderDiskJobRequest request, PhysicsRenderRecordingContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SourceChanged)
            {
                throw new InvalidOperationException("The recording source was reloaded.");
            }
        }

        public PhysicsRenderCaptureResult Capture(
            StageRenderState state, PhysicsRenderRecordingFrame frame, CancellationToken cancellationToken)
        {
            Positions.Add(frame.Transforms.Items[0].Position.Y);
            Deformations.Add(frame.Deformations.Vertices.ToArray());
            OnCapture?.Invoke(frame);
            byte value = checked((byte)frame.Transforms.Items[0].Position.Y);
            return new PhysicsRenderCaptureResult(new RenderJobImage(
                1, 1, new byte[] { value, 0, 0, 255 }, Rgba8RowOrder.TopDown),
                WrongRevision ? 0 : frame.Identity.PublicationRevision,
                DropBody ? 0 : frame.Transforms.Count, frame.Deformations.Count);
        }

        public void ClearOverrides()
        {
            Clears++;
            PublishedDuringClear = Directory.Exists(destination);
            if (ClearFailure is { } failure)
            {
                throw failure;
            }
        }
    }
}
