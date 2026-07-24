// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalSelectionOutlineTests
{
    [Test]
    public async Task ImplementsFrozenSelectionOutlineInterfacesOnEveryTargetFramework()
    {
        Type commandList = typeof(MetalSilkGraphicsDevice).Assembly.GetType(
            "OpenUsd.Rendering.Silk.Metal.MetalSilkGraphicsCommandList",
            throwOnError: true)!;

        await Assert.That(typeof(ISilkSelectionOutlineGraphicsDevice).IsAssignableFrom(
            typeof(MetalSilkGraphicsDevice))).IsTrue();
        await Assert.That(
            typeof(ISilkSelectionOutlineGraphicsCommandList).IsAssignableFrom(
                commandList)).IsTrue();
    }

    [Test]
    public async Task SelectionOutlineGenerationIsMonotonic()
    {
        var generation = new MetalSelectionOutlineDeviceGeneration();

        await Assert.That(generation.Current).IsEqualTo(1UL);
        await Assert.That(generation.Invalidate()).IsEqualTo(2UL);
        await Assert.That(generation.Invalidate()).IsEqualTo(3UL);
        await Assert.That(generation.Current).IsEqualTo(3UL);
    }

    [Test]
    public async Task SourcePreservesMetalPassAndBindingAbi()
    {
        string root = FindRepositoryRoot();
        string selection = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.SelectionOutline.cs"));
        string offscreen = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Offscreen.cs"));

        await Assert.That(selection).Contains(
            "SilkSelectionOutlineCapabilities.VisibleOnly");
        await Assert.That(selection).Contains(
            "depthDescriptor.IsDepthWriteEnabled = false");
        await Assert.That(selection).Contains(
            "depthDescriptor.DepthCompareFunction = MTLCompareFunction.LessEqual");
        await Assert.That(selection).Contains(
            "color.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha");
        await Assert.That(selection).Contains(
            "color.DestinationRGBBlendFactor =");
        await Assert.That(selection).Contains(
            "MTLBlendFactor.OneMinusSourceAlpha");
        await Assert.That(offscreen).Contains(
            "color.LoadAction = MTLLoadAction.Load");
        await Assert.That(offscreen).Contains(
            "depth.LoadAction = MTLLoadAction.Load");
        await Assert.That(offscreen).Contains(
            "depth.StoreAction = MTLStoreAction.Store");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentTexture(binding.Mask.Texture, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentTexture(binding.Depth.Texture, 1)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentSamplerState(binding.Sampler.Sampler, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.SetFragmentBuffer(binding.Parameters.Buffer, 0, 0)");
        await Assert.That(offscreen).Contains(
            "encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3)");
        await Assert.That(offscreen).Contains(
            "NotifySelectionOutlineCommandBufferFailure()");
    }

    [Test]
    public async Task CheckedMslReflectionManifestAndHostedGateProveTenEntries()
    {
        string root = FindRepositoryRoot();
        string checkedRoot = Path.Combine(root, "eng", "shaders", "checked");
        string maskVertex = await File.ReadAllTextAsync(Path.Combine(
            checkedRoot,
            "selection.mask.vertex.metal"));
        string outlineVertex = await File.ReadAllTextAsync(Path.Combine(
            checkedRoot,
            "selection.outline.vertex.metal"));
        string outlineFragment = await File.ReadAllTextAsync(Path.Combine(
            checkedRoot,
            "selection.outline.fragment.metal"));
        using JsonDocument reflection = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(
                checkedRoot,
                "selection.outline.fragment.reflection.json")));
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(
                root,
                "eng",
                "shaders",
                "shader-manifest.json")));
        string project = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "OpenUsd.Rendering.Silk.Metal.csproj"));
        string workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "shaders.yml"));

        await Assert.That(maskVertex).Contains(
            "SceneParameters_0 [[buffer(0)]]");
        await Assert.That(outlineVertex).Contains("[[vertex_id]]");
        await Assert.That(outlineFragment).Contains(
            "selectionMask_1 [[texture(0)]]");
        await Assert.That(outlineFragment).Contains(
            "visibleDepth_1 [[texture(1)]]");
        await Assert.That(outlineFragment).Contains(
            "selectionSampler_1 [[sampler(0)]]");
        await Assert.That(outlineFragment).Contains(
            "SelectionOutlineParameters_1 [[buffer(0)]]");
        JsonElement resources =
            reflection.RootElement.GetProperty("resources");
        JsonElement parameters = resources.EnumerateArray().Single(
            resource => resource.GetProperty("name").GetString() ==
                "SelectionOutlineParameters");
        await Assert.That(
            parameters.GetProperty("shape").GetProperty("size").GetInt32())
            .IsEqualTo(32);
        await Assert.That(
            manifest.RootElement.GetProperty("programs").GetArrayLength())
            .IsEqualTo(10);
        await Assert.That(project).Contains(
            "checked\\selection.mask.vertex.metal");
        await Assert.That(project).Contains(
            "checked\\selection.outline.fragment.metal");
        await Assert.That(project).Contains("ten-entry");
        await Assert.That(workflow).Contains(
            "Validate ten-entry combined Metal library");
        await Assert.That(workflow).Contains("runs-on: macos-15-arm64");
        await Assert.That(workflow).Contains(
            "-p:OpenUsdRequireMetalShaderLibrary=true");
        await Assert.That(workflow).Contains(
            "/*/*/MetalSelectionOutlineConformanceTests/*");
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
