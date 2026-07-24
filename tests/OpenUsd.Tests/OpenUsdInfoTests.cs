// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class OpenUsdInfoTests
{
    [Test]
    public async Task ProductAssemblyNameMatchesProductName()
    {
        string? assemblyName = typeof(OpenUsdInfo).Assembly.GetName().Name;

        await Assert.That(assemblyName).IsEqualTo(OpenUsdInfo.ProductName);
    }

    [Test]
    public async Task ManagedAssemblyHasAVersion()
    {
        await Assert.That(OpenUsdInfo.Version).IsNotEmpty();
    }
}
