// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanCompositionPresentationTests
{
    private const string RequiredEnvironmentVariable =
        "OPENUSD_REQUIRE_VULKAN_PRESENTATION";

    [Test]
    public async Task CapabilityProbeAcceptsLinuxExternalObjectContract()
    {
        VulkanCompositionCapabilitySnapshot capabilities = CreateLinuxCapabilities();
        CompositionPresentationTarget target = CreateTarget(capabilities);

        CompositionPresenterProbeResult result =
            VulkanCompositionCompatibility.Probe(target, capabilities);

        await Assert.That(result.IsAvailable).IsTrue();
    }

    [Test]
    public async Task PrefersD3D11TextureBridgeWithoutExternalSemaphores()
    {
        VulkanCompositionCapabilitySnapshot capabilities = CreateLinuxCapabilities() with
        {
            D3D11ImageImportable = true,
            D3D11DirectRenderSupported = false,
            D3D11MissingExtensions = []
        };
        var target = new CompositionPresentationTarget(
            [
                VulkanCompositionContext.WindowsD3D11ImageHandleType,
                capabilities.ImageHandleType
            ],
            [],
            capabilities.DeviceLuid,
            []);

        CompositionPresenterProbeResult result =
            VulkanCompositionCompatibility.Probe(target, capabilities);

        await Assert.That(result.IsAvailable).IsTrue();
        await Assert.That(result.Status).Contains("GPU copy fallback");
        await Assert.That(
                VulkanCompositionCompatibility.SelectsD3D11Bridge(
                    target,
                    capabilities))
            .IsTrue();
    }

    [Test]
    public async Task D3D11TextureBridgeReportsMissingWin32Extension()
    {
        VulkanCompositionCapabilitySnapshot capabilities = CreateLinuxCapabilities() with
        {
            D3D11ImageImportable = true,
            D3D11MissingExtensions = ["VK_KHR_win32_keyed_mutex"]
        };
        var target = new CompositionPresentationTarget(
            [VulkanCompositionContext.WindowsD3D11ImageHandleType],
            [],
            capabilities.DeviceLuid,
            []);

        CompositionPresenterProbeResult result =
            VulkanCompositionCompatibility.Probe(target, capabilities);

        await Assert.That(result.IsAvailable).IsFalse();
        await Assert.That(result.Status).Contains("VK_KHR_win32_keyed_mutex");
    }

    [Test]
    public async Task SelectsMatchingAdapterAcrossMultiplePhysicalDevices()
    {
        VulkanCompositionCapabilitySnapshot first = CreateLinuxCapabilities() with
        {
            DeviceLuid = [1, 1, 1, 1, 1, 1, 1, 1],
            DeviceUuid = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]
        };
        VulkanCompositionCapabilitySnapshot second = CreateLinuxCapabilities() with
        {
            DeviceLuid = [2, 2, 2, 2, 2, 2, 2, 2],
            DeviceUuid = [2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2]
        };
        CompositionPresentationTarget target = CreateTarget(second);

        VulkanCompositionSelectionResult selection =
            VulkanCompositionDeviceSelection.Select(target, [first, second]);

        await Assert.That(selection.ProbeResult.IsAvailable).IsTrue();
        await Assert.That(selection.CandidateIndex).IsEqualTo(1);
    }

    [Test]
    public async Task RejectsInvalidLuidEvenWhenBytesMatch()
    {
        VulkanCompositionCapabilitySnapshot capabilities = CreateLinuxCapabilities() with
        {
            DeviceLuidValid = false
        };
        CompositionPresentationTarget target = CreateTarget(capabilities with
        {
            DeviceLuidValid = true
        });

        CompositionPresenterProbeResult result =
            VulkanCompositionCompatibility.Probe(target, capabilities);

        await Assert.That(result.IsAvailable).IsFalse();
        await Assert.That(result.Status).Contains("LUID");
    }

    [Test]
    public async Task RejectsMissingExtensionsVulkan10AndIncompleteQueues()
    {
        VulkanCompositionCapabilitySnapshot baseline = CreateLinuxCapabilities();
        CompositionPresentationTarget target = CreateTarget(baseline);
        CompositionPresenterProbeResult missing =
            VulkanCompositionCompatibility.Probe(
                target,
                baseline with
                {
                    MissingExtensions = ["VK_KHR_external_memory_fd"]
                });
        CompositionPresenterProbeResult version =
            VulkanCompositionCompatibility.Probe(
                target,
                baseline with
                {
                    ApiVersion = Vk.Version10
                });
        CompositionPresenterProbeResult queue =
            VulkanCompositionCompatibility.Probe(
                target,
                baseline with
                {
                    HasGraphicsComputeQueue = false
                });

        await Assert.That(missing.Status).Contains("VK_KHR_external_memory_fd");
        await Assert.That(version.Status).Contains("1.1");
        await Assert.That(queue.Status).Contains("graphics and compute");
    }

    [Test]
    public async Task Vulkan11UsesCoreExternalAndDedicatedDependencies()
    {
        VulkanCompositionCapabilitySnapshot capabilities = CreateLinuxCapabilities() with
        {
            ApiVersion = Vk.Version11,
            MissingExtensions = []
        };

        CompositionPresenterProbeResult result =
            VulkanCompositionCompatibility.Probe(
                CreateTarget(capabilities),
                capabilities);

        await Assert.That(result.IsAvailable).IsTrue();
    }

    [Test]
    public async Task SelectsOnlyCombinedGraphicsComputeQueue()
    {
        bool found = VulkanCompositionDeviceRules.TryFindGraphicsComputeQueue(
            [
                QueueFlags.GraphicsBit,
                QueueFlags.ComputeBit,
                QueueFlags.GraphicsBit | QueueFlags.ComputeBit
            ],
            out uint queueFamily);

        await Assert.That(found).IsTrue();
        await Assert.That(queueFamily).IsEqualTo(2u);
    }

    [Test]
    public async Task ImageBarriersKeepQueueFamilyIgnored()
    {
        var range = new ImageSubresourceRange(
            ImageAspectFlags.ColorBit,
            0,
            1,
            0,
            1);
        ImageMemoryBarrier acquire = VulkanCompositionBarriers.CreateAcquire(
            default,
            range,
            firstUse: false);
        ImageMemoryBarrier release = VulkanCompositionBarriers.CreateRelease(
            default,
            range);

        await Assert.That(acquire.SrcQueueFamilyIndex)
            .IsEqualTo(Vk.QueueFamilyIgnored);
        await Assert.That(acquire.DstQueueFamilyIndex)
            .IsEqualTo(Vk.QueueFamilyIgnored);
        await Assert.That(release.SrcQueueFamilyIndex)
            .IsEqualTo(Vk.QueueFamilyIgnored);
        await Assert.That(release.DstQueueFamilyIndex)
            .IsEqualTo(Vk.QueueFamilyIgnored);
        await Assert.That(acquire.OldLayout)
            .IsEqualTo(ImageLayout.TransferSrcOptimal);
        await Assert.That(release.NewLayout)
            .IsEqualTo(ImageLayout.TransferSrcOptimal);
    }

    [Test]
    public async Task HandleLeasesCommitAndRollbackIndependently()
    {
        var committedHandle = new CountingSafeHandle(123);
        var rolledBackHandle = new CountingSafeHandle(124);
        var borrowedHandle = new CountingSafeHandle(125);
        var committed = new VulkanExternalHandleLease(
            committedHandle,
            VulkanCompositionContext.LinuxImageHandleType,
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        var rolledBack = new VulkanExternalHandleLease(
            rolledBackHandle,
            VulkanCompositionContext.LinuxImageHandleType,
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);
        ICompositionExternalHandleLease borrowed = new VulkanExternalHandleLease(
            borrowedHandle,
            VulkanCompositionContext.WindowsImageHandleType,
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted);

        await Assert.That(borrowed.IsInvalid).IsFalse();
        committed.CommitTransfer();
        borrowed.CommitTransfer();
        await committed.DisposeAsync();
        await rolledBack.DisposeAsync();
        await borrowed.DisposeAsync();

        await Assert.That(committedHandle.ReleaseCount).IsEqualTo(0);
        await Assert.That(rolledBackHandle.ReleaseCount).IsEqualTo(1);
        await Assert.That(borrowedHandle.ReleaseCount).IsEqualTo(1);
        await Assert.That(borrowed.IsInvalid).IsTrue();
    }

    [Test]
    public async Task FileDescriptorZeroIsValidAndClosesExactlyOnce()
    {
        var fileDescriptor = new CountingSafeHandle(0);
        ICompositionExternalHandleLease lease = new VulkanExternalHandleLease(
            fileDescriptor,
            VulkanCompositionContext.LinuxImageHandleType,
            CompositionExternalHandleOwnership.TransferOnSuccessfulImport);

        await Assert.That(lease.Handle).IsEqualTo(0);
        await Assert.That(lease.IsInvalid).IsFalse();
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        await Assert.That(fileDescriptor.IsInvalid).IsFalse();
        await Assert.That(fileDescriptor.ReleaseCount).IsEqualTo(1);
        await Assert.That(lease.IsInvalid).IsTrue();
    }

    [Test]
    public async Task RequiredModeRejectsUnsupportedPresentation()
    {
        CompositionPresenterProbeResult unavailable =
            CompositionPresenterProbeResult.Unavailable("fake unsupported target");

        Exception? failure = null;
        try
        {
            _ = VulkanCompositionViewportPresenter.EnforceRequired(
                unavailable,
                required: true);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("fake unsupported target");
    }

    [Test]
    public async Task MeshCallbackRemainsLazyWhenCompositionIsIncompatible()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        int callbacks = 0;
        await using VulkanCompositionViewportPresenter presenter =
            VulkanCompositionViewportPresenter.Create(
                _ =>
                {
                    callbacks++;
                    return default;
                });
        var target = new CompositionPresentationTarget([], [], null, null);

        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(target);
        VulkanCompositionPresenterDiagnostics diagnostics = presenter.GetDiagnostics();

        await Assert.That(probe.IsAvailable).IsFalse();
        await Assert.That(callbacks).IsEqualTo(0);
        await Assert.That(diagnostics.ActiveGenerations).IsEqualTo(0);
        await Assert.That(diagnostics.ActiveFrames).IsEqualTo(0);
        await Assert.That(diagnostics.RenderCallbacks).IsEqualTo(0);
    }

    [Test]
    public async Task EveryRenderCallbackFrameUsesSampledDepth()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        int callbacks = 0;
        await using VulkanCompositionViewportPresenter presenter =
            VulkanCompositionViewportPresenter.Create(
                context =>
                {
                    SilkTextureUsage required =
                        SilkTextureUsage.DepthRenderTarget |
                        SilkTextureUsage.Sampled;
                    if ((context.DepthTarget.Usage & required) != required)
                    {
                        throw new InvalidOperationException(
                            "Vulkan composition callback depth is not sampled.");
                    }
                    callbacks++;
                    return context.Renderer.Render(
                        context.ColorTarget,
                        context.DepthTarget);
                });
        CompositionPresentationTarget target;
        if (OperatingSystem.IsWindows())
        {
            target = new CompositionPresentationTarget(
                [VulkanCompositionContext.WindowsD3D11ImageHandleType],
                [],
                VulkanD3D11Bridge.GetDefaultAdapterLuid(),
                []);
        }
        else
        {
            target = new CompositionPresentationTarget(
                [VulkanCompositionContext.LinuxImageHandleType],
                [VulkanCompositionContext.LinuxSemaphoreHandleType],
                deviceLuid: null,
                deviceUuid: null);
        }

        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(target);
        if (!probe.IsAvailable)
        {
            Skip.Test(probe.Status);
        }
        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(
                new ViewportDimensions(16, 12),
                frameCount: 2);
        foreach (ICompositionPresentationFrame frame in generation.Frames)
        {
            CompositionFrameRenderResult result =
                await presenter.RenderAsync(frame);
            await Assert.That(result.Status)
                .IsEqualTo(CompositionFrameRenderStatus.Presented);
            ((VulkanCompositionFrame)frame)
                .CompleteCompositorRoundTripForTesting();
        }

        await Assert.That(callbacks).IsEqualTo(2);
    }

    [Test]
    public async Task SwiftShaderExportsAndReusesCompositionRingWhenSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows SwiftShader external-handle validation runs on Windows.");
        }

        bool required = IsRequiredMode();
        VulkanCompositionViewportPresenter presenter;
        try
        {
            presenter = VulkanCompositionViewportPresenter.Create(required);
        }
        catch (Exception exception) when (
            !required && IsPresentationUnavailable(exception))
        {
            Skip.Test($"Vulkan presentation is unavailable: {exception.Message}");
            throw;
        }

        await using (presenter)
        {
            await Assert.That(presenter.IsDeviceCreatedForTesting).IsFalse();
            if (!required)
            {
                var mismatchedTarget = new CompositionPresentationTarget(
                    [VulkanCompositionContext.WindowsImageHandleType],
                    [VulkanCompositionContext.WindowsSemaphoreHandleType],
                    deviceLuid: null,
                    deviceUuid: Enumerable.Repeat(byte.MaxValue, checked((int)Vk.UuidSize))
                        .ToArray());
                CompositionPresenterProbeResult mismatch =
                    await presenter.ProbeAsync(mismatchedTarget);
                await Assert.That(mismatch.IsAvailable).IsFalse();
                await Assert.That(presenter.IsDeviceCreatedForTesting).IsFalse();
            }

            var target = new CompositionPresentationTarget(
                [VulkanCompositionContext.WindowsImageHandleType],
                [VulkanCompositionContext.WindowsSemaphoreHandleType],
                deviceLuid: null,
                deviceUuid: null);
            CompositionPresenterProbeResult probe = await presenter.ProbeAsync(target);
            if (!probe.IsAvailable)
            {
                Skip.Test(probe.Status);
            }
            await Assert.That(presenter.IsDeviceCreatedForTesting).IsTrue();

            await using ICompositionPresentationGeneration generation =
                await presenter.CreateGenerationAsync(new ViewportDimensions(32, 24), 3);
            var frame = (VulkanCompositionFrame)generation.Frames[0];
            await using ICompositionExternalHandleLease image =
                await frame.LeaseImageHandleAsync();
            await using ICompositionExternalHandleLease semaphore =
                await frame.LeaseSemaphoreHandleAsync(frame.Semaphores[0].ResourceId);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                CompositionFrameRenderResult cycleResult =
                    await presenter.RenderAsync(frame);
                await Assert.That(cycleResult.Status)
                    .IsEqualTo(CompositionFrameRenderStatus.Presented);
                frame.CompleteCompositorRoundTripForTesting();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            CompositionFrameRenderResult warmed = await presenter.RenderAsync(frame);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            frame.CompleteCompositorRoundTripForTesting();

            await Assert.That(generation.Frames).Count().IsEqualTo(3);
            await Assert.That(image.Handle).IsNotEqualTo(0);
            await Assert.That(semaphore.Handle).IsNotEqualTo(0);
            await Assert.That(image.Ownership)
                .IsEqualTo(CompositionExternalHandleOwnership.BorrowedUntilImportCompleted);
            await Assert.That(warmed.Status)
                .IsEqualTo(CompositionFrameRenderStatus.Presented);
            await Assert.That(warmed.Synchronization.Kind)
                .IsEqualTo(CompositionFrameSynchronizationKind.Semaphores);
            await Assert.That(allocated).IsEqualTo(0);
            VulkanCompositionPresenterDiagnostics live = presenter.GetDiagnostics();
            await Assert.That(live.ActiveGenerations).IsEqualTo(1);
            await Assert.That(live.ActiveFrames).IsEqualTo(3);
        }
    }

    [Test]
    public async Task D3D11BridgeImportsPixelsAndReusesKeyedMutex()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The D3D11 Vulkan bridge is available only on Windows.");
            return;
        }

        bool required = IsRequiredMode();
        byte[] luid = VulkanD3D11Bridge.GetDefaultAdapterLuid();
        await using VulkanCompositionViewportPresenter presenter =
            VulkanCompositionViewportPresenter.Create(required);
        var target = new CompositionPresentationTarget(
            [VulkanCompositionContext.WindowsD3D11ImageHandleType],
            [],
            luid,
            []);
        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(target);
        if (!probe.IsAvailable)
        {
            Skip.Test(probe.Status);
        }

        await using (ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(32, 24), 3))
        {
            var frame = (VulkanCompositionFrame)generation.Frames[0];
            await using ICompositionExternalHandleLease image =
                await frame.LeaseImageHandleAsync();
            await Assert.That(image.Handle).IsNotEqualTo(0);
            await Assert.That(frame.Image.HandleType)
                .IsEqualTo(VulkanCompositionContext.WindowsD3D11ImageHandleType);
            await Assert.That(frame.Semaphores).IsEmpty();

            byte[] pixels = new byte[32 * 24 * 4];
            CompositionFrameRenderResult first = await presenter.RenderAsync(frame);
            frame.ReadbackD3D11SharedTextureForTesting(pixels);
            await Assert.That(first.Status)
                .IsEqualTo(CompositionFrameRenderStatus.Presented);
            await Assert.That(pixels[0]).IsBetween((byte)14, (byte)18);
            await Assert.That(pixels[1]).IsBetween((byte)30, (byte)34);
            await Assert.That(pixels[2]).IsBetween((byte)62, (byte)66);
            await Assert.That(pixels[3]).IsEqualTo(byte.MaxValue);

            CompositionFrameRenderResult second = await presenter.RenderAsync(frame);
            frame.ReadbackD3D11SharedTextureForTesting(pixels);
            await Assert.That(second.Status)
                .IsEqualTo(CompositionFrameRenderStatus.Presented);
        }

        VulkanCompositionPresenterDiagnostics diagnostics = presenter.GetDiagnostics();
        await Assert.That(diagnostics.ImageHandleType)
            .IsEqualTo(VulkanCompositionContext.WindowsD3D11ImageHandleType);
        await Assert.That(diagnostics.PresentationPath).IsEqualTo("D3D11GpuCopy");
        await Assert.That(diagnostics.ActiveGenerations).IsEqualTo(0);
        await Assert.That(diagnostics.ActiveFrames).IsEqualTo(0);
        await Assert.That(diagnostics.RingReuseFrames).IsEqualTo(1);
    }

    private static CompositionPresentationTarget CreateTarget(
        VulkanCompositionCapabilitySnapshot capabilities) =>
        new(
            [capabilities.ImageHandleType],
            [capabilities.SemaphoreHandleType],
            capabilities.DeviceLuid,
            capabilities.DeviceUuid);

    private static VulkanCompositionCapabilitySnapshot CreateLinuxCapabilities() =>
        new()
        {
            ApiVersion = Vk.Version11,
            ImageHandleType = VulkanCompositionContext.LinuxImageHandleType,
            SemaphoreHandleType = VulkanCompositionContext.LinuxSemaphoreHandleType,
            MissingExtensions = [],
            ImageExportable = true,
            SemaphoreExportable = true,
            HasGraphicsComputeQueue = true,
            QueueFamilyIndex = 0,
            DeviceLuidValid = true,
            DeviceLuid = [1, 2, 3, 4, 5, 6, 7, 8],
            DeviceUuid = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]
        };

    private static bool IsRequiredMode() =>
        string.Equals(
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    private static bool IsPresentationUnavailable(Exception exception) =>
        exception is PlatformNotSupportedException or
            DllNotFoundException or
            InvalidOperationException;

    private sealed class CountingSafeHandle : SafeHandle
    {
        internal CountingSafeHandle(nint value)
            : base(invalidHandleValue: -1, ownsHandle: true)
        {
            SetHandle(value);
        }

        internal int ReleaseCount { get; private set; }

        public override bool IsInvalid => handle == -1;

        protected override bool ReleaseHandle()
        {
            ReleaseCount++;
            return true;
        }
    }
}
