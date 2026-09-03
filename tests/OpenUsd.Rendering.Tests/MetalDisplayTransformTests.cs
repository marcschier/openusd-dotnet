// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalDisplayTransformTests
{
    [Test]
    public async Task ImplementsFrozenDisplayTransformInterfacesOnEveryTargetFramework()
    {
        Type commandList = typeof(MetalSilkGraphicsDevice).Assembly.GetType(
            "OpenUsd.Rendering.Silk.Metal.MetalSilkGraphicsCommandList",
            throwOnError: true)!;

        await Assert.That(typeof(ISilkDisplayTransformGraphicsDevice).IsAssignableFrom(
            typeof(MetalSilkGraphicsDevice))).IsTrue();
        await Assert.That(
            typeof(ISilkDisplayTransformGraphicsCommandList).IsAssignableFrom(
                commandList)).IsTrue();
    }

    [Test]
    public async Task SourcePreservesMetalDisplayTransformPassAndBindingAbi()
    {
        string root = FindRepositoryRoot();
        string offscreen = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Offscreen.cs"));
        string displayTransform = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.DisplayTransform.cs"));

        // Colour attachment: DontCare load (pass overwrites entirely), store.
        await Assert.That(offscreen).Contains(
            "color.LoadAction = MTLLoadAction.DontCare");
        await Assert.That(offscreen).Contains(
            "color.StoreAction = MTLStoreAction.Store");

        // Fragment shader ABI from display.transform.fragment.metal:
        // sceneColor [[texture(0)]], displayLut/lattice [[texture(1)]],
        // displaySampler [[sampler(0)]], DisplayTransformParameters [[buffer(0)]]
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentTexture(binding.SceneColor.Texture, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentTexture(binding.Lattice.Texture, 1)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentSamplerState(binding.Sampler.Sampler, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentBuffer(binding.Parameters.Buffer, 0, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3)");

        // Pipeline: no blending (full overwrite), no depth attachment.
        await Assert.That(displayTransform).Contains(
            "color.IsBlendingEnabled = false");

        // Device generation is shared with the selection outline generation.
        await Assert.That(displayTransform).Contains(
            "SelectionOutlineDeviceGeneration");
    }

    [Test]
    public async Task CheckedMetalDisplayTransformShaderDeclaresDirectArgumentsAtCheckedIndices()
    {
        string checkedRoot = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        string fragment = await File.ReadAllTextAsync(Path.Combine(
            checkedRoot,
            "display.transform.fragment.metal"));
        string vertex = await File.ReadAllTextAsync(Path.Combine(
            checkedRoot,
            "display.transform.vertex.metal"));

        // Fragment stage direct-argument indices (from Slang-generated .metal source):
        await Assert.That(fragment).Contains("sceneColor_1 [[texture(0)]]");
        await Assert.That(fragment).Contains("displayLut_1 [[texture(1)]]");
        await Assert.That(fragment).Contains("displaySampler_1 [[sampler(0)]]");
        await Assert.That(fragment).Contains(
            "DisplayTransformParameters_1 [[buffer(0)]]");

        // Vertex stage: SV_VertexID only, no vertex buffers.
        await Assert.That(vertex).Contains("[[vertex_id]]");
        await Assert.That(vertex).DoesNotContain("[[buffer(");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
    }
}
