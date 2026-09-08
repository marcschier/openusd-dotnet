// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed class SilkDepthCapturePackageTests
{
    [Test]
    public async Task PackedDepthCaptureExposesTheReviewedSurfaceForEveryProductionFramework()
    {
        string? packagePath = Environment.GetEnvironmentVariable("OPENUSD_SILK_DEPTH_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            Skip.Test("Set OPENUSD_SILK_DEPTH_PACKAGE to the freshly packed OpenUsd.Rendering.Silk nupkg.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
        {
            ZipArchiveEntry entry = package.GetEntry($"lib/{framework}/OpenUsd.Rendering.Silk.dll")
                ?? throw new InvalidDataException($"The package is missing the {framework} library.");
            using var assemblyBytes = new MemoryStream();
            using (Stream source = entry.Open())
            {
                await source.CopyToAsync(assemblyBytes);
            }
            assemblyBytes.Position = 0;
            using var image = new PEReader(assemblyBytes);
            MetadataReader metadata = image.GetMetadataReader();
            TypeDefinition depth = FindType(metadata, "SilkDepthCaptureResult");
            string[] depthMethods = PublicMethods(metadata, depth);
            await Assert.That(depthMethods).IsEquivalentTo(
                ["get_Width", "get_Height", "get_Values", "get_Convention", "get_ClearValue"]);
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkFrameCaptureResult")))
                .Contains("get_Depth");
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkFrameCapture"))
                .Count(name => name == "CaptureRetainedWithDepth")).IsEqualTo(2);
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkFrameCapturer"))
                .Count(name => name == "CaptureWithDepth")).IsEqualTo(2);
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkDepthCaptureOptions")))
                .IsEquivalentTo(
                    [".ctor", "get_Default", "get_MaximumPixelCount", "get_MaximumReadbackBytes"]);
            TypeDefinition convention = FindType(metadata, "SilkDepthConvention");
            string[] values =
            [
                .. convention.GetFields()
                    .Select(metadata.GetFieldDefinition)
                    .Where(field => (field.Attributes & FieldAttributes.Literal) != 0)
                    .Select(field => metadata.GetString(field.Name))
            ];
            await Assert.That(values).IsEquivalentTo(["NormalizedDeviceDepthZeroToOne"]);

            ZipArchiveEntry documentation = package.GetEntry($"lib/{framework}/OpenUsd.Rendering.Silk.xml")
                ?? throw new InvalidDataException($"The package is missing the {framework} API documentation.");
            using Stream documentationStream = documentation.Open();
            string byteQuotaDocumentation = XDocument.Load(documentationStream)
                .Descendants("member")
                .Single(member => member.Attribute("name")?.Value ==
                    "M:OpenUsd.Rendering.Silk.SilkDepthCaptureOptions.#ctor(System.Int32,System.Int64)")
                .Elements("param")
                .Single(parameter => parameter.Attribute("name")?.Value == "maximumReadbackBytes")
                .Value;
            await Assert.That(byteQuotaDocumentation).Contains("20 bytes per pixel");
            await Assert.That(byteQuotaDocumentation).Contains("managed RGBA8 selection-upload copy");
            await Assert.That(byteQuotaDocumentation).DoesNotContain("16 bytes per pixel");
        }
    }

    private static TypeDefinition FindType(MetadataReader metadata, string name) =>
        metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type =>
                metadata.GetString(type.Namespace) == "OpenUsd.Rendering.Silk" &&
                metadata.GetString(type.Name) == name &&
                (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public);

    private static string[] PublicMethods(MetadataReader metadata, TypeDefinition type) =>
    [
        .. type.GetMethods()
            .Select(metadata.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
            .Select(method => metadata.GetString(method.Name))
    ];
}
