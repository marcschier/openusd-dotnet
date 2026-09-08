// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class StormAovOwnershipTests
{
    private static int _releases;
    private static bool _failCapture;

    [Test]
    public async Task BorrowedViewLeaseDefersAnyThreadOwnerRelease()
    {
        _releases = 0;
        using var owner = new StormAovOwnerHandle<NativeCalls>();
        owner.Initialize((nint)7);
        bool borrowed = false;
        owner.DangerousAddRef(ref borrowed);
        try
        {
            await Task.Run(owner.Dispose);
            await Assert.That(Volatile.Read(ref _releases)).IsEqualTo(0);
        }
        finally
        {
            if (borrowed)
            {
                owner.DangerousRelease();
            }
        }
        await Assert.That(Volatile.Read(ref _releases)).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeFailureAlwaysReleasesAnyPublishedOwner(bool failCapture)
    {
        _releases = 0;
        _failCapture = failCapture;
        bool failed = false;
        try
        {
            _ = OpenUsdStormRuntime.RenderAovs<NativeCalls>(
                (nint)1, new StormAovRequest(1, 1, 0, [StormAovKind.Depth]));
        }
        catch (OpenUsdStormException)
        {
            failed = true;
        }
        await Assert.That(failed).IsTrue();
        await Assert.That(Volatile.Read(ref _releases)).IsEqualTo(1);
    }

    private readonly struct NativeCalls : OpenUsdStormRuntime.IStormAovCall
    {
        public static OpenUsdNativeStatus Capture(
            nint renderer, in StormAovNative.Request request, out nint owner,
            Span<byte> error, out nuint required)
        {
            owner = (nint)7;
            required = 0;
            return _failCapture ? OpenUsdNativeStatus.NativeError : OpenUsdNativeStatus.Ok;
        }

        public static OpenUsdNativeStatus GetView(
            nint owner, ref StormAovNative.View view, Span<byte> error,
            out nuint required)
        {
            required = 0;
            return OpenUsdNativeStatus.NativeError;
        }

        public static void Release(nint owner)
        {
            if (owner != (nint)7)
            {
                throw new InvalidOperationException("Unexpected owned handle.");
            }
            Interlocked.Increment(ref _releases);
        }
    }
}
