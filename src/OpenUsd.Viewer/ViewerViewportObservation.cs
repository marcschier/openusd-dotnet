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
        ViewportDimensions bookmark)
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
            Span<char> line = stackalloc char[256];
            if (!line.TryWrite(CultureInfo.InvariantCulture,
                $"[OWN-viewer-viewport] {label} ui={ui.Width}x{ui.Height} " +
                $"state={state.Width}x{state.Height} nav={navigation.Width}x{navigation.Height} " +
                $"bookmark={bookmark.Width}x{bookmark.Height}\n", out int written))
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
