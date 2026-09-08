// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

public static unsafe partial class OpenUsdNativeRuntime
{
    internal const int ReviewMaximumEnvelopeBytes = 24 * 1024 * 1024;
    internal const int ReviewMaximumDocumentBytes = 16 * 1024 * 1024;

    internal static OpenUsdNativeStage OpenStageForReview(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        _ = LayerEditingTextSize(sourcePath);
        EnsureCompatibleAbi();
        nint stage = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.StageOpenForReview(sourcePath, out stage, ref error);
                ThrowIfFailed(status, errorBytes, error);
                if (!IsReviewOwner(stage))
                {
                    throw InvalidReviewResult("missing or misaligned stage handle");
                }
                var result = new OpenUsdNativeStage(stage);
                stage = 0;
                return result;
            }
            finally
            {
                if (IsReviewOwner(stage))
                {
                    NativeMethods.StageRelease(stage);
                }
            }
        }
    }

    internal static byte[] CaptureReviewSourceBinding(OpenUsdNativeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        nint owner = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.StageReviewSourceBinding(
                    lease.Handle, out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CopyReviewBuffer(owner, view);
            }
            finally
            {
                ReleaseReviewBuffer(owner);
            }
        }
    }

    internal static byte[] CaptureReviewDocument(
        OpenUsdNativeLayer layer, ReadOnlySpan<byte> binding, string targetDocumentPath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ValidateReviewInput(binding, ReviewMaximumEnvelopeBytes);
        int targetSize = LayerEditingTextSize(targetDocumentPath);
        if (!Path.IsPathFullyQualified(targetDocumentPath))
        {
            throw new ArgumentException("Portable review publication requires an absolute filesystem path.",
                nameof(targetDocumentPath));
        }
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(layer);
        nint owner = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* bindingPointer = binding)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.LayerReviewCapture(
                    lease.Handle, bindingPointer, (nuint)binding.Length, targetDocumentPath, (nuint)targetSize,
                    out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CopyReviewBuffer(owner, view);
            }
            finally
            {
                ReleaseReviewBuffer(owner);
            }
        }
    }

    internal static byte[] ReadReviewDocument(ReadOnlySpan<byte> document, string expectedSourcePath)
    {
        ValidateReviewInput(document, ReviewMaximumDocumentBytes);
        int sourceSize = LayerEditingTextSize(expectedSourcePath);
        EnsureCompatibleAbi();
        nint owner = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* documentPointer = document)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.ReviewDocumentRead(
                    documentPointer, (nuint)document.Length, expectedSourcePath, (nuint)sourceSize,
                    out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CopyReviewBuffer(owner, view);
            }
            finally
            {
                ReleaseReviewBuffer(owner);
            }
        }
    }

    internal static byte[] InspectReviewDocument(ReadOnlySpan<byte> document)
    {
        ValidateReviewInput(document, ReviewMaximumDocumentBytes);
        EnsureCompatibleAbi();
        nint owner = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* documentPointer = document)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.ReviewDocumentInspect(
                    documentPointer, (nuint)document.Length, out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CopyReviewBuffer(owner, view);
            }
            finally
            {
                ReleaseReviewBuffer(owner);
            }
        }
    }

    internal static OpenUsdNativeLayerEditResult ImportReviewDocument(
        OpenUsdNativeStage stage, ReadOnlySpan<byte> document, ReadOnlySpan<byte> binding)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ValidateReviewInput(document, ReviewMaximumDocumentBytes);
        ValidateReviewInput(binding, ReviewMaximumEnvelopeBytes);
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        nint owner = 0;
        int outcome = 0;
        var view = new NativeEditBufferView();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* documentPointer = document)
        fixed (byte* bindingPointer = binding)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            try
            {
                OpenUsdNativeStatus status = NativeMethods.StageReviewImport(
                    lease.Handle, documentPointer, (nuint)document.Length, bindingPointer, (nuint)binding.Length,
                    out outcome, out owner, ref view, ref error);
                ThrowIfFailed(status, errorBytes, error);
                byte[]? packet = CopyLayerEditingBuffer(owner, view, outcome);
                string? diagnostic = errorBytes[0] == 0
                    ? null
                    : CreateNativeException(OpenUsdNativeStatus.NativeError, errorBytes, error).Message;
                return new OpenUsdNativeLayerEditResult(outcome, packet, diagnostic);
            }
            finally
            {
                ReleaseReviewBuffer(owner);
            }
        }
    }

    internal static bool AcknowledgeReviewSaved(OpenUsdNativeLayer layer, ReadOnlySpan<byte> receipt)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ValidateReviewInput(receipt, ReviewMaximumEnvelopeBytes);
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(layer);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        errorBytes.Clear();
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* receiptPointer = receipt)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.LayerReviewAcknowledgeSaved(
                lease.Handle, receiptPointer, (nuint)receipt.Length, out int acknowledged, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return DecodeLayerSavedAcknowledgement(acknowledged);
        }
    }

    internal static byte[] CopyReviewBuffer(nint owner, NativeEditBufferView view)
    {
        if (!IsReviewOwner(owner) || view.Data == null ||
            view.Size is 0 or > ReviewMaximumEnvelopeBytes || (nuint)view.Data > nuint.MaxValue - view.Size)
        {
            throw InvalidReviewResult("missing/misaligned owner, missing data or invalid portable buffer size");
        }
        return new ReadOnlySpan<byte>(view.Data, (int)view.Size).ToArray();
    }

    private static bool IsReviewOwner(nint owner) =>
        owner != 0 && owner != -1 && (nuint)owner % (nuint)sizeof(nint) == 0;

    private static void ReleaseReviewBuffer(nint owner)
    {
        if (IsReviewOwner(owner))
        {
            NativeMethods.EditBufferRelease(owner);
        }
    }

    private static void ValidateReviewInput(ReadOnlySpan<byte> bytes, int maximumBytes)
    {
        if (bytes.IsEmpty || bytes.Length > maximumBytes)
        {
            throw new ArgumentException("Portable review input is empty or exceeds its byte budget.", nameof(bytes));
        }
    }

    private static OpenUsdNativeException InvalidReviewResult(string detail) =>
        new(OpenUsdNativeStatus.NativeError, $"Invalid native portable review result: {detail}.");
}
