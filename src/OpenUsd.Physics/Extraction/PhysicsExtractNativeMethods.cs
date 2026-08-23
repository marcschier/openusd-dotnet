// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Physics.Extraction;

/// <summary>Caller-provided extraction bounds, mirroring <c>openusd_physics_extract_options</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsExtractNativeOptions
{
    internal uint StructSize;
    internal uint Version;
    internal double TimeCode;
    internal uint Flags;
    internal uint MaxObjects;
    internal uint MaxProperties;
    internal uint MaxRelationships;
    internal uint MaxTargets;
    internal uint MaxNumbers;
    internal uint MaxTexts;
    internal uint MaxPoints;
    internal uint MaxIndices;
    internal uint MaxDiagnostics;
    internal uint MaxStringBytes;
    internal uint Reserved0;
}

/// <summary>A borrowed view of one native extraction page.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysicsExtractNativeView
{
    internal uint StructSize;
    internal uint Version;
    internal byte* Data;
    internal nuint ByteSize;
}

/// <summary>The native error buffer shape shared by every <c>openusd_dotnet</c> entry point.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct PhysicsExtractNativeError
{
    internal PhysicsExtractNativeError(byte* data, nuint capacity)
    {
        Data = data;
        Capacity = capacity;
        Required = 0;
    }

    internal readonly byte* Data;
    internal readonly nuint Capacity;
    internal readonly nuint Required;
}

/// <summary>
/// Declares the physics extraction entry points of the project owned C ABI.
/// </summary>
/// <remarks>
/// The whole surface is four entry points, and only one of them touches a stage. That single
/// call performs the one and only composed traversal, so no per prim or per property call
/// crosses the boundary.
/// </remarks>
internal static unsafe partial class PhysicsExtractNativeMethods
{
    private const int ErrorBufferSize = 4096;

    /// <summary>Gets the exact byte size the native options struct must declare.</summary>
    internal static uint OptionsBytes => (uint)sizeof(PhysicsExtractNativeOptions);

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_physics_extract_stage")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ExtractStage(
        OpenUsdNativeStage stage,
        in PhysicsExtractNativeOptions options,
        out nint extraction,
        ref PhysicsExtractNativeView view,
        ref PhysicsExtractNativeError error);

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_physics_extraction_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void ExtractionRelease(nint extraction);

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_physics_extract_get_traversal_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong GetTraversalCount();

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_physics_extract_get_visited_prim_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong GetVisitedPrimCount();

    /// <summary>
    /// Performs the single native traversal and copies the resulting page into managed memory.
    /// </summary>
    /// <param name="stage">The borrowed native stage handle.</param>
    /// <param name="options">The extraction bounds and flags.</param>
    /// <returns>An owned copy of the immutable extraction page.</returns>
    internal static byte[] Extract(
        OpenUsdNativeStage stage, PhysicsExtractNativeOptions options)
    {
        var view = new PhysicsExtractNativeView
        {
            StructSize = (uint)sizeof(PhysicsExtractNativeView),
            Version = PhysicsExtractAbi.ViewVersion,
        };

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nint extraction = nint.Zero;
        int status;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new PhysicsExtractNativeError(errorPointer, (nuint)errorBytes.Length);
            status = ExtractStage(stage, in options, out extraction, ref view, ref error);
            if (status != 0)
            {
                throw new UsdPhysicsExtractionException(DescribeError(errorBytes, status));
            }
        }

        try
        {
            if (extraction == nint.Zero || view.Data is null)
            {
                throw new UsdPhysicsExtractionException(
                    "The native extraction returned no page.");
            }
            if (view.ByteSize == 0 || view.ByteSize > int.MaxValue)
            {
                throw new UsdPhysicsExtractionException(
                    $"The native extraction page size {view.ByteSize} is not usable.");
            }

            // The page is copied out so that no pointer into native memory ever survives the
            // call. Everything downstream works on managed bytes only.
            var page = new byte[(int)view.ByteSize];
            new ReadOnlySpan<byte>(view.Data, (int)view.ByteSize).CopyTo(page);
            return page;
        }
        finally
        {
            if (extraction != nint.Zero)
            {
                ExtractionRelease(extraction);
            }
        }
    }

    private static string DescribeError(ReadOnlySpan<byte> errorBytes, int status)
    {
        int end = errorBytes.IndexOf((byte)0);
        ReadOnlySpan<byte> text = end < 0 ? errorBytes : errorBytes[..end];
        return text.IsEmpty
            ? $"The native physics extraction failed with status {status}."
            : $"The native physics extraction failed with status {status}: " +
                Encoding.UTF8.GetString(text);
    }
}
