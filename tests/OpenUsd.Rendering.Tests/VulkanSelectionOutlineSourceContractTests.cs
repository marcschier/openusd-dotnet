// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class VulkanSelectionOutlineSourceContractTests
{
    [Test]
    public async Task VulkanImplementsFrozenSelectionOutlineContract()
    {
        string source = await ReadVulkanSource();

        await Assert.That(source).Contains("ISilkSelectionOutlineGraphicsDevice");
        await Assert.That(source).Contains("ISilkSelectionOutlineGraphicsCommandList");
        await Assert.That(source).Contains("SelectionOutlineDeviceGeneration");
        await Assert.That(source).Contains(
            "SilkSelectionOutlineCapabilities.Full");
        await Assert.That(source).Contains(
            "CreateSelectionMaskGraphicsPipeline(");
        await Assert.That(source).Contains(
            "CreateSelectionOutlineGraphicsPipeline(");
        await Assert.That(source).Contains("CreateSelectionOutlineBinding(");
        await Assert.That(source).Contains("BeginSelectionMaskRendering(");
        await Assert.That(source).Contains("SetSelectionMaskGraphicsPipeline(");
        await Assert.That(source).Contains("BeginSelectionOutlineRendering(");
        await Assert.That(source).Contains("SetSelectionOutlineGraphicsPipeline(");
        await Assert.That(source).Contains("SetSelectionOutlineBinding(");
        await Assert.That(source).Contains(
            "DrawSelectionOutlineFullscreenTriangle()");
    }

    [Test]
    public async Task VulkanUsesExactAttachmentsBindingsAndBlend()
    {
        string source = await ReadVulkanSource();

        await Assert.That(source).Contains("Format.R8G8B8A8Unorm");
        await Assert.That(source).Contains("Format.D32Sfloat");
        await Assert.That(source).Contains("SampleCountFlags.Count1Bit");
        await Assert.That(source).Contains("DepthWriteEnable = false");
        await Assert.That(source).Contains(
            "DepthTestEnable = Descriptor.DepthTestEnabled");
        await Assert.That(source).Contains("? CompareOp.LessOrEqual");
        await Assert.That(source).Contains(": CompareOp.Always");
        await Assert.That(source).Contains(
            "InitialLayout = ImageLayout.DepthStencilReadOnlyOptimal");
        await Assert.That(source).Contains("LoadOp = AttachmentLoadOp.Load");
        await Assert.That(source).Contains("StoreOp = AttachmentStoreOp.Store");
        await Assert.That(source).Contains("Binding = 0");
        // The descriptor-set layout is built from a parameterized helper rather
        // than four literal bindings, so the contract is that the helper exists
        // and that every DstBinding the writes use is still pinned below.
        await Assert.That(source).Contains("Binding = binding");
        await Assert.That(source).Contains("writes[2] = CreateImageWrite(");
        await Assert.That(source).Contains("DstBinding = 3");
        await Assert.That(source).Contains("DescriptorType.SampledImage");
        await Assert.That(source).Contains("DescriptorType.Sampler");
        await Assert.That(source).Contains("DescriptorType.UniformBuffer");
        await Assert.That(source).Contains("SrcColorBlendFactor = BlendFactor.SrcAlpha");
        await Assert.That(source).Contains(
            "DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha");
        await Assert.That(source).Contains("_api.CmdDraw(commands, 3, 1, 0, 0)");
    }

    [Test]
    public async Task VulkanTransitionsCachesAndInvalidatesLossSafely()
    {
        string source = await ReadVulkanSource();
        string root = FindRepositoryRoot();
        string probe = await File.ReadAllTextAsync(Path.Combine(
            root,
            "tests",
            "OpenUsd.RhiProbe",
            "Program.cs"));

        await Assert.That(source).Contains("ImageUsageFlags.SampledBit");
        await Assert.That(source).Contains("ImageLayout.ShaderReadOnlyOptimal");
        await Assert.That(source).Contains(
            "ImageLayout.DepthStencilReadOnlyOptimal");
        await Assert.That(source).Contains("_descriptorSets");
        await Assert.That(source).Contains("GetFramebuffer(");
        await Assert.That(source).Contains("NormalizeVertexIdShader");
        await Assert.That(source).Contains("capabilityDrawParameters = 4427");
        await Assert.That(source).Contains(
            "FailNextSelectionOutlineSubmissionForTesting");
        await Assert.That(source).Contains(
            "FailNextSelectionOutlineFenceForTesting");
        await Assert.That(source).Contains("NotifySelectionOutlineDeviceLost");
        await Assert.That(source).Contains("ReleaseSelectionDependent");
        await Assert.That(source).Contains("LiveDependentObjects");
        await Assert.That(probe).Contains("ProbeVulkanSelectionOutline(vulkan)");
        await Assert.That(probe).Contains("SampledDepthTarget(size, size)");
        await Assert.That(probe).Contains("CountVulkanOutlinePixels(outlined)");
        await Assert.That(probe).Contains(
            "cleared.AsSpan().SequenceEqual(baseline)");
    }

    /// <summary>
    /// The selection render passes declare colour-attachment load/store
    /// synchronization, so a mask pass that loads what an earlier pass stored
    /// and a composite that loads the visible colour are both ordered.
    /// </summary>
    /// <remarks>
    /// Both passes use <c>AttachmentLoadOp.Load</c>. A load is a read of the
    /// attachment, and the writer in every case is another colour-attachment
    /// write, so without a <c>ColorAttachmentWrite</c> source and a
    /// <c>ColorAttachmentRead | ColorAttachmentWrite</c> destination at
    /// <c>ColorAttachmentOutput</c> the loads race the earlier stores. The
    /// symptom is a mask that keeps only the last selected mesh, or an outline
    /// composited over a partially written frame -- both of which look like
    /// selection bugs rather than missing barriers.
    /// </remarks>
    [Test]
    public async Task VulkanOrdersSelectionAttachmentLoadsAgainstEarlierWrites()
    {
        string source = await ReadVulkanSource();

        await Assert.That(source).Contains(
            "SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit |\n" +
            "                PipelineStageFlags.TransferBit |\n" +
            "                PipelineStageFlags.LateFragmentTestsBit,\n" +
            "            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit |\n" +
            "                PipelineStageFlags.EarlyFragmentTestsBit,\n" +
            "            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit |\n" +
            "                AccessFlags.TransferWriteBit |\n" +
            "                AccessFlags.DepthStencilAttachmentWriteBit,\n" +
            "            DstAccessMask = AccessFlags.ColorAttachmentReadBit |\n" +
            "                AccessFlags.ColorAttachmentWriteBit |\n" +
            "                AccessFlags.DepthStencilAttachmentReadBit,");
        await Assert.That(source).Contains(
            "SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit |\n" +
            "                PipelineStageFlags.FragmentShaderBit |\n" +
            "                PipelineStageFlags.TransferBit,\n" +
            "            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,\n" +
            "            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit |\n" +
            "                AccessFlags.ShaderReadBit |\n" +
            "                AccessFlags.TransferReadBit,\n" +
            "            DstAccessMask = AccessFlags.ColorAttachmentReadBit |\n" +
            "                AccessFlags.ColorAttachmentWriteBit,");

        // The matching outgoing half, so this pass's stores are ordered against
        // the next mask pass's load and the composite's sample of the mask.
        await Assert.That(source).Contains("SrcSubpass = 0,");
        await Assert.That(source).Contains("DstSubpass = Vk.SubpassExternal,");
        await Assert.That(source).Contains(
            "SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,\n" +
            "            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit |\n" +
            "                PipelineStageFlags.FragmentShaderBit,\n" +
            "            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,\n" +
            "            DstAccessMask = AccessFlags.ColorAttachmentReadBit |\n" +
            "                AccessFlags.ColorAttachmentWriteBit |\n" +
            "                AccessFlags.ShaderReadBit,");
        await Assert.That(source).Contains("DependencyCount = 2,");
        await Assert.That(source).Contains(
            "DependencyFlags = DependencyFlags.ByRegionBit");
    }

    private static async Task<string> ReadVulkanSource()
    {
        string root = FindRepositoryRoot();
        string[] files =
        [
            "VulkanSilkGraphicsDevice.cs",
            "VulkanSilkGraphicsDevice.Offscreen.cs",
            "VulkanSilkGraphicsDevice.SelectionOutline.cs"
        ];
        var source = new System.Text.StringBuilder();
        foreach (string file in files)
        {
            source.Append(await File.ReadAllTextAsync(Path.Combine(
                root,
                "src",
                "OpenUsd.Rendering.Silk.Vulkan",
                file)));
        }
        return source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
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
