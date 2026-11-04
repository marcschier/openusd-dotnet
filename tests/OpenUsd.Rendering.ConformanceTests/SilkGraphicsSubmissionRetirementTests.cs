// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGraphicsSubmissionRetirementTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12ObservedCompletionSurvivesLaterDeviceLoss(bool pick, bool poll)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer? readback = pick ? device.CreatePickReadbackBuffer() : null;
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        byte[] source = new byte[64];
        source[0] = 17;
        source[1] = 34;
        source[2] = 51;
        source[3] = 68;
        commands.UploadTexture(texture, source);
        if (readback is not null)
        {
            ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
                texture, new SilkTexturePixelCoordinate(0, 0), readback);
        }
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            device.FailFenceWaitsForTesting(() => new InvalidOperationException("Injected fence failure."));
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            device.FailFenceWaitsForTesting(null);
            if (poll)
            {
                await Assert.That(submission.IsCompleted).IsTrue();
            }
            else
            {
                submission.Wait();
            }
            if (readback is not null)
            {
                var pixel = new byte[SilkPickTokenEncoding.ByteSize];
                readback.ReadRgba8Pixel(pixel);
                await Assert.That(pixel).IsEquivalentTo(new byte[] { 17, 34, 51, 68 });
            }

            device.FailFenceWaitsForTesting(() => new InvalidOperationException("Injected device removal."));
            device.SetTeardownHookForTesting(new RemovedD3D12Device());
            await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            submission.Wait();
            await Assert.That(submission.IsCompleted).IsTrue();
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            device.SetTeardownHookForTesting(null);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12DeviceLossKeepsSubmissionInvalid(bool lossAtWait)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            device.FailFenceWaitsForTesting(() => new InvalidOperationException("Injected fence failure."));
            if (lossAtWait)
            {
                device.SetTeardownHookForTesting(new RemovedD3D12Device());
            }
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            if (lossAtWait)
            {
                device.FailFenceWaitsForTesting(null);
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
                await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
                device.FailFenceWaitsForTesting(() => new InvalidOperationException("Injected fence failure."));
            }

            device.SetTeardownHookForTesting(new RemovedD3D12Device());
            await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            device.SetTeardownHookForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12NonterminalWaitOrDisposeRetainsUploadsUntilRetry(bool dispose)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(784);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(784ul);

            var failure = new InvalidOperationException("Injected nonterminal fence wait failure.");
            device.FailFenceWaitsForTesting(() => failure);
            InvalidOperationException? observed = dispose
                ? await Assert.That(submission.Dispose).Throws<InvalidOperationException>()
                : await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(784ul);
            await Assert.That(staging.Usage.ReservationCount).IsEqualTo(1ul);

            device.FailFenceWaitsForTesting(null);
            submission.Wait();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12FailedWaitAndDisposeRemainOwnedUntilDeviceDrain(bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(784);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            var failure = new InvalidOperationException("Injected nonterminal fence wait failure.");
            device.FailFenceWaitsForTesting(() => failure);
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(submission.Dispose).Throws<InvalidOperationException>();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(784ul);

            await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(784ul);
            device.FailFenceWaitsForTesting(null);
            texture.Dispose();
            if (disposeDevice)
            {
                device.Dispose();
            }
            else
            {
                device.WaitIdle();
            }
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12FailedWaitRemainsOwnedUntilDeviceDrain(bool pick, bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(pick ? 1040u : 784u);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer? readback = pick ? device.CreatePickReadbackBuffer() : null;
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        byte[] source = new byte[64];
        source[0] = 17;
        source[1] = 34;
        source[2] = 51;
        source[3] = 68;
        commands.UploadTexture(texture, source);
        if (readback is not null)
        {
            ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
                texture, new SilkTexturePixelCoordinate(0, 0), readback);
        }
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            var failure = new InvalidOperationException("Injected nonterminal fence wait failure.");
            device.FailFenceWaitsForTesting(() => failure);
            InvalidOperationException? observed =
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(pick ? 1040ul : 784ul);

            device.FailFenceWaitsForTesting(null);
            if (disposeDevice)
            {
                texture.Dispose();
                readback?.Dispose();
                device.Dispose();
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            }
            else
            {
                device.WaitIdle();
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(pick ? 256ul : 0ul);
                submission.Wait();
                await Assert.That(submission.IsCompleted).IsTrue();
                if (readback is not null)
                {
                    var pixel = new byte[SilkPickTokenEncoding.ByteSize];
                    readback.ReadRgba8Pixel(pixel);
                    await Assert.That(pixel).IsEquivalentTo(new byte[] { 17, 34, 51, 68 });

                    byte[] retrySource = new byte[64];
                    retrySource[0] = 85;
                    using ISilkGraphicsCommandList retryCommands = device.CreateCommandList();
                    retryCommands.UploadTexture(texture, retrySource);
                    ((ISilkPickGraphicsCommandList)retryCommands).CopyRgba8Pixel(
                        texture, new SilkTexturePixelCoordinate(0, 0), readback);
                    using ISilkGraphicsSubmission retry = device.Submit(retryCommands);
                    submission.Dispose();
                    retry.Wait();
                    readback.ReadRgba8Pixel(pixel);
                    await Assert.That(pixel).IsEquivalentTo(new byte[] { 85, 0, 0, 0 });
                }
                submission.Dispose();
                await Assert.That(submission.Wait).Throws<ObjectDisposedException>();
            }
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12PickDeviceLossKeepsSubmissionAndReadbackInvalid(bool lossAtWait)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(1040);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer readback = device.CreatePickReadbackBuffer();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
            texture, new SilkTexturePixelCoordinate(0, 0), readback);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            device.FailFenceWaitsForTesting(() => new InvalidOperationException("Injected fence failure."));
            if (lossAtWait)
            {
                device.SetTeardownHookForTesting(new RemovedD3D12Device());
            }
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            var pixel = new byte[SilkPickTokenEncoding.ByteSize];
            if (lossAtWait)
            {
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
                await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
                await Assert.That(() => readback.ReadRgba8Pixel(pixel)).Throws<InvalidOperationException>();
            }

            device.SetTeardownHookForTesting(new RemovedD3D12Device());
            await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(256ul);
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
            await Assert.That(() => readback.ReadRgba8Pixel(pixel)).Throws<InvalidOperationException>();
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            device.SetTeardownHookForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task D3D12PickNonterminalWaitKeepsSlotAndUploadsUntilRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(1040);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer readback = device.CreatePickReadbackBuffer();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        byte[] source = new byte[64];
        source[0] = 17;
        source[1] = 34;
        source[2] = 51;
        source[3] = 68;
        commands.UploadTexture(texture, source);
        ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
            texture, new SilkTexturePixelCoordinate(0, 0), readback);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(1040ul);
            var failure = new InvalidOperationException("Injected nonterminal pick fence wait failure.");
            device.FailFenceWaitsForTesting(() => failure);

            InvalidOperationException? observed =
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(1040ul);
            var pixel = new byte[SilkPickTokenEncoding.ByteSize];
            await Assert.That(() => readback.ReadRgba8Pixel(pixel)).Throws<InvalidOperationException>();

            device.FailFenceWaitsForTesting(null);
            submission.Wait();
            readback.ReadRgba8Pixel(pixel);
            await Assert.That(pixel).IsEquivalentTo(new byte[] { 17, 34, 51, 68 });
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(256ul);
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12PickFailedDisposeRemainsOwnedUntilDeviceDrain(bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(1040);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer readback = device.CreatePickReadbackBuffer();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
            texture, new SilkTexturePixelCoordinate(0, 0), readback);
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            var failure = new InvalidOperationException("Injected nonterminal pick fence wait failure.");
            device.FailFenceWaitsForTesting(() => failure);
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            InvalidOperationException? observed =
                await Assert.That(submission.Dispose).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(1040ul);

            device.FailFenceWaitsForTesting(null);
            if (disposeDevice)
            {
                texture.Dispose();
                readback.Dispose();
                device.Dispose();
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            }
            else
            {
                device.WaitIdle();
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(256ul);
                var pixel = new byte[SilkPickTokenEncoding.ByteSize];
                readback.ReadRgba8Pixel(pixel);
            }
        }
        finally
        {
            device.FailFenceWaitsForTesting(null);
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12ReadbackFailureKeepsSourceAndStagingUntilIdle(
        bool texture, bool disposeDevice, bool signalFailure)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(784);
        var buffers = new SilkGpuBufferBudget(64);
        var textures = new SilkGpuTextureBudget(64);
        staging.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        using ISilkGraphicsTexture? image = texture
            ? device.CreateTexture2D(new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.CopySource))
            : null;
        using ISilkGraphicsBuffer? buffer = texture
            ? null
            : device.CreateBuffer(64, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        buffer?.Write(new byte[64]);
        var failure = new InvalidOperationException("Injected readback fence failure.");
        if (signalFailure)
        {
            device.FailNextReadbackSignalForTesting();
        }
        else
        {
            device.FailNextReadbackFenceForTesting(failure);
        }
        device.DeferNextReadbackDrainForTesting();
        var actual = new byte[64];
        void Readback()
        {
            if (image is not null)
            {
                image.ReadbackForTesting(actual);
            }
            else
            {
                buffer!.ReadbackForTesting(actual);
            }
        }
        if (signalFailure)
        {
            COMException? observed = await Assert.That(Readback).Throws<COMException>();
            await Assert.That(observed!.HResult).IsEqualTo(unchecked((int)0x80004005));
        }
        else
        {
            InvalidOperationException? observed =
                await Assert.That(Readback).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
        }
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(texture ? 784ul : 64ul);
        if (image is D3D12SilkGraphicsTexture d3dImage)
        {
            await Assert.That(d3dImage.State)
                .IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.CopySource);
        }
        image?.Dispose();
        buffer?.Dispose();
        await Assert.That(texture ? textures.Usage.ReservedBytes : buffers.Usage.ReservedBytes)
            .IsEqualTo(64ul);

        if (disposeDevice)
        {
            device.Dispose();
        }
        else
        {
            device.WaitIdle();
        }
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(texture ? textures.Usage.ReservedBytes : buffers.Usage.ReservedBytes)
            .IsEqualTo(0ul);
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task D3D12TextureReadbackWaitFailurePublishesSubmittedState()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var image = (D3D12SilkGraphicsTexture)device.CreateTexture2D(
            new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination));
        await Assert.That(image.State).IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.Common);
        device.FailNextReadbackFenceForTesting(new InvalidOperationException("Injected readback fence failure."));
        device.DeferNextReadbackDrainForTesting();
        await Assert.That(() => image.ReadbackForTesting(new byte[64])).Throws<InvalidOperationException>();
        await Assert.That(image.State).IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.CopySource);
        device.WaitIdle();
        byte[] source = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(image, source);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        var actual = new byte[64];
        image.ReadbackForTesting(actual);
        await Assert.That(actual).IsEquivalentTo(source);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12ReadbackWaitFailureReleasesAfterRecoveryDrainAndAllowsRetry(bool texture)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(784);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture? image = texture
            ? device.CreateTexture2D(new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination))
            : null;
        using ISilkGraphicsBuffer? buffer = texture
            ? null
            : device.CreateBuffer(64, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        byte[] source = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        if (image is not null)
        {
            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            commands.UploadTexture(image, source);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        else
        {
            buffer!.Write(source);
        }

        var failure = new InvalidOperationException("Injected readback fence failure.");
        device.FailNextReadbackFenceForTesting(failure);
        var actual = new byte[64];
        void Readback()
        {
            if (image is not null)
            {
                image.ReadbackForTesting(actual);
            }
            else
            {
                buffer!.ReadbackForTesting(actual);
            }
        }
        InvalidOperationException? observed =
            await Assert.That(Readback).Throws<InvalidOperationException>();
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        Readback();
        await Assert.That(actual).IsEquivalentTo(source);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task D3D12ReadbackRetirementReportsBothFailuresAndRetriesUnfinishedSteps()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var reservation = new FailingDisposable("Injected reservation failure.");
        var source = new FailingDisposable("Injected source lease failure.");
        var retirement = new D3D12SilkReadbackRetirement(device);
        device.RegisterDependentObject();
        retirement.SetDependentRegistered();
        retirement.SetReservation(reservation);
        retirement.SetSourceLease(source);
        var readFailure = new InvalidOperationException("Injected readback failure.");
        try
        {
            AggregateException observed = Assert.Throws<AggregateException>(
                () => retirement.DisposeAfterFailure(readFailure));
            await Assert.That(observed.InnerExceptions.Count).IsEqualTo(2);
            await Assert.That(observed.InnerExceptions[0]).IsSameReferenceAs(readFailure);
            await Assert.That(observed.InnerExceptions[1]).IsSameReferenceAs(reservation.Failure);
            await Assert.That(retirement.IsPending).IsTrue();
            await Assert.That(source.Attempts).IsEqualTo(0);

            InvalidOperationException? disposeFailure =
                await Assert.That(device.Dispose).Throws<InvalidOperationException>();
            await Assert.That(disposeFailure).IsSameReferenceAs(reservation.Failure);
            await Assert.That(reservation.Attempts).IsEqualTo(2);
            await Assert.That(source.Attempts).IsEqualTo(0);

            reservation.Refuse = false;
            InvalidOperationException? sourceFailure =
                await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            await Assert.That(sourceFailure).IsSameReferenceAs(source.Failure);
            await Assert.That(reservation.Attempts).IsEqualTo(3);
            await Assert.That(source.Attempts).IsEqualTo(1);
            await Assert.That(retirement.IsPending).IsTrue();

            source.Refuse = false;
            device.WaitIdle();
            await Assert.That(reservation.Attempts).IsEqualTo(3);
            await Assert.That(source.Attempts).IsEqualTo(2);
            await Assert.That(retirement.IsPending).IsFalse();
        }
        finally
        {
            reservation.Refuse = false;
            source.Refuse = false;
            device.WaitIdle();
            retirement.Dispose();
        }
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task D3D12ReadbackRetirementDoesNotReleaseAReentrantLeaseTwice()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var retirement = new D3D12SilkReadbackRetirement(device);
        int attempts = 0;
        retirement.SetSourceLease(new CallbackDisposable(() =>
        {
            if (++attempts == 1)
            {
                retirement.Dispose();
            }
        }));
        retirement.Dispose();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [SupportedOSPlatform("windows")]
    public async Task D3D12SignalFailureRetainsSubmittedWorkUntilDeviceDrain(
        bool pick, bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 submission retirement requires Windows.");
            return;
        }

        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var staging = new SilkGpuStagingBudget(pick ? 1040u : 784u);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        using ISilkPickReadbackBuffer? readback = pick ? device.CreatePickReadbackBuffer() : null;
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        byte[] source = new byte[64];
        source[0] = 17;
        source[1] = 34;
        source[2] = 51;
        source[3] = 68;
        commands.UploadTexture(texture, source);
        if (readback is not null)
        {
            ((ISilkPickGraphicsCommandList)commands).CopyRgba8Pixel(
                texture, new SilkTexturePixelCoordinate(0, 0), readback);
        }
        ulong lossGeneration = device.DeviceLossGeneration;
        ulong pickGeneration = device.PickDeviceGeneration;
        device.InjectNextOffscreenSignalFailureForTesting();
        device.DeferNextOffscreenDrainForTesting();

        await Assert.That(() => device.Submit(commands)).Throws<COMException>();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(pick ? 1040ul : 784ul);
        await Assert.That(device.DeviceLossGeneration).IsEqualTo(lossGeneration);
        await Assert.That(device.PickDeviceGeneration).IsEqualTo(pickGeneration);
        await Assert.That(((D3D12SilkGraphicsTexture)texture).State)
            .IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.CopySource);
        if (readback is not null)
        {
            var pendingPixel = new byte[SilkPickTokenEncoding.ByteSize];
            await Assert.That(() => readback.ReadRgba8Pixel(pendingPixel))
                .Throws<InvalidOperationException>();
        }

        if (disposeDevice)
        {
            texture.Dispose();
            readback?.Dispose();
            device.Dispose();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            return;
        }
        device.WaitIdle();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(pick ? 256ul : 0ul);
        if (readback is not null)
        {
            var pixel = new byte[SilkPickTokenEncoding.ByteSize];
            readback.ReadRgba8Pixel(pixel);
            await Assert.That(pixel).IsEquivalentTo(new byte[] { 17, 34, 51, 68 });
        }
        using ISilkGraphicsCommandList retry = device.CreateCommandList();
        retry.UploadTexture(texture, source);
        if (readback is not null)
        {
            ((ISilkPickGraphicsCommandList)retry).CopyRgba8Pixel(
                texture, new SilkTexturePixelCoordinate(0, 0), readback);
        }
        using ISilkGraphicsSubmission submission = device.Submit(retry);
        submission.Wait();
        if (readback is not null)
        {
            var pixel = new byte[SilkPickTokenEncoding.ByteSize];
            readback.ReadRgba8Pixel(pixel);
            await Assert.That(pixel).IsEquivalentTo(new byte[] { 17, 34, 51, 68 });
        }
    }

    [Test]
    public async Task VulkanCreatesSubmissionOwnerBeforeQueueSubmit()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        bool observed = false;
        bool alreadySubmitted = true;
        device.SubmissionOwnerCreationProbeForTesting = submitted =>
        {
            observed = true;
            alreadySubmitted = submitted;
        };
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        await Assert.That(observed).IsTrue();
        await Assert.That(alreadySubmitted).IsFalse();
    }

    [Test]
    public async Task VulkanSubmissionOwnerFailureReleasesPreparationAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        byte[] source = Enumerable.Repeat((byte)42, 64).ToArray();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, source);
        var failure = new InvalidOperationException("Injected submission owner allocation failure.");
        device.SubmissionOwnerCreationProbeForTesting = _ => throw failure;
        try
        {
            InvalidOperationException observed = Assert.Throws<InvalidOperationException>(
                () => device.Submit(commands));
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(((VulkanSilkGraphicsTexture)texture).Layout)
                .IsEqualTo(global::Silk.NET.Vulkan.ImageLayout.Undefined);
        }
        finally
        {
            device.SubmissionOwnerCreationProbeForTesting = null;
        }

        using ISilkGraphicsCommandList retry = device.CreateCommandList();
        retry.UploadTexture(texture, source);
        using ISilkGraphicsSubmission submission = device.Submit(retry);
        submission.Wait();
        var actual = new byte[source.Length];
        texture.ReadbackForTesting(actual);
        await Assert.That(actual).IsEquivalentTo(source);
    }

    [Test]
    public async Task VulkanInjectedSubmitLossDoesNotExecuteWorkOnHealthyDevice()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        byte[] original = Enumerable.Repeat((byte)17, 64).ToArray();
        using (ISilkGraphicsCommandList initial = device.CreateCommandList())
        {
            initial.UploadTexture(texture, original);
            using ISilkGraphicsSubmission submission = device.Submit(initial);
            submission.Wait();
        }

        ulong generation = device.DeviceLossGeneration;
        using ISilkGraphicsCommandList rejected = device.CreateCommandList();
        rejected.UploadTexture(texture, Enumerable.Repeat((byte)99, 64).ToArray());
        device.InjectNextOffscreenSubmitDeviceLossForTesting();
        await Assert.That(() => device.Submit(rejected)).Throws<InvalidOperationException>();
        await Assert.That(device.DeviceLossGeneration).IsNotEqualTo(generation);
        var actual = new byte[original.Length];
        texture.ReadbackForTesting(actual);
        await Assert.That(actual).IsEquivalentTo(original);
    }

    [Test]
    public async Task VulkanIdleDeviceLossReportsRetirementFailureAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var pending = new FailingPendingRetirement(device);
        device.RetainPendingSubmission(pending);
        try
        {
            ulong pickGeneration = device.PickDeviceGeneration;
            ulong selectionGeneration = device.SelectionOutlineDeviceGeneration;
            device.InjectNextIdleDeviceLossForTesting();
            AggregateException observed = Assert.Throws<AggregateException>(device.WaitIdle);
            await Assert.That(observed.InnerExceptions.Count).IsEqualTo(2);
            await Assert.That(observed.InnerExceptions[0].Message).Contains("ErrorDeviceLost");
            await Assert.That(observed.InnerExceptions[1]).IsSameReferenceAs(pending.Failure);
            await Assert.That(device.PickDeviceGeneration).IsNotEqualTo(pickGeneration);
            await Assert.That(device.SelectionOutlineDeviceGeneration).IsNotEqualTo(selectionGeneration);
            await Assert.That(pending.IsPending).IsTrue();
            await Assert.That(pending.Attempts).IsEqualTo(1);

            pending.Refuse = false;
            device.WaitIdle();
            await Assert.That(pending.IsPending).IsFalse();
            await Assert.That(pending.Attempts).IsEqualTo(2);
        }
        finally
        {
            pending.Refuse = false;
            device.WaitIdle();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task VulkanReadbackWaitFailureKeepsSourceAndStagingUntilIdle(
        bool texture, bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        var buffers = new SilkGpuBufferBudget(64);
        var textures = new SilkGpuTextureBudget(64);
        staging.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        using ISilkGraphicsTexture? image = texture
            ? device.CreateTexture2D(new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource))
            : null;
        using ISilkGraphicsBuffer? buffer = texture
            ? null
            : device.CreateBuffer(64, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        byte[] source = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        if (image is not null)
        {
            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            commands.UploadTexture(image, source);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        else
        {
            buffer!.Write(source);
        }

        device.FailNextReadbackFenceForTesting();
        var actual = new byte[64];
        await Assert.That(() =>
        {
            if (image is not null)
            {
                image.ReadbackForTesting(actual);
            }
            else
            {
                buffer!.ReadbackForTesting(actual);
            }
        }).Throws<InvalidOperationException>();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(64ul);
        image?.Dispose();
        buffer?.Dispose();
        await Assert.That(texture ? textures.Usage.ReservedBytes : buffers.Usage.ReservedBytes)
            .IsEqualTo(64ul);

        if (disposeDevice)
        {
            device.Dispose();
        }
        else
        {
            device.WaitIdle();
        }
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(texture ? textures.Usage.ReservedBytes : buffers.Usage.ReservedBytes)
            .IsEqualTo(0ul);
    }

    [Test]
    public async Task VulkanTextureReadbackWaitFailurePublishesLayoutAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        var descriptor = new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.ColorRenderTarget |
            SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource);
        using ISilkGraphicsTexture sourceImage = device.CreateTexture2D(descriptor);
        using ISilkGraphicsTexture image = device.CreateTexture2D(descriptor);
        byte[] source = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.UploadTexture(sourceImage, source);
            ((ISilkTextureCopyGraphicsCommandList)commands).CopyRenderTarget(sourceImage, image);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        await Assert.That(((VulkanSilkGraphicsTexture)image).Layout)
            .IsEqualTo(global::Silk.NET.Vulkan.ImageLayout.TransferDstOptimal);

        var actual = new byte[64];
        device.FailNextReadbackFenceForTesting();
        await Assert.That(() => image.ReadbackForTesting(actual)).Throws<InvalidOperationException>();
        await Assert.That(((VulkanSilkGraphicsTexture)image).Layout)
            .IsEqualTo(global::Silk.NET.Vulkan.ImageLayout.TransferSrcOptimal);
        await Assert.That(() => image.ReadbackForTesting(actual))
            .Throws<SilkGpuStagingBudgetExceededException>();

        device.WaitIdle();
        image.ReadbackForTesting(actual);
        await Assert.That(actual).IsEquivalentTo(source);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VulkanReadbackRetirementReportsBothFailuresAndRetriesUnfinishedSteps()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var reservation = new FailingDisposable("Injected reservation failure.");
        var source = new FailingDisposable("Injected source lease failure.");
        VulkanSilkReadbackRetirement retirement = device.CreateReadbackRetirementForTesting();
        device.RegisterDependentObject();
        retirement.SetDependentRegistered();
        retirement.SetReservation(reservation);
        retirement.SetSourceLease(source);
        var readFailure = new InvalidOperationException("Injected readback failure.");
        try
        {
            AggregateException observed = Assert.Throws<AggregateException>(
                () => retirement.DisposeAfterFailure(readFailure));
            await Assert.That(observed.InnerExceptions.Count).IsEqualTo(2);
            await Assert.That(observed.InnerExceptions[0]).IsSameReferenceAs(readFailure);
            await Assert.That(observed.InnerExceptions[1]).IsSameReferenceAs(reservation.Failure);
            await Assert.That(retirement.IsPending).IsTrue();
            await Assert.That(source.Attempts).IsEqualTo(0);

            InvalidOperationException? disposeFailure =
                await Assert.That(device.Dispose).Throws<InvalidOperationException>();
            await Assert.That(disposeFailure).IsSameReferenceAs(reservation.Failure);
            await Assert.That(reservation.Attempts).IsEqualTo(2);
            await Assert.That(source.Attempts).IsEqualTo(0);

            reservation.Refuse = false;
            InvalidOperationException? sourceFailure =
                await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
            await Assert.That(sourceFailure).IsSameReferenceAs(source.Failure);
            await Assert.That(reservation.Attempts).IsEqualTo(3);
            await Assert.That(source.Attempts).IsEqualTo(1);
            await Assert.That(retirement.IsPending).IsTrue();

            source.Refuse = false;
            device.WaitIdle();
            await Assert.That(reservation.Attempts).IsEqualTo(3);
            await Assert.That(source.Attempts).IsEqualTo(2);
            await Assert.That(retirement.IsPending).IsFalse();
        }
        finally
        {
            reservation.Refuse = false;
            source.Refuse = false;
            device.WaitIdle();
            retirement.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanReadbackPreparationFailureReleasesPartialAllocationAndAllowsRetry(bool texture)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture? image = texture
            ? device.CreateTexture2D(new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination))
            : null;
        using ISilkGraphicsBuffer? buffer = texture
            ? null
            : device.CreateBuffer(64, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        byte[] source = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        if (image is not null)
        {
            using ISilkGraphicsCommandList commands = device.CreateCommandList();
            commands.UploadTexture(image, source);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        else
        {
            buffer!.Write(source);
        }

        var failure = new InvalidOperationException("Injected readback allocation failure.");
        device.ReadbackBufferCreationProbeForTesting = () => throw failure;
        var actual = new byte[64];
        void Readback()
        {
            if (image is not null)
            {
                image.ReadbackForTesting(actual);
            }
            else
            {
                buffer!.ReadbackForTesting(actual);
            }
        }
        try
        {
            InvalidOperationException? observed =
                await Assert.That(Readback).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            device.ReadbackBufferCreationProbeForTesting = null;
            Readback();
            await Assert.That(actual).IsEquivalentTo(source);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            device.ReadbackBufferCreationProbeForTesting = null;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanReadbackDeviceLossRetiresSourceAndStaging(bool texture)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        var buffers = new SilkGpuBufferBudget(64);
        var textures = new SilkGpuTextureBudget(64);
        staging.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        using ISilkGraphicsTexture? image = texture
            ? device.CreateTexture2D(new SilkTextureDescriptor(
                4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.CopySource))
            : null;
        using ISilkGraphicsBuffer? buffer = texture
            ? null
            : device.CreateBuffer(64, SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        ulong generation = device.DeviceLossGeneration;
        device.InjectNextReadbackDeviceLossForTesting();
        var actual = new byte[64];
        await Assert.That(() =>
        {
            if (image is not null)
            {
                image.ReadbackForTesting(actual);
            }
            else
            {
                buffer!.ReadbackForTesting(actual);
            }
        }).Throws<InvalidOperationException>();
        await Assert.That(device.DeviceLossGeneration).IsGreaterThan(generation);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        image?.Dispose();
        buffer?.Dispose();
        await Assert.That(texture ? textures.Usage.ReservedBytes : buffers.Usage.ReservedBytes)
            .IsEqualTo(0ul);
    }

    [Test]
    public async Task VulkanReadbackRetirementDoesNotReleaseAReentrantLeaseTwice()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        VulkanSilkReadbackRetirement retirement = device.CreateReadbackRetirementForTesting();
        int attempts = 0;
        retirement.SetSourceLease(new CallbackDisposable(() =>
        {
            if (++attempts == 1)
            {
                retirement.Dispose();
            }
        }));
        retirement.Dispose();
        await Assert.That(attempts).IsEqualTo(1);
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    [SupportedOSPlatform("windows")]
    private sealed class RemovedD3D12Device : ID3D12DeviceTeardownHook
    {
        public void WaitIdle() { }
        public int GetDeviceRemovedReason() => unchecked((int)0x887A0005);
    }

    private sealed class FailingDisposable(string message) : IDisposable
    {
        internal InvalidOperationException Failure { get; } = new(message);
        internal bool Refuse { get; set; } = true;
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Attempts++;
            if (Refuse)
            {
                throw Failure;
            }
        }
    }

    private sealed class FailingPendingRetirement(VulkanSilkGraphicsDevice device)
        : SilkPendingSubmissionRetirement
    {
        internal InvalidOperationException Failure { get; } = new("Injected retirement failure.");
        internal bool Refuse { get; set; } = true;
        internal int Attempts { get; private set; }

        internal override void RetireAfterDeviceIdle()
        {
            Attempts++;
            if (Refuse)
            {
                throw Failure;
            }
            device.RemovePendingSubmission(this);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanSelectionFenceMemoryFailureRetainsUploadsUntilRetry(bool dispose)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ((ISilkSelectionOutlineGraphicsCommandList)commands).BeginSelectionOutlineRendering(
            new SilkSelectionOutlineRenderingDescriptor(color));
        commands.EndRendering();
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(64ul);

            device.FailNextSelectionOutlineFenceForTesting(deviceLost: false);
            if (dispose)
            {
                await Assert.That(submission.Dispose).Throws<InvalidOperationException>();
            }
            else
            {
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            }
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(64ul);
            await Assert.That(staging.Usage.ReservationCount).IsEqualTo(1ul);

            submission.Wait();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanFailedWaitAndDisposeRemainOwnedUntilDeviceDrain(bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ((ISilkSelectionOutlineGraphicsCommandList)commands).BeginSelectionOutlineRendering(
            new SilkSelectionOutlineRenderingDescriptor(color));
        commands.EndRendering();
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            device.FailNextSelectionOutlineFenceForTesting(deviceLost: false);
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            device.FailNextSelectionOutlineFenceForTesting(deviceLost: false);
            await Assert.That(submission.Dispose).Throws<InvalidOperationException>();
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(64ul);

            texture.Dispose();
            color.Dispose();
            if (disposeDevice)
            {
                device.Dispose();
            }
            else
            {
                device.WaitIdle();
            }
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
        }
        finally
        {
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task VulkanFailedWaitOrPollRemainsOwnedUntilDeviceDrain(
        bool poll, bool disposeDevice)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        var staging = new SilkGpuStagingBudget(64);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        ((ISilkSelectionOutlineGraphicsCommandList)commands).BeginSelectionOutlineRendering(
            new SilkSelectionOutlineRenderingDescriptor(color));
        commands.EndRendering();
        ISilkGraphicsSubmission submission = device.Submit(commands);
        try
        {
            commands.Dispose();
            device.WaitIdle();
            device.FailNextSelectionOutlineFenceForTesting(deviceLost: false);
            if (poll)
            {
                await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
            }
            else
            {
                await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            }
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(64ul);

            if (disposeDevice)
            {
                texture.Dispose();
                color.Dispose();
                device.Dispose();
            }
            else
            {
                device.WaitIdle();
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
                submission.Wait();
                await Assert.That(submission.IsCompleted).IsTrue();
                submission.Dispose();
                await Assert.That(submission.Wait).Throws<ObjectDisposedException>();
            }
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
        }
        finally
        {
            submission.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanObservedCompletionSurvivesLaterDeviceLoss(bool poll)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        ((ISilkSelectionOutlineGraphicsCommandList)commands).BeginSelectionOutlineRendering(
            new SilkSelectionOutlineRenderingDescriptor(color));
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        device.WaitIdle();
        device.FailNextSelectionOutlineFenceForTesting(deviceLost: false);
        await Assert.That(submission.Wait).Throws<InvalidOperationException>();
        if (poll)
        {
            await Assert.That(submission.IsCompleted).IsTrue();
        }
        else
        {
            submission.Wait();
        }

        device.InjectNextIdleDeviceLossForTesting();
        await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VulkanDeviceLossKeepsSubmissionInvalid(bool lossAtWait)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("Vulkan submission retirement requires Windows or Linux.");
            return;
        }

        using var device = (VulkanSilkGraphicsDevice)SilkDepthCaptureConformance.CreateDevice(
            SilkGraphicsBackend.Vulkan);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.ColorRenderTarget));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        ((ISilkSelectionOutlineGraphicsCommandList)commands).BeginSelectionOutlineRendering(
            new SilkSelectionOutlineRenderingDescriptor(color));
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        device.WaitIdle();
        device.FailNextSelectionOutlineFenceForTesting(deviceLost: lossAtWait);
        await Assert.That(submission.Wait).Throws<InvalidOperationException>();
        if (lossAtWait)
        {
            await Assert.That(submission.Wait).Throws<InvalidOperationException>();
            await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
            device.WaitIdle();
        }
        else
        {
            device.InjectNextIdleDeviceLossForTesting();
            await Assert.That(device.WaitIdle).Throws<InvalidOperationException>();
        }
        await Assert.That(submission.Wait).Throws<InvalidOperationException>();
        await Assert.That(() => { _ = submission.IsCompleted; }).Throws<InvalidOperationException>();
    }
}
