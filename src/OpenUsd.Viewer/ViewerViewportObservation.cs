// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal enum ViewerViewportEvent
{
    CaptureBegin,
    CaptureReturn,
    ResizeBegin,
    ResizeReturn,
    RecallBegin,
    RecallEnd,
    Limit
}

internal readonly record struct ViewerViewportLayout(
    double? RequestedWidth, double? RequestedHeight,
    double? ClientWidth, double? ClientHeight,
    double? StageColumnWidth, double? InspectorColumnWidth,
    double? ContentWidth, double? ContentHeight, double? Scale);

internal static class ViewerViewportObservation
{
    private static readonly (bool Sdk, bool Viewer) Mode = ReadMode();
    private static int _records;
    private static int _bytes;
    private static int _failedRecords;

    internal static bool IsEnabled => Mode.Viewer && !Mode.Sdk;
    internal static bool IsExclusiveModeSelected => Mode.Sdk || Mode.Viewer;
    internal static int FailedRecords => Volatile.Read(ref _failedRecords);
    internal static int ReservedRecords => Math.Min(64, Volatile.Read(ref _records));
    internal static int ReservedBytes => Volatile.Read(ref _bytes);

    internal static void RecordFailure() => Interlocked.Increment(ref _failedRecords);

    internal static void Write(
        ViewerViewportEvent kind,
        ViewportDimensions ui,
        ViewportDimensions state,
        ViewportDimensions navigation,
        ViewportDimensions bookmark,
        ViewerViewportLayout layout = default)
    {
        if (!IsEnabled)
        {
            return;
        }
        int record;
        do
        {
            record = Volatile.Read(ref _records);
            if (record >= 64)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _records, record + 1, record) != record);
        record++;
        int systemError = Marshal.GetLastSystemError();
        int pinvokeError = Marshal.GetLastPInvokeError();
        try
        {
            if (record == 64)
            {
                kind = ViewerViewportEvent.Limit;
                ui = state = navigation = bookmark = ViewportDimensions.Empty;
                layout = default;
            }
            string label = kind switch
            {
                ViewerViewportEvent.CaptureBegin => "capture.begin",
                ViewerViewportEvent.CaptureReturn => "capture.return",
                ViewerViewportEvent.ResizeBegin => "resize.begin",
                ViewerViewportEvent.ResizeReturn => "resize.return",
                ViewerViewportEvent.RecallBegin => "recall.begin",
                ViewerViewportEvent.RecallEnd => "recall.end",
                ViewerViewportEvent.Limit => "limit",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            ReadOnlySpan<char> requestedWidth = FormatPixels(stackalloc char[10], layout.RequestedWidth, layout.Scale);
            ReadOnlySpan<char> requestedHeight = FormatPixels(
                stackalloc char[10], layout.RequestedHeight, layout.Scale);
            ReadOnlySpan<char> clientWidth = FormatPixels(stackalloc char[10], layout.ClientWidth, layout.Scale);
            ReadOnlySpan<char> clientHeight = FormatPixels(stackalloc char[10], layout.ClientHeight, layout.Scale);
            ReadOnlySpan<char> stageWidth = FormatPixels(stackalloc char[10], layout.StageColumnWidth, layout.Scale);
            ReadOnlySpan<char> inspectorWidth = FormatPixels(
                stackalloc char[10], layout.InspectorColumnWidth, layout.Scale);
            ReadOnlySpan<char> contentWidth = FormatPixels(stackalloc char[10], layout.ContentWidth, layout.Scale);
            ReadOnlySpan<char> contentHeight = FormatPixels(stackalloc char[10], layout.ContentHeight, layout.Scale);
            ReadOnlySpan<char> scale = FormatScale(stackalloc char[12], layout.Scale);
            if (requestedWidth.IsEmpty || requestedHeight.IsEmpty || clientWidth.IsEmpty || clientHeight.IsEmpty ||
                stageWidth.IsEmpty || inspectorWidth.IsEmpty || contentWidth.IsEmpty || contentHeight.IsEmpty ||
                scale.IsEmpty)
            {
                RecordFailure();
                return;
            }
            Span<char> line = stackalloc char[256];
            if (!line.TryWrite(CultureInfo.InvariantCulture,
                $"[OWN-viewer-viewport] {label} px u={ui.Width}x{ui.Height} " +
                $"s={state.Width}x{state.Height} n={navigation.Width}x{navigation.Height} " +
                $"b={bookmark.Width}x{bookmark.Height} " +
                $"w={requestedWidth}x{requestedHeight}/{clientWidth}x{clientHeight} " +
                $"p={stageWidth},{inspectorWidth} c={contentWidth}x{contentHeight} z~={scale}\n",
                out int written))
            {
                RecordFailure();
                return;
            }
            Interlocked.Add(ref _bytes, written);
            try
            {
                Console.Error.Write(line[..written]);
            }
            catch (IOException)
            {
                RecordFailure();
            }
            catch (ObjectDisposedException)
            {
                RecordFailure();
            }
        }
        finally
        {
            Marshal.SetLastSystemError(systemError);
            Marshal.SetLastPInvokeError(pinvokeError);
        }
    }

    private static ReadOnlySpan<char> FormatPixels(Span<char> buffer, double? value, double? scaling)
    {
        if (value is not { } dimension)
        {
            return "?";
        }
        if (double.IsNaN(dimension))
        {
            return "A";
        }
        if (double.IsInfinity(dimension))
        {
            return dimension > 0 ? "I" : "-I";
        }
        if (dimension < 0)
        {
            return "N";
        }
        if (scaling is not { } scale || !double.IsFinite(scale) || scale <= 0)
        {
            return "?";
        }
        double pixels = dimension * scale;
        if (!double.IsFinite(pixels) || pixels > int.MaxValue)
        {
            return "O";
        }
        int rounded = dimension == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(pixels));
        return rounded.TryFormat(buffer, out int written, provider: CultureInfo.InvariantCulture)
            ? buffer[..written] : ReadOnlySpan<char>.Empty;
    }

    private static ReadOnlySpan<char> FormatScale(Span<char> buffer, double? scaling) =>
        scaling is not { } scale ? "?" :
        scale.TryFormat(buffer, out int written, "G5", CultureInfo.InvariantCulture)
            ? buffer[..written] : ReadOnlySpan<char>.Empty;

    private static (bool Sdk, bool Viewer) ReadMode()
    {
        int systemError = Marshal.GetLastSystemError();
        int pinvokeError = Marshal.GetLastPInvokeError();
        try
        {
            return (
                Environment.GetEnvironmentVariable("OPENUSD_DOTNET_OWN_SDK_PHASES") == "1",
                Environment.GetEnvironmentVariable("OPENUSD_VIEWER_VIEWPORT_OBSERVATION") == "1");
        }
        finally
        {
            Marshal.SetLastSystemError(systemError);
            Marshal.SetLastPInvokeError(pinvokeError);
        }
    }
}
