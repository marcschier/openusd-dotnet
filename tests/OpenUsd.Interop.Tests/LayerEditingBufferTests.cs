// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop.Tests;

public sealed class LayerEditingBufferTests
{
    [Test]
    public async Task BufferAbiUsesOnlyPointerAndNativeSize()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRuntime.NativeEditBufferView>()).IsEqualTo(2 * IntPtr.Size);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeRuntime.NativeEditBufferView>("Data").ToInt32()).IsEqualTo(0);
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeRuntime.NativeEditBufferView>("Size").ToInt32())
            .IsEqualTo(IntPtr.Size);
    }

    [Test]
    public async Task AppliedBuffersAreCopiedBeforeNativeOwnerRelease()
    {
        byte[] original = [0x55, 0x45, 0x44, 0x31, 0, 1, 2, 3];
        byte[] copy = Copy(original, "")!;
        original[0] = 99;
        await Assert.That(copy[0]).IsEqualTo((byte)0x55);
        copy[1] = 99;
        await Assert.That(original[1]).IsEqualTo((byte)0x45);
    }

    [Test]
    public async Task MaximumPacketExtentIsAdmittedWithoutTruncation()
    {
        var bytes = new byte[OpenUsdNativeRuntime.LayerEditingMaximumBytes];
        bytes[0] = 0x55;
        bytes[^1] = 42;
        byte[] copy = Copy(bytes, "")!;
        await Assert.That(copy.Length).IsEqualTo(4 * 1024 * 1024);
        await Assert.That(copy[0]).IsEqualTo((byte)0x55);
        await Assert.That(copy[^1]).IsEqualTo((byte)42);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task DomainOutcomesHaveNoAfterPayload(int outcome)
    {
        await Assert.That(CopyOutcome(outcome)).IsNull();
    }

    [Test]
    [Arguments("negative-outcome")]
    [Arguments("unknown-outcome")]
    [Arguments("conflict-owner")]
    [Arguments("conflict-data")]
    [Arguments("conflict-size")]
    [Arguments("missing-owner")]
    [Arguments("invalid-owner")]
    [Arguments("misaligned-owner")]
    [Arguments("missing-data")]
    [Arguments("empty-size")]
    [Arguments("over-budget")]
    [Arguments("size-overflow")]
    [Arguments("pointer-overflow")]
    public async Task InvalidOwnershipAndExtentsFailBeforeDereferencing(string corruption)
    {
        await Assert.That(() => Copy([1, 2, 3, 4], corruption)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task SavedAcknowledgementIsAnExactBoolean()
    {
        await Assert.That(OpenUsdNativeRuntime.DecodeLayerSavedAcknowledgement(0)).IsFalse();
        await Assert.That(OpenUsdNativeRuntime.DecodeLayerSavedAcknowledgement(1)).IsTrue();
        await Assert.That(() => OpenUsdNativeRuntime.DecodeLayerSavedAcknowledgement(-1))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() => OpenUsdNativeRuntime.DecodeLayerSavedAcknowledgement(2))
            .Throws<OpenUsdNativeException>();
    }

    private static unsafe byte[]? CopyOutcome(int outcome) =>
        OpenUsdNativeRuntime.CopyLayerEditingBuffer(0, default, outcome);

    private static unsafe byte[]? Copy(byte[] bytes, string corruption)
    {
        fixed (byte* pointer = bytes)
        {
            nint owner = 8;
            int outcome = 0;
            var view = new OpenUsdNativeRuntime.NativeEditBufferView { Data = pointer, Size = (nuint)bytes.Length };
            switch (corruption)
            {
                case "negative-outcome":
                    outcome = -1;
                    break;
                case "unknown-outcome":
                    outcome = 4;
                    break;
                case "conflict-owner":
                    outcome = 1;
                    view = default;
                    break;
                case "conflict-data":
                    outcome = 1;
                    owner = 0;
                    view.Size = 0;
                    break;
                case "conflict-size":
                    outcome = 1;
                    owner = 0;
                    view.Data = null;
                    break;
                case "missing-owner":
                    owner = 0;
                    break;
                case "invalid-owner":
                    owner = -1;
                    break;
                case "misaligned-owner":
                    owner = 1;
                    break;
                case "missing-data":
                    view.Data = null;
                    break;
                case "empty-size":
                    view.Size = 0;
                    break;
                case "over-budget":
                    view.Size = OpenUsdNativeRuntime.LayerEditingMaximumBytes + 1u;
                    break;
                case "size-overflow":
                    view.Size = nuint.MaxValue;
                    break;
                case "pointer-overflow":
                    view.Data = (byte*)(nuint.MaxValue - 1);
                    break;
            }
            return OpenUsdNativeRuntime.CopyLayerEditingBuffer(owner, view, outcome);
        }
    }
}
