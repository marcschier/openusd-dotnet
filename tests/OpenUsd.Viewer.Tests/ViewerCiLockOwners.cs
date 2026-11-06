// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Viewer.Tests;

internal static partial class ViewerCiLockOwners
{
    private const uint Success = 0;
    private const uint MoreData = 234;
    private const uint MaximumReportedProcesses = 128;

    internal readonly record struct Result(string Status, uint[] ProcessIds);

    internal static unsafe Result Query(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Result("unsupported", []);
        }

        try
        {
            char* sessionKey = stackalloc char[33];
            uint result = RmStartSession(out uint session, 0, sessionKey);
            if (result != Success)
            {
                return new Result($"start-{result}", []);
            }

            try
            {
                fixed (char* file = path)
                {
                    char** files = stackalloc char*[1];
                    files[0] = file;
                    result = RmRegisterResources(session, 1, files, 0, 0, 0, 0);
                }
                if (result != Success)
                {
                    return new Result($"register-{result}", []);
                }

                uint count = 0;
                result = RmGetList(session, out uint needed, ref count, null, out _);
                if (result == Success)
                {
                    return new Result("success", []);
                }

                for (int attempt = 0; result == MoreData && attempt < 3; attempt++)
                {
                    if (needed == 0 || needed > MaximumReportedProcesses)
                    {
                        return new Result($"list-size-{needed}", []);
                    }
                    var processes = new RestartManagerProcessInfo[checked((int)needed)];
                    count = needed;
                    fixed (RestartManagerProcessInfo* pointer = processes)
                    {
                        result = RmGetList(session, out needed, ref count, pointer, out _);
                    }
                    if (result == Success)
                    {
                        return new Result("success", processes.Take(checked((int)count))
                            .Select(process => process.Process.ProcessId).ToArray());
                    }
                }
                return new Result($"list-{result}", []);
            }
            finally
            {
                _ = RmEndSession(session);
            }
        }
        catch (Exception exception)
        {
            return new Result($"unavailable-{exception.GetType().Name}", []);
        }
    }

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint RmStartSession(out uint session, uint flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint RmRegisterResources(
        uint session, uint fileCount, char** files, uint applicationCount, nint applications,
        uint serviceCount, nint services);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint RmGetList(
        uint session, out uint needed, ref uint count, RestartManagerProcessInfo* processes, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint RmEndSession(uint session);

    [StructLayout(LayoutKind.Sequential)]
    private struct RestartManagerUniqueProcess
    {
        public uint ProcessId;
        public uint StartTimeLow;
        public uint StartTimeHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RestartManagerProcessInfo
    {
        public RestartManagerUniqueProcess Process;
        public fixed char ApplicationName[256];
        public fixed char ServiceShortName[64];
        public int ApplicationType;
        public uint ApplicationStatus;
        public uint TerminalSessionId;
        public int Restartable;
    }
}
