// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalCompositionViewportPresenterTests
{
    [Test]
    public async Task UsesAvaloniaMetalExternalObjectDescriptors()
    {
        await Assert.That(MetalCompositionViewportPresenter.IOSurfaceHandleType)
            .IsEqualTo("IOSurfaceRef");
        await Assert.That(MetalCompositionViewportPresenter.SharedEventHandleType)
            .IsEqualTo("MetalSharedEvent");
    }

    [Test]
    public async Task OptionalProbeIsUnavailableOutsideMacOS()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is not applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await using var presenter = new MetalCompositionViewportPresenter();
        CompositionPresenterProbeResult result = await presenter.ProbeAsync(
            CreateTarget());

        await Assert.That(result.IsAvailable).IsFalse();
        await Assert.That(result.Status).Contains("macOS 12");
    }

    [Test]
    public async Task RequiredProbeFailsOutsideMacOS()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is not applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await using var presenter = new MetalCompositionViewportPresenter(required: true);
        Exception? failure = null;
        try
        {
            _ = await presenter.ProbeAsync(CreateTarget());
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("macOS 12");
    }

    [Test]
    public async Task CallbackModeAcceptsRendererWithoutDiagnosticPipeline()
    {
        await Assert.That(MetalCompositionViewportPresenter.HasRequiredResources(
                callbackMode: true,
                hasPipeline: false,
                hasRenderer: true))
            .IsTrue();
    }

    [Test]
    public async Task DefaultModeRequiresDiagnosticPipeline()
    {
        await Assert.That(MetalCompositionViewportPresenter.HasRequiredResources(
                callbackMode: false,
                hasPipeline: false,
                hasRenderer: true))
            .IsFalse();
        await Assert.That(MetalCompositionViewportPresenter.HasRequiredResources(
                callbackMode: false,
                hasPipeline: true,
                hasRenderer: false))
            .IsTrue();
    }

    [Test]
    public async Task CallbackModeRejectsMissingRenderer()
    {
        await Assert.That(MetalCompositionViewportPresenter.HasRequiredResources(
                callbackMode: true,
                hasPipeline: true,
                hasRenderer: false))
            .IsFalse();
    }

    [Test]
    public async Task CallbackFramesAllocateSampledVisibleDepth()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalCompositionViewportPresenter.cs"));
        int createStart = source.IndexOf(
            "internal static MetalCompositionPresentationFrame Create(",
            StringComparison.Ordinal);
        int createEnd = source.IndexOf(
            "catch (Exception creationFailure)",
            createStart,
            StringComparison.Ordinal);
        string create = source[createStart..createEnd];

        await Assert.That(create).Contains(
            "SilkTextureDescriptor.SampledDepthTarget(width, height)");
        await Assert.That(create).DoesNotContain(
            "SilkTextureDescriptor.DepthTarget(width, height)");
    }

    private static CompositionPresentationTarget CreateTarget() =>
        new(
            [MetalCompositionViewportPresenter.IOSurfaceHandleType],
            [MetalCompositionViewportPresenter.SharedEventHandleType],
            deviceLuid: null,
            deviceUuid: null);

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
