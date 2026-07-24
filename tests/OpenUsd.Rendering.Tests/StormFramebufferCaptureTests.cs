// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormFramebufferCaptureTests
{
    [Test]
    public async Task DisposedSessionRejectsFramebufferCapture()
    {
        var session = new OpenUsdStormChildSession(0, 0, null!, "test");

        await Assert.That(() => session.CaptureFramebuffer())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ManagedCaptureLimitAcceptsExactly64MiB()
    {
        int bytes = OpenUsdStormChildRuntime.GetCaptureByteCount(4096, 4096);

        await Assert.That(bytes).IsEqualTo(64 * 1024 * 1024);
    }

    [Test]
    public async Task ManagedCaptureLimitRejectsOversizedFramebuffer()
    {
        await Assert.That(() =>
                OpenUsdStormChildRuntime.GetCaptureByteCount(4097, 4096))
            .Throws<InvalidOperationException>();
    }
}
