// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Compression;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed class SilkGpuHdrColorCapturePackageTests
{
    [Test]
    public async Task PackedGpuHdrDocumentsCompletedFrameOnlyFailureAndUnchangedPixelQuota()
    {
        string? packagePath = Environment.GetEnvironmentVariable("OPENUSD_SILK_GPU_HDR_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            Skip.Test("Set OPENUSD_SILK_GPU_HDR_PACKAGE to the newly packed GPU HDR Silk package.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
        {
            using Stream source = (archive.GetEntry($"lib/{framework}/OpenUsd.Rendering.Silk.xml")
                ?? throw new InvalidDataException("Missing package XML documentation.")).Open();
            XDocument documentation = XDocument.Load(source);
            XElement[] gpuMethods =
            [
                .. documentation.Descendants("member").Where(member =>
                {
                    string name = member.Attribute("name")?.Value ?? string.Empty;
                    return (name.StartsWith(
                        "M:OpenUsd.Rendering.Silk.SilkFrameCapture.CaptureRetainedWithHdrColor(", StringComparison.Ordinal) ||
                        name.StartsWith(
                            "M:OpenUsd.Rendering.Silk.SilkFrameCapturer.CaptureWithHdrColor(", StringComparison.Ordinal)) &&
                        !name.Contains("SilkOpenColorIoProcessor", StringComparison.Ordinal);
                })
            ];
            await Assert.That(gpuMethods.Length).IsEqualTo(2);
            foreach (XElement method in gpuMethods)
            {
                string text = Normalize(method.Value);
                await Assert.That(text).Contains("this completed frame");
                await Assert.That(text).Contains("RGBA8 fallback");
                await Assert.That(text).Contains("drain");
                await Assert.That(text).DoesNotContain("Only the built-in renderer and CPU display conversion");
                await Assert.That(method.Elements("exception").Select(exception => exception.Attribute("cref")?.Value)
                    .Contains("T:System.InvalidOperationException")).IsTrue();
            }
            string semantics = Normalize(documentation.Descendants("member").Single(member =>
                member.Attribute("name")?.Value == "T:OpenUsd.Rendering.Silk.SilkHdrColorCaptureResult").Value);
            await Assert.That(semantics).Contains("GPU display transforms");
            await Assert.That(semantics).Contains("without another pixel copy");
            string options = Normalize(documentation.Descendants("member").Single(member =>
                member.Attribute("name")?.Value == "T:OpenUsd.Rendering.Silk.SilkHdrColorCaptureOptions").Value);
            await Assert.That(options).Contains("lattice");
            string quota = Normalize(documentation.Descendants("member").Single(member =>
                member.Attribute("name")?.Value ==
                    "M:OpenUsd.Rendering.Silk.SilkHdrColorCaptureOptions.#ctor(System.Boolean,System.Int32,System.Int64)").Value);
            await Assert.That(quota).Contains("20 bytes per pixel");
            await Assert.That(quota).Contains("unchanged when depth or selection is disabled");
        }
    }

    private static string Normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
