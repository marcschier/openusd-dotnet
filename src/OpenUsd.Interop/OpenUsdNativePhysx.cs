// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

internal enum OpenUsdNativePhysxStatus
{
    Ok = 0,
    InvalidArgument = 1,
    BufferTooSmall = 2,
    NativeError = 3
}

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static string PhysxVersion
    {
        get
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            OpenUsdNativePhysxStatus status = GetPhysxString(
                errorBytes,
                PhysxNativeMethods.GetVersion,
                out string value);
            ThrowIfPhysxFailed(status, errorBytes, default);
            return value;
        }
    }

    internal static void SimulatePhysicsStageFile(string stagePath, float timeStep, uint stepCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        if (!float.IsFinite(timeStep) || timeStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStep), "The time step must be positive and finite.");
        }

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativePhysxStatus status = PhysxNativeMethods.SimulateStageFile(
                stagePath,
                timeStep,
                stepCount,
                ref error);
            ThrowIfPhysxFailed(status, errorBytes, error);
        }
    }

    private static OpenUsdNativePhysxStatus GetPhysxString(
        Span<byte> errorBytes,
        NativePhysxStringGetter getter,
        out string value)
    {
        value = string.Empty;
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativePhysxStatus status = getter(null, 0, out nuint required, ref error);
            if (status != OpenUsdNativePhysxStatus.BufferTooSmall || required == 0 || required > int.MaxValue)
            {
                return status;
            }

            byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
            fixed (byte* buffer = bytes)
            {
                error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                status = getter(buffer, required, out nuint written, ref error);
                if (status == OpenUsdNativePhysxStatus.Ok && written == required)
                {
                    value = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
                }
                return status;
            }
        }
    }

    private static void ThrowIfPhysxFailed(
        OpenUsdNativePhysxStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error)
    {
        if (status == OpenUsdNativePhysxStatus.Ok)
        {
            return;
        }

        OpenUsdNativeStatus mapped = status switch
        {
            OpenUsdNativePhysxStatus.InvalidArgument => OpenUsdNativeStatus.InvalidArgument,
            OpenUsdNativePhysxStatus.BufferTooSmall => OpenUsdNativeStatus.BufferTooSmall,
            _ => OpenUsdNativeStatus.NativeError
        };
        throw CreateNativeException(mapped, errorBytes, error);
    }

    private unsafe delegate OpenUsdNativePhysxStatus NativePhysxStringGetter(
        byte* buffer,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error);

    private static partial class PhysxNativeMethods
    {
        private const string LibraryName = "openusd_physx";

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_physx_get_version")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial OpenUsdNativePhysxStatus GetVersion(
            byte* buffer,
            nuint capacity,
            out nuint required,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_physx_stage_simulate_file",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativePhysxStatus SimulateStageFile(
            string stagePath,
            float timeStep,
            uint stepCount,
            ref NativeErrorBuffer error);
    }
}
