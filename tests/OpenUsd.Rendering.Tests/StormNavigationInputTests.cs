// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class StormNavigationInputTests
{
    private const int AllocationIterations = 1000;
    private static long _sequence;

    [Test]
    public async Task NativeNavigationInputUsesPinnedAbi8Layout()
    {
        await Assert.That(
            Unsafe.SizeOf<OpenUsdStormChildRuntime.NativeNavigationInput>())
            .IsEqualTo(104);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.Sequence)))
            .IsEqualTo(8);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.CumulativeWheelDelta)))
            .IsEqualTo(32);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.FrameSelectedPressCount)))
            .IsEqualTo(40);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.State)))
            .IsEqualTo(64);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.OrbitLeftPressCount)))
            .IsEqualTo(72);
        await Assert.That(Offset(nameof(
            OpenUsdStormChildRuntime.NativeNavigationInput.OrbitDownPressCount)))
            .IsEqualTo(96);
    }

    [Test]
    public async Task NavigationInputReturnsDetachedMappedValue()
    {
        OpenUsdStormNavigationInput first =
            OpenUsdStormChildRuntime.GetNavigationInput<SuccessfulNavigationCall>(
                (nint)1);
        OpenUsdStormNavigationInput second =
            OpenUsdStormChildRuntime.GetNavigationInput<SuccessfulNavigationCall>(
                (nint)1);

        await Assert.That(second.Sequence).IsEqualTo(first.Sequence + 1);
        await Assert.That(first.PointerX).IsEqualTo(23);
        await Assert.That(first.PointerY).IsEqualTo(41);
        await Assert.That(first.Buttons).IsEqualTo(
            OpenUsdStormPointerButtons.Left |
            OpenUsdStormPointerButtons.Middle);
        await Assert.That(first.Modifiers).IsEqualTo(
            OpenUsdStormInputModifiers.Alt |
            OpenUsdStormInputModifiers.Control);
        await Assert.That(first.CumulativeWheelDelta).IsEqualTo(2.5);
        await Assert.That(first.FrameSelectedPressCount).IsEqualTo(3UL);
        await Assert.That(first.ResetAutomaticPressCount).IsEqualTo(4UL);
        await Assert.That(first.ToggleProjectionPressCount).IsEqualTo(5UL);
        await Assert.That(first.OrbitLeftPressCount).IsEqualTo(6UL);
        await Assert.That(first.OrbitRightPressCount).IsEqualTo(7UL);
        await Assert.That(first.OrbitUpPressCount).IsEqualTo(8UL);
        await Assert.That(first.OrbitDownPressCount).IsEqualTo(9UL);
        await Assert.That(first.Focused).IsTrue();
        await Assert.That(first.Inside).IsTrue();
    }

    [Test]
    public async Task NavigationInputRejectsIncompatibleNativeLayoutAndFlags()
    {
        await Assert.That(() =>
                OpenUsdStormChildRuntime
                    .GetNavigationInput<InvalidNavigationLayoutCall>((nint)1))
            .Throws<OpenUsdStormException>();
        await Assert.That(() =>
                OpenUsdStormChildRuntime
                    .GetNavigationInput<InvalidNavigationFlagsCall>((nint)1))
            .Throws<OpenUsdStormException>();
    }

    [Test]
    public async Task NavigationPollingAllocatesNothingAfterWarmup()
    {
        for (int index = 0; index < 32; index++)
        {
            ConsumeNavigation(OpenUsdStormChildRuntime
                .GetNavigationInput<SuccessfulNavigationCall>((nint)1));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
        {
            ConsumeNavigation(OpenUsdStormChildRuntime
                .GetNavigationInput<SuccessfulNavigationCall>((nint)1));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task DisposedSessionRejectsNavigationPolling()
    {
        var session = new OpenUsdStormChildSession(0, 0, null!, "test");

        await Assert.That(session.GetNavigationInput)
            .Throws<ObjectDisposedException>();
    }

    private static int Offset(string field) => checked((int)Marshal.OffsetOf<
        OpenUsdStormChildRuntime.NativeNavigationInput>(field));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConsumeNavigation(OpenUsdStormNavigationInput input)
    {
        if (input.Sequence == 0)
        {
            throw new InvalidOperationException("The test snapshot was not populated.");
        }
    }

    private readonly struct SuccessfulNavigationCall :
        OpenUsdStormChildRuntime.INavigationInputCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            ref OpenUsdStormChildRuntime.NativeNavigationInput input,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = child;
            _ = errorBytes;
            input.StructSize = 104;
            input.Version = OpenUsdStormChildRuntime.NavigationInputVersion;
            input.Sequence = checked((ulong)Interlocked.Increment(ref _sequence));
            input.PointerX = 23;
            input.PointerY = 41;
            input.Buttons =
                OpenUsdStormPointerButtons.Left |
                OpenUsdStormPointerButtons.Middle;
            input.Modifiers =
                OpenUsdStormInputModifiers.Alt |
                OpenUsdStormInputModifiers.Control;
            input.CumulativeWheelDelta = 2.5;
            input.FrameSelectedPressCount = 3;
            input.ResetAutomaticPressCount = 4;
            input.ToggleProjectionPressCount = 5;
            input.OrbitLeftPressCount = 6;
            input.OrbitRightPressCount = 7;
            input.OrbitUpPressCount = 8;
            input.OrbitDownPressCount = 9;
            input.State =
                OpenUsdStormNavigationState.Focused |
                OpenUsdStormNavigationState.Inside;
            input.Reserved = 0;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct InvalidNavigationLayoutCall :
        OpenUsdStormChildRuntime.INavigationInputCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            ref OpenUsdStormChildRuntime.NativeNavigationInput input,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = child;
            _ = errorBytes;
            input.StructSize = 64;
            input.Version = OpenUsdStormChildRuntime.NavigationInputVersion;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct InvalidNavigationFlagsCall :
        OpenUsdStormChildRuntime.INavigationInputCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            ref OpenUsdStormChildRuntime.NativeNavigationInput input,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = child;
            _ = errorBytes;
            input.StructSize = 104;
            input.Version = OpenUsdStormChildRuntime.NavigationInputVersion;
            input.Buttons = (OpenUsdStormPointerButtons)0x80;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }
}
