// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the checked subprim pick vertex stage: the clip-space depth
/// adjustment that separates a coincident edge or point from its surface, and
/// the explicit one-pixel point size Vulkan and Metal require.
/// </summary>
/// <remarks>
/// Both are properties no pipeline state can express portably, so they are
/// asserted against the checked artifacts and the generated sources rather than
/// against backend state. The SPIR-V module is inspected as SPIR-V because the
/// checked binaries are stripped of debug names, so the built-in decoration is
/// the only evidence the point size survived compilation.
/// </remarks>
public sealed class SilkSubprimPickShaderContractTests
{
    [Test]
    public async Task TheSubprimPassUsesTheSubprimVertexStageAndTheSurfacePassDoesNot()
    {
        SilkPickPipelineDescriptor triangles =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.TriangleList);
        SilkPickPipelineDescriptor lines =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.LineList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkPickDepthBias.Coincident);
        SilkPickPipelineDescriptor points =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.PointList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkPickDepthBias.Coincident);

        triangles.Validate();
        lines.Validate();
        points.Validate();

        await Assert.That(triangles.VertexShader.EntryPoint)
            .IsEqualTo("pickVertexMain");
        await Assert.That(lines.VertexShader.EntryPoint)
            .IsEqualTo("pickSubprimVertexMain");
        await Assert.That(points.VertexShader.EntryPoint)
            .IsEqualTo("pickSubprimVertexMain");
        await Assert.That(lines.DepthBias).IsEqualTo(SilkPickDepthBias.Coincident);
        await Assert.That(points.DepthBias).IsEqualTo(SilkPickDepthBias.Coincident);
        await Assert.That(triangles.DepthBias).IsEqualTo(SilkPickDepthBias.None);

        // The fragment stage and every depth convention stay shared, so the
        // subprim pass writes the same tokens against the same depth the
        // surface pass established.
        await Assert.That(lines.FragmentShader.EntryPoint)
            .IsEqualTo(triangles.FragmentShader.EntryPoint);
        await Assert.That(lines.DepthCompare).IsEqualTo(triangles.DepthCompare);
        await Assert.That(lines.DepthFormat).IsEqualTo(triangles.DepthFormat);
    }

    [Test]
    public async Task AWholeLineOrPointResourceUsesTheUnbiasedWholeVertexStage()
    {
        // A whole basis-curve or UsdGeomPoints resource is its own surface. It
        // is drawn by the surface pass as a line or point list, so it needs the
        // point size the triangle stage never writes and must not be pulled
        // toward the viewer by the coincident offset the overlay pass uses.
        SilkPickPipelineDescriptor lines =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.LineList);
        SilkPickPipelineDescriptor points =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.PointList);

        lines.Validate();
        points.Validate();

        await Assert.That(lines.DepthBias).IsEqualTo(SilkPickDepthBias.None);
        await Assert.That(points.DepthBias).IsEqualTo(SilkPickDepthBias.None);
        await Assert.That(lines.VertexShader.EntryPoint)
            .IsEqualTo("pickWholeVertexMain");
        await Assert.That(points.VertexShader.EntryPoint)
            .IsEqualTo("pickWholeVertexMain");
        await Assert.That(lines.ColorWriteEnabled).IsTrue();
    }

    [Test]
    public async Task AWholeLineOrPointMaskUsesTheUnbiasedWholeVertexStage()
    {
        SilkSelectionMaskPipelineDescriptor wholeLines =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                depthTested: true,
                SilkSelectionMaskPrimitiveTopology.LineList);
        SilkSelectionMaskPipelineDescriptor overlayLines =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                depthTested: true,
                SilkSelectionMaskPrimitiveTopology.LineList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkSelectionMaskStage.SubprimOverlay);

        wholeLines.Validate();
        overlayLines.Validate();

        await Assert.That(wholeLines.Stage)
            .IsEqualTo(SilkSelectionMaskStage.WholeResource);
        await Assert.That(wholeLines.VertexShader.EntryPoint)
            .IsEqualTo("selectionMaskWholeVertexMain");
        await Assert.That(overlayLines.VertexShader.EntryPoint)
            .IsEqualTo("selectionMaskSubprimVertexMain");
    }

    [Test]
    public async Task TheOverlayStageIsRefusedOnTheTriangleSurfaceTopology()
    {
        // Both stages draw line and point lists, so the stage is stated rather
        // than inferred. Applying the coincident separation to the surface
        // itself has no meaning and is refused outright.
        await Assert.That(() => SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.TriangleList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkPickDepthBias.Coincident))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                depthTested: true,
                SilkSelectionMaskPrimitiveTopology.TriangleList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkSelectionMaskStage.SubprimOverlay))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AFaceRequestDrawsCurvesAndPointsAsPureOccluders()
    {
        // The occluder pipeline differs from the ordinary one in exactly one
        // property: it writes no colour. It keeps the depth test, the depth
        // write and the depth convention, because the whole point of drawing it
        // is that it still hides the faces behind it.
        SilkPickPipelineDescriptor occluder =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil,
                SilkPickPrimitiveTopology.LineList,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkPickDepthBias.None,
                colorWriteEnabled: false);
        occluder.Validate();

        await Assert.That(occluder.ColorWriteEnabled).IsFalse();
        await Assert.That(occluder.DepthTestEnabled).IsTrue();
        await Assert.That(occluder.DepthWriteEnabled).IsTrue();
        await Assert.That(occluder.DepthBias).IsEqualTo(SilkPickDepthBias.None);
        await Assert.That(occluder.VertexShader.EntryPoint)
            .IsEqualTo("pickWholeVertexMain");
    }

    [Test]
    public async Task TheSpirVWholeStageDeclaresThePointSizeBuiltIn()
    {
        byte[] pick = await ReadCheckedAsync("pick.whole.vertex.spv");
        byte[] mask = await ReadCheckedAsync("selection.mask.whole.vertex.spv");

        await Assert.That(DecodeBuiltIns(pick)).Contains(1u);
        await Assert.That(DecodeBuiltIns(mask)).Contains(1u);
    }

    [Test]
    public async Task TheWholeStageSourcesApplyNoDepthOffset()
    {
        string pick = await ReadSourceAsync("pick.whole.vertex.slang");
        string mask = await ReadSourceAsync("selection.mask.whole.vertex.slang");

        await Assert.That(pick).DoesNotContain("SubprimDepthOffset");
        await Assert.That(mask).DoesNotContain("SubprimDepthOffset");
        await Assert.That(pick).Contains("output.pointSize = 1.0;");
        await Assert.That(mask).Contains("output.pointSize = 1.0;");
    }

    [Test]
    public async Task TheMetalWholeStageDeclaresAOnePixelPointSize()
    {
        string pick = await ReadCheckedTextAsync("pick.whole.vertex.metal");
        string mask = await ReadCheckedTextAsync("selection.mask.whole.vertex.metal");

        await Assert.That(pick).Contains("[[point_size]]");
        await Assert.That(mask).Contains("[[point_size]]");
    }

    [Test]
    public async Task TheSpirVSubprimStageDeclaresThePointSizeBuiltIn()
    {
        byte[] module = await ReadCheckedAsync("pick.subprim.vertex.spv");

        await Assert.That(DecodeBuiltIns(module)).Contains(1u);
    }

    [Test]
    public async Task TheSurfaceStageDeclaresNoPointSizeBuiltIn()
    {
        byte[] module = await ReadCheckedAsync("pick.vertex.spv");

        await Assert.That(DecodeBuiltIns(module)).DoesNotContain(1u);
    }

    [Test]
    public async Task TheMetalSubprimStageDeclaresAOnePixelPointSize()
    {
        string source = await ReadCheckedTextAsync("pick.subprim.vertex.metal");

        await Assert.That(source).Contains("[[point_size]]");
        await Assert.That(source).Contains("1.0f");
    }

    [Test]
    public async Task TheDxilSubprimStageOmitsThePointSizeSemantic()
    {
        // Direct3D has no programmable point size, and DXIL rejects the
        // semantic outright, so the same source must compile without it there.
        byte[] module = await ReadCheckedAsync("pick.subprim.vertex.dxil");
        string text = System.Text.Encoding.ASCII.GetString(module);

        await Assert.That(text).DoesNotContain("SV_PointSize");
        await Assert.That(module.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task TheSubprimSourceOffsetsClipSpaceDepthRatherThanRasterizerState()
    {
        string source = await ReadSourceAsync("pick.subprim.vertex.slang");

        await Assert.That(source).Contains("SubprimDepthOffset");
        await Assert.That(source).Contains("clip.z -= SubprimDepthOffset * clip.w");

        // The generated backends must not add a rasterizer bias on top: two
        // separations would not compose to a defined amount.
        string d3d12 = await ReadRepositoryTextAsync(
            "src",
            "OpenUsd.Rendering.Silk.D3D12",
            "D3D12SilkGraphicsDevice.Picking.cs");
        string vulkan = await ReadRepositoryTextAsync(
            "src",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Picking.cs");
        string metal = await ReadRepositoryTextAsync(
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Offscreen.cs");

        await Assert.That(d3d12).Contains("DepthBias = 0");
        await Assert.That(d3d12).Contains("SlopeScaledDepthBias = 0.0f");
        await Assert.That(vulkan).Contains("DepthBiasEnable = false");
        await Assert.That(metal).DoesNotContain("encoder.SetDepthBias(");
    }

    [Test]
    public async Task TheCheckedManifestDeclaresTheSubprimProgramWithOnlySceneParameters()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            await ReadRepositoryTextAsync(
                "eng",
                "shaders",
                "shader-manifest.json"));

        JsonElement program = manifest.RootElement
            .GetProperty("programs")
            .EnumerateArray()
            .Single(entry =>
                entry.GetProperty("name").GetString() == "pick.subprim.vertex");

        await Assert.That(program.GetProperty("entryPoint").GetString())
            .IsEqualTo("pickSubprimVertexMain");
        await Assert.That(program.GetProperty("stage").GetString())
            .IsEqualTo("vertex");

        // The offset is a checked constant rather than a bound uniform, so the
        // stage adds no binding a backend would have to plumb through.
        await Assert.That(program.GetProperty("resources").GetArrayLength())
            .IsEqualTo(1);
        await Assert.That(program.GetProperty("resources")[0]
                .GetProperty("name").GetString())
            .IsEqualTo("SceneParameters");
    }

    private static uint[] DecodeBuiltIns(byte[] module)
    {
        var builtIns = new List<uint>();
        ReadOnlySpan<byte> bytes = module;
        int index = 5 * sizeof(uint);
        while (index + sizeof(uint) <= bytes.Length)
        {
            uint instruction = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32LittleEndian(bytes[index..]);
            int wordCount = (int)(instruction >> 16);
            uint opcode = instruction & 0xFFFF;
            if (wordCount <= 0 || index + (wordCount * sizeof(uint)) > bytes.Length)
            {
                break;
            }

            // OpDecorate BuiltIn and OpMemberDecorate BuiltIn are the only two
            // places a built-in can be named.
            if (opcode == 71 && wordCount >= 4 && Word(bytes, index, 2) == 11)
            {
                builtIns.Add(Word(bytes, index, 3));
            }
            else if (opcode == 72 && wordCount >= 5 && Word(bytes, index, 3) == 11)
            {
                builtIns.Add(Word(bytes, index, 4));
            }
            index += wordCount * sizeof(uint);
        }
        return [.. builtIns];
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int instructionOffset, int word) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            bytes[(instructionOffset + (word * sizeof(uint)))..]);

    private static Task<byte[]> ReadCheckedAsync(string name) =>
        File.ReadAllBytesAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            name));

    private static Task<string> ReadCheckedTextAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            name));

    private static Task<string> ReadSourceAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "sources",
            name));

    private static Task<string> ReadRepositoryTextAsync(params string[] segments) =>
        File.ReadAllTextAsync(Path.Combine(
            [FindRepositoryRoot(), .. segments]));

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
