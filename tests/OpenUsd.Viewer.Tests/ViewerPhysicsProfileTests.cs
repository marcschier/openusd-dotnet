// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPhysicsProfileTests
{
    [Test]
    public async Task FullScenePhysicsRequiresExplicitOptInAndDoesNotLeakBetweenHosts()
    {
        try
        {
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.FullScenePhysics).IsFalse();
            ViewerStartupOptions.Initialize(["--full-scene-physics"]);
            await Assert.That(ViewerStartupOptions.FullScenePhysics).IsTrue();
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.FullScenePhysics).IsFalse();
            ViewerStartupOptions.Initialize(new ViewerHostOptions { FullScenePhysics = true });
            await Assert.That(ViewerStartupOptions.FullScenePhysics).IsTrue();
            ViewerStartupOptions.Initialize(new ViewerHostOptions());
            await Assert.That(ViewerStartupOptions.FullScenePhysics).IsFalse();
        }
        finally
        {
            ViewerStartupOptions.Initialize([]);
        }
    }
}
