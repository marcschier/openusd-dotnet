// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Interop;

/// <summary>
/// Provides the initial safe managed surface over the versioned OpenUsd native ABI.
/// </summary>
public static unsafe partial class OpenUsdNativeRuntime
{
    private const int ErrorBufferSize = 4096;
    internal const string StageAccessWrongThreadFailFastMarker =
        "OPENUSD_STAGE_ACCESS_WRONG_THREAD_FAILFAST";
    internal const string StageAccessCombinedFailureMessage =
        "The OpenUSD stage callback and native stage-access release both failed.";
    private static readonly Lazy<uint> AbiVersionValue = new(NativeMethods.GetAbiVersion);
    private static readonly Lazy<ulong> CapabilitiesValue = new(NativeMethods.GetCapabilities);

    /// <summary>Gets the ABI version exported by the loaded native runtime.</summary>
    public static uint AbiVersion => AbiVersionValue.Value;

    /// <summary>Gets the capabilities exported by the loaded native runtime.</summary>
    public static ulong Capabilities => CapabilitiesValue.Value;

    /// <summary>Gets the OpenUSD version exported by the loaded native runtime.</summary>
    public static string Version
    {
        get
        {
            EnsureCompatibleAbi();
            return GetString(NativeMethods.GetVersion);
        }
    }

    /// <summary>Registers plugins discovered below a plug-info path.</summary>
    /// <param name="path">A plug-info file or directory understood by OpenUSD.</param>
    /// <returns>The number of newly registered plugins.</returns>
    public static nuint RegisterPlugins(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureCompatibleAbi();

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.RegisterPlugins(path, out nuint pluginCount, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return pluginCount;
        }
    }

