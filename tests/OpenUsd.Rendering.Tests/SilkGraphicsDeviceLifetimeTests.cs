// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGraphicsDeviceLifetimeTests
{
    [Test]
    public async Task FailedPreTeardownAttemptCanBeRetried()
    {
        var lifetime = new TestDeviceLifetime();

        bool firstAttempt = lifetime.Begin();
        lifetime.Cancel();
        bool retry = lifetime.Begin();
        lifetime.Complete();
        bool afterCompletion = lifetime.Begin();

        await Assert.That(firstAttempt).IsTrue();
        await Assert.That(retry).IsTrue();
        await Assert.That(afterCompletion).IsFalse();
    }

    [Test]
    public async Task LiveDependentRejectsDeviceDisposal()
    {
        var lifetime = new TestDeviceLifetime();
        lifetime.Register();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => _ = lifetime.Begin());

        await Assert.That(exception.Message).IsEqualTo("live dependents");

        lifetime.Release();
        await Assert.That(lifetime.Begin()).IsTrue();
    }

    private sealed class TestDeviceLifetime : SilkGraphicsDeviceLifetimeBase
    {
        internal void Register() => RegisterDependentLifetime();

        internal void Release() => ReleaseDependentLifetime();

        internal bool Begin() => TryBeginLifetimeDispose("live dependents");

        internal void Cancel() => CancelLifetimeDispose();

        internal void Complete() => CompleteLifetimeDispose();
    }
}
