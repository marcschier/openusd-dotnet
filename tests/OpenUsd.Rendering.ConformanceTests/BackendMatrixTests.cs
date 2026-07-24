// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class BackendMatrixTests
{
    [Test]
    public async Task InitialBackendMatrixContainsFourRenderers()
    {
        await Assert.That(Enum.GetValues<RenderBackendKind>().Length).IsEqualTo(4);
    }
}
