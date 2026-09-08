// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop;

internal readonly record struct OpenUsdNativeLayerEditResult(int Outcome, byte[]? Packet, string? Diagnostic);

public static unsafe partial class OpenUsdNativeRuntime
{
    internal const int LayerEditingMaximumBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding LayerEditingUtf8 = new(false, true);

    internal static OpenUsdNativeLayer GetUserReviewLayer(OpenUsdNativeStage stage) =>
        GetEditingLayer(stage, null);

    internal static OpenUsdNativeLayer GetLocalEditingLayer(OpenUsdNativeStage stage, string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        return GetEditingLayer(stage, identifier);
    }

    internal static byte[] GetLayerEditingState(OpenUsdNativeLayer layer) =>
        InvokeLayerEditing(layer, LayerEditingCall.State).Packet!;

    internal static byte[] CaptureLayerAuthored(OpenUsdNativeLayer layer, ReadOnlySpan<byte> addresses) =>
        InvokeLayerEditing(layer, LayerEditingCall.Capture, addresses).Packet!;

    internal static byte[] CaptureLayerCheckpoint(OpenUsdNativeLayer layer) =>
        InvokeLayerEditing(layer, LayerEditingCall.Checkpoint).Packet!;

    internal static OpenUsdNativeLayerEditResult ApplyLayerEdits(
        OpenUsdNativeLayer layer, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> mutations) =>
        InvokeLayerEditing(layer, LayerEditingCall.Apply, expected, mutations);

    internal static OpenUsdNativeLayerEditResult RestoreLayerAuthored(
        OpenUsdNativeLayer layer, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> restore) =>
        InvokeLayerEditing(layer, LayerEditingCall.Restore, expected, restore);

    internal static OpenUsdNativeLayerEditResult RestoreLayerCheckpoint(
        OpenUsdNativeLayer layer, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> restore) =>
        InvokeLayerEditing(layer, LayerEditingCall.RestoreCheckpoint, expected, restore);

