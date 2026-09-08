// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerWelcomeTests
{
    [Test]
    public async Task TheWelcomeSampleIsLicensedSelfContainedAndAvailableFromTheShippedAssembly()
    {
        using Stream? sample = typeof(MainWindow).Assembly.GetManifestResourceStream(
            "OpenUsd.Viewer.Assets.ReviewScene.usda");
        await Assert.That(sample).IsNotNull();
        await Assert.That(sample!.Length).IsLessThanOrEqualTo(16 * 1024);
        using var reader = new StreamReader(sample);
        string text = await reader.ReadToEndAsync();
        await Assert.That(text).StartsWith("#usda 1.0");
        await Assert.That(text).Contains("SPDX-License-Identifier: MIT");
        await Assert.That(text).Contains("Copyright (c) marcschier");
        await Assert.That(text).Contains("def Xform \"Review\"");
        await Assert.That(text).DoesNotContain("@");
    }
}
