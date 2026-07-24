// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class RenderBackendKindTests
{
    [Test]
    public async Task StormIsThePrimaryBackend()
    {
        string? firstBackend = Enum.GetName(Enum.GetValues<RenderBackendKind>()[0]);

        await Assert.That(firstBackend).IsEqualTo(nameof(RenderBackendKind.Storm));
    }
}
