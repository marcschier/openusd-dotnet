// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Viewer;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class ViewerStageCameraDiscoveryNativeCoverageTests
{
    [Test]
    public async Task DiscoveryListsStageCamerasAndReadsPrimaryCameraPrim()
    {
        string stagePath = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "native-coverage-primary-camera.usda");
        Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0
            (
                defaultPrim = "World"
                primaryCameraPrim = </World/HeroCamera>
            )

            def Xform "World"
            {
                def Camera "HeroCamera"
                {
                }

                def Xform "NotCamera"
                {
                }
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);
        ViewerStageCameraMenuEntry[] cameras = ViewerStageCameraDiscovery.ListCameras(stage);

        await Assert.That(ViewerStageCameraDiscovery.GetPrimaryCameraPath(stage))
            .IsEqualTo("/World/HeroCamera");
        await Assert.That(cameras.Select(camera => camera.Path).ToArray())
            .IsEquivalentTo(["/World/HeroCamera"]);
    }
}
