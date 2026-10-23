// Copyright (c) marcschier. Licensed under the MIT License.

#nullable enable

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Tooling;

/// <summary>Owns a child attached to a private, kill-on-close Windows job before its first instruction.</summary>
public sealed class BoundedWindowsProcess : IDisposable
{
    private IntPtr _job;
    private IntPtr _process;
    private bool _assignedToJob;
    public uint ProcessId { get; private set; }

    private BoundedWindowsProcess()
    {
    }

    public static HostMemorySnapshot ReadHostMemory()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Host memory admission requires Windows.");
        }
        var information = new PerformanceInformation
        {
            Size = (uint)Marshal.SizeOf<PerformanceInformation>()
        };
        Require(GetPerformanceInfo(ref information, information.Size), "Read host memory admission counters");
        ulong pageBytes = information.PageSize.ToUInt64();
        ulong commitTotal = information.CommitTotal.ToUInt64();
        ulong commitLimit = information.CommitLimit.ToUInt64();
        ulong physicalAvailable = information.PhysicalAvailable.ToUInt64();
        if (pageBytes == 0 || commitTotal > commitLimit ||
            physicalAvailable > information.PhysicalTotal.ToUInt64())
        {
            throw new InvalidOperationException("Windows returned inconsistent host memory counters.");
        }
        return new HostMemorySnapshot(
            checked(physicalAvailable * pageBytes), checked((commitLimit - commitTotal) * pageBytes));
    }

    public readonly struct HostMemorySnapshot
    {
        public HostMemorySnapshot(ulong availablePhysicalBytes, ulong availableCommitBytes)
        {
            AvailablePhysicalBytes = availablePhysicalBytes;
            AvailableCommitBytes = availableCommitBytes;
        }

        public ulong AvailablePhysicalBytes { get; }
        public ulong AvailableCommitBytes { get; }

        public bool Admits(ulong maximumCommitBytes, ulong? physicalReserveBytes, ulong? commitReserveBytes)
        {
            if (maximumCommitBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCommitBytes));
            }
            return Fits(AvailablePhysicalBytes, maximumCommitBytes, physicalReserveBytes) &&
                Fits(AvailableCommitBytes, maximumCommitBytes, commitReserveBytes);
        }

        private static bool Fits(ulong available, ulong maximumCommitBytes, ulong? reserve) =>
            reserve is null || (available >= reserve.Value && available - reserve.Value >= maximumCommitBytes);
    }

    public static BoundedWindowsProcess Start(string executable, string[] arguments, string workingDirectory,
        string stdoutPath, string stderrPath, ulong maximumCommitBytes, ulong? maximumWorkingSetBytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Kernel job limits require Windows.");
        }
        if (maximumCommitBytes == 0 || maximumWorkingSetBytes is < 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCommitBytes),
                "Commit must be positive and a requested working-set ceiling must be at least 1 MiB.");
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
            !Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new ArgumentException("Use an existing absolute executable and working directory.");
        }
        var owner = new BoundedWindowsProcess();
        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        IntPtr input = IntPtr.Zero;
        IntPtr output = IntPtr.Zero;
        IntPtr error = IntPtr.Zero;
        IntPtr attributes = IntPtr.Zero;
        IntPtr handles = IntPtr.Zero;
        IntPtr thread = IntPtr.Zero;
        bool attributesInitialized = false;
        try
        {
            owner._job = CreateJobObjectW(IntPtr.Zero, null);
            RequireHandle(owner._job, "CreateJobObject");
            var limits = new ExtendedLimits();
            // Per-process/tree commit and kill-on-close; breakaway is deliberately not enabled.
            limits.Basic.Flags = 0x00000100 | 0x00000200 | 0x00002000;
            if (maximumWorkingSetBytes is { } workingSet)
            {
                limits.Basic.Flags |= 0x00000001;
                limits.Basic.MinimumWorkingSet = new UIntPtr(1_048_576);
                limits.Basic.MaximumWorkingSet = new UIntPtr(workingSet);
            }
            limits.ProcessMemory = new UIntPtr(maximumCommitBytes);
            limits.JobMemory = new UIntPtr(maximumCommitBytes);
            Require(SetInformationJobObject(owner._job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()),
                "SetInformationJobObject");
            ExtendedLimits installed = owner.ReadLimits();
            if (installed.Basic.Flags != limits.Basic.Flags ||
                installed.ProcessMemory != limits.ProcessMemory || installed.JobMemory != limits.JobMemory ||
                installed.Basic.MaximumWorkingSet != limits.Basic.MaximumWorkingSet)
            {
                throw new InvalidOperationException("Windows did not retain the requested job limits.");
            }

            input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0x80, IntPtr.Zero);
            RequireHandle(input, "Open child stdin");
            output = CreateFileW(stdoutPath, 0x40000000, 1, ref security, 1, 0x80, IntPtr.Zero);
            RequireHandle(output, "Create child stdout");
            error = CreateFileW(stderrPath, 0x40000000, 1, ref security, 1, 0x80, IntPtr.Zero);
            RequireHandle(error, "Create child stderr");
            IntPtr attributeSize = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            if (attributeSize == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Size process attributes");
            }
            attributes = Marshal.AllocHGlobal(attributeSize);
            Require(InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize),
                "Initialize process attributes");
            attributesInitialized = true;
            handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.Copy(new[] { input, output, error }, 0, handles, 3);
            Require(UpdateProcThreadAttribute(attributes, 0, new UIntPtr(0x00020002), handles,
                new UIntPtr((uint)(IntPtr.Size * 3)), IntPtr.Zero, IntPtr.Zero), "Restrict inherited handles");
            var startup = new StartupInfoEx
            {
                Startup = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = 0x00000100,
                    StandardInput = input,
                    StandardOutput = output,
                    StandardError = error
                },
                Attributes = attributes
            };
            var command = new StringBuilder(Quote(executable));
            foreach (string argument in arguments)
            {
                command.Append(' ').Append(Quote(argument));
            }
            // Suspend first, assign the job, then resume: no child code runs outside the configured limits.
            Require(CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                0x00000004 | 0x00080000 | 0x08000000, IntPtr.Zero, workingDirectory,
                ref startup, out ProcessInformation process), "Create suspended child");
            owner._process = process.Process;
            owner.ProcessId = process.ProcessId;
            thread = process.Thread;
            uint packageNameLength = 0;
            int packageStatus = GetPackageFullName(owner._process, ref packageNameLength, IntPtr.Zero);
            if (packageStatus is 0 or 122)
            {
                throw new NotSupportedException(
                    "Packaged executables cannot guarantee private-job containment of unpackaged descendants. " +
                    "Use an unpackaged executable; the suspended child has not been resumed.");
            }
            if (packageStatus != 15700)
            {
                throw new Win32Exception(packageStatus, "Read suspended child package identity");
            }
            Require(AssignProcessToJobObject(owner._job, owner._process), "Assign suspended child to job");
            owner._assignedToJob = true;
            if (ResumeThread(thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Resume bounded child");
            }
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
        finally
        {
            Close(ref thread);
            Close(ref input);
            Close(ref output);
            Close(ref error);
            if (attributesInitialized)
            {
                DeleteProcThreadAttributeList(attributes);
            }
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(handles);
        }
    }

    public bool WaitForExit(int milliseconds)
    {
        ObjectDisposedException.ThrowIf(_process == IntPtr.Zero, this);
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        uint result = WaitForSingleObject(_process, (uint)milliseconds);
        if (result == 0)
        {
            return true;
        }
        if (result == 258)
        {
            return false;
        }
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Wait for bounded child");
    }

    public uint ExitCode
    {
        get
        {
            if (!WaitForExit(0))
            {
                throw new InvalidOperationException("The child has not exited.");
            }
            Require(GetExitCodeProcess(_process, out uint exitCode), "Get child exit code");
            return exitCode;
        }
    }

    public ulong PeakProcessMemoryUsedBytes => ReadLimits().PeakProcessMemory.ToUInt64();
    public ulong PeakJobMemoryUsedBytes => ReadLimits().PeakJobMemory.ToUInt64();
    public uint ObservedProcessCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_job == IntPtr.Zero, this);
            Require(QueryInformationJobObject(_job, 1, out BasicAccounting accounting,
                Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero), "Read job process accounting");
            return accounting.TotalProcesses;
        }
    }

    private ExtendedLimits ReadLimits()
    {
        ObjectDisposedException.ThrowIf(_job == IntPtr.Zero, this);
        Require(QueryInformationJobObject(_job, 9, out ExtendedLimits limits,
            Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero), "Read job memory accounting");
        return limits;
    }

    public void Dispose()
    {
        try
        {
            Stop();
        }
        finally
        {
            Close(ref _job);
            Close(ref _process);
        }
    }

    public void Stop()
    {
        if (_process == IntPtr.Zero || (!_assignedToJob && WaitForExit(0)))
        {
            return;
        }
        // A held handle cannot target a reused PID. Natural exit may race the termination request.
        bool terminated = _assignedToJob ? TerminateJobObject(_job, 1) : TerminateProcess(_process, 1);
        int code = Marshal.GetLastWin32Error();
        if (!WaitForExit(30000))
        {
            if (!terminated)
            {
                throw new Win32Exception(code, "Terminate owned process tree");
            }
            throw new TimeoutException("The owned child did not terminate.");
        }
        if (_assignedToJob)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                Require(QueryInformationJobObject(_job, 1, out BasicAccounting accounting,
                    Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero), "Read stopped job process accounting");
                if (accounting.ActiveProcesses == 0)
                {
                    return;
                }
                if (wait.ElapsedMilliseconds >= 30000)
                {
                    throw new TimeoutException("The owned process tree did not terminate.");
                }
                System.Threading.Thread.Sleep(10);
            }
        }
    }

    private static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0'))
        {
            throw new ArgumentException("Process arguments cannot contain NUL.", nameof(value));
        }
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            text.Append(character);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void Require(bool success, string operation)
    {
        if (!success)
        {
            int code = Marshal.GetLastWin32Error();
            throw new Win32Exception(code, $"{operation} ({code}): {new Win32Exception(code).Message}");
        }
    }

    private static void RequireHandle(IntPtr handle, string operation) =>
        Require(handle != IntPtr.Zero && handle != new IntPtr(-1), operation);

    private static void Close(ref IntPtr handle)
    {
        IntPtr previous = handle;
        handle = IntPtr.Zero;
        if (previous != IntPtr.Zero && previous != new IntPtr(-1))
        {
            Require(CloseHandle(previous), "Close native handle");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool Inherit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessUserTime, JobUserTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XCharacters, YCharacters, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
        public UIntPtr SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("psapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int kind, out ExtendedLimits limits, int size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int kind, out BasicAccounting accounting, int size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetPackageFullName(IntPtr process, ref uint length, IntPtr name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string path, uint access, uint sharing,
        ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute,
        IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string executable, StringBuilder command,
        IntPtr processSecurity, IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
