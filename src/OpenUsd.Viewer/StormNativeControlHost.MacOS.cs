// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Platform;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal static class StormNativeControlHostMacOS
{
    internal const string HandleDescriptor = "NSView";

    internal static bool IsParent(IPlatformHandle parent) =>
        OperatingSystem.IsMacOS() &&
        string.Equals(
            parent.HandleDescriptor,
            HandleDescriptor,
            StringComparison.OrdinalIgnoreCase);

    internal static void InjectDiagnosticInput(nint view)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Cocoa input diagnostics require macOS.");
        }
        OpenUsdStormChildRuntime.InjectMacOSViewDiagnosticInput(view);
    }
}