    internal static bool AcknowledgeLayerSaved(OpenUsdNativeLayer layer, ReadOnlySpan<byte> checkpoint)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ValidateLayerEditingInput(checkpoint);
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* checkpointPointer = checkpoint)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerEditAcknowledgeSaved(
                lease.Handle, checkpointPointer, (nuint)checkpoint.Length, out int acknowledged, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return DecodeLayerSavedAcknowledgement(acknowledged);
        }
    }

    internal static byte[] ExportLayerCheckpoint(ReadOnlySpan<byte> checkpoint, string destinationPath)
    {
        ValidateLayerEditingInput(checkpoint);
        int destinationSize = LayerEditingTextSize(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath) ||
            !destinationPath.EndsWith(".usda", StringComparison.Ordinal))
        {
            throw new ArgumentException("Checkpoint export requires an absolute .usda destination.",
                nameof(destinationPath));
        }
        EnsureCompatibleAbi();
        nint owner = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* checkpointPointer = checkpoint)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.EditCheckpointExport(
                    checkpointPointer, (nuint)checkpoint.Length, destinationPath, (nuint)destinationSize,
                    out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CopyLayerEditingBuffer(owner, view, 0)!;
            }
            finally
            {
                if (owner != 0)
                {
                    NativeMethods.EditBufferRelease(owner);
                }
            }
        }
    }

    internal static byte[]? CopyLayerEditingBuffer(nint owner, NativeEditBufferView view, int outcome)
    {
        if (outcome is < 0 or > 3)
        {
            throw InvalidLayerEditingResult("invalid transaction outcome");
        }
        if (outcome != 0)
        {
            if (owner != 0 || view.Data != null || view.Size != 0)
            {
                throw InvalidLayerEditingResult("a non-applied outcome owns a buffer or has a nonempty view");
            }
            return null;
        }
        if (owner == 0 || owner == -1 || (nuint)owner % (nuint)sizeof(nint) != 0 ||
            view.Data == null || view.Size is 0 or > LayerEditingMaximumBytes ||
            (nuint)view.Data > nuint.MaxValue - view.Size)
        {
            throw InvalidLayerEditingResult("missing/misaligned owner, missing data or invalid buffer size");
        }
        return new ReadOnlySpan<byte>(view.Data, (int)view.Size).ToArray();
    }

    internal static bool DecodeLayerSavedAcknowledgement(int acknowledged) => acknowledged switch
    {
        0 => false,
        1 => true,
        _ => throw InvalidLayerEditingResult("invalid saved acknowledgement flag")
    };

    private enum LayerEditingCall
    {
        State,
        Capture,
        Checkpoint,
        Apply,
        Restore,
        RestoreCheckpoint
    }

    private static OpenUsdNativeLayer GetEditingLayer(OpenUsdNativeStage stage, string? identifier)
    {
        ArgumentNullException.ThrowIfNull(stage);
        int size = identifier is null ? 0 : LayerEditingTextSize(identifier);
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        nint handle = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = identifier is null
                    ? NativeMethods.StageEditGetUserLayer(lease.Handle, out handle, ref error)
                    : NativeMethods.StageEditGetLocalLayer(
                        lease.Handle, identifier, (nuint)size, out handle, ref error);
                ThrowIfFailed(status, errorBytes, error);
                if (handle == 0 || handle == -1)
                {
                    throw InvalidLayerEditingResult("missing layer handle");
                }
                var result = new OpenUsdNativeLayer(handle);
                handle = 0;
                return result;
            }
            finally
            {
                if (handle != 0)
                {
                    NativeMethods.LayerRelease(handle);
                }
            }
        }
    }

    private static OpenUsdNativeLayerEditResult InvokeLayerEditing(
        OpenUsdNativeLayer layer, LayerEditingCall call,
        ReadOnlySpan<byte> first = default, ReadOnlySpan<byte> second = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (call is not (LayerEditingCall.State or LayerEditingCall.Checkpoint))
        {
            ValidateLayerEditingInput(first);
        }
        if (call is LayerEditingCall.Apply or LayerEditingCall.Restore or LayerEditingCall.RestoreCheckpoint)
        {
            ValidateLayerEditingInput(second);
        }
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(layer);
        nint owner = 0;
        int outcome = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* firstPointer = first)
        fixed (byte* secondPointer = second)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = call switch
                {
                    LayerEditingCall.State => NativeMethods.LayerEditGetState(
                        lease.Handle, out owner, ref view, ref error),
                    LayerEditingCall.Capture => NativeMethods.LayerEditCapture(
                        lease.Handle, firstPointer, (nuint)first.Length, out owner, ref view, ref error),
                    LayerEditingCall.Checkpoint => NativeMethods.LayerEditCheckpoint(
                        lease.Handle, out owner, ref view, ref error),
                    LayerEditingCall.Apply => NativeMethods.LayerEditApply(
                        lease.Handle, firstPointer, (nuint)first.Length, secondPointer, (nuint)second.Length,
                        out outcome, out owner, ref view, ref error),
                    LayerEditingCall.Restore => NativeMethods.LayerEditRestore(
                        lease.Handle, firstPointer, (nuint)first.Length, secondPointer, (nuint)second.Length,
                        out outcome, out owner, ref view, ref error),
                    LayerEditingCall.RestoreCheckpoint => NativeMethods.LayerEditCheckpointRestore(
                        lease.Handle, firstPointer, (nuint)first.Length, secondPointer, (nuint)second.Length,
                        out outcome, out owner, ref view, ref error),
                    _ => throw new InvalidOperationException("Unsupported layer editing call.")
                };
                ThrowIfFailed(status, errorBytes, error);
                byte[]? packet = CopyLayerEditingBuffer(owner, view, outcome);
                string? diagnostic = errorBytes[0] == 0
                    ? null
                    : CreateNativeException(OpenUsdNativeStatus.NativeError, errorBytes, error).Message;
                return new OpenUsdNativeLayerEditResult(outcome, packet, diagnostic);
            }
            finally
            {
                if (owner != 0)
                {
                    NativeMethods.EditBufferRelease(owner);
                }
            }
        }
    }

    private static void ValidateLayerEditingInput(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is < 12 or > LayerEditingMaximumBytes)
        {
            throw new ArgumentException("A layer editing packet must contain 12 bytes through 4 MiB.", nameof(packet));
        }
    }

    private static int LayerEditingTextSize(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 4096 || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Layer editing text must be NUL-free and at most 4096 UTF-8 bytes.", nameof(value));
        }
        int size = LayerEditingUtf8.GetByteCount(value);
        if (size > 4096)
        {
            throw new ArgumentException("Layer editing text exceeds 4096 UTF-8 bytes.", nameof(value));
        }
        return size;
    }

    private static OpenUsdNativeException InvalidLayerEditingResult(string detail) =>
        new(OpenUsdNativeStatus.NativeError, $"Invalid native layer editing result: {detail}.");
}
