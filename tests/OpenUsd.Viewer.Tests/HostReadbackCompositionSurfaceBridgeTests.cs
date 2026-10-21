// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class HostReadbackCompositionSurfaceBridgeTests
{
    [Test]
    public async Task ImportRejectsFramesWithoutHostReadbackContract()
    {
        await using var bridge = new HostReadbackCompositionSurfaceBridge(
            _ => { },
            () => Task.CompletedTask);

        await Assert.That(async () => await bridge.ImportAsync(
                new ReadbackFrame("test-image", new ViewportDimensions(2, 2)),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await bridge.ImportAsync(
                new ReadbackFrame(
                    CompositionHostReadback.ImageHandleType,
                    new ViewportDimensions(2, 2),
                    CompositionExternalImageFormat.B8G8R8A8UNorm),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await bridge.ImportAsync(
                new ReadbackFrame(
                    CompositionHostReadback.ImageHandleType,
                    new ViewportDimensions(2, 2),
                    topLeftOrigin: false),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PresentCopiesEachFrameIntoPublishedBitmapAndReleasesItOnDispose()
    {
        await RunUiAsync(async () =>
        {
            var published = new List<WriteableBitmap?>();
            int compositorWaits = 0;
            int imports = 0;
            int presents = 0;
            var bridge = new HostReadbackCompositionSurfaceBridge(
                published.Add,
                () =>
                {
                    compositorWaits++;
                    return Task.CompletedTask;
                },
                _ => imports++,
                _ => presents++);
            var frame = new ReadbackFrame(
                CompositionHostReadback.ImageHandleType,
                new ViewportDimensions(3, 2));
            IImportedCompositionFrame imported =
                await bridge.ImportAsync(frame, CancellationToken.None);

            frame.Fill(10, 20, 30);
            await bridge.PresentAsync(imported, CompositionFrameSynchronization.Automatic);
            WriteableBitmap bitmap = published[^1]!;
            byte[] first = ReadPixels(bitmap);
            frame.Fill(200, 100, 50);
            await bridge.PresentAsync(imported, CompositionFrameSynchronization.Automatic);
            byte[] edited = ReadPixels(published[^1]!);

            await Assert.That(bitmap.PixelSize.Width).IsEqualTo(3);
            await Assert.That(bitmap.PixelSize.Height).IsEqualTo(2);
            await Assert.That(first).IsEquivalentTo(frame.Expected(10, 20, 30));
            await Assert.That(edited).IsEquivalentTo(frame.Expected(200, 100, 50));
            await Assert.That(published[^1]).IsSameReferenceAs(bitmap);
            await Assert.That(imports).IsEqualTo(1);
            await Assert.That(presents).IsEqualTo(2);
            await Assert.That(compositorWaits).IsEqualTo(2);
            await Assert.That(async () => await bridge.PresentAsync(
                    imported,
                    CompositionFrameSynchronization.Semaphores(1, 2)))
                .Throws<InvalidOperationException>();

            await imported.DisposeAsync();
            await bridge.DisposeAsync();

            await Assert.That(published[^1]).IsNull();
            await Assert.That(async () => await bridge.ImportAsync(frame, CancellationToken.None))
                .Throws<ObjectDisposedException>();
        });
    }

    [Test]
    public async Task ResizedFrameReplacesBitmapBeforeDisposingPrevious()
    {
        await RunUiAsync(async () =>
        {
            var published = new List<WriteableBitmap?>();
            await using var bridge = new HostReadbackCompositionSurfaceBridge(
                published.Add,
                () => Task.CompletedTask);
            var small = new ReadbackFrame(
                CompositionHostReadback.ImageHandleType,
                new ViewportDimensions(2, 2));
            var large = new ReadbackFrame(
                CompositionHostReadback.ImageHandleType,
                new ViewportDimensions(4, 3));
            small.Fill(1, 2, 3);
            large.Fill(4, 5, 6);

            await bridge.PresentAsync(
                await bridge.ImportAsync(small, CancellationToken.None),
                CompositionFrameSynchronization.Automatic);
            WriteableBitmap first = published[^1]!;
            await bridge.PresentAsync(
                await bridge.ImportAsync(large, CancellationToken.None),
                CompositionFrameSynchronization.Automatic);
            WriteableBitmap second = published[^1]!;

            await Assert.That(second).IsNotSameReferenceAs(first);
            await Assert.That(second.PixelSize.Width).IsEqualTo(4);
            await Assert.That(second.PixelSize.Height).IsEqualTo(3);
            await Assert.That(ReadPixels(second)).IsEquivalentTo(large.Expected(4, 5, 6));
        });
    }

    private static byte[] ReadPixels(WriteableBitmap bitmap)
    {
        int rowBytes = bitmap.PixelSize.Width * 4;
        byte[] pixels = new byte[rowBytes * bitmap.PixelSize.Height];
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        for (int row = 0; row < bitmap.PixelSize.Height; row++)
        {
            Marshal.Copy(
                framebuffer.Address + (row * framebuffer.RowBytes),
                pixels,
                row * rowBytes,
                rowBytes);
        }
        if (bitmap.Format == PixelFormat.Bgra8888)
        {
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
            }
        }
        else if (bitmap.Format != PixelFormat.Rgba8888)
        {
            throw new InvalidOperationException($"Unexpected bitmap format {bitmap.Format}.");
        }
        return pixels;
    }

    private static async Task RunUiAsync(Func<Task> action)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntryPoint));
        await session.Dispatch(async () =>
        {
            await action();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static class SkiaHeadlessEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<Application>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private sealed class ReadbackFrame(
        string handleType,
        ViewportDimensions size,
        CompositionExternalImageFormat format = CompositionExternalImageFormat.R8G8B8A8UNorm,
        bool topLeftOrigin = true)
        : ICompositionReadbackFrame
    {
        private readonly byte[] _pixels = new byte[size.Width * size.Height * 4];

        public long AllocationId => 1;

        public CompositionExternalImage Image { get; } =
            new(handleType, size, format, topLeftOrigin: topLeftOrigin);

        public IReadOnlyList<CompositionExternalSemaphore> Semaphores => [];

        internal void Fill(byte red, byte green, byte blue) =>
            Expected(red, green, blue).CopyTo(_pixels, 0);

        internal byte[] Expected(byte red, byte green, byte blue)
        {
            byte[] pixels = new byte[_pixels.Length];
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = (byte)(blue + (offset / 4));
                pixels[offset + 3] = byte.MaxValue;
            }
            return pixels;
        }

        public void CopyPresentedPixels(Span<byte> destination) =>
            _pixels.CopyTo(destination);

        public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
            long resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
