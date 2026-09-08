// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Viewer;

[SupportedOSPlatform("windows")]
internal static class ViewerWindowsFiles
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadAttributes = 0x80;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint Overlapped = 0x40000000;
    private const uint WriteThrough = 0x80000000;
    private const uint BackupSemantics = 0x02000000;
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparseAttribute = 0x400;
    private const int MaximumLeafCharacters = 255;
    private const int MaximumRenameCharacters = 4160;

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Choose an absolute regular filesystem path, not a device namespace.");
        }
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!;
        if (fullPath.AsSpan(root.Length).Contains(':') || fullPath.Length > 4096)
        {
            throw new NotSupportedException("Alternate data streams and oversized paths are not review destinations.");
        }
        return fullPath;
    }

    internal static SafeFileHandle OpenDirectory(string path)
    {
        SafeFileHandle handle = Open(path, 1 | ReadAttributes, 1 | 2, 3, BackupSemantics | OpenReparsePoint);
        try
        {
            CheckKind(handle, directory: true);
            RequireCanonicalPath(handle, path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static FileStream OpenRead(string path, string? expectedPath = null)
        => OpenReadCore(path, expectedPath, deleteAccess: false);

    internal static FileStream OpenReadForDelete(string path, string expectedPath)
        => OpenReadCore(path, expectedPath, deleteAccess: true);

    private static FileStream OpenReadCore(string path, string? expectedPath, bool deleteAccess)
    {
        SafeFileHandle handle = Open(
            path, GenericRead | (deleteAccess ? DeleteAccess : 0), 1, 3, OpenReparsePoint | Overlapped);
        try
        {
            CheckKind(handle, directory: false);
            RequireCanonicalPath(handle, expectedPath ?? path);
            return new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static FileStream CreateStaging(string physicalDirectory, string name)
    {
        SafeFileHandle handle = Open(Path.Combine(physicalDirectory, name),
            GenericWrite | DeleteAccess | ReadAttributes, 0, 1, OpenReparsePoint | Overlapped | WriteThrough);
        try
        {
            CheckKind(handle, directory: false);
            return new FileStream(handle, FileAccess.Write, 64 * 1024, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static ViewerPhysicalFileIdentity GetIdentity(SafeFileHandle handle)
    {
        if (!GetFileId(handle, 18, out FileIdInfo info, (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            throw Error("Could not read a physical file identity.");
        }
        return new ViewerPhysicalFileIdentity(info.Volume, info.Low, info.High);
    }

    internal static string GetPhysicalDirectory(SafeFileHandle handle) => GetFinalPath(handle, 1);

    internal static void Rename(
        SafeFileHandle source, SafeFileHandle parent, string name, bool replace)
    {
        if (name.Length is <= 0 or > MaximumLeafCharacters || name.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
        {
            throw new ArgumentException("The review destination must have a regular bounded file name.", nameof(name));
        }
        bool retained = false;
        try
        {
            parent.DangerousAddRef(ref retained);
            string destination = Path.Combine(GetPhysicalDirectory(parent), name);
            if (destination.Length >= MaximumRenameCharacters)
            {
                throw new NotSupportedException("The physical publication path exceeds its safety bound.");
            }
            var info = new RenameInfo
            {
                Replace = replace ? 1u : 0u,
                Parent = 0,
                NameBytes = checked((uint)destination.Length * 2)
            };
            for (int index = 0; index < destination.Length; index++)
            {
                info.Name[index] = destination[index];
            }
            if (!RenameFile(source, 3, ref info, (uint)Marshal.SizeOf<RenameInfo>()))
            {
                throw Error("The review file could not be atomically published.");
            }
        }
        finally
        {
            if (retained)
            {
                parent.DangerousRelease();
            }
        }
    }

    internal static void DeleteOwnedStaging(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!SetDisposition(handle, 4, ref delete, 1))
        {
            throw Error("The owned review file could not be removed.");
        }
    }

    private static SafeFileHandle Open(string path, uint access, uint share, uint creation, uint flags)
    {
        SafeFileHandle handle = CreateFileW(path, access, share, 0, creation, flags, 0);
        if (!handle.IsInvalid)
        {
            return handle;
        }
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error == 2)
        {
            throw new FileNotFoundException("The selected file does not exist.", path);
        }
        if (error == 3)
        {
            throw new DirectoryNotFoundException("The selected file's directory does not exist.");
        }
        throw new IOException($"Could not retain the filesystem path '{path}'.", new Win32Exception(error));
    }

    private static void CheckKind(SafeFileHandle handle, bool directory)
    {
        if (!GetAttributeTag(handle, 9, out AttributeTagInfo info, (uint)Marshal.SizeOf<AttributeTagInfo>()))
        {
            throw Error("Could not inspect filesystem attributes.");
        }
        if ((info.Attributes & ReparseAttribute) != 0)
        {
            throw new NotSupportedException(
                "Review publication does not follow reparse points or filesystem aliases.");
        }
        if (((info.Attributes & DirectoryAttribute) != 0) != directory)
        {
            throw new IOException(
                directory ? "A path ancestor is not a directory." : "The destination is not a file.");
        }
    }

    private static void RequireCanonicalPath(SafeFileHandle handle, string expected)
    {
        string actual = GetFinalPath(handle, 0);
        actual = actual.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? @"\\" + actual[8..] : actual.StartsWith(@"\\?\", StringComparison.Ordinal) ? actual[4..] : actual;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
            Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Use the physical filesystem path rather than an alias or short name.");
        }
    }

    private static string GetFinalPath(SafeFileHandle handle, uint flags)
    {
        char[] buffer = new char[512];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, flags);
        if (length == 0)
        {
            throw Error("Could not resolve a retained filesystem path.");
        }
        if (length >= buffer.Length)
        {
            if (length > 32768)
            {
                throw new NotSupportedException("The physical filesystem path exceeds its safety bound.");
            }
            buffer = new char[checked((int)length + 1)];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, flags);
            if (length == 0 || length >= buffer.Length)
            {
                throw Error("Could not resolve a bounded physical filesystem path.");
            }
        }
        return new string(buffer, 0, checked((int)length));
    }

    private static IOException Error(string message) =>
        new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong Volume;
        internal ulong Low;
        internal ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        internal uint Attributes;
        internal uint Tag;
    }

    [InlineArray(MaximumRenameCharacters)]
    private struct FileNameBuffer
    {
        private ushort _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RenameInfo
    {
        internal uint Replace;
        internal nint Parent;
        internal uint NameBytes;
        internal FileNameBuffer Name;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileId(SafeFileHandle handle, int kind, out FileIdInfo info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributeTag(SafeFileHandle handle, int kind, out AttributeTagInfo info, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, [Out] char[] buffer, uint length, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RenameFile(SafeFileHandle handle, int kind, ref RenameInfo info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDisposition(SafeFileHandle handle, int kind, ref byte delete, uint size);
}