    /// <summary>Opens an existing USD stage.</summary>
    /// <param name="path">The stage path or resolver identifier.</param>
    /// <returns>An owned stage handle.</returns>
    internal static OpenUsdNativeStage OpenStage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureCompatibleAbi();

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageOpen(path, out nint stage, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeStage(stage);
        }
    }

    /// <summary>Opens a stage with a packed population mask.</summary>
    internal static OpenUsdNativeStage OpenStageMasked(
        string path,
        ReadOnlySpan<string> maskPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureCompatibleAbi();

        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(maskPaths);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetPointer = offsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var view = new NativeStringListView
            {
                StructSize = (uint)sizeof(NativeStringListView),
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetPointer,
                OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                Count = (nuint)offsets.Length
            };
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageOpenMasked(
                path,
                ref view,
                out nint stage,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeStage(stage);
        }
    }

    /// <summary>Creates a new file-backed USD stage.</summary>
    /// <param name="path">The new root layer path.</param>
    /// <returns>An owned stage handle.</returns>
    internal static OpenUsdNativeStage CreateStage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureCompatibleAbi();

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageCreateNew(path, out nint stage, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeStage(stage);
        }
    }

    internal static OpenUsdNativeStage RetainStage(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageRetain(lease.Handle, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeStage(lease.Handle);
        }
    }

    internal static (long Live, long Peak) GetStageCoreDiagnostics()
    {
        return (
            checked((long)DiagnosticNativeMethods.GetLiveStageCoreCount()),
            checked((long)DiagnosticNativeMethods.GetPeakStageCoreCount()));
    }

    internal static void ResetStageCoreDiagnosticPeak() =>
        DiagnosticNativeMethods.ResetPeakStageCoreCount();

    internal static void SetDiagnosticDisplayColor(
        OpenUsdNativeStage stage,
        string primPath,
        float red,
        float green,
        float blue)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = DiagnosticNativeMethods.SetDisplayColor(
                lease.Handle,
                primPath,
                red,
                green,
                blue,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static partial class DiagnosticNativeMethods
    {
        private const string LibraryName = "openusd_dotnet";

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_diagnostic_get_live_stage_core_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetLiveStageCoreCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_diagnostic_get_peak_stage_core_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetPeakStageCoreCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_diagnostic_reset_peak_stage_core_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ResetPeakStageCoreCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_diagnostic_set_display_color",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetDisplayColor(
            nint stage,
            string primPath,
            float red,
            float green,
            float blue,
            ref NativeErrorBuffer error);
    }

    internal static T WithStageAccess<T>(OpenUsdNativeStage stage, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(action);

        nint access = BeginStageAccess(stage);
        ExceptionDispatchInfo? actionFailure = null;
        T result = default!;
        try
        {
            result = action();
        }
        catch (Exception exception)
        {
            actionFailure = ExceptionDispatchInfo.Capture(exception);
        }

        ExceptionDispatchInfo? endFailure = null;
        try
        {
            EndStageAccess(access);
        }
        catch (Exception exception)
        {
            endFailure = ExceptionDispatchInfo.Capture(exception);
        }

        ThrowStageAccessFailures(actionFailure, endFailure);
        return result;
    }

    internal static void WithStageAccess(OpenUsdNativeStage stage, Action action) =>
        WithStageAccess(
            stage,
            () =>
            {
                action();
                return true;
            });

    internal static nint BeginStageAccess(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status =
                NativeMethods.StageAccessBegin(lease.Handle, out nint access, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return access;
        }
    }

    internal static void EndStageAccess(nint access)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageAccessEnd(access, ref error);
            if (status == OpenUsdNativeStatus.Ok)
            {
                return;
            }

            OpenUsdNativeException exception = CreateNativeException(status, errorBytes, error);
            HandleStageAccessEndFailure(exception);
        }
    }

    internal static void HandleStageAccessEndFailure(OpenUsdNativeException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Status == OpenUsdNativeStatus.WrongThread)
        {
            Environment.FailFast(
                $"{StageAccessWrongThreadFailFastMarker}: " +
                "A native OpenUSD stage access guard remains owned after a wrong-thread end.",
                exception);
        }

        throw exception;
    }

    internal static void ThrowStageAccessFailures(
        ExceptionDispatchInfo? actionFailure,
        ExceptionDispatchInfo? endFailure)
    {
        if (actionFailure is not null && endFailure is not null)
        {
            throw new InvalidOperationException(
                StageAccessCombinedFailureMessage,
                new AggregateException(
                    actionFailure.SourceException,
                    endFailure.SourceException));
        }

        endFailure?.Throw();
        actionFailure?.Throw();
    }

    internal static string GetRootLayerIdentifier(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetRootLayerIdentifier(
                    handle,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static string GetSessionLayerIdentifier(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetSessionLayerIdentifier(
                    handle,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static string GetEditTargetLayerIdentifier(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetEditTargetLayerIdentifier(
                    handle,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void SetEditTargetToRootLayer(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        InvokeStageAction(lease.Handle, NativeMethods.StageSetEditTargetRootLayer);
    }

    internal static void SetEditTargetToSessionLayer(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        InvokeStageAction(lease.Handle, NativeMethods.StageSetEditTargetSessionLayer);
    }

    internal static void SetEditTarget(OpenUsdNativeStage stage, OpenUsdNativeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(layer);
        using var stageLease = new SafeHandleLease(stage);
        using var layerLease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetEditTargetLayer(
                stageLease.Handle,
                layerLease.Handle,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetLayerStackIdentifiers(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetLayerStackIdentifiers(
                lease.Handle,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static void MuteLayer(OpenUsdNativeStage stage, string layerIdentifier) =>
        InvokeStageIdentifierAction(stage, layerIdentifier, NativeMethods.StageMuteLayer);

    internal static void UnmuteLayer(OpenUsdNativeStage stage, string layerIdentifier) =>
        InvokeStageIdentifierAction(stage, layerIdentifier, NativeMethods.StageUnmuteLayer);

    internal static bool IsLayerMuted(OpenUsdNativeStage stage, string layerIdentifier)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerIdentifier);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageIsLayerMuted(
                lease.Handle,
                layerIdentifier,
                out int muted,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return muted != 0;
        }
    }

    internal static OpenUsdNativeLayer GetRootLayer(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetRootLayer(
                lease.Handle,
                out nint layer,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeLayer(layer);
        }
    }

    internal static OpenUsdNativeLayer GetSessionLayer(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetSessionLayer(
                lease.Handle,
                out nint layer,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeLayer(layer);
        }
    }

    internal static void SaveStage(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        InvokeStageAction(lease.Handle, NativeMethods.StageSave);
    }

    internal static void ReloadStage(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        InvokeStageAction(lease.Handle, NativeMethods.StageReload);
    }

    internal static void ExportStage(OpenUsdNativeStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageExport(lease.Handle, path, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static double GetStartTimeCode(OpenUsdNativeStage stage) =>
        GetStageDouble(stage, NativeMethods.StageGetStartTimeCode);

    internal static void SetStartTimeCode(OpenUsdNativeStage stage, double value) =>
        SetStageDouble(stage, value, NativeMethods.StageSetStartTimeCode);

    internal static double GetEndTimeCode(OpenUsdNativeStage stage) =>
        GetStageDouble(stage, NativeMethods.StageGetEndTimeCode);

    internal static void SetEndTimeCode(OpenUsdNativeStage stage, double value) =>
        SetStageDouble(stage, value, NativeMethods.StageSetEndTimeCode);

    internal static double GetFramesPerSecond(OpenUsdNativeStage stage) =>
        GetStageDouble(stage, NativeMethods.StageGetFramesPerSecond);

    internal static void SetFramesPerSecond(OpenUsdNativeStage stage, double value) =>
        SetStageDouble(stage, value, NativeMethods.StageSetFramesPerSecond);

    internal static double GetTimeCodesPerSecond(OpenUsdNativeStage stage) =>
        GetStageDouble(stage, NativeMethods.StageGetTimeCodesPerSecond);

    internal static void SetTimeCodesPerSecond(OpenUsdNativeStage stage, double value) =>
        SetStageDouble(stage, value, NativeMethods.StageSetTimeCodesPerSecond);

    internal static string GetDefaultPrimPath(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetDefaultPrimPath(
                    handle,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void SetDefaultPrim(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetDefaultPrim(
                lease.Handle,
                primPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void ClearDefaultPrim(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        InvokeStageAction(lease.Handle, NativeMethods.StageClearDefaultPrim);
    }

    internal static void DefinePrim(OpenUsdNativeStage stage, string primPath, string? typeName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageDefinePrim(
                lease.Handle,
                primPath,
                typeName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void OverridePrim(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageOverridePrim(
                lease.Handle,
                primPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void CreateClassPrim(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageCreateClassPrim(
                lease.Handle,
                primPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetPrimPaths(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetPrimPaths(
                lease.Handle,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static string GetPrimTypeName(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetPrimTypeName(
                    handle,
                    primPath,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static string[] GetPrimAppliedSchemas(OpenUsdNativeStage stage, string primPath) =>
        GetPrimStringList(stage, primPath, NativeMethods.StageGetPrimAppliedSchemas);

    internal static string[] GetPrimChildPaths(OpenUsdNativeStage stage, string primPath) =>
        GetPrimStringList(stage, primPath, NativeMethods.StageGetPrimChildPaths);

    internal static string[] GetPrimAttributeNames(OpenUsdNativeStage stage, string primPath) =>
        GetPrimStringList(stage, primPath, NativeMethods.StageGetPrimAttributeNames);

    internal static string[] GetPrimRelationshipNames(OpenUsdNativeStage stage, string primPath) =>
        GetPrimStringList(stage, primPath, NativeMethods.StageGetPrimRelationshipNames);

    internal static string GetAttributeTypeName(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetAttributeTypeName(
                    handle,
                    primPath,
                    attributeName,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static OpenUsdNativeAttributeValueState GetAttributeValueState(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetAttributeValueState(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out int hasAuthoredValueOpinion,
                out int valueIsBlocked,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return new OpenUsdNativeAttributeValueState(
                hasAuthoredValueOpinion != 0,
                valueIsBlocked != 0);
        }
    }

    internal static double[] GetAttributeTimeSamples(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetAttributeTimeSamples(
                lease.Handle,
                primPath,
                attributeName,
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }

        if (required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native time-sample list is too large for a managed array.");
        }

        double[] values = GC.AllocateUninitializedArray<double>((int)required);
        fixed (double* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetAttributeTimeSamples(
                lease.Handle,
                primPath,
                attributeName,
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native time-sample list changed during the bulk read.");
            }
        }
        return values;
    }

    internal static void ClearAttributeValue(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName) =>
        InvokeAttributeAction(stage, primPath, attributeName, NativeMethods.StageClearAttributeValue);

    internal static void BlockAttributeValue(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName) =>
        InvokeAttributeAction(stage, primPath, attributeName, NativeMethods.StageBlockAttributeValue);

    internal static OpenUsdNativeScalarResult GetAttributeScalarValue(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        var value = new OpenUsdNativeScalarValue
        {
            StructSize = (uint)sizeof(OpenUsdNativeScalarValue)
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetAttributeScalarValue(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref value,
                null,
                0,
                out required,
                ref error);
            if (status == OpenUsdNativeStatus.Ok)
            {
                return new OpenUsdNativeScalarResult(value, null);
            }
            if (status != OpenUsdNativeStatus.BufferTooSmall)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
        }

        OpenUsdNativeScalarKind kind = (OpenUsdNativeScalarKind)value.KindValue;
        if ((kind != OpenUsdNativeScalarKind.Text && kind != OpenUsdNativeScalarKind.Token) ||
            required == 0 ||
            required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid scalar string length.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        fixed (byte* bufferPointer = bytes)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetAttributeScalarValue(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref value,
                bufferPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native scalar string changed during the bulk read.");
            }
        }

        string text = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        return new OpenUsdNativeScalarResult(value, text);
    }

    private static string[] GetPrimStringList(
        OpenUsdNativeStage stage,
        string primPath,
        NativePrimStringListGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    private static string[] DecodeStringListView(NativeStringListView view)
    {
        if (view.StructSize < sizeof(NativeStringListView))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned a truncated string list view.");
        }
        if (view.Count == 0)
        {
            if (view.Data != null || view.DataSize != 0 ||
                view.Offsets != null || view.OffsetsSize != 0)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned buffers for an empty string list.");
            }
            return [];
        }
        if (view.Count > int.MaxValue ||
            view.Count > nuint.MaxValue / (nuint)sizeof(nuint) ||
            view.DataSize > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an oversized string list buffer.");
        }
        nuint expectedOffsetsSize = checked(view.Count * (nuint)sizeof(nuint));
        if (view.OffsetsSize != expectedOffsetsSize ||
            view.Data == null || view.Offsets == null ||
            (nuint)view.Offsets % (nuint)sizeof(nuint) != 0)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native runtime returned an invalid string list buffer " +
                $"(count={view.Count}, dataSize={view.DataSize}, " +
                $"offsetsSize={view.OffsetsSize}, pointerSize={sizeof(nuint)}).");
        }

        var data = new ReadOnlySpan<byte>(view.Data, (int)view.DataSize);
        var offsets = new ReadOnlySpan<nuint>(view.Offsets, (int)view.Count);
        return NativePackedStringListDecoder.Decode(data, offsets, "string list buffer");
    }

    internal static void SetDouble(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetDouble(
                lease.Handle,
                primPath,
                attributeName,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static double GetDouble(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetDouble(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out double value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetDoubleArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<double> values,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (double* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetDoubleArray(
                lease.Handle,
                primPath,
                attributeName,
                valuesPointer,
                (nuint)values.Length,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static double[] GetDoubleArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);

        nuint required;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetDoubleArray(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }

        if (required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native double array is too large for a managed array.");
        }

        double[] values = GC.AllocateUninitializedArray<double>((int)required);
        fixed (double* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetDoubleArray(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native double array changed while it was being copied.");
            }
        }
        return values;
    }

    internal static void SetMatrix4d(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        OpenUsdNativeMatrix4d value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetMatrix4d(
                lease.Handle,
                primPath,
                attributeName,
                ref value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeMatrix4d GetMatrix4d(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetMatrix4d(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeMatrix4d value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetInt32Array(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<int> values,
        double? timeCode) =>
        SetArray(stage, primPath, attributeName, values, timeCode, NativeMethods.StageSetInt32Array);

    internal static int[] GetInt32Array(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode) =>
        GetArray<int>(
            stage, primPath, attributeName, timeCode, NativeMethods.StageGetInt32Array, "int32");

    internal static void SetFloatArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<float> values,
        double? timeCode) =>
        SetArray(stage, primPath, attributeName, values, timeCode, NativeMethods.StageSetFloatArray);

    internal static float[] GetFloatArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode) =>
        GetArray<float>(
            stage, primPath, attributeName, timeCode, NativeMethods.StageGetFloatArray, "float");

    internal static void SetVec2fArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<OpenUsdNativeVec2f> values,
        double? timeCode) =>
        SetArray(stage, primPath, attributeName, values, timeCode, NativeMethods.StageSetVec2fArray);

    internal static OpenUsdNativeVec2f[] GetVec2fArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode) =>
        GetArray<OpenUsdNativeVec2f>(
            stage, primPath, attributeName, timeCode, NativeMethods.StageGetVec2fArray, "vec2f");

    internal static void SetVec3fArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode) =>
        SetArray(stage, primPath, attributeName, values, timeCode, NativeMethods.StageSetVec3fArray);

    internal static OpenUsdNativeVec3f[] GetVec3fArray(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode) =>
        GetArray<OpenUsdNativeVec3f>(
            stage, primPath, attributeName, timeCode, NativeMethods.StageGetVec3fArray, "vec3f");

    internal static OpenUsdNativeBounds3d GetWorldBounds(
        OpenUsdNativeStage stage,
        string? targetPrimPath,
        uint purposeMask,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        NativeStringValidation.ThrowIfInvalidOptionalAbsolutePrimPath(
            targetPrimPath,
            nameof(targetPrimPath));
        if ((purposeMask & ~0xFU) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(purposeMask),
                "The purpose mask contains unsupported bits.");
        }
        if (timeCode.HasValue && !double.IsFinite(timeCode.GetValueOrDefault()))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }

        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            var bounds = new OpenUsdNativeBounds3d
            {
                StructSize = (uint)sizeof(OpenUsdNativeBounds3d),
                Version = OpenUsdNativeBounds3d.CurrentVersion
            };
            OpenUsdNativeStatus status = NativeMethods.StageGetWorldBounds(
                lease.Handle,
                targetPrimPath,
                purposeMask,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref bounds,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            ValidateWorldBoundsResult(bounds);
            return bounds;
        }
    }

    private static void ValidateWorldBoundsResult(OpenUsdNativeBounds3d bounds)
    {
        bool finite =
            double.IsFinite(bounds.MinimumX) &&
            double.IsFinite(bounds.MinimumY) &&
            double.IsFinite(bounds.MinimumZ) &&
            double.IsFinite(bounds.MaximumX) &&
            double.IsFinite(bounds.MaximumY) &&
            double.IsFinite(bounds.MaximumZ);
        bool validFlags =
            bounds.StructSize == sizeof(OpenUsdNativeBounds3d) &&
            bounds.Version == OpenUsdNativeBounds3d.CurrentVersion &&
            bounds.IsValid == 1 &&
            (bounds.IsEmpty == 0 || bounds.IsEmpty == 1);
        bool validEmpty =
            bounds.IsEmpty == 0 ||
            (bounds.MinimumX == 0 &&
             bounds.MinimumY == 0 &&
             bounds.MinimumZ == 0 &&
             bounds.MaximumX == 0 &&
             bounds.MaximumY == 0 &&
             bounds.MaximumZ == 0);
        bool ordered =
            bounds.IsEmpty != 0 ||
            (bounds.MinimumX <= bounds.MaximumX &&
             bounds.MinimumY <= bounds.MaximumY &&
             bounds.MinimumZ <= bounds.MaximumZ);
        bool finiteExtents =
            double.IsFinite(bounds.MaximumX - bounds.MinimumX) &&
            double.IsFinite(bounds.MaximumY - bounds.MinimumY) &&
            double.IsFinite(bounds.MaximumZ - bounds.MinimumZ);
        if (!finite || !validFlags || !validEmpty || !ordered || !finiteExtents)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native world-bounds result violated the data ABI contract.");
        }
    }

    internal static bool IsGeomSchema(
        OpenUsdNativeStage stage,
        string primPath,
        int schemaKind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomIsSchema(
                lease.Handle, primPath, schemaKind, out int matches, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return matches != 0;
        }
    }

    internal static void DefineGeomXform(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.GeomDefineXform);

    internal static void DefineGeomMesh(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.GeomDefineMesh);

    internal static void DefineGeomCamera(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.GeomDefineCamera);

    internal static void DefineGeomSchema(OpenUsdNativeStage stage, string primPath, int schemaKind)
    {
        if (schemaKind < 2 || schemaKind > 18)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaKind),
                "Only concrete UsdGeom schema kinds can be defined.");
        }
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomDefineSchema(
                lease.Handle,
                primPath,
                schemaKind,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void SetGeomInt32(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        int value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomSetInt32Attr(
                lease.Handle,
                primPath,
                attributeName,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static int GetGeomInt32(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomGetInt32Attr(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out int value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetGeomVisibility(
        OpenUsdNativeStage stage,
        string primPath,
        int visibility,
        double? timeCode) =>
        InvokeGeomTimedIntSetter(
            stage,
            primPath,
            visibility,
            timeCode,
            NativeMethods.GeomImageableSetVisibility);

    internal static int GetGeomVisibility(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode) =>
        InvokeGeomTimedIntGetter(
            stage,
            primPath,
            timeCode,
            NativeMethods.GeomImageableGetVisibility);

    internal static void SetGeomPurpose(
        OpenUsdNativeStage stage,
        string primPath,
        int purpose) =>
        InvokeGeomIntSetter(stage, primPath, purpose, NativeMethods.GeomImageableSetPurpose);

    internal static int GetGeomPurpose(OpenUsdNativeStage stage, string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomImageableGetPurpose);

    internal static void SetGeomLocalTransform(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeMatrix4d value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomXformableSetLocalTransform(
                lease.Handle,
                primPath,
                ref value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeMatrix4d GetGeomLocalTransform(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomXformableGetLocalTransform(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeMatrix4d value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static OpenUsdNativeMatrix4d GetGeomWorldTransform(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (timeCode.HasValue && !double.IsFinite(timeCode.GetValueOrDefault()))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomXformableGetWorldTransform(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeMatrix4d value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            ValidateWorldTransformResult(value);
            return value;
        }
    }

    private static void ValidateWorldTransformResult(OpenUsdNativeMatrix4d value)
    {
        bool finite =
            double.IsFinite(value.M00) &&
            double.IsFinite(value.M01) &&
            double.IsFinite(value.M02) &&
            double.IsFinite(value.M03) &&
            double.IsFinite(value.M10) &&
            double.IsFinite(value.M11) &&
            double.IsFinite(value.M12) &&
            double.IsFinite(value.M13) &&
            double.IsFinite(value.M20) &&
            double.IsFinite(value.M21) &&
            double.IsFinite(value.M22) &&
            double.IsFinite(value.M23) &&
            double.IsFinite(value.M30) &&
            double.IsFinite(value.M31) &&
            double.IsFinite(value.M32) &&
            double.IsFinite(value.M33);
        if (!finite)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "Native world-transform output contained a non-finite matrix element.");
        }
    }

    internal static void SetGeomResetXformStack(
        OpenUsdNativeStage stage,
        string primPath,
        bool reset) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            reset ? 1 : 0,
            NativeMethods.GeomXformableSetResetXformStack);

    internal static bool GetGeomResetXformStack(OpenUsdNativeStage stage, string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomXformableGetResetXformStack) != 0;

    internal static void SetGeomMeshPoints(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode) =>
        SetGeomArray(stage, primPath, values, timeCode, NativeMethods.GeomMeshSetPoints);

    internal static OpenUsdNativeVec3f[] GetGeomMeshPoints(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode) =>
        GetGeomArray<OpenUsdNativeVec3f>(
            stage, primPath, timeCode, NativeMethods.GeomMeshGetPoints, "mesh points");

    internal static void SetGeomMeshTopology(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<int> faceVertexCounts,
        ReadOnlySpan<int> faceVertexIndices)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (int* countsPointer = faceVertexCounts)
        fixed (int* indicesPointer = faceVertexIndices)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomMeshSetTopology(
                lease.Handle,
                primPath,
                countsPointer,
                (nuint)faceVertexCounts.Length,
                indicesPointer,
                (nuint)faceVertexIndices.Length,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static int[] GetGeomMeshFaceVertexCounts(
        OpenUsdNativeStage stage,
        string primPath) =>
        GetGeomUntimedArray<int>(
            stage,
            primPath,
            NativeMethods.GeomMeshGetFaceVertexCounts,
            "mesh face vertex counts");

    internal static int[] GetGeomMeshFaceVertexIndices(
        OpenUsdNativeStage stage,
        string primPath) =>
        GetGeomUntimedArray<int>(
            stage,
            primPath,
            NativeMethods.GeomMeshGetFaceVertexIndices,
            "mesh face vertex indices");

    internal static void SetGeomMeshNormals(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        int interpolation,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (OpenUsdNativeVec3f* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomMeshSetNormals(
                lease.Handle,
                primPath,
                valuesPointer,
                (nuint)values.Length,
                interpolation,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeVec3f[] GetGeomMeshNormals(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode) =>
        GetGeomArray<OpenUsdNativeVec3f>(
            stage, primPath, timeCode, NativeMethods.GeomMeshGetNormals, "mesh normals");

    internal static void SetGeomMeshNormalsInterpolation(
        OpenUsdNativeStage stage,
        string primPath,
        int interpolation) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            interpolation,
            NativeMethods.GeomMeshSetNormalsInterpolation);

    internal static int GetGeomMeshNormalsInterpolation(
        OpenUsdNativeStage stage,
        string primPath) =>
        InvokeGeomIntGetter(
            stage,
            primPath,
            NativeMethods.GeomMeshGetNormalsInterpolation);

    internal static void SetGeomMeshSubdivisionScheme(
        OpenUsdNativeStage stage,
        string primPath,
        int scheme) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            scheme,
            NativeMethods.GeomMeshSetSubdivisionScheme);

    internal static int GetGeomMeshSubdivisionScheme(
        OpenUsdNativeStage stage,
        string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomMeshGetSubdivisionScheme);

    internal static void SetGeomMeshOrientation(
        OpenUsdNativeStage stage,
        string primPath,
        int orientation) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            orientation,
            NativeMethods.GeomMeshSetOrientation);

    internal static int GetGeomMeshOrientation(OpenUsdNativeStage stage, string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomMeshGetOrientation);

    internal static void SetGeomMeshDoubleSided(
        OpenUsdNativeStage stage,
        string primPath,
        bool doubleSided) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            doubleSided ? 1 : 0,
            NativeMethods.GeomMeshSetDoubleSided);

    internal static bool GetGeomMeshDoubleSided(OpenUsdNativeStage stage, string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomMeshGetDoubleSided) != 0;

    internal static void SetGeomMeshExtent(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeExtent3f extent,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomMeshSetExtent(
                lease.Handle,
                primPath,
                ref extent,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeExtent3f GetGeomMeshExtent(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomMeshGetExtent(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeExtent3f extent,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return extent;
        }
    }

    internal static void SetGeomCameraProjection(
        OpenUsdNativeStage stage,
        string primPath,
        int projection) =>
        InvokeGeomIntSetter(
            stage,
            primPath,
            projection,
            NativeMethods.GeomCameraSetProjection);

    internal static int GetGeomCameraProjection(OpenUsdNativeStage stage, string primPath) =>
        InvokeGeomIntGetter(stage, primPath, NativeMethods.GeomCameraGetProjection);

    internal static void SetGeomCameraFloat(
        OpenUsdNativeStage stage,
        string primPath,
        int property,
        float value)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomCameraSetFloatProperty(
                lease.Handle, primPath, property, value, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static float GetGeomCameraFloat(
        OpenUsdNativeStage stage,
        string primPath,
        int property)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomCameraGetFloatProperty(
                lease.Handle, primPath, property, out float value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetGeomCameraClippingRange(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeVec2f value)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomCameraSetClippingRange(
                lease.Handle, primPath, ref value, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeVec2f GetGeomCameraClippingRange(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GeomCameraGetClippingRange(
                lease.Handle, primPath, out OpenUsdNativeVec2f value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static OpenUsdNativeCameraState GetGeomCameraState(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (timeCode.HasValue && !double.IsFinite(timeCode.GetValueOrDefault()))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }

        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            var state = new OpenUsdNativeCameraState
            {
                StructSize = (uint)sizeof(OpenUsdNativeCameraState),
                Version = OpenUsdNativeCameraState.CurrentVersion
            };
            OpenUsdNativeStatus status = NativeMethods.GeomCameraGetState(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref state,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            ValidateGeomCameraStateResult(state);
            return state;
        }
    }

    private static void ValidateGeomCameraStateResult(OpenUsdNativeCameraState state)
    {
        double windowWidth = state.WindowRight - state.WindowLeft;
        double windowHeight = state.WindowTop - state.WindowBottom;
        double clippingDepth = state.ClippingFar - state.ClippingNear;
        double windowCenterX = (state.WindowLeft / 2d) + (state.WindowRight / 2d);
        double windowCenterY = (state.WindowBottom / 2d) + (state.WindowTop / 2d);
        bool finite =
            double.IsFinite(state.WindowLeft) &&
            double.IsFinite(state.WindowRight) &&
            double.IsFinite(state.WindowBottom) &&
            double.IsFinite(state.WindowTop) &&
            double.IsFinite(state.ClippingNear) &&
            double.IsFinite(state.ClippingFar) &&
            double.IsFinite(state.FocalLength) &&
            double.IsFinite(state.HorizontalAperture) &&
            double.IsFinite(state.VerticalAperture) &&
            double.IsFinite(state.HorizontalApertureOffset) &&
            double.IsFinite(state.VerticalApertureOffset) &&
            double.IsFinite(state.FocusDistance) &&
            double.IsFinite(state.FStop) &&
            double.IsFinite(windowWidth) &&
            double.IsFinite(windowHeight) &&
            double.IsFinite(clippingDepth) &&
            double.IsFinite(windowCenterX) &&
            double.IsFinite(windowCenterY);
        bool validHeader =
            state.StructSize == sizeof(OpenUsdNativeCameraState) &&
            state.Version == OpenUsdNativeCameraState.CurrentVersion &&
            state.IsValid == 1;
        bool validProjection = state.Projection is 0 or 1;
        bool validFrustum =
            state.WindowLeft < state.WindowRight &&
            state.WindowBottom < state.WindowTop &&
            state.ClippingNear < state.ClippingFar &&
            (state.Projection != 0 || state.ClippingNear > 0d);
        bool validOptics =
            state.FocalLength >= 0d &&
            (state.Projection != 0 || state.FocalLength > 0d) &&
            state.HorizontalAperture > 0d &&
            state.VerticalAperture > 0d &&
            state.FocusDistance >= 0d &&
            state.FStop >= 0d;
        if (!finite ||
            !validHeader ||
            !validProjection ||
            !validFrustum ||
            !validOptics)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native camera-state result violated the data ABI contract.");
        }
    }

    private static void SetGeomArray<T>(
        OpenUsdNativeStage stage,
        string primPath,
        ReadOnlySpan<T> values,
        double? timeCode,
        NativeGeomArraySetter<T> setter)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(
                lease.Handle,
                primPath,
                valuesPointer,
                (nuint)values.Length,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static T[] GetGeomArray<T>(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode,
        NativeGeomArrayGetter<T> getter,
        string label)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }
        if (required > int.MaxValue || required > nuint.MaxValue / (nuint)sizeof(T))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native {label} array is too large for a managed array.");
        }
        T[] values = GC.AllocateUninitializedArray<T>((int)required);
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    $"The native {label} array changed while it was being copied.");
            }
        }
        return values;
    }

    private static T[] GetGeomUntimedArray<T>(
        OpenUsdNativeStage stage,
        string primPath,
        NativeGeomUntimedArrayGetter<T> getter,
        string label)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle, primPath, null, 0, out required, ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }
        if (required > int.MaxValue || required > nuint.MaxValue / (nuint)sizeof(T))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native {label} array is too large for a managed array.");
        }
        T[] values = GC.AllocateUninitializedArray<T>((int)required);
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    $"The native {label} array changed while it was being copied.");
            }
        }
        return values;
    }

    private static void InvokeGeomIntSetter(
        OpenUsdNativeStage stage,
        string primPath,
        int value,
        NativeGeomIntSetter setter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(lease.Handle, primPath, value, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static int InvokeGeomIntGetter(
        OpenUsdNativeStage stage,
        string primPath,
        NativeGeomIntGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, primPath, out int value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static void InvokeGeomTimedIntSetter(
        OpenUsdNativeStage stage,
        string primPath,
        int value,
        double? timeCode,
        NativeGeomTimedIntSetter setter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(
                lease.Handle,
                primPath,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static int InvokeGeomTimedIntGetter(
        OpenUsdNativeStage stage,
        string primPath,
        double? timeCode,
        NativeGeomTimedIntGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out int value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static void SetArray<T>(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        ReadOnlySpan<T> values,
        double? timeCode,
        NativeArraySetter<T> setter)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = setter(
                lease.Handle,
                primPath,
                attributeName,
                valuesPointer,
                (nuint)values.Length,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static T[] GetArray<T>(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode,
        NativeArrayGetter<T> getter,
        string typeLabel)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);

        nuint required;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
            if (required == 0)
            {
                return [];
            }
        }

        if (required > int.MaxValue || required > nuint.MaxValue / (nuint)sizeof(T))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native {typeLabel} array is too large for a managed array.");
        }

        T[] values = GC.AllocateUninitializedArray<T>((int)required);
        fixed (T* valuesPointer = values)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                valuesPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    $"The native {typeLabel} array changed while it was being copied.");
            }
        }
        return values;
    }

    internal static void SetBool(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        bool value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetBool(
                lease.Handle,
                primPath,
                attributeName,
                value ? 1 : 0,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static bool GetBool(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetBool(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out int value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value != 0;
        }
    }

    internal static void SetInt64(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        long value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetInt64(
                lease.Handle,
                primPath,
                attributeName,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static long GetInt64(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetInt64(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out long value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetStringAttribute(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        string value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ArgumentNullException.ThrowIfNull(value);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetString(
                lease.Handle,
                primPath,
                attributeName,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string GetStringAttribute(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetString(
                    handle,
                    primPath,
                    attributeName,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void SetTokenAttribute(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        string value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ArgumentNullException.ThrowIfNull(value);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetToken(
                lease.Handle,
                primPath,
                attributeName,
                value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string GetTokenAttribute(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetToken(
                    handle,
                    primPath,
                    attributeName,
                    timeCode.HasValue ? 1 : 0,
                    timeCode.GetValueOrDefault(),
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void SetVec3f(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        OpenUsdNativeVec3f value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetVec3f(
                lease.Handle,
                primPath,
                attributeName,
                ref value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeVec3f GetVec3f(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetVec3f(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeVec3f value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static void SetColor3f(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        OpenUsdNativeVec3f value,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetColor3f(
                lease.Handle,
                primPath,
                attributeName,
                ref value,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativeVec3f GetColor3f(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        double? timeCode)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetColor3f(
                lease.Handle,
                primPath,
                attributeName,
                timeCode.HasValue ? 1 : 0,
                timeCode.GetValueOrDefault(),
                out OpenUsdNativeVec3f value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    internal static bool HasPrim(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageHasPrim(
                lease.Handle,
                primPath,
                out int exists,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return exists != 0;
        }
    }

    internal static void RemovePrim(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageRemovePrim(lease.Handle, primPath, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void SetPrimActive(OpenUsdNativeStage stage, string primPath, bool active)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetPrimActive(
                lease.Handle,
                primPath,
                active ? 1 : 0,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static bool GetPrimActive(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetPrimActive(
                lease.Handle,
                primPath,
                out int active,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return active != 0;
        }
    }

    internal static void CreateRelationship(OpenUsdNativeStage stage, string primPath, string relationshipName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageCreateRelationship(
                lease.Handle,
                primPath,
                relationshipName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void SetRelationshipTargets(
        OpenUsdNativeStage stage,
        string primPath,
        string relationshipName,
        ReadOnlySpan<string> targets)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);
        using var lease = new SafeHandleLease(stage);

        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(targets);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* dataPointer = data)
        fixed (nuint* offsetPointer = offsets)
        fixed (byte* errorPointer = errorBytes)
        {
            var view = new NativeStringListView
            {
                StructSize = (uint)sizeof(NativeStringListView),
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetPointer,
                OffsetsSize = checked((nuint)offsets.Length * (nuint)sizeof(nuint)),
                Count = (nuint)offsets.Length
            };
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetRelationshipTargets(
                lease.Handle,
                primPath,
                relationshipName,
                ref view,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetRelationshipTargets(
        OpenUsdNativeStage stage,
        string primPath,
        string relationshipName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetRelationshipTargets(
                lease.Handle,
                primPath,
                relationshipName,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static void ClearRelationshipTargets(
        OpenUsdNativeStage stage,
        string primPath,
        string relationshipName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageClearRelationshipTargets(
                lease.Handle,
                primPath,
                relationshipName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void AddReference(
        OpenUsdNativeStage stage,
        string primPath,
        string assetPath,
        string? targetPrimPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageAddReference(
                lease.Handle,
                primPath,
                assetPath,
                targetPrimPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void ClearReferences(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageClearReferences(lease.Handle, primPath, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void AddPayload(
        OpenUsdNativeStage stage,
        string primPath,
        string assetPath,
        string? targetPrimPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageAddPayload(
                lease.Handle,
                primPath,
                assetPath,
                targetPrimPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void ClearPayloads(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageClearPayloads(lease.Handle, primPath, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static OpenUsdNativePayloadArc[] GetComposedPayloadArcs(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        var view = new NativePayloadArcListView
        {
            StructSize = (uint)sizeof(NativePayloadArcListView),
            Version = NativePayloadArcListDecoder.Version
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetComposedPayloadArcs(
                lease.Handle,
                primPath,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleasePayloadArcList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodePayloadArcListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.PayloadArcListRelease(list);
            }
        }
    }

    private static OpenUsdNativePayloadArc[] DecodePayloadArcListView(
        NativePayloadArcListView view)
    {
        if (view.StructSize < sizeof(NativePayloadArcListView))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned a truncated payload-arc list view.");
        }
        if (view.Count == 0)
        {
            if (view.Version != NativePayloadArcListDecoder.Version ||
                view.Data != null || view.DataSize != 0 ||
                view.Offsets != null || view.OffsetsSize != 0)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned an invalid empty payload-arc list.");
            }
            return [];
        }
        if (view.Count > (nuint)(int.MaxValue / 3) ||
            view.Count > nuint.MaxValue / 3 ||
            view.DataSize > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an oversized payload-arc list.");
        }

        nuint entryCount = view.Count * 3;
        if (entryCount > nuint.MaxValue / (nuint)sizeof(nuint))
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an oversized payload-arc offset table.");
        }
        nuint expectedOffsetsSize = checked(entryCount * (nuint)sizeof(nuint));
        if (view.OffsetsSize != expectedOffsetsSize ||
            view.Data == null || view.Offsets == null ||
            (nuint)view.Offsets % (nuint)sizeof(nuint) != 0)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"The native runtime returned an invalid payload-arc list buffer " +
                $"(count={view.Count}, dataSize={view.DataSize}, " +
                $"offsetsSize={view.OffsetsSize}, pointerSize={sizeof(nuint)}).");
        }

        var data = new ReadOnlySpan<byte>(view.Data, (int)view.DataSize);
        var offsets = new ReadOnlySpan<nuint>(view.Offsets, (int)entryCount);
        return NativePayloadArcListDecoder.Decode(
            view.Version,
            view.Count,
            data,
            offsets);
    }

    internal static OpenUsdNativePcpPrimIndex GetPcpPrimIndex(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        var view = new NativePcpPrimIndexView
        {
            StructSize = (uint)sizeof(NativePcpPrimIndexView),
            Version = 1
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.PcpGetPrimIndex(
                lease.Handle,
                primPath,
                out list,
                ref view,
                ref error);
            if (status != OpenUsdNativeStatus.Ok && list != 0)
            {
                NativeMethods.PcpPrimIndexListRelease(list);
                list = 0;
            }
            ThrowIfFailed(status, errorBytes, error);
        }

        try
        {
            return DecodePcpPrimIndexView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.PcpPrimIndexListRelease(list);
            }
        }
    }

    private static OpenUsdNativePcpPrimIndex DecodePcpPrimIndexView(NativePcpPrimIndexView view)
    {
        if (view.Version != 1 || view.StructSize < sizeof(NativePcpPrimIndexView) ||
            view.NodesSize != view.NodeCount * (nuint)sizeof(OpenUsdNativePcpNodeRecord) ||
            view.OffsetsSize != view.StringCount * (nuint)sizeof(nuint) ||
            view.DataSize > int.MaxValue || view.NodeCount > int.MaxValue ||
            view.StringCount > int.MaxValue || view.ErrorOffset > view.StringCount ||
            view.ErrorCount > view.StringCount - view.ErrorOffset)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid Pcp prim-index buffer.");
        }
        if (view.NodeCount == 0)
        {
            return new OpenUsdNativePcpPrimIndex([], []);
        }

        var records = new ReadOnlySpan<OpenUsdNativePcpNodeRecord>(view.Nodes, (int)view.NodeCount);
        string[] strings = NativePackedStringListDecoder.Decode(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<nuint>(view.Offsets, (int)view.StringCount),
            "Pcp prim-index buffer");
        var nodes = new OpenUsdNativePcpNode[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            OpenUsdNativePcpNodeRecord record = records[i];
            if (record.StringCount != 5 ||
                record.StringOffset > (nuint)strings.Length ||
                record.StringCount > (nuint)strings.Length - record.StringOffset ||
                record.LayerOffset > (nuint)strings.Length ||
                record.LayerCount > (nuint)strings.Length - record.LayerOffset)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned an invalid Pcp node string range.");
            }
            int field = (int)record.StringOffset;
            string[] layers = strings[(int)record.LayerOffset..(int)(record.LayerOffset + record.LayerCount)];
            nodes[i] = new OpenUsdNativePcpNode(
                record.ParentIndex,
                record.ArcType,
                record.IsCulled != 0,
                record.IsInert != 0,
                record.IsDueToAncestor != 0,
                record.HasSpecs != 0,
                record.CanContributeSpecs != 0,
                record.NamespaceDepth,
                record.DepthBelowIntroduction,
                record.SiblingIndexAtOrigin,
                strings[field],
                strings[field + 1],
                strings[field + 2],
                strings[field + 3],
                strings[field + 4],
                layers);
        }
        string[] errors = strings[(int)view.ErrorOffset..(int)(view.ErrorOffset + view.ErrorCount)];
        return new OpenUsdNativePcpPrimIndex(nodes, errors);
    }

    internal static void AddInherit(
        OpenUsdNativeStage stage,
        string primPath,
        string inheritedPrimPath) =>
        InvokeStagePrimPairAction(
            stage,
            primPath,
            inheritedPrimPath,
            NativeMethods.StageAddInherit);

    internal static void ClearInherits(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.StageClearInherits);

    internal static void AddSpecialize(
        OpenUsdNativeStage stage,
        string primPath,
        string specializedPrimPath) =>
        InvokeStagePrimPairAction(
            stage,
            primPath,
            specializedPrimPath,
            NativeMethods.StageAddSpecialize);

    internal static void ClearSpecializes(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.StageClearSpecializes);

    internal static void LoadPrim(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.StageLoadPrim);

    internal static void UnloadPrim(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.StageUnloadPrim);

    internal static bool IsPrimLoaded(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.StageIsPrimLoaded);

    internal static void SetInstanceable(OpenUsdNativeStage stage, string primPath, bool instanceable)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetInstanceable(
                lease.Handle,
                primPath,
                instanceable ? 1 : 0,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static bool GetInstanceable(OpenUsdNativeStage stage, string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetInstanceable(
                lease.Handle,
                primPath,
                out int instanceable,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return instanceable != 0;
        }
    }

    internal static bool IsPrimInstance(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.StageIsPrimInstance);

    internal static bool IsPrimPrototype(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.StageIsPrimPrototype);

    internal static string GetPrimPrototypePath(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimString(stage, primPath, NativeMethods.StageGetPrimPrototypePath);

    internal static void AddVariantSet(OpenUsdNativeStage stage, string primPath, string variantSetName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSetName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageAddVariantSet(
                lease.Handle,
                primPath,
                variantSetName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetVariantSetNames(OpenUsdNativeStage stage, string primPath) =>
        GetPrimStringList(stage, primPath, NativeMethods.StageGetVariantSetNames);

    internal static void AddVariant(
        OpenUsdNativeStage stage,
        string primPath,
        string variantSetName,
        string variantName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageAddVariant(
                lease.Handle,
                primPath,
                variantSetName,
                variantName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void SetVariantSelection(
        OpenUsdNativeStage stage,
        string primPath,
        string variantSetName,
        string? variantSelection)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSetName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetVariantSelection(
                lease.Handle,
                primPath,
                variantSetName,
                variantSelection,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string GetVariantSelection(
        OpenUsdNativeStage stage,
        string primPath,
        string variantSetName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSetName);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetVariantSelection(
                    handle,
                    primPath,
                    variantSetName,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static string[] GetVariantNames(OpenUsdNativeStage stage, string primPath, string variantSetName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSetName);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetVariantNames(
                lease.Handle,
                primPath,
                variantSetName,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static void SetPrimMetadataString(OpenUsdNativeStage stage, string primPath, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tagged = new OpenUsdNativeMetadataValue { Kind = (int)OpenUsdNativeMetadataKind.String };
        SetPrimMetadataValue(stage, primPath, key, tagged, value);
    }

    internal static void SetPrimMetadataBool(OpenUsdNativeStage stage, string primPath, string key, bool value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Bool,
            BoolValue = value ? 1 : 0
        };
        SetPrimMetadataValue(stage, primPath, key, tagged, null);
    }

    internal static void SetPrimMetadataInt64(OpenUsdNativeStage stage, string primPath, string key, long value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Int64,
            Int64Value = value
        };
        SetPrimMetadataValue(stage, primPath, key, tagged, null);
    }

    internal static void SetPrimMetadataDouble(OpenUsdNativeStage stage, string primPath, string key, double value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Double,
            DoubleValue = value
        };
        SetPrimMetadataValue(stage, primPath, key, tagged, null);
    }

    internal static string GetPrimMetadataString(OpenUsdNativeStage stage, string primPath, string key)
    {
        GetPrimMetadataValue(stage, primPath, key, OpenUsdNativeMetadataKind.String, out string? result);
        return result!;
    }

    internal static bool GetPrimMetadataBool(OpenUsdNativeStage stage, string primPath, string key) =>
        GetPrimMetadataValue(stage, primPath, key, OpenUsdNativeMetadataKind.Bool, out _).BoolValue != 0;

    internal static long GetPrimMetadataInt64(OpenUsdNativeStage stage, string primPath, string key) =>
        GetPrimMetadataValue(stage, primPath, key, OpenUsdNativeMetadataKind.Int64, out _).Int64Value;

    internal static double GetPrimMetadataDouble(OpenUsdNativeStage stage, string primPath, string key) =>
        GetPrimMetadataValue(stage, primPath, key, OpenUsdNativeMetadataKind.Double, out _).DoubleValue;

    internal static void ClearPrimMetadata(OpenUsdNativeStage stage, string primPath, string key)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageClearPrimMetadata(
                lease.Handle,
                primPath,
                key,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void SetPrimMetadataValue(
        OpenUsdNativeStage stage,
        string primPath,
        string key,
        OpenUsdNativeMetadataValue value,
        string? stringValue)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(stage);
        value.StructSize = (uint)sizeof(OpenUsdNativeMetadataValue);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageSetPrimMetadata(
                lease.Handle,
                primPath,
                key,
                ref value,
                stringValue,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static OpenUsdNativeMetadataValue GetPrimMetadataValue(
        OpenUsdNativeStage stage,
        string primPath,
        string key,
        OpenUsdNativeMetadataKind kind,
        out string? stringResult)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(stage);

        var value = new OpenUsdNativeMetadataValue { StructSize = (uint)sizeof(OpenUsdNativeMetadataValue) };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];

        if (kind != OpenUsdNativeMetadataKind.String)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.StageGetPrimMetadata(
                    lease.Handle,
                    primPath,
                    key,
                    (int)kind,
                    ref value,
                    null,
                    0,
                    out _,
                    ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
            stringResult = null;
            return value;
        }

        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetPrimMetadata(
                lease.Handle,
                primPath,
                key,
                (int)kind,
                ref value,
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.BufferTooSmall)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
        }

        if (required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native metadata string is too large for a managed string.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        fixed (byte* bufferPointer = bytes)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetPrimMetadata(
                lease.Handle,
                primPath,
                key,
                (int)kind,
                ref value,
                bufferPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            stringResult = Encoding.UTF8.GetString(bytes, 0, (int)written - 1);
        }
        return value;
    }

    internal static ulong GetChangeSerial(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetChangeSerial(
                lease.Handle,
                out ulong serial,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return serial;
        }
    }

    internal static void ReleaseStage(nint stage)
    {
        NativeMethods.StageRelease(stage);
    }

    internal static string GetLayerIdentifier(OpenUsdNativeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        using var lease = new SafeHandleLease(layer);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.LayerGetIdentifier(
                    handle,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static void SaveLayer(OpenUsdNativeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerSave(lease.Handle, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static bool ReloadLayer(OpenUsdNativeLayer layer, bool force)
    {
        ArgumentNullException.ThrowIfNull(layer);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerReload(
                lease.Handle,
                force ? 1 : 0,
                out int reloaded,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return reloaded != 0;
        }
    }

    internal static void ExportLayer(OpenUsdNativeLayer layer, string path)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerExport(lease.Handle, path, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void AddSublayer(OpenUsdNativeLayer layer, string sublayerPath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sublayerPath);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerAddSublayer(
                lease.Handle,
                sublayerPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void RemoveSublayer(OpenUsdNativeLayer layer, string sublayerPath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sublayerPath);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerRemoveSublayer(
                lease.Handle,
                sublayerPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static string[] GetSublayerPaths(OpenUsdNativeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        using var lease = new SafeHandleLease(layer);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerGetSublayerPaths(
                lease.Handle,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            return DecodeStringListView(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static void SetMetadataString(OpenUsdNativeLayer layer, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tagged = new OpenUsdNativeMetadataValue { Kind = (int)OpenUsdNativeMetadataKind.String };
        SetLayerMetadataValue(layer, key, tagged, value);
    }

    internal static void SetMetadataBool(OpenUsdNativeLayer layer, string key, bool value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Bool,
            BoolValue = value ? 1 : 0
        };
        SetLayerMetadataValue(layer, key, tagged, null);
    }

    internal static void SetMetadataInt64(OpenUsdNativeLayer layer, string key, long value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Int64,
            Int64Value = value
        };
        SetLayerMetadataValue(layer, key, tagged, null);
    }

    internal static void SetMetadataDouble(OpenUsdNativeLayer layer, string key, double value)
    {
        var tagged = new OpenUsdNativeMetadataValue
        {
            Kind = (int)OpenUsdNativeMetadataKind.Double,
            DoubleValue = value
        };
        SetLayerMetadataValue(layer, key, tagged, null);
    }

    internal static string GetMetadataString(OpenUsdNativeLayer layer, string key)
    {
        GetLayerMetadataValue(layer, key, OpenUsdNativeMetadataKind.String, out string? result);
        return result!;
    }

    internal static bool GetMetadataBool(OpenUsdNativeLayer layer, string key) =>
        GetLayerMetadataValue(layer, key, OpenUsdNativeMetadataKind.Bool, out _).BoolValue != 0;

    internal static long GetMetadataInt64(OpenUsdNativeLayer layer, string key) =>
        GetLayerMetadataValue(layer, key, OpenUsdNativeMetadataKind.Int64, out _).Int64Value;

    internal static double GetMetadataDouble(OpenUsdNativeLayer layer, string key) =>
        GetLayerMetadataValue(layer, key, OpenUsdNativeMetadataKind.Double, out _).DoubleValue;

    internal static void ClearMetadata(OpenUsdNativeLayer layer, string key)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerClearMetadata(lease.Handle, key, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void SetLayerMetadataValue(
        OpenUsdNativeLayer layer,
        string key,
        OpenUsdNativeMetadataValue value,
        string? stringValue)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(layer);
        value.StructSize = (uint)sizeof(OpenUsdNativeMetadataValue);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerSetMetadata(
                lease.Handle,
                key,
                ref value,
                stringValue,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static OpenUsdNativeMetadataValue GetLayerMetadataValue(
        OpenUsdNativeLayer layer,
        string key,
        OpenUsdNativeMetadataKind kind,
        out string? stringResult)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var lease = new SafeHandleLease(layer);

        var value = new OpenUsdNativeMetadataValue { StructSize = (uint)sizeof(OpenUsdNativeMetadataValue) };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];

        if (kind != OpenUsdNativeMetadataKind.String)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.LayerGetMetadata(
                    lease.Handle,
                    key,
                    (int)kind,
                    ref value,
                    null,
                    0,
                    out _,
                    ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
            stringResult = null;
            return value;
        }

        nuint required;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerGetMetadata(
                lease.Handle,
                key,
                (int)kind,
                ref value,
                null,
                0,
                out required,
                ref error);
            if (status != OpenUsdNativeStatus.BufferTooSmall)
            {
                ThrowIfFailed(status, errorBytes, error);
            }
        }

        if (required > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native metadata string is too large for a managed string.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        fixed (byte* bufferPointer = bytes)
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerGetMetadata(
                lease.Handle,
                key,
                (int)kind,
                ref value,
                bufferPointer,
                required,
                out nuint written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            stringResult = Encoding.UTF8.GetString(bytes, 0, (int)written - 1);
        }
        return value;
    }

    internal static void ReleaseLayer(nint layer)
    {
        NativeMethods.LayerRelease(layer);
    }

    private static void InvokeStageAction(nint stage, NativeHandleAction action)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(stage, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void InvokeStageIdentifierAction(
        OpenUsdNativeStage stage,
        string layerIdentifier,
        NativeStageIdentifierAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerIdentifier);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(
                lease.Handle,
                layerIdentifier,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void InvokeStagePrimAction(
        OpenUsdNativeStage stage,
        string primPath,
        NativeStagePrimAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(lease.Handle, primPath, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void InvokeStagePrimPairAction(
        OpenUsdNativeStage stage,
        string primPath,
        string targetPrimPath,
        NativeStagePrimPairAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrimPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(
                lease.Handle,
                primPath,
                targetPrimPath,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static bool GetStagePrimBool(
        OpenUsdNativeStage stage,
        string primPath,
        NativeStagePrimBoolGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(
                lease.Handle,
                primPath,
                out int value,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value != 0;
        }
    }

    private static string GetStagePrimString(
        OpenUsdNativeStage stage,
        string primPath,
        NativeStagePrimStringGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = getter(
                    handle,
                    primPath,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    private static void InvokeAttributeAction(
        OpenUsdNativeStage stage,
        string primPath,
        string attributeName,
        NativeAttributeAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(
                lease.Handle,
                primPath,
                attributeName,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static double GetStageDouble(OpenUsdNativeStage stage, NativeHandleDoubleGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out double value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static void SetStageDouble(
        OpenUsdNativeStage stage,
        double value,
        NativeHandleDoubleAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(lease.Handle, value, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void EnsureCompatibleAbi()
    {
        ValidateAbiCompatibility(AbiVersion, Capabilities);
    }

    internal static void ValidateAbiCompatibility(uint actual, ulong capabilities)
    {
        if (actual != OpenUsdNativeContract.AbiVersion)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"Native ABI {actual} is incompatible with managed ABI {OpenUsdNativeContract.AbiVersion}.");
        }

        ulong missing = OpenUsdNativeContract.RequiredCapabilities & ~capabilities;
        if (missing != 0)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                $"Native ABI {actual} is missing required capabilities 0x{missing:X}.");
        }
    }

    private static string GetString(NativeStringGetter getter)
    {
        OpenUsdNativeStatus status = getter(null, 0, out nuint required);
        if (status != OpenUsdNativeStatus.BufferTooSmall || required == 0 || required > int.MaxValue)
        {
            throw new OpenUsdNativeException(status, "The native runtime returned an invalid string length.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        fixed (byte* pointer = bytes)
        {
            status = getter(pointer, required, out nuint written);
            if (status != OpenUsdNativeStatus.Ok || written != required)
            {
                throw new OpenUsdNativeException(status, "The native runtime could not return the requested string.");
            }
        }

        return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
    }

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error)
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }

        throw CreateNativeException(status, errorBytes, error);
    }

    private static void ThrowIfFailedAndReleaseStringList(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error,
        ref nint list)
    {
        if (status != OpenUsdNativeStatus.Ok && list != 0)
        {
            NativeMethods.StringListRelease(list);
            list = 0;
        }
        ThrowIfFailed(status, errorBytes, error);
    }

    private static void ThrowIfFailedAndReleasePayloadArcList(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error,
        ref nint list)
    {
        if (status != OpenUsdNativeStatus.Ok && list != 0)
        {
            NativeMethods.PayloadArcListRelease(list);
            list = 0;
        }
        ThrowIfFailed(status, errorBytes, error);
    }

    internal static nint CreateTsSpline()
    {
        nint spline = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.TsSplineCreate(out spline, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
        return spline;
    }

    internal static void ReleaseTsSpline(nint spline) => NativeMethods.TsSplineRelease(spline);

    internal static void SetTsSplineData(nint spline, OpenUsdNativeTsSplineData data)
    {
        ArgumentNullException.ThrowIfNull(data.Knots);
        var view = new NativeTsSplineDataView
        {
            StructSize = (uint)sizeof(NativeTsSplineDataView),
            Version = 1,
            CurveType = data.CurveType,
            IsTimeValued = data.IsTimeValued ? 1 : 0,
            PreExtrapolation = new NativeTsExtrapolationRecord
            {
                Mode = data.PreExtrapolation.Mode,
                Slope = data.PreExtrapolation.Slope
            },
            PostExtrapolation = new NativeTsExtrapolationRecord
            {
                Mode = data.PostExtrapolation.Mode,
                Slope = data.PostExtrapolation.Slope
            },
            KnotsSize = (nuint)(data.Knots.Length * sizeof(OpenUsdNativeTsKnotRecord)),
            KnotCount = (nuint)data.Knots.Length
        };
        fixed (OpenUsdNativeTsKnotRecord* knots = data.Knots)
        {
            view.Knots = knots;
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.TsSplineSetData(spline, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
        }
    }

    internal static OpenUsdNativeTsSplineData GetTsSplineData(nint spline)
    {
        var view = new NativeTsSplineDataView
        {
            StructSize = (uint)sizeof(NativeTsSplineDataView),
            Version = 1
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.TsSplineGetData(spline, ref view, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
        if (view.KnotsSize != view.KnotCount * (nuint)sizeof(OpenUsdNativeTsKnotRecord) ||
            view.KnotCount > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid Ts knot buffer.");
        }
        var knots = new OpenUsdNativeTsKnotRecord[(int)view.KnotCount];
        new ReadOnlySpan<OpenUsdNativeTsKnotRecord>(view.Knots, knots.Length).CopyTo(knots);
        return new OpenUsdNativeTsSplineData(
            view.CurveType,
            view.IsTimeValued != 0,
            new OpenUsdNativeTsExtrapolation(
                view.PreExtrapolation.Mode,
                view.PreExtrapolation.Slope),
            new OpenUsdNativeTsExtrapolation(
                view.PostExtrapolation.Mode,
                view.PostExtrapolation.Slope),
            knots);
    }

    internal static double? EvalTsSpline(nint spline, double time)
    {
        double value;
        int hasValue;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.TsSplineEval(
                spline,
                time,
                out value,
                out hasValue,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
        return hasValue == 0 ? null : value;
    }

    internal static OpenUsdNativeValidationMetadata[] GetValidationMetadata()
    {
        var view = new NativeValidationMetadataView
        {
            StructSize = (uint)sizeof(NativeValidationMetadataView),
            Version = 1
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ValidationGetRegisteredValidators(
                out list,
                ref view,
                ref error);
            if (status != OpenUsdNativeStatus.Ok && list != 0)
            {
                NativeMethods.ValidationMetadataListRelease(list);
                list = 0;
            }
            ThrowIfFailed(status, errorBytes, error);
        }
        try
        {
            return DecodeValidationMetadata(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.ValidationMetadataListRelease(list);
            }
        }
    }

    internal static OpenUsdNativeValidationError[] ValidateStage(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        return ValidateCore(
            static (nint handle, ref NativeValidationErrorView view, ref NativeErrorBuffer error, out nint list) =>
                NativeMethods.ValidationValidateStage(handle, out list, ref view, ref error),
            lease.Handle);
    }

    internal static OpenUsdNativeValidationError[] ValidatePrim(
        OpenUsdNativeStage stage,
        string primPath)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeValidationErrorView
        {
            StructSize = (uint)sizeof(NativeValidationErrorView),
            Version = 1
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ValidationValidatePrim(
                lease.Handle,
                primPath,
                out list,
                ref view,
                ref error);
            if (status != OpenUsdNativeStatus.Ok && list != 0)
            {
                NativeMethods.ValidationErrorListRelease(list);
                list = 0;
            }
            ThrowIfFailed(status, errorBytes, error);
        }
        try
        {
            return DecodeValidationErrors(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.ValidationErrorListRelease(list);
            }
        }
    }

    private delegate OpenUsdNativeStatus ValidationStageInvoker(
        nint handle,
        ref NativeValidationErrorView view,
        ref NativeErrorBuffer error,
        out nint list);

    private static OpenUsdNativeValidationError[] ValidateCore(
        ValidationStageInvoker invoker,
        nint handle)
    {
        var view = new NativeValidationErrorView
        {
            StructSize = (uint)sizeof(NativeValidationErrorView),
            Version = 1
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = invoker(handle, ref view, ref error, out list);
            if (status != OpenUsdNativeStatus.Ok && list != 0)
            {
                NativeMethods.ValidationErrorListRelease(list);
                list = 0;
            }
            ThrowIfFailed(status, errorBytes, error);
        }
        try
        {
            return DecodeValidationErrors(view);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.ValidationErrorListRelease(list);
            }
        }
    }

    private static OpenUsdNativeValidationMetadata[] DecodeValidationMetadata(
        NativeValidationMetadataView view)
    {
        if (view.Version != 1 || view.StructSize < sizeof(NativeValidationMetadataView) ||
            view.RecordsSize != view.Count * (nuint)sizeof(OpenUsdNativeValidationMetadataRecord) ||
            view.OffsetsSize != view.StringCount * (nuint)sizeof(nuint) ||
            view.Count > int.MaxValue || view.StringCount > int.MaxValue ||
            view.DataSize > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid validation metadata buffer.");
        }
        var records = new ReadOnlySpan<OpenUsdNativeValidationMetadataRecord>(
            view.Records,
            (int)view.Count);
        string[] strings = NativePackedStringListDecoder.Decode(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<nuint>(view.Offsets, (int)view.StringCount),
            "validation metadata buffer");
        var result = new OpenUsdNativeValidationMetadata[records.Length];
        for (int index = 0; index < records.Length; index++)
        {
            OpenUsdNativeValidationMetadataRecord record = records[index];
            if (record.StringCount != 3)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned invalid validation metadata fields.");
            }
            int field = (int)record.StringOffset;
            result[index] = new OpenUsdNativeValidationMetadata(
                strings[field],
                strings[field + 1],
                strings[field + 2],
                strings[(int)record.KeywordOffset..(int)(record.KeywordOffset + record.KeywordCount)],
                strings[(int)record.SchemaTypeOffset..(int)(record.SchemaTypeOffset + record.SchemaTypeCount)],
                record.IsSuite != 0,
                record.IsTimeDependent != 0);
        }
        return result;
    }

    private static OpenUsdNativeValidationError[] DecodeValidationErrors(
        NativeValidationErrorView view)
    {
        if (view.Version != 1 || view.StructSize < sizeof(NativeValidationErrorView) ||
            view.RecordsSize != view.Count * (nuint)sizeof(OpenUsdNativeValidationErrorRecord) ||
            view.OffsetsSize != view.StringCount * (nuint)sizeof(nuint) ||
            view.Count > int.MaxValue || view.StringCount > int.MaxValue ||
            view.DataSize > int.MaxValue)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "The native runtime returned an invalid validation error buffer.");
        }
        var records = new ReadOnlySpan<OpenUsdNativeValidationErrorRecord>(
            view.Records,
            (int)view.Count);
        string[] strings = NativePackedStringListDecoder.Decode(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<nuint>(view.Offsets, (int)view.StringCount),
            "validation error buffer");
        var result = new OpenUsdNativeValidationError[records.Length];
        for (int index = 0; index < records.Length; index++)
        {
            OpenUsdNativeValidationErrorRecord record = records[index];
            if (record.StringCount != 3)
            {
                throw new OpenUsdNativeException(
                    OpenUsdNativeStatus.NativeError,
                    "The native runtime returned invalid validation error fields.");
            }
            int field = (int)record.StringOffset;
            result[index] = new OpenUsdNativeValidationError(
                record.Severity,
                strings[field],
                strings[field + 1],
                strings[field + 2],
                strings[(int)record.SiteOffset..(int)(record.SiteOffset + record.SiteCount)]);
        }
        return result;
    }

    private static OpenUsdNativeException CreateNativeException(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error)
    {
        int terminator = errorBytes.IndexOf((byte)0);
        int length = terminator >= 0 ? terminator : errorBytes.Length;
        string message = length == 0
            ? $"Native OpenUSD operation failed with status {status}."
            : Encoding.UTF8.GetString(errorBytes[..length]);
        if (error.Required > error.Capacity)
        {
            message += $" The full native diagnostic required {error.Required} bytes.";
        }

        return new OpenUsdNativeException(status, message);
    }

    private delegate OpenUsdNativeStatus NativeStringGetter(
        byte* buffer,
        nuint capacity,
        out nuint required);

    private delegate OpenUsdNativeStatus NativeHandleAction(
        nint handle,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeHandleDoubleGetter(
        nint handle,
        out double value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeHandleDoubleAction(
        nint handle,
        double value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeStageIdentifierAction(
        nint stage,
        string layerIdentifier,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeStagePrimAction(
        nint stage,
        string primPath,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeStagePrimPairAction(
        nint stage,
        string primPath,
        string targetPrimPath,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeStagePrimBoolGetter(
        nint stage,
        string primPath,
        out int value,
        ref NativeErrorBuffer error);

    private unsafe delegate OpenUsdNativeStatus NativeStagePrimStringGetter(
        nint stage,
        string primPath,
        byte* buffer,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error);

    private unsafe delegate OpenUsdNativeStatus NativeArraySetter<T>(
        nint stage,
        string primPath,
        string attributeName,
        T* values,
        nuint count,
        int timeSampled,
        double timeCode,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private unsafe delegate OpenUsdNativeStatus NativeArrayGetter<T>(
        nint stage,
        string primPath,
        string attributeName,
        int timeSampled,
        double timeCode,
        T* values,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private unsafe delegate OpenUsdNativeStatus NativeGeomArraySetter<T>(
        nint stage,
        string primPath,
        T* values,
        nuint count,
        int timeSampled,
        double timeCode,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private unsafe delegate OpenUsdNativeStatus NativeGeomArrayGetter<T>(
        nint stage,
        string primPath,
        int timeSampled,
        double timeCode,
        T* values,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private unsafe delegate OpenUsdNativeStatus NativeGeomUntimedArrayGetter<T>(
        nint stage,
        string primPath,
        T* values,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error)
        where T : unmanaged;

    private delegate OpenUsdNativeStatus NativeGeomIntSetter(
        nint stage,
        string primPath,
        int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeGeomIntGetter(
        nint stage,
        string primPath,
        out int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeGeomTimedIntSetter(
        nint stage,
        string primPath,
        int value,
        int timeSampled,
        double timeCode,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeGeomTimedIntGetter(
        nint stage,
        string primPath,
        int timeSampled,
        double timeCode,
        out int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePrimStringListGetter(
        nint stage,
        string primPath,
        out nint list,
        ref NativeStringListView view,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeAttributeAction(
        nint stage,
        string primPath,
        string attributeName,
        ref NativeErrorBuffer error);

    private ref struct SafeHandleLease
    {
        private readonly SafeHandle _owner;
        private readonly bool _addedReference;

        internal SafeHandleLease(SafeHandle owner)
        {
            ObjectDisposedException.ThrowIf(owner.IsInvalid || owner.IsClosed, owner);
            _owner = owner;
            bool addedReference = false;
            owner.DangerousAddRef(ref addedReference);
            _addedReference = addedReference;
            Handle = owner.DangerousGetHandle();
        }

        internal nint Handle { get; }

        public void Dispose()
        {
            if (_addedReference)
            {
                _owner.DangerousRelease();
            }
        }
    }

}
