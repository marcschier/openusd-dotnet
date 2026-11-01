// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

public static unsafe partial class OpenUsdSilkRuntime
{
    internal static VolumeBudgetHandle CreateVolumeBudget(ulong maximumBytes)
    {
        ValidateNativeAbi();
        Span<byte> message = stackalloc byte[ErrorBufferSize];
        fixed (byte* pointer = message)
        {
            var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
            nint budget = 0;
            try
            {
                ThrowIfFailed(NativeMethods.CreateVolumeBudget(maximumBytes, out budget, ref error), message, error);
                if (budget == 0)
                {
                    throw new OpenUsdSilkException(
                        OpenUsdNativeStatus.NativeError, "The native decoded-field budget handle is invalid.");
                }
                var result = new VolumeBudgetHandle(budget);
                budget = 0;
                return result;
            }
            catch (EntryPointNotFoundException exception)
            {
                throw new NotSupportedException(
                    "Decoded-field admission requires the native volume-budget extension.", exception);
            }
            finally
            {
                if (budget != 0)
                {
                    NativeMethods.ReleaseVolumeBudget(budget);
                }
            }
        }
    }

    internal static void SetVolumeBudget(nint session, VolumeBudgetHandle budget)
    {
        bool retained = false;
        try
        {
            budget.DangerousAddRef(ref retained);
            Span<byte> message = stackalloc byte[ErrorBufferSize];
            fixed (byte* pointer = message)
            {
                var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
                try
                {
                    ThrowIfFailed(NativeMethods.SetVolumeBudget(
                        session, budget.DangerousGetHandle(), ref error), message, error);
                }
                catch (EntryPointNotFoundException exception)
                {
                    throw new NotSupportedException(
                        "Decoded-field admission requires the native volume-budget extension.", exception);
                }
            }
        }
        finally
        {
            if (retained)
            {
                budget.DangerousRelease();
            }
        }
    }

    internal static SilkVolumeFieldUsage GetVolumeBudgetUsage(VolumeBudgetHandle budget, ulong maximumBytes)
    {
        bool retained = false;
        try
        {
            budget.DangerousAddRef(ref retained);
            var usage = new NativeVolumeBudgetUsage
            {
                StructSize = (uint)sizeof(NativeVolumeBudgetUsage),
                Version = 1
            };
            Span<byte> message = stackalloc byte[ErrorBufferSize];
            fixed (byte* pointer = message)
            {
                var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
                try
                {
                    ThrowIfFailed(NativeMethods.GetVolumeBudgetUsage(
                        budget.DangerousGetHandle(), ref usage, ref error), message, error);
                }
                catch (EntryPointNotFoundException exception)
                {
                    throw new NotSupportedException(
                        "Decoded-field admission requires the native volume-budget extension.", exception);
                }
            }
            if (usage.StructSize != sizeof(NativeVolumeBudgetUsage) || usage.Version != 1 ||
                usage.MaximumBytes != maximumBytes || usage.ReservedBytes > usage.PeakReservedBytes ||
                usage.PeakReservedBytes > maximumBytes ||
                (usage.ReservedBytes == 0) != (usage.ReservationCount == 0))
            {
                throw new OpenUsdSilkException(
                    OpenUsdNativeStatus.NativeError, "Native decoded-field accounting is invalid.");
            }
            return new SilkVolumeFieldUsage(
                usage.MaximumBytes, usage.ReservedBytes, usage.PeakReservedBytes, usage.ReservationCount);
        }
        finally
        {
            if (retained)
            {
                budget.DangerousRelease();
            }
        }
    }

    internal sealed class VolumeBudgetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal VolumeBudgetHandle(nint budget) : base(ownsHandle: true) => SetHandle(budget);

        protected override bool ReleaseHandle()
        {
            NativeMethods.ReleaseVolumeBudget(handle);
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVolumeBudgetUsage
    {
        internal uint StructSize;
        internal uint Version;
        internal ulong MaximumBytes;
        internal ulong ReservedBytes;
        internal ulong PeakReservedBytes;
        internal ulong ReservationCount;
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_volume_budget_create")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CreateVolumeBudget(
            ulong maximumBytes, out nint budget, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_set_volume_budget")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetVolumeBudget(
            nint session, nint budget, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_volume_budget_get_usage")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetVolumeBudgetUsage(
            nint budget, ref NativeVolumeBudgetUsage usage, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_volume_budget_release")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial void ReleaseVolumeBudget(nint budget);
    }
}
