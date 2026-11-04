// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkVolumeCameraPublicationTests
{
    [Test]
    public async Task RefusedCandidatePreservesPublishedResourcesAndCorrectedRetryReplacesThem()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        var oldAtlas = new TrackingTexture(SilkTextureFormat.R32Float);
        SilkVolumeCameraPass.ResourceSet oldResources = CreateResources(oldAtlas, ownsAtlas: true);
        using (var initial = new SilkVolumeCameraPass.Prepared(pass, oldResources))
        {
            initial.Commit();
        }

        var refusedAtlas = new TrackingTexture(SilkTextureFormat.R32Float);
        var refusedResources = CreateResources(refusedAtlas, ownsAtlas: true);
        var refusedCandidate = new SilkVolumeCameraPass.Prepared(pass, refusedResources);
        refusedCandidate.Dispose();

        await Assert.That(oldAtlas.ReleaseCount).IsEqualTo(0);
        await Assert.That(oldResources.IsDisposed).IsFalse();
        await Assert.That(refusedAtlas.ReleaseCount).IsEqualTo(1);

        var correctedAtlas = new TrackingTexture(SilkTextureFormat.R32Float);
        SilkVolumeCameraPass.ResourceSet correctedResources =
            CreateResources(correctedAtlas, ownsAtlas: true);
        using (var corrected = new SilkVolumeCameraPass.Prepared(pass, correctedResources))
        {
            corrected.Commit();
            await Assert.That(oldAtlas.ReleaseCount).IsEqualTo(0);
        }

        await Assert.That(oldAtlas.ReleaseCount).IsEqualTo(1);
        await Assert.That(oldResources.IsDisposed).IsTrue();
        await Assert.That(correctedAtlas.ReleaseCount).IsEqualTo(0);

        pass.Dispose();

        await Assert.That(correctedAtlas.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task ReusedAtlasOwnershipTransfersToPublishedCandidateExactlyOnce()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        var atlas = new TrackingTexture(SilkTextureFormat.R32Float);
        using (var initial = new SilkVolumeCameraPass.Prepared(
            pass, CreateResources(atlas, ownsAtlas: true)))
        {
            initial.Commit();
        }

        using (var replacement = new SilkVolumeCameraPass.Prepared(
            pass, CreateResources(atlas, ownsAtlas: false)))
        {
            replacement.Commit();
        }

        await Assert.That(atlas.ReleaseCount).IsEqualTo(0);

        pass.Dispose();

        await Assert.That(atlas.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task EmptyPreparationRollsBackOrRetiresThePublishedVolumeOnCommit()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        using var scene = new SilkSceneState();
        var atlas = new TrackingTexture(SilkTextureFormat.R32Float);
        SilkVolumeCameraPass.ResourceSet resources = CreateResources(atlas, ownsAtlas: true);
        using (var initial = new SilkVolumeCameraPass.Prepared(pass, resources))
        {
            initial.Commit();
        }

        using (pass.Prepare(scene, null, 1, 1, false, []))
        {
        }
        await Assert.That(resources.IsDisposed).IsFalse();
        await Assert.That(atlas.ReleaseCount).IsEqualTo(0);

        using (var removal = pass.Prepare(scene, null, 1, 1, false, []))
        {
            removal.Commit();
            await Assert.That(atlas.ReleaseCount).IsEqualTo(0);
        }
        await Assert.That(resources.IsDisposed).IsTrue();
        await Assert.That(atlas.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task EmptyPagePreparationRollsBackOrDefersRetirementUntilDisposal()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        using var scene = new SilkSceneState();
        var atlas = new TrackingTexture(SilkTextureFormat.R32Float);
        SilkVolumeCameraPass.ResourceSet resources = CreateResources(atlas, ownsAtlas: true);
        using (var initial = new SilkVolumeCameraPass.Prepared(pass, resources))
        {
            initial.Commit();
        }

        using (pass.PreparePage(scene, null, 1, 1, false, [], requiresRenderCandidate: false))
        {
        }
        bool aliveAfterRollback = !resources.IsDisposed && atlas.ReleaseCount == 0;
        int releasesAtCommit;
        using (var removal = pass.PreparePage(scene, null, 1, 1, false, [], requiresRenderCandidate: false))
        {
            removal.Commit();
            removal.Commit();
            releasesAtCommit = atlas.ReleaseCount;
        }

        await Assert.That(aliveAfterRollback).IsTrue();
        await Assert.That(releasesAtCommit).IsEqualTo(0);
        await Assert.That(resources.IsDisposed).IsTrue();
        await Assert.That(atlas.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task EmptyPagePreparationRetriesFailedRetirementBeforeAnotherPage()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        using var scene = new SilkSceneState();
        var atlas = new TrackingTexture(SilkTextureFormat.R32Float, failuresBeforeRelease: 1);
        using (var initial = new SilkVolumeCameraPass.Prepared(pass, CreateResources(atlas, ownsAtlas: true)))
        {
            initial.Commit();
        }

        InvalidOperationException? failure = null;
        var removal = pass.PreparePage(scene, null, 1, 1, false, [], requiresRenderCandidate: false);
        removal.Commit();
        try
        {
            removal.Dispose();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        int attemptsAfterFailure = atlas.ReleaseAttempts;
        int releasesAfterFailure = atlas.ReleaseCount;
        using (pass.PreparePage(scene, null, 1, 1, false, [], requiresRenderCandidate: false))
        {
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(attemptsAfterFailure).IsEqualTo(1);
        await Assert.That(releasesAfterFailure).IsEqualTo(0);
        await Assert.That(atlas.ReleaseAttempts).IsEqualTo(2);
        await Assert.That(atlas.ReleaseCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailedRetirementKeepsPublishedResourcesAndRetriesAllFailedReleases()
    {
        using var pass = new SilkVolumeCameraPass(null!, default, null!);
        var oldAtlas = new TrackingTexture(SilkTextureFormat.R32Float, failuresBeforeRelease: 1);
        var oldDepth = new TrackingTexture(SilkTextureFormat.D32Float, failuresBeforeRelease: 1);
        var atlasAdmission = new TrackingReservation();
        using (var initial = new SilkVolumeCameraPass.Prepared(
            pass, CreateResources(oldAtlas, ownsAtlas: true, oldDepth, atlasAdmission)))
        {
            initial.Commit();
        }
        var publishedAtlas = new TrackingTexture(SilkTextureFormat.R32Float);
        using var replacement = new SilkVolumeCameraPass.Prepared(
            pass, CreateResources(publishedAtlas, ownsAtlas: true));
        replacement.Commit();

        AggregateException? cleanup = null;
        try
        {
            replacement.Dispose();
        }
        catch (AggregateException exception)
        {
            cleanup = exception;
        }
        await Assert.That(cleanup).IsNotNull();
        await Assert.That(cleanup!.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(oldDepth.ReleaseAttempts).IsEqualTo(1);
        await Assert.That(oldAtlas.ReleaseAttempts).IsEqualTo(1);
        await Assert.That(atlasAdmission.ReleaseCount).IsEqualTo(0);
        await Assert.That(publishedAtlas.ReleaseCount).IsEqualTo(0);

        using var scene = new SilkSceneState();
        using var empty = pass.Prepare(scene, null, 1, 1, false, []);
        await Assert.That(oldDepth.ReleaseAttempts).IsEqualTo(2);
        await Assert.That(oldAtlas.ReleaseAttempts).IsEqualTo(2);
        await Assert.That(atlasAdmission.ReleaseCount).IsEqualTo(1);
        await Assert.That(publishedAtlas.ReleaseCount).IsEqualTo(0);

        pass.Dispose();
        await Assert.That(publishedAtlas.ReleaseCount).IsEqualTo(1);
    }

    internal static SilkVolumeCameraPass.ResourceSet CreateResources(
        TrackingTexture atlas,
        bool ownsAtlas,
        TrackingTexture? depth = null,
        IDisposable? atlasAdmission = null) =>
        new(
            null!,
            default,
            1,
            1,
            new TrackingBuffer(32, SilkBufferUsage.Uniform | SilkBufferUsage.Upload),
            new TrackingBuffer(128, SilkBufferUsage.Storage | SilkBufferUsage.Upload),
            new TrackingBuffer(72, SilkBufferUsage.Vertex | SilkBufferUsage.Upload),
            new TrackingBuffer(12, SilkBufferUsage.Index | SilkBufferUsage.Upload),
            depth ?? new TrackingTexture(SilkTextureFormat.D32Float),
            atlas,
            ownsAtlas,
            [atlas],
            [],
            1,
            1,
            1,
            null,
            atlasAdmission);

    private sealed class TrackingReservation : IDisposable
    {
        internal int ReleaseCount { get; private set; }

        public void Dispose() => ReleaseCount++;
    }

    private sealed class TrackingBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            _ = ValidateWrite(data.Length, offset);

        public override void ReadbackForTesting(Span<byte> destination) =>
            _ = ValidateReadback(destination.Length);

        protected override void ReleaseNative()
        {
        }
    }

    internal sealed class TrackingTexture(SilkTextureFormat format, int failuresBeforeRelease = 0)
        : SilkGraphicsTextureBase(new SilkTextureDescriptor(
            1,
            1,
            format,
            format == SilkTextureFormat.D32Float
                ? SilkTextureUsage.DepthRenderTarget
                : SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination))
    {
        internal int ReleaseCount { get; private set; }
        internal int ReleaseAttempts { get; private set; }

        internal bool IsDisposed => ReleaseCount != 0;

        public override void ReadbackForTesting(Span<byte> destination) =>
            _ = ValidateReadback(destination.Length);

        public override void ReadbackForTesting(Span<float> destination) =>
            _ = ValidateDepthReadback(destination.Length);

        protected override void ReleaseNative()
        {
            ReleaseAttempts++;
            if (ReleaseAttempts <= failuresBeforeRelease)
            {
                throw new InvalidOperationException("Injected texture release failure.");
            }
            ReleaseCount++;
        }
    }
}
