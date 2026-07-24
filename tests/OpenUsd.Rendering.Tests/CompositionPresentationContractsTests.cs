// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class CompositionPresentationContractsTests
{
    [Test]
    public async Task PresentationTargetDefensivelyCopiesCapabilitiesAndDeviceIds()
    {
        string[] imageTypes = ["image"];
        string[] semaphoreTypes = ["semaphore"];
        byte[] luid = [1, 2];
        byte[] uuid = [3, 4];

        var target = new CompositionPresentationTarget(
            imageTypes,
            semaphoreTypes,
            luid,
            uuid);
        imageTypes[0] = "changed";
        semaphoreTypes[0] = "changed";
        luid[0] = 9;
        uuid[0] = 9;

        await Assert.That(target.ImageHandleTypes[0]).IsEqualTo("image");
        await Assert.That(target.SemaphoreHandleTypes[0]).IsEqualTo("semaphore");
        await Assert.That(target.DeviceLuid[0]).IsEqualTo((byte)1);
        await Assert.That(target.DeviceUuid[0]).IsEqualTo((byte)3);
    }

    [Test]
    public async Task SynchronizationFactoriesRetainKeysResourceIdsAndTimelineValues()
    {
        var keyed =
            CompositionFrameSynchronization.KeyedMutex(7, 8);
        var timeline =
            CompositionFrameSynchronization.TimelineSemaphores(
                waitSemaphoreId: 1,
                waitValue: 11,
                signalSemaphoreId: 1,
                signalValue: 12);

        await Assert.That(keyed.Kind)
            .IsEqualTo(CompositionFrameSynchronizationKind.KeyedMutex);
        await Assert.That(keyed.WaitValue).IsEqualTo(7ul);
        await Assert.That(keyed.SignalValue).IsEqualTo(8ul);
        await Assert.That(timeline.WaitSemaphoreId).IsEqualTo(1L);
        await Assert.That(timeline.SignalSemaphoreId).IsEqualTo(1L);
        await Assert.That(timeline.WaitValue).IsEqualTo(11ul);
        await Assert.That(timeline.SignalValue).IsEqualTo(12ul);
    }

    [Test]
    public async Task ExternalDescriptorsContainMetadataRatherThanRawHandles()
    {
        var image = new CompositionExternalImage(
            "image",
            new ViewportDimensions(64, 32),
            CompositionExternalImageFormat.B8G8R8A8UNorm);
        var semaphore = new CompositionExternalSemaphore(42, "semaphore");

        await Assert.That(image.HandleType).IsEqualTo("image");
        await Assert.That(image.Size).IsEqualTo(new ViewportDimensions(64, 32));
        await Assert.That(semaphore.ResourceId).IsEqualTo(42L);
        await Assert.That(typeof(CompositionExternalImage).GetProperty("Handle")).IsNull();
        await Assert.That(typeof(CompositionExternalSemaphore).GetProperty("Handle")).IsNull();
    }

    [Test]
    public async Task HandleValidityPolicyAllowsFdZeroAndRejectsInvalidTypedValues()
    {
        ICompositionExternalHandleLease fdZero =
            new ContractLease(0, "VulkanOpaquePosixFileDescriptor");
        ICompositionExternalHandleLease fdNegative =
            new ContractLease(-1, "VulkanOpaquePosixFileDescriptor");
        ICompositionExternalHandleLease ntZero =
            new ContractLease(0, "VulkanOpaqueNtHandle");
        ICompositionExternalHandleLease ioSurfaceZero =
            new ContractLease(0, "IOSurfaceRef");
        ICompositionExternalHandleLease sharedEventZero =
            new ContractLease(0, "MetalSharedEvent");

        await Assert.That(fdZero.ValidityPolicy)
            .IsEqualTo(CompositionExternalHandleValidityPolicy.NonNegativeFileDescriptor);
        await Assert.That(fdZero.IsInvalid).IsFalse();
        await Assert.That(fdNegative.IsInvalid).IsTrue();
        await Assert.That(ntZero.ValidityPolicy)
            .IsEqualTo(CompositionExternalHandleValidityPolicy.NonZero);
        await Assert.That(ntZero.IsInvalid).IsTrue();
        await Assert.That(ioSurfaceZero.IsInvalid).IsTrue();
        await Assert.That(sharedEventZero.IsInvalid).IsTrue();
    }

    private sealed class ContractLease(nint handle, string handleType)
        : ICompositionExternalHandleLease
    {
        public nint Handle { get; } = handle;

        public string HandleType { get; } = handleType;

        public CompositionExternalHandleOwnership Ownership =>
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted;

        public void CommitTransfer() => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
