// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the image aspects the Vulkan offscreen executor names when it transitions
/// a texture that a shader is about to sample.
/// </summary>
/// <remarks>
/// <para>
/// A depth image's only aspect is depth. A barrier that names the colour aspect of
/// a depth image is invalid usage: the validation layers reject it, and a driver is
/// free to translate it into no synchronisation at all. SwiftShader accepts it and
/// produces the right pixels anyway, so an execution gate on the software rasteriser
/// cannot see the difference -- which is exactly why the contract is pinned here in
/// source instead of being left to a backend that happens to be permissive.
/// </para>
/// <para>
/// The case that made this reachable is a shadow map: an image rendered as a depth
/// attachment in one pass and sampled as a texture in the next. Every other texture
/// bound through <c>SetTexture</c> up to now was an uploaded colour image, so the
/// hard-coded colour aspect was never wrong before and would not have been noticed
/// until a real Vulkan driver rejected it.
/// </para>
/// </remarks>
public sealed class VulkanSampledDepthBarrierSourceContractTests
{
    [Test]
    public async Task BindingATextureForSamplingTransitionsWithTheTexturesOwnAspect()
    {
        string source = await ReadOffscreenSourceAsync();
        string setTexture = Slice(
            source,
            "case SilkGraphicsCommandKind.SetTexture:",
            "case SilkGraphicsCommandKind.SetSampler:");

        // The transition must derive the aspect from the texture, because the same
        // command binds both colour images and depth images.
        await Assert.That(setTexture).Contains("materialTexture.AspectMask");
        await Assert.That(setTexture)
            .DoesNotContain("ImageAspectFlags.ColorBit")
            .Because(
                "A shadow map bound through SetTexture is a depth image, so a " +
                "hard-coded colour aspect is invalid usage on a conformant driver.");
    }

    [Test]
    public async Task TheTextureAspectIsDerivedFromTheDepthFormat()
    {
        // Non-vacuity for the case above: AspectMask must actually distinguish
        // depth from colour rather than being a constant under another name.
        string source = await ReadOffscreenSourceAsync();
        string aspect = Slice(
            source,
            "internal ImageAspectFlags AspectMask =>",
            "internal ImageSubresourceRange SubresourceRange");

        await Assert.That(aspect).Contains("SilkTextureFormat.D32Float");
        await Assert.That(aspect).Contains("ImageAspectFlags.DepthBit");
        await Assert.That(aspect).Contains("ImageAspectFlags.ColorBit");
    }

    [Test]
    public async Task DepthAttachmentTransitionsStillNameTheDepthAspect()
    {
        // The attachment paths were already correct and must stay that way: a
        // regression there would be invisible on SwiftShader for the same reason.
        string source = await ReadOffscreenSourceAsync();
        string beginRendering = Slice(
            source,
            "case SilkGraphicsCommandKind.BeginRendering:",
            "case SilkGraphicsCommandKind.BeginSelectionMaskRendering:");

        await Assert.That(beginRendering).Contains("ImageAspectFlags.DepthBit");
        await Assert.That(beginRendering).Contains("ImageAspectFlags.ColorBit");
    }

    private static async Task<string> ReadOffscreenSourceAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Offscreen.cs")))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = startIndex < 0
            ? -1
            : value.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not slice Vulkan offscreen source from '{start}' to '{end}'.");
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
