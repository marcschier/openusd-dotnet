// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDependencyTests
{
    [Test]
    public async Task ViewerUsesTheOpenUsdProductIdentity()
    {
        string? assemblyName = typeof(OpenUsdInfo).Assembly.GetName().Name;

        await Assert.That(assemblyName).IsEqualTo(OpenUsdInfo.ProductName);
    }
}
