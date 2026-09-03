// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class D3D12SelectionOutlineSourceContractTests
{
    [Test]
    public async Task OutlineOwnsExactD3D12AbiAndPipelinePolicy()
    {
        string source = await ReadSource(
            "D3D12SilkGraphicsDevice.SelectionOutline.cs");
        string commands = await ReadSource("D3D12SilkGraphicsDevice.Offscreen.cs");
        string picking = await ReadSource("D3D12SilkGraphicsDevice.Picking.cs");

        await Assert.That(source).Contains(": ISilkSelectionOutlineGraphicsDevice");
        await Assert.That(source).Contains(
            ": ISilkSelectionOutlineGraphicsCommandList");
        await Assert.That(source).Contains("SilkSelectionOutlineCapabilities.Full");
        await Assert.That(source).Contains(
            "SelectionOutlineDeviceGeneration => PickDeviceGeneration");
        await Assert.That(picking).Contains("ObserveNativeDeviceRemoval");
        await Assert.That(picking).Contains("AdvancePickDeviceGeneration");
        await Assert.That(source).Contains("DescriptorRangeType.Srv");
        await Assert.That(source).Contains("DescriptorRangeType.Sampler");
        await Assert.That(source).Contains("new RootDescriptor(0, 0)");
        await Assert.That(source).Contains("DepthWriteMask = DepthWriteMask.Zero");
        // The visible-only mask keeps the read-only less-equal depth test; the
        // x-ray mask disables it so the whole selected silhouette reaches the
        // mask, which is what the second composite outlines.
        await Assert.That(source).Contains("DepthEnable = descriptor.DepthTestEnabled");
        await Assert.That(source).Contains("? ComparisonFunc.LessEqual");
        await Assert.That(source).Contains(": ComparisonFunc.Always");
        await Assert.That(source).Contains("BlendEnable = true");
        await Assert.That(source).Contains("SrcBlend = Blend.SrcAlpha");
        await Assert.That(source).Contains("DestBlend = Blend.InvSrcAlpha");
        await Assert.That(commands).Contains("DrawInstanced(3, 1, 0, 0)");
    }

    [Test]
    public async Task SampledDepthUsesTypelessResourceAndSeparateWritableReadOnlyViews()
    {
        string source = await ReadSource("D3D12SilkGraphicsDevice.Offscreen.cs");

        await Assert.That(source).Contains("Format.FormatR32Typeless");
        await Assert.That(source).Contains("Format.FormatD32Float");
        await Assert.That(source).Contains("Format.FormatR32Float");
        await Assert.That(source).Contains("DsvFlags.ReadOnlyDepth");
        await Assert.That(source).Contains("ReadOnlyDepthView");
        await Assert.That(source).Contains("ShaderResourceView");
        await Assert.That(source).Contains("ResourceStates.DepthRead");
        await Assert.That(source).Contains("ResourceStates.PixelShaderResource");
    }

    [Test]
    public async Task BindingCopiesPersistentDescriptorsAndSubmissionLeasesResources()
    {
        string selection = await ReadSource(
            "D3D12SilkGraphicsDevice.SelectionOutline.cs");
        string commands = await ReadSource("D3D12SilkGraphicsDevice.Offscreen.cs");

        await Assert.That(selection).Contains("DescriptorHeapFlags.ShaderVisible");
        await Assert.That(selection).Contains("_device->CopyDescriptorsSimple");
        await Assert.That(selection).Contains("mask.AcquireLease()");
        await Assert.That(selection).Contains("depth.AcquireLease()");
        await Assert.That(selection).Contains("parameters.AcquireLease()");
        await Assert.That(commands).Contains(
            "leases.Add(leasedSelectionOutlineBinding.AcquireLease())");
        string draw = SliceCase(
            commands,
            "case D3D12SelectionOutlineCommandKind.DrawFullscreenTriangle:");
        await Assert.That(draw).DoesNotContain("CreateDescriptorHeap");
        await Assert.That(draw).DoesNotContain("CreateCommittedResource");
    }

    private static async Task<string> ReadSource(string fileName)
    {
        string root = FindRepositoryRoot();
        return await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.D3D12",
            fileName));
    }

    private static string SliceCase(string source, string label)
    {
        int start = source.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find '{label}'.");
        }
        int end = source.IndexOf(
            "case D3D12SelectionOutlineCommandKind.None:",
            start,
            StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException(
                "Could not find the end of the fullscreen draw case.");
        }
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
