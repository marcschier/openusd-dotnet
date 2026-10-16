// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace OpenUsd.Viewer;

internal static class ViewerReleaseGateObservation
{
    private static readonly bool Enabled = !ViewerViewportObservation.IsExclusiveModeSelected &&
        Environment.GetEnvironmentVariable("OPENUSD_VIEWER_OWN_ROOT_OBSERVATION") == "1";
    private static int _records;

    internal static bool IsEnabled => Enabled && Volatile.Read(ref _records) < 24;

    internal static void Write(string phase, string detail)
    {
        if (!Enabled || Interlocked.Increment(ref _records) > 24)
        { return; }
        Console.Error.WriteLine(
            $"[DEBUG-viewer-owned-root] utc={DateTimeOffset.UtcNow:O} phase={phase} {detail}");
    }

    internal static void WriteOwnedWindowsModules()
    {
        if (!IsEnabled || !OperatingSystem.IsWindows())
        { return; }
        try
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                using (module)
                {
                    if (!module.ModuleName.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase) &&
                        !module.ModuleName.Equals("openusd_storm_child.dll", StringComparison.OrdinalIgnoreCase) &&
                        !module.ModuleName.Equals("openusd_dotnet.dll", StringComparison.OrdinalIgnoreCase))
                    { continue; }
                    string path = module.FileName;
                    using FileStream stream = File.OpenRead(path);
                    Write("owned-mapped-module",
                        $"pid={process.Id} module={module.ModuleName} path={path} " +
                        $"sha256={Convert.ToHexString(SHA256.HashData(stream))}");
                }
            }
        }
        catch (Win32Exception exception) { Write("mapped-module-unknown", exception.Message); }
        catch (IOException exception) { Write("mapped-module-unknown", exception.Message); }
        catch (UnauthorizedAccessException exception) { Write("mapped-module-unknown", exception.Message); }
    }
}
