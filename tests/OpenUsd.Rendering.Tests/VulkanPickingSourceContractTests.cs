// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class VulkanPickingSourceContractTests
{
    [Test]
    public async Task VulkanPickingUsesExactFormatsBindingsAndCachedShaders()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Picking.cs"));
        source += await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.cs"));
        source += await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Offscreen.cs"));
        string project = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "OpenUsd.Rendering.Silk.Vulkan.csproj"));
        string fragment = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "Shaders",
            "pick.replay.fragment.slang"));

        await Assert.That(source).Contains("ISilkPickingGraphicsDevice");
        await Assert.That(source).Contains("ISilkPickGraphicsCommandList");
        await Assert.That(source).Contains("descriptor.Validate();");
        await Assert.That(source).Contains("Format.R8G8B8A8Unorm");
        await Assert.That(source).Contains("Format.D32Sfloat");
        await Assert.That(source).Contains("SampleCountFlags.Count1Bit");
        await Assert.That(source).Contains("Binding = 0");
        await Assert.That(source).Contains("Binding = 1");
        await Assert.That(source).Contains("DescriptorCount = 1");
        await Assert.That(source).Contains("MatchesSecondaryCommands");
        await Assert.That(project).Contains("pick.replay.fragment.spv");
        await Assert.That(source).Contains(
            "descriptor.VertexShader.Code.ToArray()");
        await Assert.That(fragment).Contains("uint token = pickToken.x;");
        await Assert.That(fragment).Contains("token & 0xffu");
        await Assert.That(fragment).Contains("(token >> 24u) & 0xffu");
        await Assert.That(fragment).DoesNotContain("SV_PrimitiveID");
    }

    [Test]
    public async Task VulkanReadbackIsPersistentAlignedInvalidatedAndNonblocking()
    {
        string source = await ReadPickingSource();
        string create = Slice(
            source,
            "public ISilkPickReadbackBuffer CreatePickReadbackBuffer()",
            "internal VulkanSilkPickingDiagnostics PickDiagnostics");
        string read = Slice(
            source,
            "public void ReadRgba8Pixel(Span<byte> destination)",
            "public void Dispose()");
        string poll = Slice(
            source,
            "internal bool TryComplete(ulong serial)",
            "internal void Wait(ulong serial)");
        string submit = Slice(
            source,
            "internal ulong RecordAndSubmit(",
            "internal bool TryComplete(ulong serial)");

        await Assert.That(create).Contains("VulkanSilkPickReadbackBuffer");
        await Assert.That(source).Contains("_copyOffset = requiredCopyAlignment");
        await Assert.That(source).Contains("_nonCoherentAtomSize");
        await Assert.That(source).Contains("OptimalBufferCopyOffsetAlignment");
        await Assert.That(source).Contains("NonCoherentAtomSize");
        await Assert.That(read).Contains("InvalidateMappedMemoryRanges");
        await Assert.That(read).Contains("(byte*)_mapped");
        await Assert.That(poll).Contains("GetFenceStatus");
        await Assert.That(poll).Contains("Result.NotReady");
        await Assert.That(poll).DoesNotContain("WaitForFences");
        await Assert.That(submit).Contains("CmdCopyImageToBuffer");
        await Assert.That(submit).Contains("ImageExtent = new Extent3D(1, 1, 1)");
        await Assert.That(submit).DoesNotContain("CreateFence");
        await Assert.That(submit).DoesNotContain("CreateCommandPool");
        await Assert.That(submit).DoesNotContain("AllocateMemory");
    }

    [Test]
    public async Task VulkanPickColorReturnsToAttachmentLayoutAndTracksGeneration()
    {
        string source = await ReadPickingSource();
        string submit = Slice(
            source,
            "internal ulong RecordAndSubmit(",
            "internal bool TryComplete(ulong serial)");

        await Assert.That(submit).Contains(
            "ImageLayout.ColorAttachmentOptimal,\n" +
            "            ImageLayout.TransferSrcOptimal");
        await Assert.That(submit).Contains(
            "ImageLayout.TransferSrcOptimal,\n" +
            "            ImageLayout.ColorAttachmentOptimal");
        await Assert.That(submit).Contains(
            "color.Layout = ImageLayout.ColorAttachmentOptimal");
        await Assert.That(source).Contains("PickDeviceGeneration");
        await Assert.That(source).Contains("NotifyPickDeviceLost");
        await Assert.That(source).Contains("AdvancePickDeviceGeneration");
        await Assert.That(source).Contains(
            "pipeline.DeviceGeneration != _deviceGeneration");
    }

    [Test]
    public async Task VulkanTeardownAndNativeAotProbeRemainFailureSafe()
    {
        string source = await ReadPickingSource();
        string root = FindRepositoryRoot();
        string probe = await File.ReadAllTextAsync(Path.Combine(
            root,
            "tests",
            "OpenUsd.RhiProbe",
            "Program.cs"));

        await Assert.That(source).Contains("CompleteForTeardown()");
        await Assert.That(source).Contains("Owner.IsPickDeviceLost");
        await Assert.That(source).Contains(
            "TryConsumePickFenceFailureForTesting");
        await Assert.That(source).Contains("ReleasePickReadbackDependent");
        await Assert.That(source).Contains("LiveCommandPools");
        await Assert.That(source).Contains("LiveFences");
        await Assert.That(source).Contains("LiveMappings");
        await Assert.That(probe).Contains("ProbeVulkanPicking(vulkan)");
        await Assert.That(probe).Contains("\"SwiftShader\"");
        await Assert.That(probe).Contains("RenderPickStatus.Hit");
        await Assert.That(probe).Contains("result.BackendToken != range.FirstToken");
        await Assert.That(probe).Contains("result.WorldPosition is not null");
        await Assert.That(probe).Contains("result.WorldNormal is not null");
        await Assert.That(probe).Contains("result.NormalizedDepth is not null");
    }

    private static async Task<string> ReadPickingSource()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Picking.cs"));
        return source.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not slice source from '{start}' to '{end}'.");
        }
        return value[startIndex..endIndex];
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
            ?? throw new InvalidOperationException(
                "Could not locate repository root.");
    }
}
