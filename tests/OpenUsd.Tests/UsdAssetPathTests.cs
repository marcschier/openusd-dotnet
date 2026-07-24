// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdAssetPathTests
{
    [Test]
    public async Task PreservesAuthoredPath()
    {
        var value = new UsdAssetPath("textures/albédo.png");

        await Assert.That(value.Path).IsEqualTo("textures/albédo.png");
        await Assert.That(value.ToString()).IsEqualTo(value.Path);
    }

    [Test]
    public async Task RejectsNullPath()
    {
        await Assert.That(() => new UsdAssetPath(null!)).Throws<ArgumentNullException>();
    }
}
