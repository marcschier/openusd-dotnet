// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkSceneIngestionOptionsTests
{
    [Test]
    public async Task ExplicitOptionsPreserveTheRequestedSupportedProductChoices()
    {
        SilkSceneIngestionOptions options = new(
            RenderPurpose.Default | RenderPurpose.Render,
            "full");

        await Assert.That(options.IncludedPurposes)
            .IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
        await Assert.That(options.MaterialBindingPurpose).IsEqualTo("full");
    }

    [Test]
    public async Task EmptyAllPurposeBindingIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "allPurpose"))
            .Throws<NotSupportedException>();
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                string.Empty))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task ExcludingDefaultPurposeIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Render, string.Empty))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task MaterialBindingPurposeMustBeOneToken()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "bad token"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnsupportedCustomBindingPurposeIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "customLook"))
            .Throws<NotSupportedException>();
    }
}
