// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkMaterialStreamingTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RgbaEmissionRetainsItsOriginalRepresentationAndAlpha(SilkGraphicsBackend backend)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage geometry = Page(2, 1, transparent: true);
        _ = actual.Render(geometry);
        _ = expected.Render(geometry);
        using var material = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', true, extended: true, emissiveOnly: true, colorComponents: 4), 1);
        actual.Device.CreatedTextures.Clear();
        _ = actual.Render(material);
        _ = expected.Render(material);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture is { Width: 64, Height: 64, Format: SilkTextureFormat.Rgba32Float })).IsEqualTo(1);
        await Assert.That(actual.Device.CreatedTextures.Any(texture => texture.Format == SilkTextureFormat.R32Float)).IsFalse();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 0.003f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 0.003f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 64f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 64f)]
    public async Task HdrEmissionPreservesExactRgbaDepthAndBoundedOwnership(
        SilkGraphicsBackend backend, bool transparent, float scale)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage geometry = Page(2, 1, transparent: transparent, uvScale: scale, uvScaleY: scale);
        _ = actual.Render(geometry);
        _ = expected.Render(geometry);
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var material = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', transparent, extended: true, emissiveOnly: true), 1);
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - 43348));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm, SilkTextureUsage.Sampled), 1))
        {
            _ = actual.Render(material);
            _ = expected.Render(material);
            await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            await Assert.That(actual.Textures.Usage.PeakReservedBytes).IsEqualTo(actual.Textures.MaximumBytes);
        }
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "indexed")]
    [Arguments(SilkGraphicsBackend.Vulkan, "indexed")]
    [Arguments(SilkGraphicsBackend.D3D12, "arbitrary")]
    [Arguments(SilkGraphicsBackend.Vulkan, "arbitrary")]
    [Arguments(SilkGraphicsBackend.D3D12, "width-collision")]
    [Arguments(SilkGraphicsBackend.Vulkan, "width-collision")]
    public async Task CompactDiffuseFallbackAndMipSamplingRemainExact(SilkGraphicsBackend backend, string kind) =>
        await VerifyCompactColorFallback(backend, kind, false);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "indexed")]
    [Arguments(SilkGraphicsBackend.Vulkan, "indexed")]
    [Arguments(SilkGraphicsBackend.D3D12, "arbitrary")]
    [Arguments(SilkGraphicsBackend.Vulkan, "arbitrary")]
    [Arguments(SilkGraphicsBackend.D3D12, "width-collision")]
    [Arguments(SilkGraphicsBackend.Vulkan, "width-collision")]
    public async Task CompactEmissionFallbackAndMipSamplingRemainExact(SilkGraphicsBackend backend, string kind) =>
        await VerifyCompactColorFallback(backend, kind, true);

    private static async Task VerifyCompactColorFallback(SilkGraphicsBackend backend, string kind, bool emissive)
    {
        bool collision = kind == "width-collision";
        using var actual = new Fixture(backend, true, imageSize: collision ? 12288 : 64,
            imageHeight: collision ? 1 : 64, mixedColors: true, floatDiffuse: kind == "arbitrary");
        using var expected = new Fixture(backend, false, imageSize: collision ? 12288 : 64,
            imageHeight: collision ? 1 : 64, mixedColors: true, floatDiffuse: kind == "arbitrary");
        ulong revision = 1;
        foreach (float scale in new[] { 0.003f, 0.021211f, 0.021215f, 0.084848f, 0.084858f, 1f, 64f })
        {
            using OpenUsdSilkPage geometry = Page(++revision, 1, uvScale: scale, uvScaleY: scale);
            _ = actual.Render(geometry);
            _ = expected.Render(geometry);
            using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, ++revision,
                Material('A', false, extended: true, emissiveOnly: emissive), 1);
            actual.Device.CreatedTextures.Clear();
            _ = actual.Render(maps);
            _ = expected.Render(maps);
            await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            await Assert.That(actual.Device.CreatedTextures.Count(texture => texture.Format == SilkTextureFormat.Rg32Float))
                .IsEqualTo(kind == "indexed" ? 0 : 1);
            await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                texture is { Width: 768, Height: 7, Format: SilkTextureFormat.R32Float }))
                .IsEqualTo(kind == "indexed" ? 1 : 0);
        }
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "upload")]
    [Arguments(SilkGraphicsBackend.Vulkan, "upload")]
    [Arguments(SilkGraphicsBackend.D3D12, "cleanup")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cleanup")]
    [Arguments(SilkGraphicsBackend.D3D12, "cancel")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cancel")]
    public async Task CompactDiffuseFailuresReleaseBothPlanesAndReplay(SilkGraphicsBackend backend, string failure) =>
        await VerifyCompactColorFailures(backend, failure, false);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "upload")]
    [Arguments(SilkGraphicsBackend.Vulkan, "upload")]
    [Arguments(SilkGraphicsBackend.D3D12, "cleanup")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cleanup")]
    [Arguments(SilkGraphicsBackend.D3D12, "cancel")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cancel")]
    public async Task CompactEmissionFailuresReleaseBothPlanesAndReplay(SilkGraphicsBackend backend, string failure) =>
        await VerifyCompactColorFailures(backend, failure, true);

    private static async Task VerifyCompactColorFailures(SilkGraphicsBackend backend, string failure, bool emissive)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        byte[] before = actual.ColorBytes();
        float[] beforeDepth = actual.DepthValues();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true, vectorNormals: true, promotedNormals: true, emissiveOnly: emissive), 1);
        int[] points = failure == "cancel" ? [0, 7, 8, 15, 16, 23] : [0, 7, 8, 15, 16, 23, 24];
        foreach (int after in points)
        {
            using var cancellation = new CancellationTokenSource();
            actual.Device.RefuseSubmissionAfter = failure == "upload" ? after : null;
            actual.Device.FailSubmissionCleanupAfter = failure == "cleanup" ? after : null;
            actual.Device.CancelAfterSubmission = failure == "cancel" ? cancellation : null;
            actual.Device.CancelSubmissionCountdown = after;
            var capture = new SilkDepthCaptureRequest(32, 32, null, cancellation.Token);
            Exception? error = await Assert.That(() => actual.Renderer.ApplyAndRenderForDisplayCapture(
                maps, actual.Color, actual.Depth, SilkMeshRenderOptions.Default, capture, out _)).Throws<Exception>();
            await Assert.That(failure switch
            {
                "upload" => error is SilkGpuStagingBudgetExceededException,
                "cleanup" => error is InvalidOperationException,
                _ => error is OperationCanceledException
            }).IsTrue();
            await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(beforeDepth)).IsTrue();
            await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
            await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        actual.Device.RefuseSubmissionAfter = null;
        actual.Device.FailSubmissionCleanupAfter = null;
        actual.Device.CancelAfterSubmission = null;
        _ = actual.Render(maps);
        _ = expected.Render(maps);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0.003f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.003f)]
    [Arguments(SilkGraphicsBackend.D3D12, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, 64f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64f)]
    public async Task HdrRgbTripletsPreserveExactHdrDepthWithinScaled512MiB(SilkGraphicsBackend backend, float uvScale)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage initial = Page(2, 1, uvScale: uvScale, uvScaleY: uvScale);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true, vectorNormals: true, promotedNormals: true, flipNormalGreen: true), 1);
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - 131072));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm, SilkTextureUsage.Sampled), 1))
        {
            actual.Device.CreatedTextures.Clear();
            _ = actual.Render(maps);
            _ = expected.Render(maps);
            await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            await Assert.That(actual.Renderer.GpuResources.Statistics.PeakTextureResidentGpuBytes).IsEqualTo(130044ul);
            await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                texture is { Width: 768, Height: 7, Format: SilkTextureFormat.R32Float })).IsEqualTo(3);
            await Assert.That(actual.Device.CreatedTextures.Any(texture => texture.Format == SilkTextureFormat.Rg32Float)).IsFalse();
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0.003125f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.003125f)]
    [Arguments(SilkGraphicsBackend.D3D12, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, 64f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64f)]
    public async Task TwoPromotedNormalsPreserveExactHdrAndDepthWithinScaled512MiBBudget(
        SilkGraphicsBackend backend, float uvScale) =>
        await VerifyPromotedNormals(backend, uvScale);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0.003125f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.003125f)]
    [Arguments(SilkGraphicsBackend.D3D12, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1f)]
    public async Task RealNormalTexelsPreserveExactHdrAndDepthWithinScaled512MiBBudget(
        SilkGraphicsBackend backend, float uvScale)
    {
        string? root = Environment.GetEnvironmentVariable("OPENUSD_INDEXED_NORMAL_FIXTURE");
        if (string.IsNullOrEmpty(root))
        {
            Skip.Test("Set OPENUSD_INDEXED_NORMAL_FIXTURE to read-only 64x64 RGBA8 B.rgba/C.rgba source crops.");
            return;
        }
        byte[][] normals = [File.ReadAllBytes(Path.Combine(root, "B.rgba")), File.ReadAllBytes(Path.Combine(root, "C.rgba"))];
        await Assert.That(normals.All(static pixels => pixels.Length == 64 * 64 * 4)).IsTrue();
        await VerifyPromotedNormals(backend, uvScale, normals);
    }

    private static async Task VerifyPromotedNormals(
        SilkGraphicsBackend backend, float uvScale, byte[][]? normalTexels = null)
    {
        using var actual = new Fixture(backend, streaming: true, imageSize: 64, mixedColors: true, normalTexels: normalTexels);
        using var expected = new Fixture(backend, streaming: false, imageSize: 64, mixedColors: true, normalTexels: normalTexels);
        using OpenUsdSilkPage first = Page(2, 1, uvScale: uvScale, uvScaleY: uvScale);
        _ = actual.Render(first);
        _ = expected.Render(first);
        byte[] before = actual.ColorBytes();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, vectorNormals: true, promotedNormals: true), 1);
        // The three 4096-square maps scaled to 64-square, with the same 512 MiB cap.
        const ulong allowance = 512ul * 1024 * 1024 / (64 * 64);
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - allowance));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm,
            SilkTextureUsage.Sampled), 1))
        {
            actual.Device.CreatedTextures.Clear();
            _ = actual.Render(maps);
            _ = expected.Render(maps);
            await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsFalse();
            await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                texture is { Width: 64, Height: 64, MipLevelCount: 7, Format: SilkTextureFormat.Rgba8Unorm }))
                .IsEqualTo(3);
            await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                texture is { Width: 768, Height: 7, MipLevelCount: 1, Format: SilkTextureFormat.R32Float }))
                .IsEqualTo(2);
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, -1)]
    [Arguments(SilkGraphicsBackend.Vulkan, -1)]
    [Arguments(SilkGraphicsBackend.D3D12, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1)]
    public async Task IndexedNormalPlanesHaveAnExactAdmissionBoundaryAndReplay(
        SilkGraphicsBackend backend, int delta)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage first = Page(2, 1);
        _ = actual.Render(first);
        _ = expected.Render(first);
        byte[] before = actual.ColorBytes();
        float[] beforeDepth = actual.DepthValues();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, vectorNormals: true, promotedNormals: true), 1);
        // Three RGBA8 mip chains plus two 768x7 scalar float32 value tables.
        ulong allowance = checked((ulong)(108540 + delta));
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - allowance));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm, SilkTextureUsage.Sampled), 1))
        {
            if (delta < 0)
            {
                await Assert.That(() => actual.Render(maps)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(beforeDepth)).IsTrue();
                await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            }
            else
            {
                _ = actual.Render(maps);
                await Assert.That(actual.Textures.Usage.PeakReservedBytes)
                    .IsEqualTo(actual.Textures.MaximumBytes - (ulong)delta);
            }
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
            await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        _ = actual.Render(maps);
        _ = expected.Render(maps);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "upload")]
    [Arguments(SilkGraphicsBackend.Vulkan, "upload")]
    [Arguments(SilkGraphicsBackend.D3D12, "cleanup")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cleanup")]
    [Arguments(SilkGraphicsBackend.D3D12, "cancel")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cancel")]
    public async Task IndexedNormalPlaneFailuresPreservePublishedHdrDepthAndReplay(
        SilkGraphicsBackend backend, string failure)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage first = Page(2, 1);
        _ = actual.Render(first);
        _ = expected.Render(first);
        byte[] before = actual.ColorBytes();
        float[] beforeDepth = actual.DepthValues();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, vectorNormals: true, promotedNormals: true), 1);
        // Inside the first normal's markers, after its values have been published,
        // then at the final detail-normal values upload and the draw submission
        // after both dedicated value textures have been bound.
        // Cancellation after the final draw has been accepted may complete normally;
        // submission/cleanup failures still exercise rollback at that boundary.
        int[] failurePoints = failure == "cancel" ? [8, 15, 21, 22] : [8, 15, 21, 22, 23];
        foreach (int after in failurePoints)
        {
            ulong uploaded = actual.Renderer.GpuResources.Statistics.TextureUploadBytes;
            using var cancellation = new CancellationTokenSource();
            actual.Device.RefuseSubmissionAfter = failure == "upload" ? after : null;
            actual.Device.FailSubmissionCleanupAfter = failure == "cleanup" ? after : null;
            actual.Device.CancelAfterSubmission = failure == "cancel" ? cancellation : null;
            actual.Device.CancelSubmissionCountdown = after;
            var capture = new SilkDepthCaptureRequest(32, 32, null, cancellation.Token);
            Exception? error = await Assert.That(() => actual.Renderer.ApplyAndRenderForDisplayCapture(
                maps, actual.Color, actual.Depth, SilkMeshRenderOptions.Default, capture, out _)).Throws<Exception>();
            await Assert.That(failure switch
            {
                "upload" => error is SilkGpuStagingBudgetExceededException,
                "cleanup" => error is InvalidOperationException,
                _ => error is OperationCanceledException
            }).IsTrue();
            await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
            await Assert.That(actual.DepthValues().SequenceEqual(beforeDepth)).IsTrue();
            await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
            await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
            if (failure == "cancel")
            {
                ulong expectedBytes = after switch
                {
                    8 => 42324,
                    15 => 81576,
                    21 => 87036,
                    _ => 108540
                };
                await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes - uploaded)
                    .IsEqualTo(expectedBytes);
            }
        }
        actual.Device.RefuseSubmissionAfter = null;
        actual.Device.FailSubmissionCleanupAfter = null;
        actual.Device.CancelAfterSubmission = null;
        _ = actual.Render(maps);
        _ = expected.Render(maps);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task IndexedNormalsPreserveMipTiesWrappingAndArbitraryFloatFallback(
        SilkGraphicsBackend backend, bool floatNormals)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true, floatNormals: floatNormals);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true, floatNormals: floatNormals);
        ulong revision = 1;
        foreach (SilkTextureWrap wrap in new[] { SilkTextureWrap.Repeat, SilkTextureWrap.Mirror, SilkTextureWrap.Clamp })
        {
            // Both sides of the detail-normal and base-normal first mip ties,
            // magnification, every lower mip and clamping beyond the last mip.
            foreach (float scale in new[] { 0.003f, 0.021211f, 0.0212132f, 0.021215f, 0.084848f, 0.0848528f,
                0.084858f, 0.17f, 0.34f, 0.68f, 1.36f, 2.72f, 5.44f, 16f })
            {
                using OpenUsdSilkPage geometry = Page(++revision, 1, uvScale: scale, uvScaleY: scale);
                _ = actual.Render(geometry);
                _ = expected.Render(geometry);
                using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, ++revision,
                    Material('A', false, vectorNormals: true, promotedNormals: true, wrap: wrap,
                        uvTranslate: -0.125f), 1);
                actual.Device.CreatedTextures.Clear();
                _ = actual.Render(maps);
                _ = expected.Render(maps);
                await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
                // An arbitrary float B has more than 256 distinct values; C remains indexed.
                await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                    texture is { Width: 64, Height: 64, MipLevelCount: 7, Format: SilkTextureFormat.Rgba32Float }))
                    .IsEqualTo(floatNormals ? 1 : 0);
                await Assert.That(actual.Device.CreatedTextures.Count(texture =>
                    texture is { Width: 768, Height: 7, MipLevelCount: 1, Format: SilkTextureFormat.R32Float }))
                    .IsEqualTo(floatNormals ? 1 : 2);
            }
        }
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 4, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 4, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 65, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 65, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 64, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64, true)]
    public async Task IndexedNormalsRetainSmallNpotAndAuthoredScalarMapsExactly(
        SilkGraphicsBackend backend, int size, bool scalarMaps)
    {
        using var actual = new Fixture(backend, true, imageSize: size, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: size, mixedColors: true);
        using OpenUsdSilkPage geometry = Page(2, 1, uvScale: 0.003f, uvScaleY: 0.003f);
        _ = actual.Render(geometry);
        _ = expected.Render(geometry);
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, vectorNormals: true, promotedNormals: true, normalScalarMaps: scalarMaps), 1);
        actual.Device.CreatedTextures.Clear();
        _ = actual.Render(maps);
        _ = expected.Render(maps);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture.Format == SilkTextureFormat.Rgba32Float && texture.Width == (uint)size))
            .IsEqualTo(size == 4 ? 2 : 0);
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture.Format == SilkTextureFormat.R32Float && texture.Width == 768))
            .IsEqualTo(size == 4 ? 0 : 2);
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture.Format == SilkTextureFormat.R8Unorm && texture.Width == (uint)size))
            .IsEqualTo(scalarMaps ? 2 : 0);
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, -1)]
    [Arguments(SilkGraphicsBackend.Vulkan, -1)]
    [Arguments(SilkGraphicsBackend.D3D12, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1)]
    public async Task DedicatedNormalTablesPreserveAuthoredMapsAndExactBudget(SilkGraphicsBackend backend, int delta)
    {
        using var actual = new Fixture(backend, true, imageSize: 64, mixedColors: true);
        using var expected = new Fixture(backend, false, imageSize: 64, mixedColors: true);
        using OpenUsdSilkPage initial = Page(2, 1, uvScale: 0.003f, uvScaleY: 0.003f);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        byte[] before = actual.ColorBytes();
        float[] beforeDepth = actual.DepthValues();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, promotedNormals: true, normalScalarMaps: true, flipNormalGreen: true), 1);
        // Diffuse, two indexed normals and both authored scalar maps, with every mip.
        const ulong materialBytes = 119462;
        ulong allowance = checked((ulong)((long)materialBytes + delta));
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - allowance));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm, SilkTextureUsage.Sampled), 1))
        {
            if (delta < 0)
            {
                await Assert.That(() => actual.Render(maps)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(beforeDepth)).IsTrue();
                await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            }
            else
            {
                _ = actual.Render(maps);
                await Assert.That(actual.Textures.Usage.PeakReservedBytes)
                    .IsEqualTo(actual.Textures.MaximumBytes - (ulong)delta);
            }
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        actual.Device.CreatedTextures.Clear();
        _ = actual.Render(maps);
        _ = expected.Render(maps);
        await Assert.That(actual.Renderer.Scene.Materials["/A"].Textures.Count).IsEqualTo(5);
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture is { Width: 768, Height: 7, MipLevelCount: 1, Format: SilkTextureFormat.R32Float })).IsEqualTo(2);
        await Assert.That(actual.Device.CreatedTextures.Count(texture =>
            texture is { Width: 64, Height: 64, MipLevelCount: 7, Format: SilkTextureFormat.R8Unorm })).IsEqualTo(2);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 64f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 64f)]
    public async Task HdrDiffuseAndTwoNormalsPreserveExactPixelsWithinRgbPlaneBudget(
        SilkGraphicsBackend backend, bool oneByteShort, float uvScale)
    {
        using var actual = new Fixture(backend, streaming: true, mixedColors: true);
        using var expected = new Fixture(backend, streaming: false, mixedColors: true);
        using OpenUsdSilkPage first = Page(2, 1, uvScale: uvScale);
        _ = actual.Render(first);
        byte[] before = actual.ColorBytes();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true, vectorNormals: true), 1);
        ulong allowance = oneByteShort ? 419ul : 420ul;
        uint pressure = checked((uint)(actual.Textures.MaximumBytes - retained - allowance));
        using (actual.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm,
            SilkTextureUsage.Sampled), 1))
        {
            if (oneByteShort)
            {
                await Assert.That(() => actual.Render(maps)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
                await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            }
            else
            {
                _ = actual.Render(maps);
                _ = expected.Render(first);
                _ = expected.Render(maps);
                await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            }
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        _ = actual.Render(maps);
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(3ul);
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task StreamingBatchClippingPreservesEmptyAndPartiallyVisibleFrames(
        SilkGraphicsBackend backend, bool oneVisible)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage page = Page(2, 3, offset: 4, visibleMiddle: oneVisible);
        SilkMeshRenderResult result = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(result.DrawCount).IsEqualTo(oneVisible ? 1 : 0);
        await Assert.That(actual.Decoded.Count).IsEqualTo(oneVisible ? 1 : 0);
        await Assert.That(actual.Renderer.Scene.Meshes.Count).IsEqualTo(3);
        await Assert.That(actual.Renderer.GpuResources.Meshes.Count).IsEqualTo(3);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task StreamingClipUsesTheCurrentPhysicsOverrideAndReturnsInside(SilkGraphicsBackend backend)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Mesh0");
        var overrides = new SilkPhysicsTransformOverrides(1);
        actual.Renderer.PhysicsOverrides = overrides;
        PhysicsRenderTransformOverride[] hiddenPose =
            [new(id, new UsdVec3d(4, 0, 0), PhysicsRenderOrientation.Identity, true)];
        overrides.Stage(bindings, new PhysicsRenderOverrideView(hiddenPose, 1));
        using OpenUsdSilkPage original = Page(2, 1);
        using OpenUsdSilkPage outside = Page(2, 1, offset: 4);
        SilkMeshRenderResult hidden = actual.Render(original);
        _ = expected.Render(outside);
        await Assert.That(hidden.DrawCount).IsEqualTo(0);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(1);
        await Assert.That(actual.Decoded.Count).IsEqualTo(0);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        PhysicsRenderTransformOverride[] visiblePose =
            [new(id, new UsdVec3d(0, 0, 0), PhysicsRenderOrientation.Identity, true)];
        overrides.Stage(bindings, new PhysicsRenderOverrideView(visiblePose, 2));
        SilkMeshRenderResult returned = actual.Renderer.Render(actual.Color, actual.Depth);
        using OpenUsdSilkPage inside = Page(3, 1);
        _ = expected.Render(inside);
        await Assert.That(returned.DrawCount).IsEqualTo(1);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, -4f, 0f, 0f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, -4f, 0f, 0f, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 4f, 0f, 0f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 4f, 0f, 0f, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 0f, -4f, 0f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0f, 4f, 0f, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 0f, 0f, -4f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0f, 0f, 4f, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 1.9f, 0f, 0f, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1.9f, 0f, 0f, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 1f, 0f, 0f, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1f, 0f, 0f, false)]
    public async Task StreamingClipsOnlyProvenInvisibleTrianglesAndRetainsAllGeometry(
        SilkGraphicsBackend backend, float x, float y, float z, bool outside)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage page = Page(2, 1, offset: x, offsetY: y, offsetZ: z);
        SilkMeshRenderResult rendered = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(rendered.DrawCount).IsEqualTo(outside ? 0 : 1);
        await Assert.That(actual.Decoded.Count).IsEqualTo(outside ? 0 : 1);
        await Assert.That(actual.Renderer.Scene.Meshes.Count).IsEqualTo(1);
        await Assert.That(actual.Renderer.GpuResources.Meshes.Count).IsEqualTo(1);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        using OpenUsdSilkPage visible = Page(3, 1);
        _ = actual.Render(visible);
        _ = expected.Render(visible);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Decoded.Count).IsEqualTo(outside ? 1 : 2);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, -1)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, -1)]
    [Arguments(SilkGraphicsBackend.D3D12, true, -1)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, -1)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1)]
    public async Task SharedSourceMapsDecodeOnceWithoutSharingMutatedPixels(
        SilkGraphicsBackend backend, bool floatingPoint, int headroomDelta)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        actual.Decoded.Clear();
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, multipleMaps: true, extended: floatingPoint, assetPath: "A"), 1);
        ulong bytes = floatingPoint ? 256ul : 64ul;
        ulong allowance = checked((ulong)((long)(bytes * 3 + bytes / 4) + headroomDelta));
        int expectedDecodes = headroomDelta < 0 ? 3 : 1;
        using (actual.Cpu.Reserve(actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - allowance))
        {
            _ = actual.Render(maps);
            await Assert.That(actual.Decoded.Count).IsEqualTo(expectedDecodes);
        }
        _ = expected.Render(maps);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        actual.Decoded.Clear();
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.Decoded.Count).IsEqualTo(1);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PrivateHdrCaptureUsesOneTargetPairAndPreservesPublicPixels(
        SilkGraphicsBackend backend, bool oneByteShort)
    {
        using var fixture = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage page = Page(2, 1);
        _ = fixture.Render(page);
        byte[] originalColor = fixture.ColorBytes();
        float[] originalDepth = fixture.DepthValues();
        ulong retained = fixture.Textures.Usage.ReservedBytes;
        ulong allowance = oneByteShort ? 83ul : 84ul;
        uint pressure = checked((uint)(fixture.Textures.MaximumBytes - retained - allowance));
        var settings = new RenderSettings(1, true, false, new System.Numerics.Vector4(0, 0, 0, 1));
        SilkFrameCaptureResult capture() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            fixture.Renderer, fixture.Device, 32, 32, settings,
            new SilkHdrColorCaptureOptions(includeDeviceDepth: true), pageRevision: 2);
        using (fixture.Textures.Reserve(new(pressure, 1, SilkTextureFormat.R8Unorm,
            SilkTextureUsage.Sampled), 1))
        {
            if (oneByteShort)
            {
                SilkGpuTextureBudgetExceededException? error =
                    await Assert.That(capture).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(error!.RequestedBytes).IsEqualTo(84ul);
                await Assert.That(error.ReservedBytes + error.RequestedBytes)
                    .IsEqualTo(fixture.Textures.MaximumBytes + 1);
            }
            else
            {
                SilkFrameCaptureResult result = capture();
                await Assert.That(result.HdrColor).IsNotNull();
                await Assert.That(result.Depth).IsNotNull();
                await Assert.That(result.HdrColor!.Rgba16Float.Span.SequenceEqual(originalColor)).IsTrue();
                await Assert.That(result.Depth!.Values.Span.SequenceEqual(originalDepth)).IsTrue();
            }
            await Assert.That(fixture.ColorBytes().SequenceEqual(originalColor)).IsTrue();
            await Assert.That(fixture.DepthValues().SequenceEqual(originalDepth)).IsTrue();
            await Assert.That(fixture.Renderer.Scene.Revision).IsEqualTo(2ul);
            await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(fixture.InitialCpuBytes);
        }
        await Assert.That(fixture.Textures.Usage.ReservedBytes).IsEqualTo(retained - 32ul * 32 * 12);
        SilkFrameCaptureResult replay = capture();
        await Assert.That(replay.HdrColor!.Rgba16Float.Span.SequenceEqual(originalColor)).IsTrue();
        await Assert.That(replay.Depth!.Values.Span.SequenceEqual(originalDepth)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 256, 256)]
    [Arguments(SilkGraphicsBackend.Vulkan, 256, 256)]
    [Arguments(SilkGraphicsBackend.D3D12, 257, 258)]
    [Arguments(SilkGraphicsBackend.Vulkan, 257, 258)]
    public async Task StreamedReuseIndexEvictsTheOldestImageAtItsEntryBound(
        SilkGraphicsBackend backend, int uniqueImages, int decodedCount)
    {
        using var actual = new Fixture(backend, streaming: true, imageSize: 1, residencyBytes: 16384);
        using var expected = new Fixture(backend, streaming: false, imageSize: 1);
        List<byte[]> commands = [SilkMeshRendererConformance.CreateFrameCommand(
            32, 32, SilkMeshRendererConformance.Identity())];
        for (int index = 0; index <= uniqueImages; index++)
        {
            string suffix = index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            string source = index == uniqueImages ? "A000" : $"A{suffix}";
            commands.Add(Material('A', false, pathSuffix: suffix, assetPath: source));
            byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
                (ulong)index + 1, $"/Mesh{suffix}",
                [-0.8f, -0.8f, 0, 0.8f, -0.8f, 0, 0.8f, 0.8f, 0, -0.8f, 0.8f, 0],
                [0, 1, 2, 0, 2, 3]);
            commands.Add(VolumeCommandAuthoring.WithMaterialBinding(mesh, $"/A{suffix}"));
        }
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            commands.SelectMany(static command => command).ToArray(), (uint)commands.Count);
        SilkMeshRenderResult result = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(result.DrawCount).IsEqualTo(uniqueImages + 1);
        await Assert.That(actual.Decoded.Count).IsEqualTo(decodedCount);
        await Assert.That(actual.Renderer.GpuResources.Statistics.PeakTextureResidentGpuBytes)
            .IsLessThanOrEqualTo(257ul * 4);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 83ul, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, 83ul, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, 84ul, 3)]
    [Arguments(SilkGraphicsBackend.Vulkan, 84ul, 3)]
    [Arguments(SilkGraphicsBackend.D3D12, 167ul, 3)]
    [Arguments(SilkGraphicsBackend.Vulkan, 167ul, 3)]
    [Arguments(SilkGraphicsBackend.D3D12, 168ul, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, 168ul, 2)]
    public async Task NonadjacentReuseEvictsBeforeGpuAdmissionAndPreservesRefusedFrames(
        SilkGraphicsBackend backend, ulong allowance, int decodedCount)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        byte[] before = actual.ColorBytes();
        float[] depthBefore = actual.DepthValues();
        ulong retained = actual.Textures.Usage.ReservedBytes;
        actual.Decoded.Clear();
        using OpenUsdSilkPage page = Page(3, 3, repeatFirstAsset: true);
        uint pressureWidth = checked((uint)(actual.Textures.MaximumBytes - retained - allowance));
        using (actual.Textures.Reserve(new(pressureWidth, 1, SilkTextureFormat.R8Unorm,
            SilkTextureUsage.Sampled), 1))
        {
            if (allowance == 83)
            {
                SilkGpuTextureBudgetExceededException? error =
                    await Assert.That(() => actual.Render(page)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(error!.RequestedBytes).IsEqualTo(84ul);
                await Assert.That(error.ReservedBytes + error.RequestedBytes)
                    .IsEqualTo(actual.Textures.MaximumBytes + 1);
                await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(depthBefore)).IsTrue();
                await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
            }
            else
            {
                _ = actual.Render(page);
                _ = expected.Render(page);
                await Assert.That(actual.Decoded.Count).IsEqualTo(decodedCount);
                await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
                await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
            }
            await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        }
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        if (allowance == 83)
        {
            actual.Decoded.Clear();
            _ = actual.Render(page);
            _ = expected.Render(page);
            await Assert.That(actual.Decoded.Count).IsEqualTo(2);
            await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(3ul);
            await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task NonadjacentReuseDoesNotCrossAnAffineOrPrecisionChange(SilkGraphicsBackend backend)
    {
        using var actual = new Fixture(backend, streaming: true, residencyBytes: 1024);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage page = Page(2, 3, repeatFirstAsset: true, thirdExtended: true);
        _ = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(actual.Decoded.Count).IsEqualTo(3);
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 167ul, 3)]
    [Arguments(SilkGraphicsBackend.Vulkan, 167ul, 3)]
    [Arguments(SilkGraphicsBackend.D3D12, 168ul, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, 168ul, 2)]
    [Arguments(SilkGraphicsBackend.D3D12, 169ul, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, 169ul, 2)]
    public async Task NonadjacentImageReuseHonorsExactResidencyAndReleasesEveryFrame(
        SilkGraphicsBackend backend, ulong residencyBytes, int decodedCount)
    {
        using var actual = new Fixture(backend, streaming: true, residencyBytes: residencyBytes);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage page = Page(2, 3, repeatFirstAsset: true);
        _ = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(actual.Decoded.Count).IsEqualTo(decodedCount);
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureResidentGpuBytes).IsEqualTo(0ul);
        actual.Decoded.Clear();
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.Decoded.Count).IsEqualTo(decodedCount);
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task AdjacentMaterialsReuseOnlyExactlyEquivalentImageWorkingSets(
        SilkGraphicsBackend backend, bool changedAffine)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage page = Page(2, 2, sharedAssets: true, secondExtended: changedAffine);
        _ = actual.Render(page);
        _ = expected.Render(page);
        await Assert.That(actual.Decoded.Count).IsEqualTo(changedAffine ? 2 : 1);
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task StagedPhysicsPublicationResolvesAfterSceneSynchronization(SilkGraphicsBackend backend)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: true);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Mesh0");
        var overrides = new SilkPhysicsTransformOverrides(1);
        PhysicsRenderTransformOverride[] poses =
        [
            new(id, new UsdVec3d(0.15, 0, 0), PhysicsRenderOrientation.Identity, true)
        ];
        overrides.Stage(bindings, new PhysicsRenderOverrideView(poses, 17));
        actual.Renderer.PhysicsOverrides = overrides;
        using OpenUsdSilkPage page = Page(2, 1);
        using OpenUsdSilkPage moved = Page(2, 1, offset: 0.15f);
        _ = actual.Render(page);
        _ = expected.Render(moved);
        await Assert.That(overrides.SourceRevision).IsEqualTo(17ul);
        await Assert.That(overrides.Count).IsEqualTo(1);
        await Assert.That(overrides.UnresolvedOverrides).IsEqualTo(0L);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        overrides.Clear();
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        using OpenUsdSilkPage restored = Page(3, 1);
        _ = expected.Render(restored);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "shared")]
    [Arguments(SilkGraphicsBackend.Vulkan, "shared")]
    [Arguments(SilkGraphicsBackend.D3D12, "crossing")]
    [Arguments(SilkGraphicsBackend.Vulkan, "crossing")]
    [Arguments(SilkGraphicsBackend.D3D12, "affine")]
    [Arguments(SilkGraphicsBackend.Vulkan, "affine")]
    [Arguments(SilkGraphicsBackend.D3D12, "missing")]
    [Arguments(SilkGraphicsBackend.Vulkan, "missing")]
    [Arguments(SilkGraphicsBackend.D3D12, "boundary")]
    [Arguments(SilkGraphicsBackend.Vulkan, "boundary")]
    [Arguments(SilkGraphicsBackend.D3D12, "single-component")]
    [Arguments(SilkGraphicsBackend.Vulkan, "single-component")]
    public async Task UdimReachabilityPreservesPrimitiveInteriorsAndAllMaterialUsers(
        SilkGraphicsBackend backend, string scenario)
    {
        SilkUdimTile[] tiles = [new(1001, "A"), new(1002, "B"), new(1003, "C"), new(1004, "N")];
        using var actual = new Fixture(backend, streaming: true, udimTiles: tiles);
        using var expected = new Fixture(backend, streaming: false, udimTiles: tiles);
        using OpenUsdSilkPage page = UdimCoveragePage(2, scenario);
        _ = actual.Render(page);
        _ = expected.Render(page);
        string[] decoded = scenario switch
        {
            "shared" => ["A", "C"],
            "crossing" => ["A", "B", "C"],
            "affine" => ["C"],
            "boundary" => ["A", "B"],
            _ => ["A"]
        };
        await Assert.That(actual.Decoded.Order().SequenceEqual(decoded)).IsTrue();
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.Decoded.Clear();
        using OpenUsdSilkPage following = UdimCoveragePage(3, "affine");
        _ = actual.Render(following);
        _ = expected.Render(following);
        await Assert.That(actual.Decoded.Single()).IsEqualTo("C");
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task PackedUdimMapsMatchRetainedPixelsWithinTextureExtents(
        SilkGraphicsBackend backend, bool scalar, bool constant)
    {
        SilkUdimTile[] tiles = [new(1001, "A"), new(1002, "B"), new(1003, "C"), new(1004, "N")];
        using var actual = new Fixture(backend, streaming: true, udimTiles: tiles, constantTiles: constant);
        using var expected = new Fixture(backend, streaming: false, udimTiles: tiles, constantTiles: constant);
        using OpenUsdSilkPage initial = Page(2, 1, uvScale: 4);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        int created = actual.Device.CreatedTextures.Count;
        actual.Device.TextureDimensionLimit = 16;
        using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, scalarParameter: scalar ? SilkMaterialParameter.Roughness : null, udim: true), 1);
        _ = actual.Render(update);
        _ = expected.Render(update);
        SilkTextureFormat format = scalar ? SilkTextureFormat.R8Unorm : SilkTextureFormat.Rgba8Unorm;
        SilkTextureDescriptor atlas = actual.Device.CreatedTextures.Skip(created)
            .Single(descriptor => descriptor.Format == format && descriptor.Width >= 3);
        await Assert.That(atlas.Width).IsLessThanOrEqualTo(16u);
        await Assert.That(atlas.Height).IsLessThanOrEqualTo(16u);
        await Assert.That(atlas.MipLevelCount).IsEqualTo(1u);
        await AssertPixelsMatchWithinHalfUlp(actual.ColorBytes(), expected.ColorBytes());
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static async Task AssertPixelsMatchWithinHalfUlp(byte[] actualPixels, byte[] expectedPixels)
    {
        int maximumUlps = 0;
        for (int offset = 0; offset < actualPixels.Length; offset += 2)
        {
            ushort a = BinaryPrimitives.ReadUInt16LittleEndian(actualPixels.AsSpan(offset));
            ushort b = BinaryPrimitives.ReadUInt16LittleEndian(expectedPixels.AsSpan(offset));
            await Assert.That(Half.IsFinite(BitConverter.UInt16BitsToHalf(a))).IsTrue();
            await Assert.That(Half.IsFinite(BitConverter.UInt16BitsToHalf(b))).IsTrue();
            maximumUlps = Math.Max(maximumUlps, Math.Abs(a - b));
        }
        // Repacking changes normalized sampler coordinates; SwiftShader can round a half tie
        // to the adjacent value. Source texels and metadata are checked exactly in CPU tests.
        await Assert.That(maximumUlps).IsLessThanOrEqualTo(1);
    }

    private static OpenUsdSilkPage UdimCoveragePage(ulong revision, string scenario)
    {
        List<byte[]> commands =
        [
            SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity()),
            Material('A', false, udim: true, uvTranslate: scenario == "affine" ? 2 : 0)
        ];
        for (int index = 0; index < 2; index++)
        {
            float left = -0.9f + index;
            float low = scenario == "boundary" ? 0.9999995f : 0.1f;
            float high = scenario == "boundary" ? low : scenario == "crossing" ? 2.9f : 0.9f;
            float shift = scenario == "shared" ? index * 2 : 0;
            float[] coordinates = scenario == "single-component" ? [low + shift, high + shift, low + shift] :
                [low + shift, 0.1f, high + shift, 0.1f, low + shift, 0.9f];
            byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand((ulong)index + 1, $"/Coverage{index}",
                [left, -0.7f, 0, left + 0.8f, -0.7f, 0, left, 0.7f, 0], [0, 1, 2],
                0, 0, null, 1, null, null, null, scenario == "missing" ? [] :
                [("st", (uint)SilkAttributeSemantic.TexCoord, scenario == "single-component" ? 1u : 2u, coordinates)]);
            commands.Add(VolumeCommandAuthoring.WithMaterialBinding(mesh, "/A"));
        }
        return new(SilkCommandParser.PageAbiVersion, revision, commands.SelectMany(static item => item).ToArray(),
            (uint)commands.Count);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task UdimAtlasStreamsWithinTileAndRowBandMemory(
        SilkGraphicsBackend backend, bool sparse, bool floatingPoint)
    {
        SilkUdimTile[] tiles = sparse
            ? [new(1001, "A"), new(1003, "B")] : [new(1001, "A")];
        using var actual = new Fixture(backend, streaming: true, udimTiles: tiles);
        using var expected = new Fixture(backend, streaming: false, udimTiles: tiles);
        using OpenUsdSilkPage initial = Page(2, 1, uvScale: sparse ? 3 : 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: floatingPoint, udim: true), 1);
        ulong pixelBytes = floatingPoint ? 16ul : 4ul;
        ulong width = sparse ? 18ul : 6ul;
        ulong allowance = (ulong)tiles.Length * 16 * pixelBytes + 2 * width * pixelBytes;
        ulong before = actual.Renderer.GpuResources.Statistics.TextureUploadBytes;
        using (actual.Cpu.Reserve(actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - allowance))
        {
            _ = actual.Render(page);
            await Assert.That(actual.Cpu.Usage.PeakReservedBytes).IsEqualTo(actual.Cpu.MaximumBytes);
        }
        _ = expected.Render(page);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes - before)
            .IsEqualTo(width * 7 * pixelBytes);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "cpu")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cpu")]
    [Arguments(SilkGraphicsBackend.D3D12, "submission")]
    [Arguments(SilkGraphicsBackend.Vulkan, "submission")]
    [Arguments(SilkGraphicsBackend.D3D12, "cleanup")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cleanup")]
    [Arguments(SilkGraphicsBackend.D3D12, "cancel")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cancel")]
    public async Task UdimBandRefusalDrainsAndPreservesThePublishedFrame(
        SilkGraphicsBackend backend, string failure)
    {
        SilkUdimTile[] tiles = [new(1001, "B")];
        using var actual = new Fixture(backend, streaming: true, udimTiles: tiles);
        using var expected = new Fixture(backend, streaming: false, udimTiles: tiles);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        byte[] before = actual.ColorBytes();
        float[] depth = actual.DepthValues();
        ulong gpuBefore = actual.Textures.Usage.ReservedBytes;
        using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, udim: true), 1);
        using var cancellation = new CancellationTokenSource();
        actual.Device.RefuseSubmissionAfter = failure == "submission" ? 3 : null;
        actual.Device.FailSubmissionCleanupAfter = failure == "cleanup" ? 3 : null;
        actual.Device.CancelAfterSubmission = failure == "cancel" ? cancellation : null;
        actual.Device.CancelSubmissionCountdown = 3;
        ulong capacity = failure == "cpu" ? 111ul : 112ul;
        ulong uploadBefore = actual.Renderer.GpuResources.Statistics.TextureUploadBytes;
        using (actual.Cpu.Reserve(actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - capacity))
        {
            ulong reserved = actual.Cpu.Usage.ReservedBytes;
            var request = new SilkDepthCaptureRequest(32, 32, null, cancellation.Token);
            Exception? error = await Assert.That(() => actual.Renderer.ApplyAndRenderForDisplayCapture(
                update, actual.Color, actual.Depth, SilkMeshRenderOptions.Default, request, out _)).ThrowsException();
            await Assert.That(failure switch
            {
                "cpu" => error is SilkCpuTextureBudgetExceededException,
                "submission" => error is SilkGpuStagingBudgetExceededException,
                "cleanup" => error is InvalidOperationException,
                "cancel" => error is OperationCanceledException,
                _ => false
            }).IsTrue();
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(reserved);
        }
        if (failure != "cpu")
        {
            ulong uploaded = actual.Renderer.GpuResources.Statistics.TextureUploadBytes - uploadBefore;
            await Assert.That(uploaded).IsGreaterThan(0ul);
            await Assert.That(uploaded).IsLessThan(168ul);
        }
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(depth)).IsTrue();
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(gpuBefore);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        actual.Device.RefuseSubmissionAfter = null;
        actual.Device.FailSubmissionCleanupAfter = null;
        actual.Device.CancelAfterSubmission = null;
        using var following = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 4, [], 0);
        actual.Renderer.ApplyPage(following);
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        _ = expected.Render(update);
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(4ul);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsFalse();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task LargeFloatUploadBandsFitEightMiBOfTransferHeadroom(SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(backend, streaming: true, imageSize: 1024, stagingBytes: 8_389_376);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = fixture.Render(initial);
        ulong before = fixture.Renderer.GpuResources.Statistics.TextureUploadBytes;
        using var changed = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true), 1);
        await Assert.That(fixture.Render(changed).DrawCount).IsEqualTo(1);
        await Assert.That(fixture.Renderer.GpuResources.Statistics.TextureUploadBytes - before).IsEqualTo(16_777_212ul);
        await Assert.That(fixture.Staging.Usage.PeakReservedBytes).IsLessThanOrEqualTo(8_389_376ul);
        await Assert.That(fixture.Staging.Usage.PeakReservedBytes).IsGreaterThanOrEqualTo(8_388_608ul);
        await Assert.That(fixture.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(fixture.InitialCpuBytes);
        fixture.ReleaseResources();
        await fixture.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task ScalarMapsPreservePixelsWithinTheirSingleChannelWorkingSet(
        SilkGraphicsBackend backend, bool floatingPoint)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, multipleMaps: true, extended: floatingPoint), 1);
        ulong uploaded = actual.Renderer.GpuResources.Statistics.TextureUploadBytes;
        ulong workingSet = floatingPoint ? 252ul + 84 + 84 : 84ul + 21 + 21;
        uint pressureWidth = checked((uint)(actual.Textures.MaximumBytes -
            actual.Textures.Usage.ReservedBytes - workingSet));
        using (actual.Textures.Reserve(new(pressureWidth, 1, SilkTextureFormat.R8Unorm,
            SilkTextureUsage.Sampled), 1))
        {
            _ = actual.Render(maps);
        }
        _ = expected.Render(maps);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes - uploaded).IsEqualTo(workingSet);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task OneByteShortScalarGpuWorkingSetPreservesTheFrameAndReplays(
        SilkGraphicsBackend backend, bool floatingPoint)
    {
        using var fixture = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = fixture.Render(initial);
        byte[] before = fixture.ColorBytes();
        float[] depthBefore = fixture.DepthValues();
        ulong retained = fixture.Textures.Usage.ReservedBytes;
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, multipleMaps: true, extended: floatingPoint), 1);
        ulong allowance = floatingPoint ? 419ul : 125ul;
        uint pressureWidth = checked((uint)(fixture.Textures.MaximumBytes - retained - allowance));
        using (fixture.Textures.Reserve(new(pressureWidth, 1, SilkTextureFormat.R8Unorm, SilkTextureUsage.Sampled), 1))
        {
            SilkGpuTextureBudgetExceededException? refusal =
                await Assert.That(() => fixture.Render(maps)).Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(refusal!.RequestedBytes).IsEqualTo(floatingPoint ? 84ul : 21ul);
            await Assert.That(refusal.ReservedBytes + refusal.RequestedBytes).IsEqualTo(fixture.Textures.MaximumBytes + 1);
            await Assert.That(fixture.ColorBytes().SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.DepthValues().SequenceEqual(depthBefore)).IsTrue();
            await Assert.That(fixture.Renderer.Scene.Revision).IsEqualTo(2ul);
            await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(fixture.InitialCpuBytes);
            await Assert.That(fixture.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        await Assert.That(fixture.Textures.Usage.ReservedBytes).IsEqualTo(retained);
        _ = fixture.Render(maps);
        await Assert.That(fixture.Renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(fixture.ColorBytes().SequenceEqual(before)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task EveryScalarSlotAndCompositePreservesRetainedPixels(SilkGraphicsBackend backend, bool floatingPoint)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: false);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        SilkMaterialParameter[] slots = [SilkMaterialParameter.Roughness, SilkMaterialParameter.Metallic,
            SilkMaterialParameter.Opacity, SilkMaterialParameter.Occlusion, SilkMaterialParameter.Clearcoat,
            SilkMaterialParameter.ClearcoatRoughness, SilkMaterialParameter.Ior];
        ulong revision = 3;
        foreach (SilkMaterialParameter slot in slots)
        {
            foreach (SilkCompositeOperator operation in new[]
                { SilkCompositeOperator.None, SilkCompositeOperator.Multiply, SilkCompositeOperator.Add,
                    SilkCompositeOperator.Subtract, SilkCompositeOperator.Mix })
            {
                using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, revision++,
                    Material('A', false, extended: floatingPoint, scalarParameter: slot, composite: operation), 1);
                ulong before = actual.Renderer.GpuResources.Statistics.TextureUploadBytes;
                _ = actual.Render(page);
                _ = expected.Render(page);
                await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
                await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
                await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes - before)
                    .IsEqualTo((operation == SilkCompositeOperator.None ? 1ul : 2ul) * (floatingPoint ? 84ul : 21ul));
                await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
                await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task FloatTextureStreamsWithoutOverlappingItsBaseAndCompleteMipChain(SilkGraphicsBackend backend)
    {
        using var expected = new Fixture(backend, streaming: false);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage geometry = Page(2, 1);
        expected.Renderer.ApplyPage(geometry);
        actual.Renderer.ApplyPage(geometry);
        using var map = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true), 1);
        _ = expected.Render(map);
        using (IDisposable pressure = actual.Cpu.Reserve(
            actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - 512))
        {
            ulong before = actual.Cpu.Usage.ReservedBytes;
            _ = actual.Render(map);
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(before);
        }
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes).IsEqualTo(252ul);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task ProgressiveFloatMipsHonorExactAdjacentLevelCapacity(
        SilkGraphicsBackend backend, bool oneByteShort)
    {
        using var fixture = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage geometry = Page(2, 1);
        fixture.Renderer.ApplyPage(geometry);
        using var map = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true), 1);
        ulong allowance = oneByteShort ? 319ul : 320ul;
        using (IDisposable pressure = fixture.Cpu.Reserve(
            fixture.Cpu.MaximumBytes - fixture.Cpu.Usage.ReservedBytes - allowance))
        {
            ulong before = fixture.Cpu.Usage.ReservedBytes;
            if (oneByteShort)
            {
                await Assert.That(() => fixture.Render(map)).Throws<SilkCpuTextureBudgetExceededException>();
                await Assert.That(fixture.ColorBytes().SequenceEqual(fixture.InitialColor)).IsTrue();
            }
            else
            {
                await Assert.That(fixture.Render(map).DrawCount).IsEqualTo(1);
                await Assert.That(fixture.Cpu.Usage.PeakReservedBytes).IsEqualTo(fixture.Cpu.MaximumBytes);
            }
            await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(before);
        }
        fixture.ReleaseResources();
        await fixture.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task FailedLaterFloatMipKeepsTheOldFrameAndReplays(SilkGraphicsBackend backend)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        byte[] before = actual.ColorBytes();
        using var map = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true), 1);
        actual.Device.RefuseSubmissionAfter = 1;
        await Assert.That(() => actual.Render(map)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        actual.Device.RefuseSubmissionAfter = null;
        _ = actual.Render(map);
        _ = expected.Render(map);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task CancellationAfterFirstMipStopsFurtherUploadsAndKeepsThePublishedFrame(
        SilkGraphicsBackend backend, bool oneRowBands)
    {
        using var fixture = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = fixture.Render(initial);
        byte[] before = fixture.ColorBytes();
        ulong uploaded = fixture.Renderer.GpuResources.Statistics.TextureUploadBytes;
        using var map = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, extended: true), 1);
        using var cancelled = new CancellationTokenSource();
        fixture.Device.CancelAfterSubmission = cancelled;
        fixture.Device.CancelSubmissionCountdown = 0;
        using (IDisposable? pressure = oneRowBands ? fixture.Cpu.Reserve(
            fixture.Cpu.MaximumBytes - fixture.Cpu.Usage.ReservedBytes - 320) : null)
        {
            ulong reserved = fixture.Cpu.Usage.ReservedBytes;
            var capture = new SilkDepthCaptureRequest(32, 32, null, cancelled.Token);
            await Assert.That(() => fixture.Renderer.ApplyAndRenderForDisplayCapture(
                map, fixture.Color, fixture.Depth, SilkMeshRenderOptions.Default, capture, out _))
                .Throws<OperationCanceledException>();
            await Assert.That(fixture.Renderer.GpuResources.Statistics.TextureUploadBytes - uploaded)
                .IsEqualTo(oneRowBands ? 32ul : 128ul);
            await Assert.That(fixture.ColorBytes().SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Renderer.Scene.Revision).IsEqualTo(2ul);
            await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(reserved);
            await Assert.That(fixture.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        fixture.Device.CancelAfterSubmission = null;
        _ = fixture.Render(map);
        await Assert.That(fixture.ColorBytes().SequenceEqual(before)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task VolumePixelsReleaseAfterTheirUploadAndRenderTheDensityGrid(SilkGraphicsBackend backend)
    {
        string directory = Path.Combine(Path.GetTempPath(), "streamed-volume-" + Guid.NewGuid().ToString("N"));
        string grid = VolumeCommandAuthoring.WriteDensityGrid(directory, "density", 2, 2, 2, static (_, _, _) => 0.75f);
        try
        {
            using var fixture = new Fixture(backend, streaming: true, volumeSupport: true);
            using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
                [.. SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity()),
                    .. VolumeCommandAuthoring.CreateVolumeMaterialCommand("/Volume", 1, null, null),
                    .. VolumeCommandAuthoring.CreateVolumeProxyMeshCommand("/Mesh", "/Volume")], 3);
            fixture.Renderer.ApplyPage(initial);
            using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
                VolumeCommandAuthoring.CreateVolumeMaterialCommand("/Volume", 1, grid, "2,2,2"), 1);
            using (IDisposable pressure = fixture.Cpu.Reserve(
                fixture.Cpu.MaximumBytes - fixture.Cpu.Usage.ReservedBytes - 64))
            {
                ulong reserved = fixture.Cpu.Usage.ReservedBytes;
                await Assert.That(fixture.Render(update).DrawCount).IsEqualTo(1);
                await Assert.That(fixture.Cpu.Usage.ReservedBytes).IsEqualTo(reserved);
                await Assert.That(fixture.Cpu.Usage.PeakReservedBytes).IsEqualTo(fixture.Cpu.MaximumBytes);
            }
            await Assert.That(fixture.Renderer.GpuResources.Statistics.TextureUploadBytes).IsEqualTo(32ul);
            await Assert.That(fixture.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
            await Assert.That(fixture.ColorBytes().SequenceEqual(fixture.InitialColor)).IsFalse();
            fixture.ReleaseResources();
            await fixture.AssertReleased();
        }
        finally
        {
            File.Delete(grid);
            Directory.Delete(directory);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task LaterMapUploadFailurePreservesThePublishedMaterialAndReplays(SilkGraphicsBackend backend)
    {
        using var expected = new Fixture(backend, streaming: true);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        _ = expected.Render(initial);
        byte[] before = actual.ColorBytes();
        ulong textures = actual.Textures.Usage.ReservedBytes;
        using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, multipleMaps: true), 1);
        actual.Device.RefuseSubmissionAfter = 3;
        await Assert.That(() => actual.Render(update)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(actual.Renderer.Scene.Materials["/A"].Textures.Count).IsEqualTo(1);
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(textures);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        actual.Device.RefuseSubmissionAfter = null;
        using var following = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 4, [], 0);
        actual.Renderer.ApplyPage(following);
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        _ = expected.Render(update);
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(4ul);
        await Assert.That(actual.Renderer.Scene.Materials["/A"].Textures.Count).IsEqualTo(3);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task OneMaterialStreamsItsMapsWithinOneUploadPixelWorkingSet(SilkGraphicsBackend backend)
    {
        using var expected = new Fixture(backend, streaming: false);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage geometry = Page(2, 1);
        expected.Renderer.ApplyPage(geometry);
        actual.Renderer.ApplyPage(geometry);
        using var maps = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            Material('A', false, multipleMaps: true), 1);
        _ = expected.Render(maps);
        using (IDisposable pressure = actual.Cpu.Reserve(
            actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - 80))
        {
            ulong before = actual.Cpu.Usage.ReservedBytes;
            _ = actual.Render(maps);
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(before);
            await Assert.That(actual.Cpu.Usage.PeakReservedBytes).IsEqualTo(actual.Cpu.MaximumBytes);
        }
        await Assert.That(actual.Decoded.Count).IsEqualTo(3);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureUploadBytes).IsEqualTo(126ul);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task MissingTextureCoordinatesMatchExplicitZeroCoordinates(
        SilkGraphicsBackend backend, bool normalMap)
    {
        using var expected = new Fixture(backend, streaming: true);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage explicitPage = Page(2, 1, normalMap: normalMap, zeroUvs: true);
        using OpenUsdSilkPage missingPage = Page(2, 1, normalMap: normalMap, omitUvs: true);
        _ = expected.Render(explicitPage);
        _ = actual.Render(missingPage);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.ColorBytes().SequenceEqual(actual.InitialColor)).IsFalse();
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Select(static entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task NormalMappedInstancesShareOneGpuGeometryAndKeepBothPlacements(SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(backend, streaming: true);
        List<byte[]> commands =
        [
            SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity()),
            Material('A', false, normalMap: true)
        ];
        for (int i = 0; i < 2; i++)
        {
            byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
                (ulong)i + 1, "/Shared",
                [-0.8f, -0.5f, 0, -0.2f, -0.5f, 0, -0.2f, 0.5f, 0, -0.8f, 0.5f, 0],
                [0, 1, 2, 0, 2, 3], i, 0, null, 1, null, null, null,
                [("st", (uint)SilkAttributeSemantic.TexCoord, 2, [0, 0, 1, 0, 1, 1, 0, 1])]);
            BinaryPrimitives.WriteInt32LittleEndian(mesh.AsSpan(20), 1);
            BinaryPrimitives.WriteInt32LittleEndian(mesh.AsSpan(24), i);
            mesh = VolumeCommandAuthoring.WithMaterialBinding(mesh, "/A");
            byte[] instancer = Encoding.UTF8.GetBytes("/Instancer");
            int offset = mesh.Length;
            Array.Resize(ref mesh, offset + (instancer.Length * 2) + 8);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(260), (uint)instancer.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(264), 1);
            instancer.CopyTo(mesh, offset);
            offset += instancer.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(offset), (uint)instancer.Length);
            BinaryPrimitives.WriteInt32LittleEndian(mesh.AsSpan(offset + 4), i);
            instancer.CopyTo(mesh, offset + 8);
            commands.Add(mesh);
        }
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            commands.SelectMany(static item => item).ToArray(), (uint)commands.Count);
        SilkMeshRenderResult result = fixture.Render(page);
        await Assert.That(fixture.Renderer.Scene.Meshes.Count).IsEqualTo(2);
        await Assert.That(result.DrawCount).IsEqualTo(1);
        await Assert.That(result.Statistics.GeometryBuilds).IsEqualTo(1ul);
        await Assert.That(result.Statistics.VertexUploads).IsEqualTo(1ul);
        await Assert.That(result.Statistics.IndexUploads).IsEqualTo(1ul);
        float[] depth = fixture.DepthValues();
        await Assert.That(depth[(16 * 32) + 8]).IsEqualTo(0.5f);
        await Assert.That(depth[(16 * 32) + 24]).IsEqualTo(0.5f);
        await Assert.That(depth[(16 * 32) + 16]).IsEqualTo(1f);
        _ = fixture.Renderer.Render(fixture.Color, fixture.Depth);
        await Assert.That(fixture.Renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(1ul);
        await Assert.That(fixture.DepthValues().SequenceEqual(depth)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task FinalBatchAppliesDisplayConversionAndSelectionExactlyOnce(
        SilkGraphicsBackend backend, bool selection)
    {
        using var expected = new Fixture(backend, streaming: false);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage page = Page(2, 3);
        var transform = new RenderDisplayTransform(Path.Combine(Path.GetTempPath(), "streaming-control.ocio"),
            "linear", latticeSize: 8);
        var options = SilkMeshRenderOptions.Default with { DisplayTransform = transform };
        byte[]? expectedHdr = null;
        foreach (Fixture fixture in new[] { expected, actual })
        {
            fixture.Renderer.UpdateSelection(new SelectionState(["/Mesh0"]),
                new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3, true));
            if (selection)
            {
                _ = fixture.Renderer.ApplyAndRender(page, fixture.Color, fixture.Depth, options);
            }
            else
            {
                var request = SilkDepthCaptureRequest.ForHdrColor(32, 32, null, CancellationToken.None);
                _ = fixture.Renderer.ApplyAndRenderForDisplayCapture(
                    page, fixture.Color, fixture.Depth, options, request, out SilkHdrColorCaptureResult? hdr);
                await Assert.That(hdr).IsNotNull();
                if (expectedHdr is null)
                {
                    expectedHdr = hdr!.Rgba16Float.ToArray();
                }
                else
                {
                    await Assert.That(hdr!.Rgba16Float.Span.SequenceEqual(expectedHdr)).IsTrue();
                }
            }
        }
        byte[] output = actual.ColorBytes();
        await Assert.That(output.SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(output)).IsFalse();
        if (selection)
        {
            actual.Renderer.UpdateSelection(new SelectionState(Array.Empty<string>()));
            _ = actual.Renderer.Render(actual.Color, actual.Depth, options);
            await Assert.That(actual.ColorBytes().SequenceEqual(output)).IsFalse();
        }
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 1, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 3, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 3, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 3, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 3, true)]
    public async Task OrderedFullResolutionBatchesMatchRetainedHdrAndDepth(
        SilkGraphicsBackend backend, int meshes, bool transparent)
    {
        using var expected = new Fixture(backend, streaming: false);
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage page = Page(2, meshes, transparent);
        SilkMeshRenderResult reference = expected.Render(page);
        SilkMeshRenderResult streamed = actual.Render(page);
        await Assert.That(streamed.DrawCount).IsEqualTo(meshes);
        await Assert.That(streamed.DrawCount).IsEqualTo(reference.DrawCount);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        if (meshes != 0)
        {
            await Assert.That(actual.Decoded.Count).IsEqualTo(transparent ? 2 : meshes);
            await Assert.That(expected.Decoded.Count).IsEqualTo(transparent ? 2 : meshes);
            await Assert.That(actual.ColorBytes().SequenceEqual(actual.InitialColor)).IsFalse();
            await Assert.That(actual.DepthValues().Any(static value => value < 1)).IsEqualTo(!transparent);
        }
        if (transparent)
        {
            await Assert.That(actual.Decoded.SequenceEqual(["A", "B"])).IsTrue();
        }
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        actual.ReleaseResources();
        await actual.AssertReleased();
        expected.ReleaseResources();
        await expected.AssertReleased();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task OneMaterialFitsExactPixelCapacityButNotOneByteLess(
        SilkGraphicsBackend backend, bool oneByteShort)
    {
        using var actual = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage page = Page(2, 3);
        // The 64-byte base overlaps only one 16-byte row or its 16-byte next mip.
        ulong capacity = oneByteShort ? 79ul : 80ul;
        using (IDisposable pressure = actual.Cpu.Reserve(
            actual.Cpu.MaximumBytes - actual.Cpu.Usage.ReservedBytes - capacity))
        {
            ulong reserved = actual.Cpu.Usage.ReservedBytes;
            if (oneByteShort)
            {
                await Assert.That(() => actual.Render(page)).Throws<SilkCpuTextureBudgetExceededException>();
                await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(1ul);
            }
            else
            {
                await Assert.That(actual.Render(page).DrawCount).IsEqualTo(3);
                await Assert.That(actual.Cpu.Usage.PeakReservedBytes).IsEqualTo(actual.Cpu.MaximumBytes);
            }
            await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(reserved);
            await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        }
        if (oneByteShort)
        {
            await Assert.That(actual.ColorBytes().SequenceEqual(actual.InitialColor)).IsTrue();
            _ = actual.Render(page);
        }
        using var expected = new Fixture(backend, streaming: false);
        using (IDisposable pressure = expected.Cpu.Reserve(
            expected.Cpu.MaximumBytes - expected.Cpu.Usage.ReservedBytes - 168))
        {
            await Assert.That(() => expected.Render(page)).Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(expected.Renderer.Scene.Revision).IsEqualTo(1ul);
        }
        _ = expected.Render(page);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "submission")]
    [Arguments(SilkGraphicsBackend.Vulkan, "submission")]
    [Arguments(SilkGraphicsBackend.D3D12, "cleanup")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cleanup")]
    [Arguments(SilkGraphicsBackend.D3D12, "cancel")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cancel")]
    public async Task InterruptedLaterBatchKeepsPublishedFrameAndReplaysTheWholePage(
        SilkGraphicsBackend backend, string failure)
    {
        using var actual = new Fixture(backend, streaming: true);
        using var expected = new Fixture(backend, streaming: true);
        using OpenUsdSilkPage initial = Page(2, 1);
        _ = actual.Render(initial);
        byte[] before = actual.ColorBytes();
        float[] beforeDepth = actual.DepthValues();
        ulong gpuBefore = actual.Textures.Usage.ReservedBytes;
        using OpenUsdSilkPage changed = Page(3, 3, offset: 0.15f);
        using var cancellation = new CancellationTokenSource();
        actual.Device.RefuseSubmissionAfter = failure == "submission" ? 4 : null;
        actual.Device.FailSubmissionCleanupAfter = failure == "cleanup" ? 7 : null;
        actual.Device.CancelAfterSubmission = failure == "cancel" ? cancellation : null;
        actual.Device.CancelSubmissionCountdown = failure == "cancel" ? 3 : 0;
        var request = new SilkDepthCaptureRequest(32, 32, null, cancellation.Token);
        Exception? error = await Assert.That(() => actual.Renderer.ApplyAndRenderForDisplayCapture(
            changed, actual.Color, actual.Depth, SilkMeshRenderOptions.Default, request, out _)).ThrowsException();
        await Assert.That(failure switch
        {
            "submission" => error is SilkGpuStagingBudgetExceededException,
            "cleanup" => error is InvalidOperationException,
            _ => error is OperationCanceledException,
        }).IsTrue();
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(actual.Renderer.Scene.Meshes.Count).IsEqualTo(1);
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(beforeDepth)).IsTrue();
        await Assert.That(actual.Cpu.Usage.ReservedBytes).IsEqualTo(actual.InitialCpuBytes);
        await Assert.That(actual.Textures.Usage.ReservedBytes).IsEqualTo(gpuBefore);
        await Assert.That(actual.Staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(actual.Renderer.GpuResources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        actual.Device.RefuseSubmissionAfter = null;
        actual.Device.FailSubmissionCleanupAfter = null;
        // Replay must not inherit the cancelled capture token.
        using var following = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 4, [], 0);
        actual.Renderer.ApplyPage(following);
        await Assert.That(actual.Renderer.Scene.Revision).IsEqualTo(4ul);
        await Assert.That(actual.Renderer.Scene.Meshes.Count).IsEqualTo(3);
        _ = actual.Renderer.Render(actual.Color, actual.Depth);
        _ = expected.Render(changed);
        await Assert.That(actual.ColorBytes().SequenceEqual(expected.ColorBytes())).IsTrue();
        await Assert.That(actual.DepthValues().SequenceEqual(expected.DepthValues())).IsTrue();
        await Assert.That(actual.ColorBytes().SequenceEqual(before)).IsFalse();
        actual.ReleaseResources();
        await actual.AssertReleased();
    }

    private sealed class Fixture : IDisposable
    {
        internal SilkGpuPublicationDevice Device { get; }
        internal SilkCpuTextureBudget Cpu { get; } = new(4_194_304);
        internal SilkGpuTextureBudget Textures { get; } = new(4_194_304);
        internal SilkGpuStagingBudget Staging { get; } = new(4_194_304);
        internal SilkGpuBufferBudget Buffers { get; } = new(4_194_304);
        internal SilkMeshRenderer Renderer { get; }
        internal ISilkGraphicsTexture Color { get; }
        internal ISilkGraphicsTexture Depth { get; }
        internal List<string> Decoded { get; } = [];
        internal ulong InitialCpuBytes { get; }
        internal byte[] InitialColor { get; }
        private readonly int _imageSize;
        private readonly int _imageHeight;
        private readonly bool _constantTiles;
        private readonly bool _mixedColors;
        private readonly bool _floatNormals;
        private readonly bool _floatDiffuse;
        private readonly byte[][]? _normalTexels;

        internal Fixture(
            SilkGraphicsBackend backend, bool streaming, bool volumeSupport = false,
            int imageSize = 4, ulong stagingBytes = 4_194_304,
            IReadOnlyList<SilkUdimTile>? udimTiles = null, bool constantTiles = false,
            ulong residencyBytes = 512, bool mixedColors = false, bool floatNormals = false, byte[][]? normalTexels = null,
            int? imageHeight = null, bool floatDiffuse = false)
        {
            _imageSize = imageSize;
            _imageHeight = imageHeight ?? imageSize;
            _constantTiles = constantTiles;
            _mixedColors = mixedColors;
            _floatNormals = floatNormals;
            _floatDiffuse = floatDiffuse;
            _normalTexels = normalTexels;
            Cpu = new(Math.Max(4_194_304ul, checked((ulong)imageSize * (ulong)_imageHeight * 32)));
            Textures = new(Cpu.MaximumBytes);
            Staging = new(stagingBytes);
            ISilkGraphicsDevice native = SilkDepthCaptureConformance.CreateDevice(backend);
            Device = volumeSupport ? new VolumeDevice(native) : new(native);
            Device.ConfigureCpuTextureBudget(Cpu);
            Device.ConfigureTextureBudget(Textures);
            Device.ConfigureStagingBudget(Staging);
            Device.ConfigureBufferBudget(Buffers);
            var lattice = new TestLattice();
            Renderer = new SilkMeshRenderer(Device,
                backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
                Decode, udimResolver: udimTiles is null ? null : _ => udimTiles,
                textureResidencyOptions: new(512, residencyBytes, streaming), cpuTextureBudget: Cpu,
                ownedImageDecoder: (path, linear, budget) =>
                {
                    IDisposable reservation = budget.Reserve(
                        checked((ulong)_imageSize * (ulong)_imageHeight *
                            ((_floatNormals && path == "B") || (_floatDiffuse && path == "A") ? 16ul : 4ul)));
                    SilkDecodedImage image = Decode(path, linear);
                    image.OwnReservation(reservation);
                    return image;
                }, displayLattices: new SilkDisplayTransformLatticeCache(Cpu, lattice, identityProvider: lattice));
            Color = Device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(32, 32));
            Depth = Device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(32, 32));
            using OpenUsdSilkPage warm = Page(1, 0);
            _ = Render(warm);
            InitialCpuBytes = Cpu.Usage.ReservedBytes;
            InitialColor = ColorBytes();
        }

        private sealed class VolumeDevice(ISilkGraphicsDevice inner)
            : SilkGpuPublicationDevice(inner), ISilkVolumeTextureGraphicsDevice
        {
            public ISilkGraphicsTexture CreateTexture3D(
                uint width, uint height, uint depth, SilkTextureFormat format) =>
                ((ISilkVolumeTextureGraphicsDevice)Inner).CreateTexture3D(width, height, depth, format);
        }

        private sealed class TestLattice : ISilkDisplayTransformLatticeProvider, ISilkDisplayTransformConfigIdentityProvider
        {
            public SilkDisplayTransformConfigIdentity GetIdentity(string configPath) => new("streaming-control", true);
            public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
            {
                byte[] pixels = new byte[8 * 8 * 8 * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = (byte)(((i / 4) % 8) * 255 / 7);
                    pixels[i + 3] = 255;
                }
                return new(8, transform.ShaperMinimumLog2, transform.ShaperRangeLog2, pixels);
            }
        }

        private SilkDecodedImage Decode(string path, bool _)
        {
            Decoded.Add(path);
            if (_normalTexels is not null && path is "B" or "C")
            {
                return new((uint)_imageSize, (uint)_imageSize, (byte[])_normalTexels[path == "B" ? 0 : 1].Clone());
            }
            if ((_floatNormals && path == "B") || (_floatDiffuse && path == "A"))
            {
                byte[] floats = new byte[_imageSize * _imageHeight * 16];
                for (int i = 0; i < floats.Length; i += 16)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(i), (i / 16 + 0.125f) / floats.Length);
                    BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(i + 4), 0.625f);
                    BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(i + 8), 0.875f);
                    BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(i + 12), 1);
                }
                return new((uint)_imageSize, (uint)_imageHeight, floats, SilkTextureFormat.Rgba32Float);
            }
            byte[] pixels = new byte[checked(_imageSize * _imageHeight * 4)];
            int channel = path[0] - 'A';
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (_mixedColors && path == "A")
                {
                    pixels[i] = unchecked((byte)(17 + i * 17));
                    pixels[i + 1] = unchecked((byte)(73 + i * 29));
                    pixels[i + 2] = unchecked((byte)(193 - i * 7));
                }
                else if (_mixedColors && path is "B" or "C")
                {
                    pixels[i] = unchecked((byte)(31 + i / 4 * 17));
                    pixels[i + 1] = unchecked((byte)(151 + i / 4 * 29));
                    pixels[i + 2] = unchecked((byte)(193 - i / 4 * 7));
                }
                else if (path == "N")
                {
                    pixels[i] = 128;
                    pixels[i + 1] = 128;
                    pixels[i + 2] = 255;
                }
                else
                {
                    pixels[i + channel] = unchecked((byte)(128 + (_constantTiles ? 0 : i)));
                }
                pixels[i + 3] = 255;
            }
            return new((uint)_imageSize, (uint)_imageHeight, pixels);
        }

        internal SilkMeshRenderResult Render(OpenUsdSilkPage page) =>
            Renderer.ApplyAndRender(page, Color, Depth);

        internal byte[] ColorBytes()
        {
            byte[] bytes = new byte[32 * 32 * 8];
            Color.ReadbackForTesting(bytes);
            return bytes;
        }

        internal float[] DepthValues()
        {
            float[] values = new float[32 * 32];
            Depth.ReadbackForTesting(values);
            return values;
        }

        internal void ReleaseResources()
        {
            Renderer.Dispose();
            Color.Dispose();
            Depth.Dispose();
        }

        internal async Task AssertReleased()
        {
            await Assert.That(Cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(Textures.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(Staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(Buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        }

        public void Dispose()
        {
            ReleaseResources();
            Device.Dispose();
        }
    }

    private static OpenUsdSilkPage Page(
        ulong revision, int count, bool transparent = false, float offset = 0,
        bool normalMap = false, bool omitUvs = false, bool zeroUvs = false, float uvScale = 1,
        bool sharedAssets = false, bool secondExtended = false, bool repeatFirstAsset = false,
        bool thirdExtended = false, float offsetY = 0, float offsetZ = 0, bool visibleMiddle = false,
        float? uvScaleY = null)
    {
        List<byte[]> commands = [SilkMeshRendererConformance.CreateFrameCommand(
            32, 32, SilkMeshRendererConformance.Identity())];
        for (int i = 0; i < count; i++)
        {
            char material = (char)('A' + (transparent ? i % 2 : i));
            if (!transparent || i < 2)
            {
                commands.Add(Material(material, transparent, normalMap,
                    extended: (secondExtended && i == 1) || (thirdExtended && i == 2),
                    assetName: sharedAssets || (repeatFirstAsset && i == 2) ? 'A' : null));
            }
            float left = -0.9f + (i * 0.3f);
            float right = left + 1.2f;
            float z = -0.5f + (i * 0.3f) + offsetZ;
            byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
                (ulong)i + 1, $"/Mesh{i}",
                [left, -0.7f, z, right, -0.7f, z, right, 0.7f, z, left, 0.7f, z],
                [0, 1, 2, 0, 2, 3], visibleMiddle && i == 1 ? 0 : offset, offsetY, null, 1, null, null, null,
                omitUvs ? [] :
                    [("st", (uint)SilkAttributeSemantic.TexCoord, 2, zeroUvs ? new float[8] :
                        [0, 0, uvScale, 0, uvScale, uvScaleY ?? 1, 0, uvScaleY ?? 1])]);
            commands.Add(VolumeCommandAuthoring.WithMaterialBinding(mesh, $"/{material}"));
        }
        return new(SilkCommandParser.PageAbiVersion, revision, commands.SelectMany(static item => item).ToArray(),
            checked((uint)commands.Count));
    }

    private static byte[] Material(
        char name, bool transparent, bool normalMap = false, bool multipleMaps = false, bool extended = false,
        SilkMaterialParameter? scalarParameter = null, SilkCompositeOperator composite = SilkCompositeOperator.None,
        bool udim = false, float uvTranslate = 0, char? assetName = null,
        string pathSuffix = "", string? assetPath = null, bool vectorNormals = false,
        bool promotedNormals = false, SilkTextureWrap wrap = SilkTextureWrap.Repeat, bool normalScalarMaps = false,
        bool flipNormalGreen = false, bool emissiveOnly = false, uint colorComponents = 3)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        string path = $"/{name}{pathSuffix}";
        writer.Write((uint)SilkCommandType.MaterialUpsert);
        writer.Write(0u);
        writer.Write(VolumeCommandAuthoring.ComputeStableHash(path));
        writer.Write((uint)path.Length);
        writer.Write((uint)(promotedNormals ? SilkSurfaceKind.MdlDistilled : SilkSurfaceKind.PreviewSurface));
        writer.Write(promotedNormals ? 3u : 1u);
        writer.Write(normalScalarMaps ? 5u :
            multipleMaps || vectorNormals ? 3u : composite != SilkCompositeOperator.None ? 2u : 1u);
        writer.Write(Encoding.UTF8.GetBytes(path));
        writer.Write((uint)SilkMaterialParameter.Opacity);
        writer.Write(1u);
        writer.Write(transparent ? 0.5f : 1f);
        if (promotedNormals)
        {
            writer.Write((uint)SilkMaterialParameter.DetailUvRow0);
            writer.Write(3u);
            foreach (float value in new[] { 20f, 0, -0.125f })
            {
                writer.Write(value);
            }
            writer.Write((uint)SilkMaterialParameter.DetailUvRow1);
            writer.Write(3u);
            foreach (float value in new[] { 0f, 20, 0.25f })
            {
                writer.Write(value);
            }
        }
        SilkMaterialParameter[] parameters = normalScalarMaps
            ? [SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.Normal, SilkMaterialParameter.DetailNormal,
                SilkMaterialParameter.Roughness, SilkMaterialParameter.Metallic]
            : vectorNormals
            ? [emissiveOnly ? SilkMaterialParameter.EmissiveColor : SilkMaterialParameter.DiffuseColor,
                SilkMaterialParameter.Normal, SilkMaterialParameter.DetailNormal]
            : scalarParameter is { } scalarSlot
            ? composite == SilkCompositeOperator.None ? [scalarSlot] : [scalarSlot, scalarSlot]
            : multipleMaps
            ? [SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.Roughness, SilkMaterialParameter.Metallic]
            : [emissiveOnly ? SilkMaterialParameter.EmissiveColor :
                normalMap ? SilkMaterialParameter.Normal : SilkMaterialParameter.DiffuseColor];
        for (int map = 0; map < parameters.Length; map++)
        {
            byte[] asset = Encoding.UTF8.GetBytes(assetPath ?? (normalScalarMaps && map > 2 ? "A" : udim ? "A<UDIM>" :
                ((char)(normalMap ? 'N' : (assetName ?? name) + map)).ToString()));
            bool scalar = scalarParameter is not null ||
                parameters[map] is SilkMaterialParameter.Roughness or SilkMaterialParameter.Metallic;
            writer.Write((uint)parameters[map]);
            writer.Write((uint)wrap);
            writer.Write((uint)wrap);
            writer.Write((uint)SilkColorSpace.Raw);
            writer.Write((uint)asset.Length);
            writer.Write(2u);
            writer.Write(scalar ? 1u : colorComponents);
            bool promotedNormal = promotedNormals && map is 1 or 2;
            float gain = (extended && (!vectorNormals || map == 0)) || promotedNormal ? 2 : 1;
            float bias = promotedNormal ? -0.5f :
                scalarParameter is null || !extended ? 0 : -0.75f;
            foreach (float value in new float[] { gain, promotedNormal && flipNormalGreen ? -gain : gain,
                promotedNormal ? 1 : gain, 1, bias, promotedNormal && flipNormalGreen ? 1.5f : bias,
                promotedNormal ? 0 : bias, 0, 1, 1, 1, 1 })
            {
                writer.Write(value);
            }
            writer.Write((uint)(scalar ? SilkTextureChannel.R : SilkTextureChannel.Rgb));
            writer.Write((uint)(map == 1 ? composite : SilkCompositeOperator.None));
            writer.Write(map == 1 && composite == SilkCompositeOperator.Mix ? 0.375f : 0f);
            writer.Write(asset);
            writer.Write(Encoding.UTF8.GetBytes("st"));
        }
        writer.Write(0u);
        writer.Write(0u);
        float baseScale = promotedNormals ? 5 : 1;
        foreach (float value in new float[] { baseScale, 0, 0, baseScale, uvTranslate, 0 })
        {
            writer.Write(value);
        }
        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        return bytes;
    }
}
