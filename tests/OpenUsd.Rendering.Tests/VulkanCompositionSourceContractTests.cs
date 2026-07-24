// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class VulkanCompositionSourceContractTests
{
    [Test]
    public async Task RenderCallbackFramesAlwaysUseSampledDepthTargets()
    {
        string root = FindRepositoryRoot();
        string source = (await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanCompositionViewportPresenter.cs")))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        string allocations = Slice(
            source,
            "if (context.UsesD3D11Bridge)",
            "_image = image;");

        await Assert.That(Count(
                allocations,
                "SilkTextureDescriptor.SampledDepthTarget("))
            .IsEqualTo(3);
        await Assert.That(allocations)
            .DoesNotContain("SilkTextureDescriptor.DepthTarget(");
        await Assert.That(Count(
                allocations,
                "renderCallback is not null"))
            .IsEqualTo(4);
        await Assert.That(source).Contains(
            "new VulkanCompositionRenderContext(\n" +
            "                        _renderer!,\n" +
            "                        _colorTarget!,\n" +
            "                        _depthTarget!");
    }

    private static int Count(string value, string search)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(
                   search,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }
        return count;
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
        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
    }
}
