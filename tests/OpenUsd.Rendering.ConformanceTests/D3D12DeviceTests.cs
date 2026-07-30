// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class D3D12DeviceTests
{
    [Test]
    public async Task CompositionProbeRequiresNtHandleAndMatchingAdapter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        byte[] luid = [.. presenter.RendererAdapterLuid];

        CompositionPresenterProbeResult missingHandle = await presenter.ProbeAsync(
            new CompositionPresentationTarget([], [], luid, null));
        await Assert.That(missingHandle.IsAvailable).IsFalse();
        await Assert.That(missingHandle.Status)
            .Contains(D3D12CompositionViewportPresenter.D3D11TextureNtHandle);

        byte[] mismatch = luid.ToArray();
        mismatch[0] ^= byte.MaxValue;
        CompositionPresenterProbeResult mismatchedAdapter = await presenter.ProbeAsync(
            CreateCompositionTarget(mismatch));
        await Assert.That(mismatchedAdapter.IsAvailable).IsFalse();
        await Assert.That(mismatchedAdapter.Status).Contains("does not match renderer");

        CompositionPresenterProbeResult compatible = await presenter.ProbeAsync(
            CreateCompositionTarget(luid));
        await Assert.That(compatible.IsAvailable).IsTrue();
        await Assert.That(compatible.Status).Contains("keyed mutex");
    }

    [Test]
    public async Task CompositionRingExportsDedicatedHandlesAndProgressesKeys()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        await Assert.That(probe.IsAvailable).IsTrue();

        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(8, 6), 3);
        await Assert.That(generation.Frames.Count).IsEqualTo(3);
        await Assert.That(generation.Frames.Select(frame => frame.AllocationId).Distinct().Count())
            .IsEqualTo(3);

        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        await using ICompositionExternalHandleLease first =
            await frame.LeaseImageHandleAsync();
        await using ICompositionExternalHandleLease second =
            await frame.LeaseImageHandleAsync();
        await Assert.That(first.Handle).IsNotEqualTo(0);
        await Assert.That(second.Handle).IsNotEqualTo(0);
        await Assert.That(first.Handle).IsNotEqualTo(second.Handle);
        await Assert.That(first.IsInvalid).IsFalse();
        await Assert.That(first.ValidityPolicy)
            .IsEqualTo(CompositionExternalHandleValidityPolicy.NonZero);
        await Assert.That(first.Ownership)
            .IsEqualTo(CompositionExternalHandleOwnership.BorrowedUntilImportCompleted);
        await Assert.That(frame.CanOpenLeaseHandleForTesting(first.Handle)).IsTrue();
        await Assert.That(frame.CanOpenLeaseHandleForTesting(second.Handle)).IsTrue();

        CompositionFrameRenderResult firstRender = await presenter.RenderAsync(frame);
        await Assert.That(firstRender.Status)
            .IsEqualTo(CompositionFrameRenderStatus.Presented);
        await Assert.That(firstRender.Synchronization.Kind)
            .IsEqualTo(CompositionFrameSynchronizationKind.KeyedMutex);
        await Assert.That(firstRender.Synchronization.WaitValue).IsEqualTo(1UL);
        await Assert.That(firstRender.Synchronization.SignalValue).IsEqualTo(0UL);
        frame.SimulateConsumerReleaseForTesting(1, 0);

        CompositionFrameRenderResult secondRender = await presenter.RenderAsync(frame);
        await Assert.That(secondRender.Synchronization.WaitValue).IsEqualTo(1UL);
        await Assert.That(secondRender.Synchronization.SignalValue).IsEqualTo(0UL);
        frame.SimulateConsumerReleaseForTesting(1, 0);
    }

    [Test]
    public async Task CompositionGpuCopyPreservesDeterministicRhiContents()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        await using ICompositionExternalHandleLease lease =
            await frame.LeaseImageHandleAsync();

        _ = await presenter.RenderAsync(frame);
        byte[] pixels = new byte[4 * 3 * 4];
        frame.ReadbackSharedDestinationForTesting(lease.Handle, 1, 0, pixels);
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            await Assert.That(pixels[offset]).IsEqualTo((byte)32);
            await Assert.That(pixels[offset + 1]).IsEqualTo((byte)64);
            await Assert.That(pixels[offset + 2]).IsEqualTo((byte)191);
            await Assert.That(pixels[offset + 3]).IsEqualTo(byte.MaxValue);
        }
    }

    [Test]
    public async Task CompositionSilkRendererReplacesDefaultFrameAndReportsEvidence()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        var renderer = new TestPresentationRenderer(device);
        await using var presenter = new D3D12CompositionViewportPresenter(device, renderer);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];

        _ = await presenter.RenderAsync(frame);
        byte[] pixels = new byte[4 * 3 * 4];
        frame.ReadbackSourceForTesting(pixels);
        await Assert.That(pixels[0]).IsEqualTo((byte)128);
        await Assert.That(pixels[1]).IsEqualTo((byte)32);
        await Assert.That(pixels[2]).IsEqualTo((byte)64);
        await Assert.That(pixels[3]).IsEqualTo(byte.MaxValue);
        frame.SimulateConsumerReleaseForTesting(1, 0);

        _ = await presenter.RenderAsync(frame);
        frame.SimulateConsumerReleaseForTesting(1, 0);
        D3D12CompositionPresenterStatistics active = presenter.GetStatistics();
        await Assert.That(renderer.RenderCount).IsEqualTo(2);
        await Assert.That(renderer.ReceivedDepthTarget).IsTrue();
        await Assert.That(active.ProbeSucceeded).IsTrue();
        await Assert.That(active.ActiveGenerations).IsEqualTo(1);
        await Assert.That(active.ActiveFrames).IsEqualTo(2);
        await Assert.That(active.RenderedFrameCount).IsEqualTo(2);
        await Assert.That(active.SilkRenderedFrameCount).IsEqualTo(2);
        await Assert.That(active.KeyedMutexReuseCount).IsEqualTo(1);
        await Assert.That(active.LastSceneRevision).IsEqualTo(42UL);
        await Assert.That(active.LastDrawCount).IsEqualTo(7);

        await generation.DisposeAsync();
        D3D12CompositionPresenterStatistics disposed = presenter.GetStatistics();
        await Assert.That(disposed.ActiveGenerations).IsEqualTo(0);
        await Assert.That(disposed.ActiveFrames).IsEqualTo(0);
        await Assert.That(disposed.RetainedPresentationCopies).IsEqualTo(0);
    }

    [Test]
    public async Task CompositionResizeAndEarlyDisposalKeepImportLeaseIndependent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        ICompositionPresentationGeneration firstGeneration =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        ICompositionPresentationGeneration resizedGeneration =
            await presenter.CreateGenerationAsync(new ViewportDimensions(9, 7), 3);
        var firstFrame =
            (D3D12CompositionPresentationFrame)firstGeneration.Frames[0];
        ICompositionExternalHandleLease lease = await firstFrame.LeaseImageHandleAsync();
        nint leasedHandle = lease.Handle;

        await firstGeneration.DisposeAsync();
        await Assert.That(firstFrame.CanOpenLeaseHandleForTesting(leasedHandle)).IsTrue();
        await AssertFrameDisposed(firstFrame);
        await Assert.That(resizedGeneration.Size)
            .IsEqualTo(new ViewportDimensions(9, 7));

        await lease.DisposeAsync();
        await Assert.That(lease.Handle).IsEqualTo(0);
        await Assert.That(lease.IsInvalid).IsTrue();
        await Assert.That(firstFrame.CanOpenLeaseHandleForTesting(leasedHandle)).IsFalse();
        await resizedGeneration.DisposeAsync();
        await presenter.DisposeAsync();
        await firstGeneration.DisposeAsync();
        await resizedGeneration.DisposeAsync();
    }

    [Test]
    public async Task CompositionPresenterOwnsHeadlessGenerationLifecycle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(3, 2), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];

        await presenter.DisposeAsync();
        await AssertFrameDisposed(frame);
        await generation.DisposeAsync();
        await presenter.DisposeAsync();
    }

    [Test]
    public async Task CompositionSignalFailureDrainsExecutedCopyWithoutRetention()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        device.InjectPresentationCopyFailureForTesting(
            D3D12PresentationCopyFailure.SignalFailure);

        await AssertRenderThrows<D3D12PresentationSignalException>(presenter, frame);
        await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(0);

        CompositionFrameRenderResult recovered = await presenter.RenderAsync(frame);
        await Assert.That(recovered.Status)
            .IsEqualTo(CompositionFrameRenderStatus.Presented);
        frame.SimulateConsumerReleaseForTesting(1, 0);
    }

    [Test]
    public async Task CompositionDeviceRemovalSurfacesReasonAndCompletesTeardown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        device.InjectPresentationCopyFailureForTesting(
            D3D12PresentationCopyFailure.DeviceRemoved);

        CompositionFrameRenderResult result = await presenter.RenderAsync(frame);
        await Assert.That(result.Status)
            .IsEqualTo(CompositionFrameRenderStatus.DeviceLost);
        await Assert.That(result.ContinueRendering).IsFalse();
        await Assert.That(presenter.DeviceLossReason)
            .Contains("DXGI_ERROR_DEVICE_REMOVED");
        await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(0);
    }

    [Test]
    public async Task CompositionDeferredPostExecuteResourcesReleaseDuringDeviceTeardown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var presenter = new D3D12CompositionViewportPresenter(device);
        try
        {
            _ = await presenter.ProbeAsync(
                CreateCompositionTarget(presenter.RendererAdapterLuid));
            ICompositionPresentationGeneration generation =
                await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
            var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
            device.InjectPresentationCopyFailureForTesting(
                D3D12PresentationCopyFailure.DeferRecoveryForTesting);

            await AssertRenderThrows<D3D12PresentationSignalException>(presenter, frame);
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await presenter.DisposeAsync();
            await generation.DisposeAsync();
            device.Dispose();
        }
        finally
        {
            await presenter.DisposeAsync();
            device.Dispose();
        }
    }

    [Test]
    public async Task CompositionResizeAndDisposeCancelBlockedSlotReuse()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await using var presenter = new D3D12CompositionViewportPresenter(device);
        _ = await presenter.ProbeAsync(
            CreateCompositionTarget(presenter.RendererAdapterLuid));
        ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(4, 3), 2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        _ = await presenter.RenderAsync(frame);

        using var cancellation = new CancellationTokenSource();
        Task<CompositionFrameRenderResult> blockedRender = StartRenderAsync(
            presenter,
            frame,
            cancellation.Token);
        await WaitForProducerAcquireAsync(frame, TimeSpan.FromSeconds(5));

        await using ICompositionPresentationGeneration resized =
            await presenter.CreateGenerationAsync(new ViewportDimensions(9, 7), 3);
        Task disposal = Task.Run(async () => await generation.DisposeAsync());
        await Task.Delay(50);
        await Assert.That(disposal.IsCompleted).IsFalse();

        cancellation.Cancel();
        bool canceled = false;
        try
        {
            _ = await blockedRender;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(canceled).IsTrue();
        await Assert.That(resized.Size).IsEqualTo(new ViewportDimensions(9, 7));
    }

    [Test]
    public async Task ConfirmedDeviceRemovalReleasesEveryNativeTeardownStage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        device.AddRetainedRecordForTeardownTesting();
        var hook = new FakeTeardownHook(unchecked((int)0x887A0005));
        device.SetTeardownHookForTesting(hook);

        D3D12DeviceRemovalTeardownException exception =
            Assert.Throws<D3D12DeviceRemovalTeardownException>(device.Dispose);
        await Assert.That(exception.RemovalReason)
            .IsEqualTo(unchecked((int)0x887A0005));
        await Assert.That(exception.InnerException)
            .IsTypeOf<InvalidOperationException>();
        await Assert.That(hook.WaitIdleCount).IsEqualTo(1);
        await Assert.That(hook.RemovalReasonCount).IsEqualTo(1);
        await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
        await AssertReleaseCounts(device.TeardownReleaseCountsForTesting, retained: 1);

        device.Dispose();
        await AssertReleaseCounts(device.TeardownReleaseCountsForTesting, retained: 1);
    }

    [Test]
    public async Task NonRemovalWaitFailureLeavesDeviceRetryableAndUntouched()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        try
        {
            device.AddRetainedRecordForTeardownTesting();
            var hook = new FakeTeardownHook(unchecked((int)0x80004005));
            device.SetTeardownHookForTesting(hook);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(device.Dispose);
            await Assert.That(exception.Message).Contains("injected WaitIdle failure");
            await Assert.That(device.NativeObjectsReleasedForTesting).IsFalse();
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await AssertReleaseCounts(
                device.TeardownReleaseCountsForTesting,
                retained: 0,
                native: 0);

            device.SetTeardownHookForTesting(null);
            device.Dispose();
            await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
            await AssertReleaseCounts(device.TeardownReleaseCountsForTesting, retained: 1);
        }
        finally
        {
            device.Dispose();
        }
    }

    [Test]
    public async Task WarpCreatesQueueFenceAndBuffer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(
            4096,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        byte[] data = [1, 2, 3, 4];
        buffer.Write(data, 128);
        device.WaitIdle();

        await Assert.That(device.Backend).IsEqualTo(SilkGraphicsBackend.D3D12);
        await Assert.That(device.Capabilities.IsSoftware).IsTrue();
        await Assert.That(buffer.Size).IsEqualTo((nuint)4096);
    }

    [Test]
    public async Task WarpClearsAndReadsBackOffscreenTexture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.ClearReadbackAndDisposal(device);
    }

    [Test]
    public async Task WarpSubmissionLeasesTextureUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.SubmittedTextureSurvivesEarlyDispose(device);
    }

    [Test]
    public async Task WarpSubmitFailureReleasesTextureLeases()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.SubmitFailureReleasesAcquiredLeases(device);
    }

    [Test]
    public async Task WarpReadbackWaitsForPendingSubmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.ReadbackWaitsForPendingSubmission(device);
    }

    [Test]
    public async Task WarpClearsAndReadsBackDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.DepthClearReadbackAndLifetime(device);
    }

    [Test]
    public async Task WarpRejectsCrossDeviceDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice textureDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using D3D12SilkGraphicsDevice commandDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.CrossDeviceDepthTargetIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    public async Task WarpUploadsAndReadsBackSampledTextures()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.TextureUploadReadbackAndLifetime(device);
    }

    [Test]
    public async Task WarpRejectsCrossDeviceTextureUploads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice textureDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using D3D12SilkGraphicsDevice commandDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.CrossDeviceUploadIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    public async Task WarpCreatesAndDisposesSamplers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.SamplerCreationAndDisposal(device);
    }

    [Test]
    public async Task WarpDrawsCheckedIndexedTriangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.DrawsIndexedTriangle(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpDrawsIdenticallyThroughAMaterialBindingLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.MaterialBindingLayoutDrawsIdenticallyToSceneParameters(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpBindsMaterialTexturesAndSamplersToADraw()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.MaterialResourcesBindToADrawWithoutPerturbingIt(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpUsesDescriptorIndexedMaterialTextureTablesWhenAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        if (!device.Capabilities.SupportsDescriptorIndexedTextureTables)
        {
            Skip.Test("D3D12 WARP reported no descriptor-indexed texture table support.");
            return;
        }

        await OffscreenRhiConformance.MaterialResourcesBindToADrawWithoutPerturbingIt(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpRejectsMaterialResourcesTheLayoutDoesNotDeclare()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.MaterialBindingRejectsResourcesTheLayoutDoesNotDeclare(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpRendersRetainedSilkMeshes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkMeshRendererConformance.RendersRetainedMeshes(device);
    }

    [Test]
    public async Task WarpRejectsCrossDeviceSilkTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice rendererDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using D3D12SilkGraphicsDevice targetDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await SilkMeshRendererConformance.RejectsCrossDeviceTargets(
            rendererDevice,
            targetDevice);
    }

    [Test]
    public async Task WarpLeasesIndexedDrawResourcesUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.IndexedDrawSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpRejectsCrossDeviceGraphicsResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice resourceDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using D3D12SilkGraphicsDevice commandDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.RejectsCrossDeviceGraphicsResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpPreservesOrderedGraphicsCommands()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);

        await OffscreenRhiConformance.PreservesOrderedGraphicsCommands(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpDispatchesCheckedComputeKernels()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.DispatchesCheckedComputeKernels(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpLeasesComputeResourcesUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.ComputeSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpRejectsInvalidComputeResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice resourceDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using D3D12SilkGraphicsDevice commandDevice =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.RejectsInvalidComputeResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpInterleavesGraphicsAndComputeCommands()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.InterleavesGraphicsAndComputeCommands(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpComputeGraphicsBufferBarriers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.ComputeOutputFeedsVertexBuffer(
            device,
            SilkShaderBinaryFormat.Dxil);
        await OffscreenRhiConformance.ComputeOutputFeedsIndexBuffer(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [Test]
    public async Task WarpDispatchBoundariesAndOverflow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        await OffscreenRhiConformance.DispatchBoundariesAndOverflow(
            device,
            SilkShaderBinaryFormat.Dxil);
    }

    [SupportedOSPlatform("windows")]
    private static CompositionPresentationTarget CreateCompositionTarget(
        IEnumerable<byte> luid) =>
        new(
            [D3D12CompositionViewportPresenter.D3D11TextureNtHandle],
            [],
            [.. luid],
            null);

    [SupportedOSPlatform("windows")]
    private static async Task AssertFrameDisposed(
        D3D12CompositionPresentationFrame frame) =>
        await Assert.That(
            async () => await frame.LeaseImageHandleAsync())
            .Throws<ObjectDisposedException>();

    [SupportedOSPlatform("windows")]
    private static async Task AssertRenderThrows<TException>(
        D3D12CompositionViewportPresenter presenter,
        D3D12CompositionPresentationFrame frame)
        where TException : Exception =>
        await Assert.That(
            async () => await presenter.RenderAsync(frame))
            .Throws<TException>();

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected asynchronous state was not reached.");
            }
            await Task.Delay(10);
        }
    }

    [SupportedOSPlatform("windows")]
    private static Task<CompositionFrameRenderResult> StartRenderAsync(
        D3D12CompositionViewportPresenter presenter,
        D3D12CompositionPresentationFrame frame,
        CancellationToken cancellationToken) =>
        Task.Run(async () => await presenter.RenderAsync(frame, cancellationToken));

    [SupportedOSPlatform("windows")]
    private static Task WaitForProducerAcquireAsync(
        D3D12CompositionPresentationFrame frame,
        TimeSpan timeout) =>
        WaitUntilAsync(
            () => frame.ProducerAcquireWaitCountForTesting != 0,
            timeout);

    private static async Task AssertReleaseCounts(
        D3D12DeviceTeardownReleaseCounts counts,
        int retained,
        int native = 1)
    {
        await Assert.That(counts.RetainedRecords).IsEqualTo(retained);
        await Assert.That(counts.Fence).IsEqualTo(native);
        await Assert.That(counts.Queue).IsEqualTo(native);
        await Assert.That(counts.Device).IsEqualTo(native);
        await Assert.That(counts.Adapter).IsEqualTo(native);
        await Assert.That(counts.Factory).IsEqualTo(native);
        await Assert.That(counts.Api).IsEqualTo(native);
        await Assert.That(counts.Dxgi).IsEqualTo(native);
    }

    [SupportedOSPlatform("windows")]
    private sealed class FakeTeardownHook(int removalReason) : ID3D12DeviceTeardownHook
    {
        public int WaitIdleCount { get; private set; }

        public int RemovalReasonCount { get; private set; }

        public void WaitIdle()
        {
            WaitIdleCount++;
            throw new InvalidOperationException("injected WaitIdle failure");
        }

        public int GetDeviceRemovedReason()
        {
            RemovalReasonCount++;
            return removalReason;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class TestPresentationRenderer(D3D12SilkGraphicsDevice device)
        : ISilkPresentationRenderer
    {
        public int RenderCount { get; private set; }

        public bool ReceivedDepthTarget { get; private set; }

        public SilkPresentationRenderResult Render(
            ISilkGraphicsTexture colorTarget,
            ISilkGraphicsTexture depthTarget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderCount++;
            ReceivedDepthTarget =
                depthTarget.Format == SilkTextureFormat.D32Float &&
                depthTarget.Width == colorTarget.Width &&
                depthTarget.Height == colorTarget.Height;
            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            commands.ClearColor(colorTarget, new SilkColor(0.5f, 0.125f, 0.25f, 1));
            commands.ClearDepth(depthTarget, 1);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
            return new SilkPresentationRenderResult(42, 7);
        }
    }
}
