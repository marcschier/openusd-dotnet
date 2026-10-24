// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Displays presenter-rendered pixels through a CPU bitmap when the compositor cannot import GPU objects.
/// </summary>
/// <remarks>
/// Software display stacks such as Xvfb with Mesa lavapipe or llvmpipe expose no Vulkan external-semaphore
/// or image-import support, so Avalonia reports no usable <c>ICompositionGpuInterop</c>. The presenter still
/// renders on its own device; this bridge copies each completed frame into a <see cref="WriteableBitmap"/>
/// drawn by the owning control and paces presentation to the compositor's animation frames.
/// </remarks>
internal sealed class HostReadbackCompositionSurfaceBridge : ICompositionSurfaceBridge
{
    internal static CompositionPresentationTarget Target { get; } = new(
        [CompositionHostReadback.ImageHandleType],
        [],
        null,
        null);

    private readonly Action<WriteableBitmap?> _publish;
    private readonly Func<Task> _waitForCompositorFrame;
    private readonly Action<ICompositionPresentationFrame>? _reportImport;
    private readonly Action<CompositionFrameSynchronization>? _reportPresent;
    private WriteableBitmap? _bitmap;
    private byte[] _staging = [];
    private bool _disposed;

    internal HostReadbackCompositionSurfaceBridge(
        Action<WriteableBitmap?> publish,
        Func<Task> waitForCompositorFrame,
        Action<ICompositionPresentationFrame>? reportImport = null,
        Action<CompositionFrameSynchronization>? reportPresent = null)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(waitForCompositorFrame);
        _publish = publish;
        _waitForCompositorFrame = waitForCompositorFrame;
        _reportImport = reportImport;
        _reportPresent = reportPresent;
    }

    public bool IsLost => false;

    public ValueTask<IImportedCompositionFrame> ImportAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame is not ICompositionReadbackFrame readback ||
            !string.Equals(
                frame.Image.HandleType,
                CompositionHostReadback.ImageHandleType,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Host-readback presentation requires an {nameof(ICompositionReadbackFrame)} " +
                $"with the '{CompositionHostReadback.ImageHandleType}' handle type; " +
                $"received '{frame.Image.HandleType}'.");
        }
        if (frame.Image.Format != CompositionExternalImageFormat.R8G8B8A8UNorm)
        {
            throw new InvalidOperationException(
                $"Host-readback presentation supports R8G8B8A8UNorm frames; received {frame.Image.Format}.");
        }
        if (!frame.Image.TopLeftOrigin)
        {
            throw new InvalidOperationException(
                "Host-readback presentation requires top-left-origin frames.");
        }
        _reportImport?.Invoke(frame);
        return new ValueTask<IImportedCompositionFrame>(new ImportedReadbackFrame(readback));
    }

    public async Task PresentAsync(
        IImportedCompositionFrame importedFrame,
        CompositionFrameSynchronization synchronization)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (synchronization.Kind != CompositionFrameSynchronizationKind.Automatic)
        {
            throw new InvalidOperationException(
                $"Host-readback presentation requires automatic synchronization; received {synchronization.Kind}.");
        }
        var imported = (ImportedReadbackFrame)importedFrame;
        ViewportDimensions size = imported.Frame.Image.Size;
        int rowBytes = checked(size.Width * 4);
        int length = checked(rowBytes * size.Height);
        if (_staging.Length != length)
        {
            _staging = new byte[length];
        }
        imported.Frame.CopyPresentedPixels(_staging);

        WriteableBitmap bitmap = EnsureBitmap(size);
        if (bitmap.Format == PixelFormat.Bgra8888)
        {
            SwapRedAndBlue(_staging);
        }
        using (ILockedFramebuffer framebuffer = bitmap.Lock())
        {
            if (framebuffer.Size.Width != size.Width || framebuffer.Size.Height != size.Height)
            {
                throw new InvalidOperationException(
                    "The host-readback bitmap does not match the presented frame size.");
            }
            for (int row = 0; row < size.Height; row++)
            {
                Marshal.Copy(
                    _staging,
                    row * rowBytes,
                    framebuffer.Address + (row * framebuffer.RowBytes),
                    rowBytes);
            }
        }
        _publish(bitmap);
        await _waitForCompositorFrame().ConfigureAwait(true);
        _reportPresent?.Invoke(synchronization);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        WriteableBitmap? bitmap = _bitmap;
        _bitmap = null;
        _staging = [];
        if (bitmap is not null)
        {
            _publish(null);
            bitmap.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private WriteableBitmap EnsureBitmap(ViewportDimensions size)
    {
        WriteableBitmap? current = _bitmap;
        if (current is not null &&
            current.PixelSize.Width == size.Width &&
            current.PixelSize.Height == size.Height)
        {
            return current;
        }
        var replacement = new WriteableBitmap(
            new PixelSize(size.Width, size.Height),
            new Vector(96, 96),
            format: null,
            AlphaFormat.Premul);
        if (replacement.Format != PixelFormat.Bgra8888 && replacement.Format != PixelFormat.Rgba8888)
        {
            replacement.Dispose();
            replacement = new WriteableBitmap(
                new PixelSize(size.Width, size.Height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Premul);
        }
        _bitmap = replacement;
        if (current is not null)
        {
            _publish(replacement);
            current.Dispose();
        }
        return replacement;
    }

    private static void SwapRedAndBlue(Span<byte> pixels)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }
    }

    private sealed class ImportedReadbackFrame(ICompositionReadbackFrame frame)
        : IImportedCompositionFrame
    {
        internal ICompositionReadbackFrame Frame { get; } = frame;

        public bool IsLost => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
