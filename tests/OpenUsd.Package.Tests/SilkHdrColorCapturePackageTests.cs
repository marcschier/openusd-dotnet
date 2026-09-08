// Copyright (c) marcschier. Licensed under the MIT License.

using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

[NotInParallel]
public sealed partial class SilkHdrColorCapturePackageTests
{
    [Test]
    public async Task PackedHdrCaptureCarriesReviewedSurfaceDefaultsAndXmlForEveryProductionFramework()
    {
        string packagePath = RequirePackage();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        foreach (string framework in new[] { "net8.0", "net9.0", "net10.0" })
        {
            using Stream assemblySource = RequireEntry(package, $"lib/{framework}/OpenUsd.Rendering.Silk.dll").Open();
            using var assemblyBytes = new MemoryStream();
            await assemblySource.CopyToAsync(assemblyBytes);
            assemblyBytes.Position = 0;
            using var image = new PEReader(assemblyBytes);
            MetadataReader metadata = image.GetMetadataReader();
            TypeDefinition hdr = FindType(metadata, "SilkHdrColorCaptureResult");
            await Assert.That(PublicMethods(metadata, hdr)).IsEquivalentTo(
                ["get_Width", "get_Height", "get_Rgba16Float", "get_Convention"]);
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkFrameCaptureResult")))
                .Contains("get_HdrColor");
            await Assert.That(PublicMethods(metadata, FindType(metadata, "SilkFrameCaptureResult")))
                .Contains("get_Depth");
            TypeDefinition options = FindType(metadata, "SilkHdrColorCaptureOptions");
            await Assert.That(PublicMethods(metadata, options)).IsEquivalentTo(
                [".ctor", "get_Default", "get_IncludeDeviceDepth", "get_MaximumPixelCount", "get_MaximumReadbackBytes"]);
            MethodDefinition constructor = options.GetMethods().Select(metadata.GetMethodDefinition)
                .Single(method => metadata.GetString(method.Name) == ".ctor");
            Parameter[] parameters = [.. constructor.GetParameters().Select(metadata.GetParameter)];
            await Assert.That(parameters.Select(parameter => metadata.GetString(parameter.Name)).ToArray())
                .IsEquivalentTo(["includeDeviceDepth", "maximumPixelCount", "maximumReadbackBytes"]);
            await Assert.That(metadata.GetBlobReader(metadata.GetConstant(parameters[0].GetDefaultValue()).Value)
                .ReadBoolean()).IsFalse();
            await Assert.That(metadata.GetBlobReader(metadata.GetConstant(parameters[1].GetDefaultValue()).Value)
                .ReadInt32()).IsEqualTo(16_777_216);
            await Assert.That(metadata.GetBlobReader(metadata.GetConstant(parameters[2].GetDefaultValue()).Value)
                .ReadInt64()).IsEqualTo(268_435_456L);
            TypeDefinition convention = FindType(metadata, "SilkHdrColorConvention");
            string[] values =
            [
                .. convention.GetFields().Select(metadata.GetFieldDefinition)
                    .Where(field => (field.Attributes & FieldAttributes.Literal) != 0)
                    .Select(field => metadata.GetString(field.Name))
            ];
            await Assert.That(values).IsEquivalentTo(["RendererWorkingCompositedBeforeExposureAndDisplay"]);
            await AssertCaptureParameters(metadata, "SilkFrameCapture", "CaptureRetainedWithHdrColor",
                ["renderer", "device", "width", "height", "renderSettings", "hdrColorOptions", "pageRevision", "cancellationToken"]);
            await AssertCaptureParameters(metadata, "SilkFrameCapturer", "CaptureWithHdrColor",
                ["session", "width", "height", "renderSettings", "hdrColorOptions", "timeCode", "camera", "cancellationToken"]);

            using Stream xmlSource = RequireEntry(package, $"lib/{framework}/OpenUsd.Rendering.Silk.xml").Open();
            XDocument documentation = XDocument.Load(xmlSource);
            string raw = ReadMember(documentation, "P:OpenUsd.Rendering.Silk.SilkHdrColorCaptureResult.Rgba16Float");
            await Assert.That(raw).Contains("exactly Width * Height * 8");
            await Assert.That(raw).Contains("top-down");
            await Assert.That(raw).Contains("little-endian");
            await Assert.That(raw).Contains("without padding");
            string semantics = ReadMember(documentation, "T:OpenUsd.Rendering.Silk.SilkHdrColorCaptureResult");
            await Assert.That(semantics).Contains("before exposure");
            await Assert.That(semantics).Contains("not albedo");
            await Assert.That(semantics).Contains("not universally straight/unassociated alpha");
            await Assert.That(semantics).Contains("no unpremultiply");
            await Assert.That(semantics).Contains("without another pixel copy");
            await Assert.That(semantics).Contains("All four stored channels");
            await Assert.That(semantics).Contains("Finite negative values and signed zero");
            string quota = ReadMember(documentation,
                "M:OpenUsd.Rendering.Silk.SilkHdrColorCaptureOptions.#ctor(System.Boolean,System.Int32,System.Int64)");
            await Assert.That(quota).Contains("20 bytes per pixel");
            await Assert.That(quota).Contains("managed RGBA8 selection-upload copy");
            await Assert.That(quota).Contains("unchanged when depth or selection is disabled");
            string continuity = string.Join(" ", documentation.Descendants("member")
                .Where(member => (member.Attribute("name")?.Value ?? string.Empty)
                    .StartsWith("M:OpenUsd.Rendering.Silk.SilkFrameCapturer.CaptureWithHdrColor(", StringComparison.Ordinal))
                .Select(member => NormalizeWhitespace(member.Value)));
            await Assert.That(continuity).Contains("exclusive session synchronization ownership");
            await Assert.That(continuity).Contains("before target allocation or native Sync");
        }
    }

