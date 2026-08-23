// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Mirrors the project-owned Storm transform override C ABI declared in
/// <c>native/include/openusd_render_physics.h</c>.
/// </summary>
/// <remarks>
/// One packed batch crosses the boundary per update. Native code copies the packed items and path
/// bytes synchronously, so no managed pointer survives the call and nothing here depends on the
/// simulation SDK or on USD authoring.
/// </remarks>
internal static unsafe class StormPhysicsOverrideInterop
{
    internal const uint UpdateVersion = 1;
    internal const uint DiagnosticsVersion = 1;
    internal const uint UpdateReplace = 1;
    internal const uint ItemSnapped = 1;

    // Mirrors OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_PRESERVE_STRETCH: the item transform carries
    // rotation and translation only and the renderer keeps the rendered prim's scale and shear.
    internal const uint ItemPreserveStretch = 2;
    internal const int MaximumItems = 4096;
    internal const int MaximumPathBytes = 1024 * 1024;

    private const int ErrorBufferSize = 4096;

    internal static StormPhysicsOverrideDiagnostics Apply<TCall>(
        nint handle,
        StormPhysicsTransformOverrides overrides)
        where TCall : struct, IStormTransformOverrideCall
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ReadOnlySpan<NativeTransformOverrideItem> items = overrides.Items;
        ReadOnlySpan<byte> pathBytes = overrides.PathBytes;
        if (items.Length > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overrides),
                "Storm transform override batches are limited to 4096 packed items.");
        }
        if (pathBytes.Length > MaximumPathBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overrides),
                "Storm transform override path data exceeds the 1 MiB packed-update limit.");
        }

        var diagnostics = NativeTransformOverrideDiagnostics.Create();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            items,
            pathBytes,
            overrides.Revision,
            ref diagnostics,
            errorBytes,
            out nuint errorRequired);
        StormPickingInterop.ThrowIfFailed(
            status,
            errorBytes,
            errorRequired,
            "Storm transform override update");
        diagnostics.Validate();
        return diagnostics.ToManaged();
    }

    internal interface IStormTransformOverrideCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint handle,
            ReadOnlySpan<NativeTransformOverrideItem> items,
            ReadOnlySpan<byte> pathBytes,
            ulong revision,
            ref NativeTransformOverrideDiagnostics diagnostics,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTransformOverrideItem
    {
        internal ulong ObjectId;
        internal uint PathOffset;
        internal uint PathLength;
        internal int InstanceIndex;
        internal uint Flags;
        internal NativeTransformMatrix Transform;

        internal void SetTransform(ReadOnlySpan<double> transform) =>
            transform[..16].CopyTo(Transform);

        internal readonly void CopyTransformTo(Span<double> destination) =>
            ((ReadOnlySpan<double>)Transform).CopyTo(destination[..16]);
    }

    [InlineArray(16)]
    internal struct NativeTransformMatrix
    {
        private double _element;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTransformOverrideUpdate
    {
        internal uint StructSize;
        internal uint Version;
        internal uint ItemCount;
        internal uint Flags;
        internal ulong Revision;
        internal NativeTransformOverrideItem* Items;
        internal byte* PathBytes;
        internal uint PathBytesSize;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTransformOverrideDiagnostics
    {
        internal uint StructSize;
        internal uint Version;
        internal uint AppliedCount;
        internal uint UnresolvedCount;
        internal ulong Revision;
        internal ulong AppliedBatchCount;
        internal ulong RejectedBatchCount;
        internal ulong DirtiedPrimCount;
        internal uint Capacity;
        internal uint DroppedCount;
        internal uint UnsupportedCount;
        internal uint Reserved;

        internal static NativeTransformOverrideDiagnostics Create() => new()
        {
            StructSize =
                checked((uint)Unsafe.SizeOf<NativeTransformOverrideDiagnostics>()),
            Version = DiagnosticsVersion,
        };

        internal readonly void Validate()
        {
            if (StructSize != Unsafe.SizeOf<NativeTransformOverrideDiagnostics>() ||
                Version != DiagnosticsVersion ||
                Reserved != 0 ||
                AppliedCount > MaximumItems ||
                Capacity > MaximumItems)
            {
                throw StormPickingInterop.IncompatibleResult(
                    "Storm returned incompatible transform override diagnostics.");
            }
        }

        internal readonly StormPhysicsOverrideDiagnostics ToManaged() => new(
            checked((int)AppliedCount),
            checked((int)UnresolvedCount),
            checked((int)DroppedCount),
            checked((int)UnsupportedCount),
            checked((int)Capacity),
            Revision,
            AppliedBatchCount,
            RejectedBatchCount,
            DirtiedPrimCount);
    }

    internal const uint DeformationUpdateVersion = 1;
    internal const uint DeformationDiagnosticsVersion = 1;
    internal const uint DeformationUpdateReplace = 1;
    internal const int MaximumDeformationItems = 1024;
    internal const int MaximumDeformationPoints = 4194304;

    internal static StormPhysicsDeformationDiagnostics ApplyDeformations<TCall>(
        nint handle,
        StormPhysicsDeformationOverrides deformations)
        where TCall : struct, IStormDeformationOverrideCall
    {
        ArgumentNullException.ThrowIfNull(deformations);
        ReadOnlySpan<NativeDeformationOverrideItem> items = deformations.Items;
        ReadOnlySpan<float> points = deformations.Points;
        ReadOnlySpan<byte> pathBytes = deformations.PathBytes;
        if (items.Length > MaximumDeformationItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deformations),
                "Storm deformation override batches are limited to 1024 packed regions.");
        }
        if (points.Length / 3 > MaximumDeformationPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deformations),
                "Storm deformation override point pages are limited to 4194304 points.");
        }
        if (pathBytes.Length > MaximumPathBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deformations),
                "Storm deformation override path data exceeds the 1 MiB packed-update limit.");
        }

        var diagnostics = NativeDeformationOverrideDiagnostics.Create();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            items,
            points,
            pathBytes,
            deformations.Revision,
            ref diagnostics,
            errorBytes,
            out nuint errorRequired);
        StormPickingInterop.ThrowIfFailed(
            status,
            errorBytes,
            errorRequired,
            "Storm deformation override update");
        diagnostics.Validate();
        return diagnostics.ToManaged();
    }

    internal interface IStormDeformationOverrideCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint handle,
            ReadOnlySpan<NativeDeformationOverrideItem> items,
            ReadOnlySpan<float> points,
            ReadOnlySpan<byte> pathBytes,
            ulong revision,
            ref NativeDeformationOverrideDiagnostics diagnostics,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDeformationOverrideItem
    {
        internal ulong ObjectId;
        internal uint PathOffset;
        internal uint PathLength;
        internal int InstanceIndex;
        internal uint Flags;
        internal uint PointOffset;
        internal uint PointCount;
        internal ulong TopologyRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDeformationOverrideUpdate
    {
        internal uint StructSize;
        internal uint Version;
        internal uint ItemCount;
        internal uint Flags;
        internal ulong Revision;
        internal NativeDeformationOverrideItem* Items;
        internal float* Points;
        internal byte* PathBytes;
        internal uint PointCount;
        internal uint PathBytesSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDeformationOverrideDiagnostics
    {
        internal uint StructSize;
        internal uint Version;
        internal uint AppliedCount;
        internal uint UnresolvedCount;
        internal ulong Revision;
        internal ulong AppliedBatchCount;
        internal ulong RejectedBatchCount;
        internal ulong DirtiedPrimCount;
        internal uint Capacity;
        internal uint DroppedCount;
        internal uint UnsupportedCount;
        internal uint MismatchedCount;

        internal static NativeDeformationOverrideDiagnostics Create() => new()
        {
            StructSize =
                checked((uint)Unsafe.SizeOf<NativeDeformationOverrideDiagnostics>()),
            Version = DeformationDiagnosticsVersion,
        };

        internal readonly void Validate()
        {
            if (StructSize != Unsafe.SizeOf<NativeDeformationOverrideDiagnostics>() ||
                Version != DeformationDiagnosticsVersion ||
                AppliedCount > MaximumDeformationItems ||
                Capacity > MaximumDeformationItems)
            {
                throw StormPickingInterop.IncompatibleResult(
                    "Storm returned incompatible deformation override diagnostics.");
            }
        }

        internal readonly StormPhysicsDeformationDiagnostics ToManaged() => new(
            checked((int)AppliedCount),
            checked((int)UnresolvedCount),
            checked((int)DroppedCount),
            checked((int)UnsupportedCount),
            checked((int)MismatchedCount),
            checked((int)Capacity),
            Revision,
            AppliedBatchCount,
            RejectedBatchCount,
            DirtiedPrimCount);
    }
}
