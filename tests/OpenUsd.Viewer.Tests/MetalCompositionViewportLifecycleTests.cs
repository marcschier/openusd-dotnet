// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk.Metal;
using SharpMetal.Metal;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class MetalCompositionViewportLifecycleTests
{
    [Test]
    [SupportedOSPlatform("macos")]
    public async Task SessionImportsPresentsResizesAndDisposesMetalFramesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        var presenter = new MetalCompositionViewportPresenter(required: true);
        var surface = new MetalLifecycleSurface();
        var dispatcher = new ImmediateDispatcher();
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher,
            frameCount: 2);
        bool attached = await session.AttachAsync(
            new CompositionPresentationTarget(
                [MetalCompositionViewportPresenter.IOSurfaceHandleType],
                [MetalCompositionViewportPresenter.SharedEventHandleType],
                deviceLuid: null,
                deviceUuid: null),
            new ViewportDimensions(40, 30));

        await Assert.That(attached).IsTrue();
        await Assert.That((await session.PresentNextFrameAsync()).Result)
            .IsEqualTo(CompositionPresentResult.Presented);
        await Assert.That((await session.PresentNextFrameAsync()).Result)
            .IsEqualTo(CompositionPresentResult.Presented);
        await Assert.That((await session.PresentNextFrameAsync()).Result)
            .IsEqualTo(CompositionPresentResult.Presented);
        await session.ResizeAsync(new ViewportDimensions(24, 48));
        await Assert.That((await session.PresentNextFrameAsync()).Result)
            .IsEqualTo(CompositionPresentResult.Presented);

        await session.DisposeAsync();
        await Assert.That(surface.ImportCount).IsEqualTo(3);
        await Assert.That(surface.PresentCount).IsEqualTo(4);
        await Assert.That(surface.IsDisposed).IsTrue();
        await Assert.That(session.State).IsEqualTo(CompositionViewportState.Disposed);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectedUpdateThenResizeAndDisposeRemainBoundedOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        var presenter = new MetalCompositionViewportPresenter(required: true);
        var surface = new MetalLifecycleSurface
        {
            RejectNextPresentation = true
        };
        var session = new CompositionViewportSession(
            presenter,
            surface,
            new ImmediateDispatcher(),
            frameCount: 2);
        bool attached = await session.AttachAsync(
            CreateTarget(),
            new ViewportDimensions(32, 32));
        await Assert.That(attached).IsTrue();

        Exception? rejectedUpdate = null;
        try
        {
            _ = await session.PresentNextFrameAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            rejectedUpdate = exception;
        }
        await Assert.That(rejectedUpdate).IsTypeOf<InvalidOperationException>();

        await session.ResizeAsync(new ViewportDimensions(48, 24))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(surface.IsDisposed).IsTrue();
        await Assert.That(session.State).IsEqualTo(CompositionViewportState.Disposed);
    }

    private static CompositionPresentationTarget CreateTarget() =>
        new(
            [MetalCompositionViewportPresenter.IOSurfaceHandleType],
            [MetalCompositionViewportPresenter.SharedEventHandleType],
            deviceLuid: null,
            deviceUuid: null);

    private sealed class ImmediateDispatcher : ICompositionUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Func<ValueTask> action) => action();
    }

    [SupportedOSPlatform("macos")]
    private sealed class MetalLifecycleSurface : ICompositionSurfaceBridge
    {
        public bool IsLost => false;

        public bool IsDisposed { get; private set; }

        public int ImportCount { get; private set; }

        public int PresentCount { get; private set; }

        public bool RejectNextPresentation { get; set; }

        public async ValueTask<IImportedCompositionFrame> ImportAsync(
            ICompositionPresentationFrame frame,
            CancellationToken cancellationToken)
        {
            await using ICompositionExternalHandleLease image =
                await frame.LeaseImageHandleAsync(cancellationToken);
            await using ICompositionExternalHandleLease sharedEvent =
                await frame.LeaseSemaphoreHandleAsync(
                    frame.Semaphores.Single().ResourceId,
                    cancellationToken);
            if (image.Handle == 0 ||
                image.HandleType != MetalCompositionViewportPresenter.IOSurfaceHandleType ||
                sharedEvent.Handle == 0 ||
                sharedEvent.HandleType !=
                    MetalCompositionViewportPresenter.SharedEventHandleType)
            {
                throw new InvalidOperationException(
                    "The Metal frame did not provide valid Avalonia import handles.");
            }
            ImportCount++;
            return new ImportedMetalFrame(sharedEvent.Handle);
        }

        public Task PresentAsync(
            IImportedCompositionFrame importedFrame,
            CompositionFrameSynchronization synchronization)
        {
            var imported = (ImportedMetalFrame)importedFrame;
            var sharedEvent = new MTLSharedEvent(imported.SharedEvent);
            if (!sharedEvent.WaitUntilSignaledValue(
                    synchronization.WaitValue,
                    5000))
            {
                throw new InvalidOperationException(
                    "The Metal producer did not signal the presentation timeline.");
            }
            if (RejectNextPresentation)
            {
                RejectNextPresentation = false;
                throw new InvalidOperationException(
                    "The composition update was rejected.");
            }
            sharedEvent.SignaledValue = synchronization.SignalValue;
            PresentCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ImportedMetalFrame(nint sharedEvent)
        : IImportedCompositionFrame
    {
        internal nint SharedEvent { get; } = sharedEvent;

        public bool IsLost => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
