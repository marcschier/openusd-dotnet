// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal enum ViewerFrameRowOrder
{
    TopDown,
    BottomUp
}

internal sealed class ViewerFrameCaptureResult
{
    internal ViewerFrameCaptureResult(
        int width, int height, ReadOnlyMemory<byte> rgba, ViewerFrameRowOrder rowOrder,
        RenderDiagnosticsState? diagnostics = null)
    {
        int bytes = GetByteCount(width, height);
        if (rgba.Length != bytes)
        {
            throw new ArgumentException("The captured RGBA storage does not match its dimensions.", nameof(rgba));
        }
        if (rowOrder is not (ViewerFrameRowOrder.TopDown or ViewerFrameRowOrder.BottomUp))
        {
            throw new ArgumentOutOfRangeException(nameof(rowOrder));
        }
        Width = width;
        Height = height;
        Rgba = rgba;
        RowOrder = rowOrder;
        Diagnostics = diagnostics ?? RenderDiagnosticsState.Empty;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal ReadOnlyMemory<byte> Rgba { get; }
    internal ViewerFrameRowOrder RowOrder { get; }
    internal RenderDiagnosticsState Diagnostics { get; }
    internal RenderJobDeviceDepth? DeviceDepth { get; init; }
    internal RenderJobHdrColor? HdrColor { get; init; }

    internal static int GetByteCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long pixels = (long)width * height;
        if (pixels > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame capture is limited to 64 MiB of RGBA pixels.");
        }
        return (int)(pixels * 4);
    }
}
