// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativePropertyLimits(
    uint StructSize,
    uint Version,
    uint MaximumPropertyCount,
    uint MaximumTextBytes,
    uint PreviewElements,
    uint TimeSamplePreview,
    uint TargetPreview,
    uint MaximumMetadataWork,
    uint MaximumPreviewTextBytes,
    uint Reserved);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativePropertyPreview(
    ulong TotalCount, uint Offset, uint Count, uint Status, uint Reason);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativePropertyAssetRecord(uint StringOffset, uint Missing);

[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativePropertyEntryRecord
{
    internal uint Kind;
    internal uint Name;
    internal uint Type;
    internal uint Flags;
    internal uint Variability;
    internal uint ResolveSource;
    internal uint ValueState;
    internal uint SourceLayer;
    internal uint SourcePath;
    internal uint TargetSourceLayer;
    internal uint TargetSourcePath;
    internal uint Reserved;
    internal OpenUsdNativePropertyPreview Value;
    internal OpenUsdNativePropertyPreview TimeSamples;
    internal OpenUsdNativePropertyPreview Targets;
    internal uint AssetOffset;
    internal uint AssetCount;
}

internal sealed record OpenUsdNativePropertySource(string LayerIdentifier, string SpecPath);

internal sealed record OpenUsdNativePropertyAsset(
    string AuthoredPath, string EvaluatedPath, string ResolvedPath, string AnchorLayerIdentifier, bool? IsMissing);

internal sealed record OpenUsdNativePropertyEntry(
    OpenUsdNativePropertyEntryRecord Values,
    string Name,
    string TypeName,
    OpenUsdNativePropertySource? ValueSource,
    OpenUsdNativePropertySource? TargetSource,
    string[] Elements,
    double[] Times,
    string[] Targets,
    OpenUsdNativePropertyAsset[] Assets);

internal sealed record OpenUsdNativePropertySnapshot(
    string PrimPath,
    double? TimeCode,
    ulong ChangeSerial,
    bool IsComplete,
    int TextByteCount,
    int MetadataWorkCount,
    OpenUsdNativePropertyEntry[] Entries);

public static unsafe partial class OpenUsdNativeRuntime
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePropertyView
    {
        internal uint StructSize;
        internal uint Version;
        internal ulong ChangeSerial;
        internal uint IsComplete;
        internal uint MetadataWork;
        internal uint TimeSampled;
        internal uint Reserved;
        internal double TimeCode;
        internal OpenUsdNativePropertyEntryRecord* Entries;
        internal nuint EntriesSize;
        internal nuint EntryCount;
        internal OpenUsdNativePropertyAssetRecord* Assets;
        internal nuint AssetsSize;
        internal nuint AssetCount;
        internal double* Times;
        internal nuint TimesSize;
        internal nuint TimeCount;
        internal byte* Data;
        internal nuint DataSize;
        internal uint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
    }

    internal static OpenUsdNativePropertySnapshot GetPrimPropertySnapshot(
        OpenUsdNativeStage stage, string primPath, double? timeCode, OpenUsdNativePropertyLimits limits)
    {
        if (Encoding.UTF8.GetByteCount(primPath) >= limits.MaximumTextBytes)
        {
            throw new OpenUsdNativeException(OpenUsdNativeStatus.NativeError,
                "Property inspection text bytes quota exceeded by the selected prim path.");
        }
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        var view = new NativePropertyView
        {
            StructSize = (uint)sizeof(NativePropertyView),
            Version = 1
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetPrimPropertySnapshot(
                lease.Handle, primPath, timeCode.HasValue ? 1 : 0, timeCode.GetValueOrDefault(),
                ref limits, out nint owner, ref view, ref error);
            return CompletePropertySnapshotQuery(
                status, errorBytes, error.Required, owner, view, limits, NativeMethods.PropertySnapshotRelease);
        }
    }

    internal static OpenUsdNativePropertySnapshot CompletePropertySnapshotQuery(
        OpenUsdNativeStatus status, ReadOnlySpan<byte> errorBytes, nuint diagnosticRequired,
        nint owner, NativePropertyView view, OpenUsdNativePropertyLimits limits, Action<nint> release)
    {
        try
        {
            if (status != OpenUsdNativeStatus.Ok)
            {
                OpenUsdNativeException failure = CreateNativeException(status, errorBytes, default);
                if (diagnosticRequired > (nuint)errorBytes.Length)
                {
                    throw new OpenUsdNativeException(status,
                        $"{failure.Message} The full native diagnostic required {diagnosticRequired} bytes.");
                }
                throw failure;
            }
            if (owner == 0)
            {
                throw InvalidPropertySnapshot("a successful query has no owner");
            }
            return DecodePropertySnapshot(view, limits);
        }
        finally
        {
            if (owner != 0)
            {
                release(owner);
            }
        }
    }
}