    private static async Task AssertCaptureParameters(
        MetadataReader metadata, string type, string name, string[] builtinParameters)
    {
        MethodDefinition[] methods =
        [
            .. FindType(metadata, type).GetMethods().Select(metadata.GetMethodDefinition)
                .Where(method => metadata.GetString(method.Name) == name)
        ];
        await Assert.That(methods.Length).IsEqualTo(2);
        string[][] actual =
        [
            .. methods.Select(method => method.GetParameters()
                .Select(metadata.GetParameter)
                .Where(parameter => parameter.SequenceNumber != 0)
                .Select(parameter => metadata.GetString(parameter.Name)).ToArray())
        ];
        int processorIndex = Array.IndexOf(builtinParameters, "renderSettings") + 1;
        string[] ocioParameters =
            [.. builtinParameters[..processorIndex], "ocioProcessor", .. builtinParameters[processorIndex..]];
        await Assert.That(actual.Any(parameters => parameters.SequenceEqual(builtinParameters))).IsTrue();
        await Assert.That(actual.Any(parameters => parameters.SequenceEqual(ocioParameters))).IsTrue();
    }

    private static string RequirePackage()
    {
        string? packagePath = Environment.GetEnvironmentVariable("OPENUSD_SILK_HDR_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            Skip.Test("Set OPENUSD_SILK_HDR_PACKAGE to the freshly packed OpenUsd.Rendering.Silk nupkg.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return packagePath;
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive package, string path) =>
        package.GetEntry(path) ?? throw new InvalidDataException($"The package is missing '{path}'.");

    private static TypeDefinition FindType(MetadataReader metadata, string name) =>
        metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(type =>
            metadata.GetString(type.Namespace) == "OpenUsd.Rendering.Silk" &&
            metadata.GetString(type.Name) == name &&
            (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public);

    private static string[] PublicMethods(MetadataReader metadata, TypeDefinition type) =>
    [
        .. type.GetMethods().Select(metadata.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
            .Select(method => metadata.GetString(method.Name))
    ];

    private static string ReadMember(XDocument document, string name) =>
        NormalizeWhitespace(document.Descendants("member")
            .Single(member => member.Attribute("name")?.Value == name).Value);

    private static string NormalizeWhitespace(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
