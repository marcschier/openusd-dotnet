// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class RenderBackendSelectionPolicyTests
{
    private static readonly RenderBackendKind[] AllBackends =
    [
        RenderBackendKind.Storm,
        RenderBackendKind.D3D12,
        RenderBackendKind.Vulkan,
        RenderBackendKind.Metal
    ];

    [Test]
    [Arguments(RenderPlatform.Linux, RenderBackendKind.Vulkan)]
    [Arguments(RenderPlatform.MacOS, RenderBackendKind.Metal)]
    public async Task AutomaticSelectionOrdersStormBeforePlatformBackend(
        RenderPlatform platform,
        RenderBackendKind platformBackend)
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(platform, RequestedBackend: null),
            AllBackends);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Mode).IsEqualTo(RenderBackendSelectionMode.Automatic);
        await Assert.That(result.Candidates).Count().IsEqualTo(2);
        await Assert.That(result.Candidates[0]).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(result.Candidates[1]).IsEqualTo(platformBackend);
    }

    [Test]
    public async Task WindowsAutomaticSelectionOrdersStormD3D12ThenVulkan()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RequestedBackend: null),
            AllBackends);

        await Assert.That(result.Candidates).Count().IsEqualTo(3);
        await Assert.That(result.Candidates[0]).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(result.Candidates[1]).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(result.Candidates[2]).IsEqualTo(RenderBackendKind.Vulkan);
    }

    [Test]
    public async Task ManualSelectionReturnsOnlyRequestedAvailableBackend()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RenderBackendKind.D3D12),
            AllBackends);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Mode).IsEqualTo(RenderBackendSelectionMode.Manual);
        await Assert.That(result.Candidates).Count().IsEqualTo(1);
        await Assert.That(result.Candidates[0]).IsEqualTo(RenderBackendKind.D3D12);
    }

    [Test]
    public async Task ManualVulkanSelectionIsSupportedOnWindows()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RenderBackendKind.Vulkan),
            AllBackends);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Candidates).Count().IsEqualTo(1);
        await Assert.That(result.Candidates[0]).IsEqualTo(RenderBackendKind.Vulkan);
    }

    [Test]
    public async Task ManualSelectionRejectsUnsupportedPlatformBackend()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RenderBackendKind.Metal),
            AllBackends);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure)
            .IsEqualTo(RenderBackendSelectionFailureKind.RequestedBackendUnsupported);
        await Assert.That(result.Candidates).IsEmpty();
    }

    [Test]
    public async Task ManualSelectionRejectsUnavailableBackend()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RenderBackendKind.D3D12),
            [RenderBackendKind.Storm]);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure)
            .IsEqualTo(RenderBackendSelectionFailureKind.RequestedBackendUnavailable);
    }

    [Test]
    public async Task InitializationFailuresProgressFromD3D12ToVulkan()
    {
        var request = new RenderBackendSelectionRequest(
            RenderPlatform.Windows,
            RequestedBackend: null);

        RenderBackendSelectionResult initial = RenderBackendSelectionPolicy.Select(
            request,
            AllBackends);
        RenderBackendSelectionResult afterStormFailure = RenderBackendSelectionPolicy.Select(
            request,
            AllBackends,
            [RenderBackendKind.Storm]);
        RenderBackendSelectionResult afterD3D12Failure = RenderBackendSelectionPolicy.Select(
            request,
            AllBackends,
            [RenderBackendKind.Storm, RenderBackendKind.D3D12]);
        RenderBackendSelectionResult exhausted = RenderBackendSelectionPolicy.Select(
            request,
            AllBackends,
            [
                RenderBackendKind.Storm,
                RenderBackendKind.D3D12,
                RenderBackendKind.Vulkan
            ]);

        await Assert.That(initial.Candidates[0]).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(afterStormFailure.Candidates).Count().IsEqualTo(2);
        await Assert.That(afterStormFailure.Candidates[0]).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(afterStormFailure.Candidates[1]).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(afterD3D12Failure.Candidates).Count().IsEqualTo(1);
        await Assert.That(afterD3D12Failure.Candidates[0]).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(exhausted.IsSuccess).IsFalse();
        await Assert.That(exhausted.Failure)
            .IsEqualTo(RenderBackendSelectionFailureKind.NoBackendAvailable);
    }

    [Test]
    public async Task UnavailableD3D12ProgressesToVulkan()
    {
        RenderBackendSelectionResult result = RenderBackendSelectionPolicy.Select(
            new RenderBackendSelectionRequest(
                RenderPlatform.Windows,
                RequestedBackend: null),
            [RenderBackendKind.Storm, RenderBackendKind.Vulkan],
            [RenderBackendKind.Storm]);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Candidates).Count().IsEqualTo(1);
        await Assert.That(result.Candidates[0]).IsEqualTo(RenderBackendKind.Vulkan);
    }
}
