// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalGraphicsShaderLibraryTests
{
    [Test]
    [Arguments(SilkShaderStage.Vertex)]
    [Arguments(SilkShaderStage.Fragment)]
    public async Task GeneratedMainUsesItsOwnSourceForEachGraphicsStage(SilkShaderStage stage)
    {
        var descriptor = new SilkShaderModuleDescriptor(
            stage, SilkShaderBinaryFormat.MetalLibrary, "main", "generated source"u8.ToArray());
        int pinnedCalls = 0;
        int sourceCalls = 0;
        SilkShaderModuleDescriptor received = default;

        string library = MetalGraphicsShaderLibrary.Load(
            descriptor,
            () =>
            {
                pinnedCalls++;
                return "pinned";
            },
            shader =>
            {
                sourceCalls++;
                received = shader;
                return "compiled";
            });

        await Assert.That(library).IsEqualTo("compiled");
        await Assert.That(sourceCalls).IsEqualTo(1);
        await Assert.That(pinnedCalls).IsEqualTo(0);
        await Assert.That(received).IsEqualTo(descriptor);
    }

    [Test]
    [Arguments(SilkShaderStage.Vertex)]
    [Arguments(SilkShaderStage.Fragment)]
    public async Task CheckedEntryUsesPinnedLibraryForEachGraphicsStage(SilkShaderStage stage)
    {
        var descriptor = new SilkShaderModuleDescriptor(
            stage, SilkShaderBinaryFormat.MetalLibrary, "checkedEntry", "library"u8.ToArray());
        int pinnedCalls = 0;
        int sourceCalls = 0;

        string library = MetalGraphicsShaderLibrary.Load(
            descriptor,
            () =>
            {
                pinnedCalls++;
                return "pinned";
            },
            _ =>
            {
                sourceCalls++;
                return "compiled";
            });

        await Assert.That(library).IsEqualTo("pinned");
        await Assert.That(pinnedCalls).IsEqualTo(1);
        await Assert.That(sourceCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkShaderStage.Vertex)]
    [Arguments(SilkShaderStage.Fragment)]
    public async Task GeneratedSourceCompilationFailureDoesNotFallBackToPinnedLibrary(SilkShaderStage stage)
    {
        var descriptor = new SilkShaderModuleDescriptor(
            stage, SilkShaderBinaryFormat.MetalLibrary, "main", "bad source"u8.ToArray());
        int pinnedCalls = 0;
        var expected = new InvalidOperationException("generated source failed");

        Exception? actual = null;
        try
        {
            _ = MetalGraphicsShaderLibrary.Load<string>(
                descriptor,
                () =>
                {
                    pinnedCalls++;
                    return "pinned";
                },
                _ => throw expected);
        }
        catch (InvalidOperationException exception)
        {
            actual = exception;
        }

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(pinnedCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkShaderStage.Vertex)]
    [Arguments(SilkShaderStage.Fragment)]
    public async Task PinnedLibraryFailureDoesNotFallBackToGeneratedSource(SilkShaderStage stage)
    {
        var descriptor = new SilkShaderModuleDescriptor(
            stage, SilkShaderBinaryFormat.MetalLibrary, "checkedEntry", "library"u8.ToArray());
        int sourceCalls = 0;
        var expected = new InvalidOperationException("pinned library failed");

        Exception? actual = null;
        try
        {
            _ = MetalGraphicsShaderLibrary.Load<string>(
                descriptor,
                () => throw expected,
                _ =>
                {
                    sourceCalls++;
                    return "compiled";
                });
        }
        catch (InvalidOperationException exception)
        {
            actual = exception;
        }

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(sourceCalls).IsEqualTo(0);
    }
}
