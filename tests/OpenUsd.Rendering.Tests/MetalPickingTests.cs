// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalPickingTests
{
    [Test]
    public async Task ImplementsFinalPickingInterfacesOnEveryTargetFramework()
    {
        Type commandList = typeof(MetalSilkGraphicsDevice).Assembly.GetType(
            "OpenUsd.Rendering.Silk.Metal.MetalSilkGraphicsCommandList",
            throwOnError: true)!;

        await Assert.That(typeof(ISilkPickingGraphicsDevice).IsAssignableFrom(
            typeof(MetalSilkGraphicsDevice))).IsTrue();
        await Assert.That(typeof(ISilkPickGraphicsCommandList).IsAssignableFrom(
            commandList)).IsTrue();
    }

    [Test]
    public async Task PickParametersAndCopyPlanPreserveExactTokenAndCoordinates()
    {
        var parameters = new uint[MetalPickParameters.UInt32Count];
        MetalPickParameters.Write(0x12345678, parameters);
        MetalPickCopyPlan plan = MetalPickCopyPlan.Create(
            new SilkTexturePixelCoordinate(17, 29),
            bytesPerRow: 256);

        await Assert.That(parameters.ToArray()).IsEquivalentTo(
            new uint[] { 0x12345678, 0, 0, 0 });
        await Assert.That(plan.X).IsEqualTo(17UL);
        await Assert.That(plan.Y).IsEqualTo(29UL);
        await Assert.That(plan.Width).IsEqualTo(1UL);
        await Assert.That(plan.Height).IsEqualTo(1UL);
        await Assert.That(plan.Depth).IsEqualTo(1UL);
        await Assert.That(plan.BytesPerRow).IsEqualTo(256UL);
        await Assert.That(plan.BytesPerImage).IsEqualTo(256UL);
        await Assert.That(() => MetalPickParameters.Write(0, parameters))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MetalPickCopyPlan.Create(
                new SilkTexturePixelCoordinate(0, 0),
                bytesPerRow: 3))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task MipCopyPlanBuildsOnePlanPerLevelMatchingThePackedChainLayout()
    {
        MetalMipCopyPlan[] plans = MetalMipCopyPlan.Create(
            4,
            4,
            SilkTextureFormat.Rgba8Unorm,
            mipLevelCount: 3);

        await Assert.That(plans.Length).IsEqualTo(3);
        await Assert.That(plans[0]).IsEqualTo(
            new MetalMipCopyPlan(0, 16, 64, 4, 4, 0));
        await Assert.That(plans[1]).IsEqualTo(
            new MetalMipCopyPlan(64, 8, 16, 2, 2, 1));
        await Assert.That(plans[2]).IsEqualTo(
            new MetalMipCopyPlan(80, 4, 4, 1, 1, 2));
    }

    [Test]
    public async Task MipCopyPlanForASingleLevelTextureIsOnePlanAtOffsetZero()
    {
        MetalMipCopyPlan[] plans = MetalMipCopyPlan.Create(
            2,
            3,
            SilkTextureFormat.Rgba8Unorm,
            mipLevelCount: 1);

        await Assert.That(plans.Length).IsEqualTo(1);
        await Assert.That(plans[0]).IsEqualTo(
            new MetalMipCopyPlan(0, 8, 24, 2, 3, 0));
    }

    [Test]
    public async Task UploadTextureUsesOneCopyFromBufferCallPerMipCopyPlan()
    {
        string root = FindRepositoryRoot();
        string offscreen = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Offscreen.cs"));
        string uploadEncoding = Slice(
            offscreen,
            "case SilkGraphicsCommandKind.UploadTexture:",
            "case SilkGraphicsCommandKind.ClearColor:");

        await Assert.That(uploadEncoding).Contains("MetalMipCopyPlan.Create(");
        await Assert.That(uploadEncoding).Contains("foreach (MetalMipCopyPlan uploadPlan in uploadPlans)");
        await Assert.That(uploadEncoding).Contains("uploadPlan.DestinationLevel");
    }

    [Test]
    public async Task CommandFailureGenerationIsMonotonic()
    {
        var generation = new MetalPickDeviceGeneration();

        ulong initial = generation.Current;
        ulong firstFailure = generation.Invalidate();
        ulong secondFailure = generation.Invalidate();

        await Assert.That(initial).IsEqualTo(1UL);
        await Assert.That(firstFailure).IsEqualTo(2UL);
        await Assert.That(secondFailure).IsEqualTo(3UL);
        await Assert.That(generation.Current).IsEqualTo(secondFailure);
    }

    [Test]
    public async Task SourceUsesPersistentSharedReadbackAndOnePixelBlit()
    {
        string root = FindRepositoryRoot();
        string picking = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Picking.cs"));
        string offscreen = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Offscreen.cs"));
        string readbackCreation = Slice(
            picking,
            "public ISilkPickReadbackBuffer CreatePickReadbackBuffer()",
            "internal long PickPipelineCreationCount");
        string copyEncoding = Slice(
            offscreen,
            "case MetalPickCommandKind.CopyRgba8Pixel:",
            "default:");
        string completionPoll = Slice(
            offscreen,
            "public bool IsCompleted",
            "public void Wait()");

        await Assert.That(readbackCreation).Contains("_device.NewBuffer(");
        await Assert.That(readbackCreation).Contains(
            "MTLResourceOptions.ResourceStorageModeShared");
        await Assert.That(readbackCreation).Contains(
            "MinimumLinearTextureAlignmentForPixelFormat");
        await Assert.That(copyEncoding).Contains("CopyFromTexture(");
        await Assert.That(copyEncoding).Contains("MetalPickCopyPlan.Create(");
        await Assert.That(copyEncoding).DoesNotContain("NewBuffer(");
        await Assert.That(copyEncoding).DoesNotContain("GetBytes(");
        await Assert.That(completionPoll).Contains("TryGetCompletion(");
        await Assert.That(completionPoll).DoesNotContain("WaitUntilCompleted(");
        await Assert.That(offscreen).Contains(
            "_device.NotifyCommandBufferFailure()");
        await Assert.That(picking).Contains("MTLPixelFormat.RGBA8Unorm");
        await Assert.That(picking).Contains("MTLPixelFormat.Depth32Float");
        await Assert.That(picking).Contains(
            "pipelineDescriptor.RasterSampleCount = descriptor.SampleCount");
        await Assert.That(picking).Contains(
            "descriptor.VertexShader.EntryPoint");
        await Assert.That(picking).Contains(
            "descriptor.FragmentShader.EntryPoint");
    }

    [Test]
    public async Task CheckedMslAndHostedGateRequireAllTenEntries()
    {
        string root = FindRepositoryRoot();
        string vertex = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "shaders",
            "checked",
            "pick.vertex.metal"));
        string fragment = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "shaders",
            "checked",
            "pick.fragment.metal"));
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

        await Assert.That(vertex).Contains("[[vertex]]");
        await Assert.That(vertex).Contains("pickVertexMain");
        await Assert.That(vertex).Contains("[[buffer(0)]]");
        await Assert.That(fragment).Contains("[[fragment]]");
        await Assert.That(fragment).Contains("pickFragmentMain");
        await Assert.That(fragment).Contains("[[buffer(1)]]");
        await Assert.That(fragment).Contains("token_0 & 255U");
        await Assert.That(fragment).Contains("(token_0 >> 8U) & 255U");
        await Assert.That(fragment).Contains("(token_0 >> 16U) & 255U");
        await Assert.That(fragment).Contains("(token_0 >> 24U) & 255U");
        await Assert.That(project).Contains("checked\\pick.vertex.metal");
        await Assert.That(project).Contains("checked\\pick.fragment.metal");
        await Assert.That(project).Contains("ten-entry");
        await Assert.That(project).DoesNotContain("six-entry");
        await Assert.That(workflow).Contains(
            "Validate ten-entry combined Metal library");
        await Assert.That(workflow).Contains("runs-on: macos-15");
        await Assert.That(workflow).DoesNotContain("macos-15-intel");
        await Assert.That(workflow).Contains(
            "-p:OpenUsdRequireMetalShaderLibrary=true");
    }

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source slice '{start}' through '{end}'.");
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
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
