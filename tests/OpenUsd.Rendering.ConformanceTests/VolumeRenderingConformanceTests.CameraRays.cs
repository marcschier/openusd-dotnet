// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    [Test]
    public async Task RotatedVolumePreservesItsWorldSpaceDensityOnD3D12()
    {
        await VerifyRotatedVolumeDensity(SilkGraphicsBackend.D3D12).ConfigureAwait(false);
    }

    [Test]
    public async Task RotatedVolumePreservesItsWorldSpaceDensityOnVulkan()
    {
        await VerifyRotatedVolumeDensity(SilkGraphicsBackend.Vulkan).ConfigureAwait(false);
    }

    [Test]
    public async Task RotatedVolumePreservesItsWorldSpaceDensityOnMetal()
    {
        await VerifyRotatedVolumeDensity(SilkGraphicsBackend.Metal).ConfigureAwait(false);
    }

    [Test]
    public async Task NearPlaneExcludesClippedVolumeDensityOnD3D12()
    {
        await VerifyNearPlaneClipsVolumeDensity(SilkGraphicsBackend.D3D12).ConfigureAwait(false);
    }

    [Test]
    public async Task NearPlaneExcludesClippedVolumeDensityOnVulkan()
    {
        await VerifyNearPlaneClipsVolumeDensity(SilkGraphicsBackend.Vulkan).ConfigureAwait(false);
    }

    [Test]
    public async Task NearPlaneExcludesClippedVolumeDensityOnMetal()
    {
        await VerifyNearPlaneClipsVolumeDensity(SilkGraphicsBackend.Metal).ConfigureAwait(false);
    }

    private static async Task VerifyRotatedVolumeDensity(SilkGraphicsBackend backend)
    {
        RequireFieldBoundsBackend(backend);
        string directory = Path.Combine(StageDirectory, $"camera-ray-{Guid.NewGuid():N}");
        string original = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "original", 8, 8, 8, (x, _, _) => x < 4 ? 1 : 0);
        string permuted = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "permuted", 8, 8, 8, (_, _, z) => z < 4 ? 1 : 0);
        double[] transform =
        [
            0.8,   0,    0, 0,
              0, 0.8,    0, 0,
              0,   0,  0.8, 0,
              0,   0,  0.5, 1,
        ];
        double[] rotation =
        [
              0,   0, -0.8, 0,
              0, 0.8,    0, 0,
            0.8,   0,    0, 0,
              0,   0,  0.5, 1,
        ];
        var frame = new SilkFrameState();
        using OpenUsdSilkPage originalPage = CreateIndependentFieldPage(
            original, transform, frame, "8,8,8");
        using OpenUsdSilkPage rotatedPage = CreateIndependentFieldPage(
            permuted, rotation, frame, "8,8,8");
        using OpenUsdSilkPage wrongPage = CreateIndependentFieldPage(
            permuted, transform, frame, "8,8,8");
        Func<ISilkGraphicsDevice> createDevice = () => SilkDepthCaptureConformance.CreateDevice(backend);
        ParityImage reference = Crop(CaptureIndependentFieldPage(originalPage, createDevice), 48, 40, 64, 48);
        ParityImage rotated = Crop(CaptureIndependentFieldPage(rotatedPage, createDevice), 48, 40, 64, 48);
        ParityImage wrong = Crop(CaptureIndependentFieldPage(wrongPage, createDevice), 48, 40, 64, 48);

        ImageDelta control = ImageDelta.Compare(reference, wrong);
        await Assert.That(control.MaximumChannelDelta).IsGreaterThanOrEqualTo(16)
            .Because("the differently oriented density field must produce a visibly different control.");
        await Assert.That(control.MeanChannelDelta).IsGreaterThanOrEqualTo(4)
            .Because("the control must differ over the volume footprint, not just at an edge.");

        ImageDelta equivalence = ImageDelta.Compare(reference, rotated);
        await Assert.That(equivalence.MaximumChannelDelta).IsLessThanOrEqualTo(2)
            .Because("rotating the proxy and its density axes preserves the world-space volume.");
        await Assert.That(equivalence.MeanChannelDelta).IsLessThanOrEqualTo(0.25)
            .Because("equivalent world-space density must integrate to the same image.");
    }

    private static async Task VerifyNearPlaneClipsVolumeDensity(SilkGraphicsBackend backend)
    {
        RequireFieldBoundsBackend(backend);
        string directory = Path.Combine(StageDirectory, $"near-plane-{Guid.NewGuid():N}");
        string clippedDense = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "clipped-dense", 8, 8, 8, (_, _, z) => z < 3 ? 1 : 0.25f);
        string clippedEmpty = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "clipped-empty", 8, 8, 8, (_, _, z) => z < 3 ? 0 : 0.25f);
        string visibleDense = VolumeCommandAuthoring.WriteDensityGrid(
            directory, "visible-dense", 8, 8, 8, (_, _, z) => z < 3 ? 0 : 0.75f);
        double[] transform =
        [
            1, 0,  0, 0,
            0, 1,  0, 0,
            0, 0,  1, 0,
            0, 0, -1, 1,
        ];
        var frame = new SilkFrameState();
        using OpenUsdSilkPage densePage = CreateIndependentFieldPage(
            clippedDense, transform, frame, "8,8,8");
        using OpenUsdSilkPage emptyPage = CreateIndependentFieldPage(
            clippedEmpty, transform, frame, "8,8,8");
        using OpenUsdSilkPage controlPage = CreateIndependentFieldPage(
            visibleDense, transform, frame, "8,8,8");
        Func<ISilkGraphicsDevice> createDevice = () => SilkDepthCaptureConformance.CreateDevice(backend);
        ParityImage dense = Crop(CaptureIndependentFieldPage(densePage, createDevice), 48, 40, 64, 48);
        ParityImage empty = Crop(CaptureIndependentFieldPage(emptyPage, createDevice), 48, 40, 64, 48);
        ParityImage control = Crop(CaptureIndependentFieldPage(controlPage, createDevice), 48, 40, 64, 48);

        ImageDelta visibleChange = ImageDelta.Compare(empty, control);
        await Assert.That(visibleChange.MaximumChannelDelta).IsGreaterThanOrEqualTo(16)
            .Because("the visible-density control must change the rendered volume.");
        await Assert.That(visibleChange.MeanChannelDelta).IsGreaterThanOrEqualTo(4)
            .Because("the control must differ over the volume footprint.");

        ImageDelta clippedChange = ImageDelta.Compare(dense, empty);
        await Assert.That(clippedChange.MaximumChannelDelta).IsLessThanOrEqualTo(2)
            .Because("density behind the near plane must not contribute to the image.");
        await Assert.That(clippedChange.MeanChannelDelta).IsLessThanOrEqualTo(0.25)
            .Because("both fields have the same visible density.");
    }
}
