// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers how a retained scene's textured dome lights are resolved into the
/// prefiltered environment resources the frame binds, and what falls back to the
/// mean-radiance ambient term when they cannot be.
/// </summary>
/// <remarks>
/// <para>
/// The convolutions themselves are gated analytically by
/// <see cref="SilkEnvironmentLightingTests"/>. These cases gate the retention
/// rules around them, which are the ones a rendered scene actually depends on:
/// exactly one contribution per dome, a named fallback for every dome the
/// environment cannot carry, no rebuild while nothing changed, a rebuild when
/// the asset on disk did, and a release on device loss and on disposal.
/// </para>
/// <para>
/// Double counting is the failure most worth pinning. A dome the environment
/// carries must contribute nothing to the frame ambient term, and a dome that
/// fell back must contribute exactly the term it always did -- so the two paths
/// are complementary rather than additive, and a scene can never be lit twice by
/// one light.
/// </para>
/// </remarks>
public sealed class SilkEnvironmentRetentionTests
{
    private const string DomePath = "/World/Lights/Dome";
    private const string SecondDomePath = "/World/Lights/Dome2";
    private const string TexturePath = "/assets/studio.hdr";
    private const string SecondTexturePath = "/assets/sunset.hdr";
    private const string MeshPath = "/World/Geom/Quad";
    private const float UnitDomeAmbient = 0.96f;

    private static readonly uint[] EnvironmentTextureBindings = [33u, 34u, 36u];

    private static readonly uint[] EnvironmentSamplerBindings = [32u, 35u];

    private static readonly string[] BothTextures = [TexturePath, SecondTexturePath];

    [Test]
    public async Task APrefilteredDomeLeavesNothingInTheFrameAmbientTerm()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, float sliceCount, float sliceHeight) =
            SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(1f);
        await Assert.That(sliceCount).IsEqualTo(
            (float)SilkEnvironmentPrefilterOptions.Default.SpecularSliceCount);
        await Assert.That(sliceHeight).IsEqualTo(
            (float)SilkEnvironmentPrefilterOptions.Default.RadianceHeight);
        await Assert.That(sliceCount).IsGreaterThan(1f);

        // The whole point: the dome's emission is now in the two maps, so adding
        // its mean-radiance ambient approximation on top would light the scene
        // twice from one light.
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
        await Assert.That(resources.EnvironmentLitDomes).Contains(DomePath);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
        await Assert.That(resources.Diagnostics.Entries).IsEmpty();
    }

    [Test]
    public async Task ADomeThatIsNeverPreparedKeepsTheMeanRadianceAmbientTerm()
    {
        // A consumer that only wants the frame constants -- a harness with no
        // device, or any caller that does not run the environment step -- must
        // get exactly the result that existed before the directional response
        // did, rather than a scene that lost its dome entirely.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
    }

    [Test]
    public async Task AnUnsupportedMappingFallsBackToTheAmbientTermAndIsNamed()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Upsert(DomePath, TexturePath, format: SilkDomeTextureFormat.Angular),
            1,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported);
    }

    [Test]
    public async Task ADomeBeyondTheComposedBoundFallsBackAndIsNamed()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            options: new SilkEnvironmentPrefilterOptions { MaximumDomeLights = 1 });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        // The first dome by prim path is composed; the second keeps exactly the
        // ambient term it always had, so the scene loses directionality for one
        // light rather than brightness for both.
        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentLightingLimitExceeded);
        await Assert.That(resources.Diagnostics.Entries
            .Single(entry =>
                entry.Code == SilkRenderDiagnosticCodes.EnvironmentLightingLimitExceeded)
            .Message)
            .Contains(SecondDomePath);
    }

    [Test]
    public async Task TwoDomesWithinTheBoundAreBothComposedAndNeitherIsAmbient()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes)
            .IsEquivalentTo([DomePath, SecondDomePath]);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
        await Assert.That(resources.Diagnostics.Entries).IsEmpty();
    }

    [Test]
    public async Task AnUnreadableDomeFallsBackWhileTheReadableOneStaysDirectional()
    {
        // One broken asset must not cost the whole scene its directional
        // response: the remaining domes are still a valid environment.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (asset, _) => asset == SecondTexturePath
                ? throw new FileNotFoundException("missing", asset)
                : Constant(8, 4, 1f));
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentAssetNotFound);
    }

    [Test]
    public async Task ADeviceThatRefusesTheEnvironmentTexturesFallsBackAndIsNamed()
    {
        using var device = new EnvironmentDevice { RefuseTextures = true };
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable);
    }

    [Test]
    public async Task AnEnvironmentOverTheDecodeBudgetFallsBackToTheAmbientTerm()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) => Constant(64, 32, 1f),
            decodeByteBudget: 1024);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded);
    }

    [Test]
    public async Task APreparedEnvironmentIsNotRebuiltWhileNothingChanges()
    {
        using var device = new EnvironmentDevice();
        int decodes = 0;
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        resources.PrepareEnvironmentLighting(scene);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
        await Assert.That(decodes).IsEqualTo(1);
        // Three: the irradiance map, the specular atlas and the BRDF table, all
        // allocated under one guard so an enablement cannot be half-resourced.
        await Assert.That(device.CreatedTextureCount).IsEqualTo(3);
    }

    [Test]
    public async Task RepublishingAnIdenticalDomeReusesThePrefilteredEnvironment()
    {
        // The environment revision moves whenever a record is republished, but
        // the identity does not, so the second revision must cost no decode and
        // no prefilter at all.
        using var device = new EnvironmentDevice();
        int decodes = 0;
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();
        byte[] page = Upsert(DomePath, TexturePath);
        _ = scene.Apply(page, 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        _ = scene.Apply(page, 1, 2);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
        await Assert.That(decodes).IsEqualTo(1);
    }

    [Test]
    public async Task RewritingTheTextureFileRebuildsThePrefilteredEnvironment()
    {
        // The dome's path and controls are unchanged, so only the file stamp can
        // tell the two skies apart. A cache keyed on the path alone would keep
        // serving the first one after the artist re-exported the HDR.
        using var device = new EnvironmentDevice();
        long ticks = 100;
        int decodes = 0;
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            },
            stampReader: _ => new SilkEnvironmentAssetStamp(2048, ticks));
        var scene = new SilkSceneState();
        byte[] page = Upsert(DomePath, TexturePath);
        _ = scene.Apply(page, 1, 1);
        resources.PrepareEnvironmentLighting(scene);

        ticks = 200;
        _ = scene.Apply(page, 1, 2);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(2);
        await Assert.That(decodes).IsEqualTo(2);
        // Two more maps for the rebuilt environment. The BRDF table is scene
        // independent, so it is not one of them.
        await Assert.That(device.CreatedTextureCount).IsEqualTo(5);
    }

    [Test]
    public async Task RebindingTheDomeToAnotherAssetRebuildsAndBackAgainReuses()
    {
        using var device = new EnvironmentDevice();
        List<string> decoded = [];
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (asset, _) =>
            {
                decoded.Add(asset);
                return Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();

        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        _ = scene.Apply(Upsert(DomePath, SecondTexturePath), 1, 2);
        resources.PrepareEnvironmentLighting(scene);
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 3);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(decoded).IsEquivalentTo(BothTextures);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(2);
    }

    [Test]
    public async Task RemovingTheDomeReleasesTheEnvironmentAndReportsItDisabled()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();

        _ = scene.Apply(Remove(DomePath), 1, 2);
        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsFalse();
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(device.DisposedTextureCount).IsEqualTo(2);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
    }

    [Test]
    public async Task ADeviceGenerationChangeReleasesAndRebuildsTheEnvironment()
    {
        // A device loss invalidates every retained texture. Rebinding the dead
        // ones would fault; keeping the binding enabled while they are gone would
        // sample nothing.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        // The irradiance map, the specular atlas and the BRDF table.
        await Assert.That(device.CreatedTextureCount).IsEqualTo(3);

        device.SelectionOutlineDeviceGeneration = 1;
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(device.DisposedTextureCount).IsEqualTo(3);
        await Assert.That(device.CreatedTextureCount).IsEqualTo(6);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();

        // The prefiltered payload itself is device independent, so a lost device
        // costs two allocations and not a second convolution.
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
    }

    [Test]
    public async Task AuthoringADomeCollectionRebuildsTheEnvironmentIntoGroups()
    {
        // The grouped atlas is a different payload with a different shape, so it
        // has to be built on the frame the dome collection appears and released
        // on the frame it is retired. Leaving the composed bake in place would
        // give every prim every sky whatever its collection said; leaving the
        // grouped bake in place after retirement would keep a scene that links
        // nothing on the layout whose pixels are not byte-identical.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. DomeFrame(2),
                .. Upsert(DomePath, TexturePath, domeIndex: 0),
                .. Upsert(SecondDomePath, SecondTexturePath, domeIndex: 1),
            ],
            3,
            1);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(1u);
        await Assert.That(resources.EnvironmentBinding.ComposedGroup).IsEqualTo(0u);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);

        _ = scene.Apply(LightLink(domeCount: 2, (MeshPath, 0b01u)), 1, 2);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(scene.LightLinks.HasDomeLinks).IsTrue();
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(3u);
        await Assert.That(resources.EnvironmentBinding.ComposedGroup).IsEqualTo(2u);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(0)).IsEqualTo(0);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(1)).IsEqualTo(1);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(2);

        // Retiring the collection returns to the composed layout, and the earlier
        // composed payload is still cached under its own identity, so the return
        // costs allocations rather than a third convolution.
        _ = scene.Apply(LightLink(domeCount: 0), 1, 3);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(scene.LightLinks.HasDomeLinks).IsFalse();
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(1u);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(2);
    }

    [Test]
    public async Task ADomeWithoutAPrefilteredResponseResolvesToNoGroup()
    {
        // A dome the prefilter refused still holds a dome bit, and its bit must
        // resolve to "no group" rather than to whichever composed dome inherited
        // its index -- which is what would happen if the table were keyed by
        // position in the composed set.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (asset, _) => asset == SecondTexturePath
                ? throw new InvalidDataException("unreadable")
                : Constant(8, 4, 0.5f));
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. DomeFrame(2),
                .. Upsert(DomePath, TexturePath, domeIndex: 0),
                .. Upsert(SecondDomePath, SecondTexturePath, domeIndex: 1),
                .. LightLink(domeCount: 2, (MeshPath, 0b01u)),
            ],
            4,
            1);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(2u);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(0)).IsEqualTo(0);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(1))
            .IsEqualTo(SilkDomeGroupTable.NoGroup);
    }

    [Test]
    public async Task GroupsThatDoNotFitTheByteBudgetKeepTheComposedSkyAndAreNamed()
    {
        // The deepest exact subset rather than a refusal: the scene keeps its
        // directional sky and loses only the per-dome selection of it, which is
        // named. Falling all the way back to the mean-radiance term would have
        // cost the scene its sky as well as its linking.
        using var device = new EnvironmentDevice();
        var options = new SilkEnvironmentPrefilterOptions();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            options: options with
            {
                MaximumPrefilteredBytes = options.GetPrefilteredByteSize(1),
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. DomeFrame(1),
                .. Upsert(DomePath, TexturePath, domeIndex: 0),
                .. LightLink(domeCount: 1, (MeshPath, 0u)),
            ],
            3,
            1);
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(1u);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(0))
            .IsEqualTo(SilkDomeGroupTable.NoGroup);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDomeLinkUnavailable);
    }

    [Test]
    public async Task ADeviceGenerationChangeRebuildsAGroupedEnvironment()
    {
        // The grouped atlas is taller than the composed one, so a device loss has
        // to recreate it at the grouped shape rather than at the shape the
        // pre-linking scene allocated.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. DomeFrame(1),
                .. Upsert(DomePath, TexturePath, domeIndex: 0),
                .. LightLink(domeCount: 1, (MeshPath, 0u)),
            ],
            3,
            1);
        resources.PrepareEnvironmentLighting(scene);
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(2u);

        device.SelectionOutlineDeviceGeneration = 1;
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
        await Assert.That(resources.EnvironmentBinding.GroupCount).IsEqualTo(2u);
        await Assert.That(resources.EnvironmentBinding.DomeGroups.GetGroup(0)).IsEqualTo(0);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
    }

    [Test]
    public async Task AnAbandonedUploadIsRecordedAgainRatherThanCountedAsDone()
    {
        // Recording a copy is not performing one. A command list that is dropped,
        // or a submission that fails, leaves the target textures holding whatever
        // they held before -- so the marks are abandoned and the next attempt
        // records the copies again instead of binding memory nothing ever wrote.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);

        using (var abandoned = new EnvironmentCommandList())
        {
            resources.UploadEnvironment(abandoned);
            await Assert.That(abandoned.Uploads.Count).IsEqualTo(3);
        }

        // The submission failed, so nothing that list recorded happened.
        resources.AbandonPendingUploads();
        await Assert.That(resources.EnvironmentUploadBytes).IsEqualTo(0UL);

        using var retried = new EnvironmentCommandList();
        resources.UploadEnvironment(retried);
        await Assert.That(retried.Uploads.Count)
            .IsEqualTo(3)
            .Because(
                "The retry must re-record both maps and the BRDF table, because " +
                "the abandoned list's copies never executed.");

        resources.CommitPendingUploads();
        await Assert.That(resources.EnvironmentUploadBytes).IsGreaterThan(0UL);

        // And once committed it is idempotent again: a third list records nothing.
        using var settled = new EnvironmentCommandList();
        resources.UploadEnvironment(settled);
        await Assert.That(settled.Uploads.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AFailedSubmissionLeavesTheUploadPendingForTheNextFrame()
    {
        // The renderer's own path: the frame records the copies, the submission
        // fails, and the frame after it has to record them again. Driving it
        // through SilkMeshRenderer is what proves the abandon reaches the retry
        // rather than only existing as a method nothing calls.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);

        using (var failing = new EnvironmentCommandList())
        {
            resources.UploadEnvironment(failing);
        }

        // No commit: the submission threw. The bytes stay uncounted and the maps
        // stay un-uploaded, which is the state a retry has to see.
        resources.AbandonPendingUploads();

        using var second = new EnvironmentCommandList();
        resources.UploadEnvironment(second);
        resources.CommitPendingUploads();

        await Assert.That(second.Uploads.Count).IsEqualTo(3);
        await Assert.That(resources.EnvironmentUploadBytes).IsGreaterThan(0UL);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
    }

    [Test]
    public async Task ARetiredLinkTableEvictsItsPerMaskSurfaceBuffersAndDiagnostics()
    {
        // A live-edited collection walks through many masks, and the surface
        // block cache is keyed by one. Nothing else ever drops those blocks --
        // the material never changed -- so without eviction the retained set
        // grows with every edit, and the diagnostics the old table produced keep
        // warning about a table that no longer exists.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(LinkedMesh(), 1, 1);
        SilkMeshData mesh = scene.Meshes.Values.Single();

        int distinctMasks = 0;
        for (uint mask = 0; mask < 6; mask++)
        {
            _ = scene.Apply(
                [
                    .. DomeFrame(3, textured: 0),
                    .. LightLink(domeCount: 3, (MeshPath, mask)),
                ],
                2,
                mask + 2);
            _ = resources.RequireSurfaceBuffer(scene, mesh, RenderHeadlight.Deterministic);
            distinctMasks++;
        }

        // One block for the mask the current table resolves, plus at most the
        // shared default block: the five earlier masks are gone.
        await Assert.That(distinctMasks).IsEqualTo(6);
        await Assert.That(resources.SurfaceBufferCount)
            .IsLessThanOrEqualTo(2)
            .Because(
                "Only the masks the current table can still return may survive " +
                "a live edit that walked a collection through six shapes.");

        // A truncated table warns, and repairing it clears the warning rather
        // than leaving a stale one behind forever.
        _ = scene.Apply(TruncatedLightLink(3), 1, 100);
        _ = resources.RequireSurfaceBuffer(scene, mesh, RenderHeadlight.Deterministic);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.LightLinkTruncated);

        _ = scene.Apply(LightLink(domeCount: 0), 1, 101);
        _ = resources.RequireSurfaceBuffer(scene, mesh, RenderHeadlight.Deterministic);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.LightLinkTruncated);
    }

    [Test]
    public async Task AnEmptiedSceneStillRetiresWhatItsLastLinkTableLeftBehind()
    {
        // The revision used to be observed only from the draw loop, and a scene
        // with nothing drawable never reaches it: a stage whose prims were all
        // removed kept every per-mask surface block its last table produced and
        // kept warning about a table it no longer retained. The observation is
        // therefore made once per page and once per frame instead.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(LinkedMesh(), 1, 1);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        _ = scene.Apply(
            [.. DomeFrame(3, textured: 0), .. TruncatedLightLink(3)],
            2,
            2);
        _ = resources.RequireSurfaceBuffer(scene, mesh, RenderHeadlight.Deterministic);

        await Assert.That(resources.SurfaceBufferCount).IsGreaterThan(0);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.LightLinkTruncated);

        // Remove the only prim and retire the table in one page. Nothing is
        // drawable afterwards, so nothing but the page-level observation can
        // notice that the table changed.
        SilkSceneDelta delta = scene.Apply(
            [.. MeshRemoval(), .. LightLink(domeCount: 0)],
            2,
            3);
        resources.Apply(scene, delta);

        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(resources.SurfaceBufferCount)
            .IsEqualTo(0)
            .Because(
                "Every retained block was keyed on a mask the retired table can " +
                "no longer return, and no draw will ever evict them.");
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.LightLinkTruncated)
            .Because(
                "A table that no longer exists must stop warning, whether or not " +
                "the scene still has something to draw.");
    }

    /// <summary>Retires the single retained prim at <c>MeshPath</c>.</summary>
    private static byte[] MeshRemoval()
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(MeshPath);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 24);
        return bytes;
    }

    /// <summary>Builds one retained triangle at <c>MeshPath</c> with no material.</summary>
    private static byte[] LinkedMesh()
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(MeshPath);
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] indices = [0, 1, 2];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1f);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        pathBytes.CopyTo(bytes, 268);
        int offset = 268 + pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), index);
            offset += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        return bytes;
    }

    /// <summary>Builds a link table that reports itself truncated.</summary>
    private static byte[] TruncatedLightLink(uint domeCount)
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(MeshPath);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.Truncated),
            .. BitConverter.GetBytes(domeCount),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(SilkLightLinkCommand.AllInstances),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. pathBytes,
        ];
        List<byte> command =
        [
            .. BitConverter.GetBytes((uint)SilkCommandType.LightLink),
            .. BitConverter.GetBytes((uint)(payload.Count + 8)),
            .. payload,
        ];
        return [.. command];
    }

    /// <summary>
    /// Builds a frame publishing <paramref name="domeCount"/> domes, so a dome
    /// index and a dome mask have an ordering to name. The leading
    /// <paramref name="textured"/> of them are marked as carrying an image, which
    /// requires an environment record to supply it; the rest are untextured
    /// domes, whose whole contribution is an ambient colour.
    /// </summary>
    private static byte[] DomeFrame(int domeCount, int textured = int.MaxValue)
    {
        const int frameSize = 2248;
        const int domeCountOffset = 1976;
        const int domeTableOffset = 1992;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)frameSize);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(domeCountOffset),
            (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            // OPENUSD_SILK_DOME_FLAG_PRESENT, plus OPENUSD_SILK_DOME_FLAG_TEXTURED
            // for the domes an environment record supplies an image for.
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(domeTableOffset + (dome * 32) + 16),
                dome < textured ? 3u : 1u);
        }
        return bytes;
    }

    /// <summary>
    /// Builds a link table whose only non-default masks are the dome ones.
    /// </summary>
    private static byte[] LightLink(
        uint domeCount,
        params (string Path, uint DomeMask)[] entries)
    {
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, uint domeMask) in entries)
        {
            byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(domeMask));
            payload.AddRange(BitConverter.GetBytes(SilkLightLinkCommand.AllInstances));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }
        List<byte> command =
        [
            .. BitConverter.GetBytes((uint)SilkCommandType.LightLink),
            .. BitConverter.GetBytes((uint)(payload.Count + 8)),
            .. payload,
        ];
        return [.. command];
    }

    [Test]
    public async Task UploadingAndBindingPopulatesBothSlotsAndCopiesOnce()
    {
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);

        using var commands = new EnvironmentCommandList();
        // The upload is separate from the binding because a copy cannot be
        // recorded inside a rendering scope, and it must be idempotent because it
        // runs on every frame while the maps are rebuilt only when they change.
        resources.UploadEnvironment(commands);
        resources.UploadEnvironment(commands);
        resources.BindEnvironment(commands);
        resources.BindEnvironment(commands);

        await Assert.That(commands.Uploads.Count)
            .IsEqualTo(3)
            .Because(
                "The two maps and the split-sum BRDF table, each copied once and " +
                "not again on the second call.");

        // The bytes are counted when the upload is *committed*, not when it is
        // recorded: a copy that was recorded into a submission that never
        // completed did not happen, and must not be reported as if it had.
        await Assert.That(resources.EnvironmentUploadBytes).IsEqualTo(0UL);
        resources.CommitPendingUploads();
        await Assert.That(resources.EnvironmentUploadBytes).IsGreaterThan(0UL);
        await Assert.That(commands.Textures.Keys).IsEquivalentTo(EnvironmentTextureBindings);
        await Assert.That(commands.Samplers.Keys).IsEquivalentTo(EnvironmentSamplerBindings);

        // A latlong map has to wrap in longitude and clamp in latitude, or the
        // reflection carries a seam down its back and the poles fold together.
        SilkSamplerDescriptor sampler = commands.Samplers[32u];
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
        await Assert.That(sampler.MinFilter).IsEqualTo(SilkSamplerFilter.Linear);
    }

    [Test]
    public async Task AFrameWithNoEnvironmentStillPopulatesBothTextureSlots()
    {
        // The checked mesh fragment references both slots in every permutation,
        // so a backend pipeline layout requires both to be populated even when
        // the shader never reads them.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        using var commands = new EnvironmentCommandList();

        resources.BindEnvironment(commands);

        await Assert.That(commands.Textures.Keys).IsEquivalentTo(EnvironmentTextureBindings);
        await Assert.That(commands.Uploads).IsEmpty();
        await Assert.That(ReferenceEquals(commands.Textures[33u], commands.Textures[34u]))
            .IsTrue()
            .Because("A frame with no environment binds one stand-in for both slots.");
    }

    [Test]
    public async Task DisposingReleasesEveryEnvironmentResource()
    {
        using var device = new EnvironmentDevice();
        SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        using (var commands = new EnvironmentCommandList())
        {
            resources.UploadEnvironment(commands);
            resources.BindEnvironment(commands);
        }

        resources.Dispose();

        await Assert.That(device.DisposedTextureCount).IsEqualTo(device.CreatedTextureCount);
        await Assert.That(device.DisposedSamplerCount).IsEqualTo(device.CreatedSamplerCount);
        await Assert.That(() => resources.PrepareEnvironmentLighting(scene))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task AnAutomaticMappingIsAcceptedOnlyWhenTheImageIsEquirectangular()
    {
        // texture:format = automatic says "derive the mapping from the image".
        // The one thing an image observably carries is its aspect: an
        // equirectangular map covers 360 degrees by 180 and is therefore exactly
        // twice as wide as it is tall. A square automatic image is far more
        // likely to be a mirrored ball or an angular map, so it is refused rather
        // than integrated as if it were equirectangular.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources equirectangular = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 1f),
            describer: _ => new SilkImageDescription(8, 4, SilkTextureFormat.Rgba32Float));
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath, SilkDomeTextureFormat.Automatic), 1, 1);
        equirectangular.PrepareEnvironmentLighting(scene);

        await Assert.That(equirectangular.EnvironmentLitDomes).IsEquivalentTo([DomePath]);

        using SilkSceneGpuResources square = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 8, 1f),
            describer: _ => new SilkImageDescription(8, 8, SilkTextureFormat.Rgba32Float));
        var squareScene = new SilkSceneState();
        _ = squareScene.Apply(
            Upsert(DomePath, TexturePath, SilkDomeTextureFormat.Automatic),
            1,
            1);
        square.PrepareEnvironmentLighting(squareScene);
        ISilkGraphicsBuffer frame = square.RequireFrameBuffer(
            squareScene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(square.EnvironmentLitDomes).IsEmpty();
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        RenderDiagnostic mapping = square.Diagnostics.Entries.Single(entry =>
            entry.Code == SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported);
        await Assert.That(mapping.Message).Contains("8x8");
    }

    [Test]
    public async Task EveryNamedNonEquirectangularMappingIsRefusedByItsOwnName()
    {
        // Each of these parameterizes the sphere differently, so integrating one
        // as equirectangular weights the wrong parts of the image. The diagnostic
        // names the authored format rather than reporting a generic refusal.
        foreach (SilkDomeTextureFormat format in new[]
        {
            SilkDomeTextureFormat.MirroredBall,
            SilkDomeTextureFormat.Angular,
            SilkDomeTextureFormat.CubeMapVerticalCross,
        })
        {
            using var device = new EnvironmentDevice();
            using SilkSceneGpuResources resources = CreateResources(
                device,
                describer: _ => new SilkImageDescription(8, 4, SilkTextureFormat.Rgba32Float));
            var scene = new SilkSceneState();
            _ = scene.Apply(Upsert(DomePath, TexturePath, format), 1, 1);

            resources.PrepareEnvironmentLighting(scene);
            _ = resources.RequireFrameBuffer(
                scene,
                RenderOutputTransform.Identity,
                exposure: 1f);

            await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
            RenderDiagnostic mapping = resources.Diagnostics.Entries.Single(entry =>
                entry.Code == SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported);
            await Assert.That(mapping.Message).Contains(format.ToString());
        }
    }

    [Test]
    public async Task TheObservedColourSpaceDecidesAnAutoDeclarationRatherThanTheFormat()
    {
        // hdSilk publishes Auto because Hydra's light parameters do not expose a
        // dome texture's authored colour space at all. The image library's own
        // effective space is the observation that does exist, and it must win
        // over the inference the decoded format alone would support -- here a
        // float image the library reports as sRGB-encoded.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources raw = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 0.5f),
            describer: _ => new SilkImageDescription(
                8,
                4,
                SilkTextureFormat.Rgba32Float,
                ChannelCount: 3,
                Observed: SilkImageObservation.Queried | SilkImageObservation.ColorSpace,
                ColorSpace: SilkImageColorSpaceObservation.Raw));
        using SilkSceneGpuResources srgb = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 0.5f),
            describer: _ => new SilkImageDescription(
                8,
                4,
                SilkTextureFormat.Rgba32Float,
                ChannelCount: 3,
                Observed: SilkImageObservation.Queried | SilkImageObservation.ColorSpace,
                ColorSpace: SilkImageColorSpaceObservation.Srgb));

        SilkEnvironmentMaps? rawMaps = Prefilter(raw);
        SilkEnvironmentMaps? srgbMaps = Prefilter(srgb);

        await Assert.That(rawMaps).IsNotNull();
        await Assert.That(srgbMaps).IsNotNull();

        // 0.5 linearized through the sRGB transfer function is about 0.214, so
        // the observed-sRGB environment must be materially dimmer.
        float rawIrradiance = rawMaps!.SampleIrradiance(Vector3.UnitY).X;
        float srgbIrradiance = srgbMaps!.SampleIrradiance(Vector3.UnitY).X;
        await Assert.That(srgbIrradiance).IsLessThan(rawIrradiance * 0.5f);
        await Assert.That(srgbIrradiance).IsGreaterThan(rawIrradiance * 0.3f);
    }

    [Test]
    public async Task AControlThatInvalidatesTheExactSemanticsForcesTheFallback()
    {
        // enableColorTemperature scales the authored colour by a tint hdSilk did
        // not carry, and a non-scene poleAxis re-parameterizes the sphere. Each
        // invalidates something the prefiltered environment claims to resolve
        // exactly -- the emission and the orientation -- so a dome carrying
        // either falls back to the term that claims neither.
        foreach (SilkEnvironmentUnsupportedFeatures feature in new[]
        {
            SilkEnvironmentUnsupportedFeatures.ColorTemperature,
            SilkEnvironmentUnsupportedFeatures.PoleAxis,
        })
        {
            using var device = new EnvironmentDevice();
            using SilkSceneGpuResources resources = CreateResources(device);
            var scene = new SilkSceneState();
            _ = scene.Apply(Upsert(DomePath, TexturePath, unsupported: feature), 1, 1);

            resources.PrepareEnvironmentLighting(scene);
            ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
                scene,
                RenderOutputTransform.Identity,
                exposure: 1f);

            await Assert.That(resources.EnvironmentLitDomes)
                .IsEmpty()
                .Because($"{feature} must force the mean-radiance fallback.");
            await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
                .IsEqualTo(UnitDomeAmbient)
                .Within(1e-5f);
            RenderDiagnostic named = resources.Diagnostics.Entries.Single(entry =>
                entry.Code == SilkRenderDiagnosticCodes.EnvironmentFeatureUnsupported);
            await Assert.That(named.Message).Contains(feature.ToString());
        }
    }

    [Test]
    public async Task AnUnappliedControlIsNamedEvenWhenTheDomeIsPrefiltered()
    {
        // A link collection is equally inapplicable to both paths -- a dome is one
        // scene-wide term either way -- so it does not force a fallback. It still
        // has to be reported: a diagnostic that only appeared on the failure path
        // would let a scene that succeeded look clean while silently dropping an
        // authored collection.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Upsert(
                DomePath,
                TexturePath,
                unsupported: SilkEnvironmentUnsupportedFeatures.LinkCollection),
            1,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentFeatureUnsupported);
    }

    [Test]
    public async Task AMalformedDomeIsIsolatedFromTheValidOnesBesideIt()
    {
        // A non-finite texel is discovered while the source is validated, which
        // happens with the candidate index still in hand. Discovering it halfway
        // through the accumulation instead could only fail the whole composed
        // environment, which would let one broken dome take the directional
        // response away from every valid one.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (asset, _) => asset == SecondTexturePath
                ? NonFinite(8, 4)
                : Constant(8, 4, 1f));
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);
    }

    [Test]
    public async Task AnAssetRewrittenInPlaceIsObservedWithoutAnySceneCommand()
    {
        // Nothing republishes the dome: its path, its controls and the scene
        // revision are all unchanged. Only the file moved, and only the stamp can
        // see that, so the stamps are re-read on every resolve rather than only
        // when a command arrives.
        using var device = new EnvironmentDevice();
        long ticks = 100;
        int decodes = 0;
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            },
            stampReader: _ => new SilkEnvironmentAssetStamp(2048, ticks));
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        resources.PrepareEnvironmentLighting(scene);
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);

        ticks = 200;
        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(2);
        await Assert.That(decodes).IsEqualTo(2);
        // Two more maps for the rebuilt environment. The BRDF table is scene
        // independent, so it is not one of them.
        await Assert.That(device.CreatedTextureCount).IsEqualTo(5);
    }

    [Test]
    public async Task AnOversizedImageIsRefusedFromItsDescribedShapeBeforeAnyDecode()
    {
        // The describer reports the shape from the file's header, so the byte
        // count is known before an allocator is asked for anything. A budget that
        // could only be checked against the decoded buffer would already have
        // spent the memory it exists to refuse.
        using var device = new EnvironmentDevice();
        int decodes = 0;
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            },
            describer: _ => new SilkImageDescription(
                4096,
                2048,
                SilkTextureFormat.Rgba32Float),
            decodeByteBudget: 1024);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(decodes)
            .IsEqualTo(0)
            .Because("An image over the budget must never be decoded at all.");
    }

    [Test]
    public async Task TheAggregateSourceBudgetRefusesTheSetRatherThanOneImage()
    {
        // Each image is inside the per-image ceiling; together they are not. Only
        // one is ever resident, so this bounds the transient work a composed
        // environment performs rather than a peak footprint -- and it has to be
        // enforced, or four domes at the per-image ceiling would be a gigabyte of
        // decoding for one frame.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 1f),
            describer: _ => new SilkImageDescription(8, 4, SilkTextureFormat.Rgba32Float),
            options: new SilkEnvironmentPrefilterOptions
            {
                MaximumSourceBytes = 1024,
                MaximumAggregateSourceBytes = 768,
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        // One image fits, two do not, so the second is dropped and the first is
        // still composed: the bound costs the scene one dome's directionality
        // rather than all of it.
        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
    }

    [Test]
    public async Task ADeviceLossRecreatesEveryEnvironmentOwnedObject()
    {
        // The two maps are the obvious half. The samplers, the one-texel stand-in
        // and the BRDF table are the half that is easy to miss: they are created
        // once and reused across every scene edit, so a release that only dropped
        // the maps would rebind objects belonging to a device that no longer
        // exists.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        using (var first = new EnvironmentCommandList())
        {
            resources.UploadEnvironment(first);
            resources.BindEnvironment(first);
        }

        int createdBefore = device.CreatedTextureCount;
        int samplersBefore = device.CreatedSamplerCount;
        await Assert.That(createdBefore).IsEqualTo(3);
        await Assert.That(samplersBefore).IsEqualTo(2);

        device.DeviceLossGeneration = 1;
        resources.PrepareEnvironmentLighting(scene);
        using (var second = new EnvironmentCommandList())
        {
            resources.UploadEnvironment(second);
            resources.BindEnvironment(second);
        }

        await Assert.That(device.DisposedTextureCount).IsEqualTo(createdBefore);
        await Assert.That(device.DisposedSamplerCount).IsEqualTo(samplersBefore);
        await Assert.That(device.CreatedTextureCount).IsEqualTo(createdBefore * 2);
        await Assert.That(device.CreatedSamplerCount).IsEqualTo(samplersBefore * 2);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();

        // The payload is device independent, so a lost device costs allocations
        // and not a second convolution.
        await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
    }

    [Test]
    public async Task AGeneratedMaterialXSurfaceIsWrittenAsUnlit()
    {
        // MaterialXGenerated is exactly and only ND_surface_unlit in this ABI, so
        // the checked permutation that stands in for the generated fragment while
        // it compiles -- or after it fails -- has to shade unlit. The third state
        // of the mode is what carries that, and it is written here rather than
        // inferred in the shader from an absence.
        byte[] unlit = WriteSurface(SilkSurfaceKind.MaterialXGenerated);
        byte[] shaded = WriteSurface(SilkSurfaceKind.PreviewSurface);
        byte[] unbound = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(null, default, unbound);

        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(unlit.AsSpan(72, 4)))
            .IsEqualTo(2f)
            .Because("A generated MaterialX surface is unlit by authored intent.");
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(shaded.AsSpan(72, 4)))
            .IsEqualTo(1f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(unbound.AsSpan(72, 4)))
            .IsEqualTo(0f);
    }

    private static byte[] WriteSurface(SilkSurfaceKind kind)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(MaterialUpsert(kind), 1, 1);
        byte[] block = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(scene.Materials[MaterialPath], default, block);
        return block;
    }

    private static byte[] MaterialUpsert(SilkSurfaceKind kind)
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(MaterialPath);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(SilkEnvironmentLightingTests.ComputeStableHash(MaterialPath)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)kind),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];
        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static SilkEnvironmentMaps? Prefilter(SilkSceneGpuResources resources)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);
        resources.PrepareEnvironmentLighting(scene);
        return resources.EnvironmentPayloadForTesting;
    }

    private const string MaterialPath = "/World/Materials/Generated";

    private static SilkDecodedImage NonFinite(uint width, uint height)
    {
        float[] values = new float[width * height * 4];
        for (int index = 0; index < values.Length; index += 4)
        {
            values[index] = index == 0 ? float.NaN : 1f;
            values[index + 1] = 1f;
            values[index + 2] = 1f;
            values[index + 3] = 1f;
        }
        return new SilkDecodedImage(
            width,
            height,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
            SilkTextureFormat.Rgba32Float);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EachEnvironmentAllocationFailureLeavesNoPartialEnvironment(
        int failingTexture)
    {
        // The environment declares five GPU objects and the shader reads all of
        // them. Allocating them in three places -- the maps on prepare, the table
        // on upload, the samplers on bind -- let a device refuse the table after
        // the frame constants had already declared the environment enabled, and
        // the shader then read a one-texel stand-in as its split-sum table. Each
        // allocation is failed separately here because a guard that only rolls
        // back the first one looks correct until the third one happens.
        using var device = new EnvironmentDevice { FailTextureOrdinal = failingTexture };
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentBinding.Enabled).IsFalse();
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);

        // Every object the failed transaction did create is disposed. The one that
        // threw was never constructed, so it is counted but not disposed.
        await Assert.That(device.DisposedTextureCount).IsEqualTo(failingTexture - 1);

        // And the dome keeps the ambient it would have had, named as unavailable.
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AnEnvironmentSamplerFailureLeavesNoPartialEnvironment(
        int failingSampler)
    {
        // The same transaction, from the other side. A sampler was previously
        // created on first bind, which is inside a render rather than inside a
        // prepare a caller can fall back from.
        using var device = new EnvironmentDevice { FailSamplerOrdinal = failingSampler };
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentBinding.Enabled).IsFalse();
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();

        // The three textures the transaction created before the sampler threw are
        // released rather than leaked into a disabled environment.
        await Assert.That(device.DisposedTextureCount).IsEqualTo(3);
        await Assert.That(device.DisposedSamplerCount).IsEqualTo(failingSampler - 1);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
    }

    [Test]
    public async Task AnUnsupportedDomeStillCountsAsAuthoredSceneLighting()
    {
        // A dome authored black, or specular-only, or refused by the prefilter,
        // contributes nothing measurable: the ambient term is zero and the
        // environment is disabled. Keying the headlight on either of those would
        // switch a camera light on for a stage that is lit exactly as its author
        // asked, and replace the author's dome with one nobody placed.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                DomePath,
                TexturePath,
                SilkDomeTextureFormat.MirroredBall,
                intensity: 0f),
            1,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        (float enabled, _, _) = SilkEnvironmentLightingTests.ReadEnvironmentControls(frame);
        await Assert.That(enabled).IsEqualTo(0f);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
        await Assert.That(resources.EnvironmentBinding.AuthoredSceneLighting).IsTrue();
        await Assert.That(
            SilkEnvironmentLightingTests.ReadAuthoredSceneLighting(frame)).IsEqualTo(1f);

        // Retiring the dome retires the claim with it.
        _ = scene.Apply(Remove(DomePath), 1, 2);
        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer after = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);
        await Assert.That(resources.EnvironmentBinding.AuthoredSceneLighting).IsFalse();
        await Assert.That(
            SilkEnvironmentLightingTests.ReadAuthoredSceneLighting(after)).IsEqualTo(0f);
    }

    [Test]
    public async Task AFallbackDomeRewrittenInPlaceMovesTheAmbientWithoutACommand()
    {
        // The prefiltered path already re-read its assets every resolve. The
        // fallback did not: its ambient was a function of the scene revision
        // alone, so a dome whose file was repaired or re-exported under a running
        // session kept lighting from the bytes that were no longer there until
        // some unrelated command happened to move the revision.
        var stamp = new SilkEnvironmentAssetStamp(1, 1);
        float radiance = 1f;
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, radiance),
            stampReader: _ => stamp);
        var scene = new SilkSceneState();

        // An authored colour temperature invalidates the semantics of the image
        // the prefilter would produce, so this dome is forced onto the fallback --
        // and unlike an unsupported *mapping*, the fallback still reads the file,
        // which is what makes the rewrite observable at all.
        _ = scene.Apply(
            Upsert(
                DomePath,
                TexturePath,
                unsupported: SilkEnvironmentUnsupportedFeatures.ColorTemperature),
            1,
            1);
        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);
        byte[] before = SilkEnvironmentLightingTests.ReadFrameBytes(frame);
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);

        // The file is rewritten in place. No command arrives, and the scene
        // revision does not move.
        radiance = 0.5f;
        stamp = new SilkEnvironmentAssetStamp(1, 2);
        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer rewritten = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(rewritten).X)
            .IsEqualTo(UnitDomeAmbient * 0.5f)
            .Within(1e-5f);
        await Assert.That(SilkEnvironmentLightingTests.ReadFrameBytes(rewritten))
            .IsNotEquivalentTo(before);
    }

    [Test]
    public async Task AnOversizedFallbackDomeIsRefusedBeforeItIsDecoded()
    {
        // The fallback is where a refused dome lands, so decoding half a gigabyte
        // there -- after the prefilter refused the very same image on the very
        // same budget -- would defeat the budget rather than enforce it. The
        // describer reports a shape over the ceiling and no decode happens at all.
        int decodes = 0;
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return Constant(8, 4, 1f);
            },
            describer: _ => new SilkImageDescription(
                4096,
                2048,
                SilkTextureFormat.Rgba32Float),
            decodeByteBudget: 1024);
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(decodes).IsEqualTo(0);
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();

        // The dome keeps its untextured emission, which is what the fallback
        // degrades to when it cannot read the image.
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded);
    }

    [Test]
    public async Task TheAggregateBoundIsIndependentOfThePerImageCeiling()
    {
        // The aggregate used to be derived as per-image times the dome bound,
        // which made it a restatement of the per-image rule rather than a second
        // bound: it could never refuse a set whose members each fit, which is
        // exactly the case it exists to refuse.
        var options = SilkEnvironmentPrefilterOptions.Default;
        await Assert.That(options.MaximumAggregateSourceBytes)
            .IsEqualTo(SilkEnvironmentPrefilterOptions.DefaultMaximumAggregateSourceBytes);
        await Assert.That(options.MaximumAggregateSourceBytes)
            .IsLessThan(options.MaximumSourceBytes *
                (ulong)SilkEnvironmentPrefilterOptions.DefaultMaximumDomeLights);
    }

    [Test]
    public async Task TheAggregateBoundDropsOverBudgetDomesWithoutRedecodingThePrefix()
    {
        // Each image fits; together they do not. The set is preflighted once from
        // the describer, the domes that do not fit are diagnosed and dropped, and
        // only the survivors are decoded -- each exactly once.
        //
        // The counters are the evidence. The resolve used to restart whenever a
        // dome was dropped, re-decoding every source before it, and no observable
        // except a decode count could tell the difference.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 1f),
            describer: _ => new SilkImageDescription(8, 4, SilkTextureFormat.Rgba32Float),
            options: new SilkEnvironmentPrefilterOptions
            {
                MaximumSourceBytes = 1024,
                MaximumAggregateSourceBytes = 768,
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);

        // One decode, for the one dome that fits. Not two, and not three.
        await Assert.That(resources.EnvironmentDecodeCount).IsEqualTo(1);
        await Assert.That(resources.EnvironmentDecodedBytes).IsEqualTo(512UL);
        await Assert.That(resources.EnvironmentDecodedBytes)
            .IsLessThanOrEqualTo(768UL);
    }

    [Test]
    public async Task AMalformedDomeIsSkippedWithoutRedecodingTheValidOnesBeforeIt()
    {
        // Three valid domes followed by a broken one used to cost six decodes
        // rather than four, because dropping the broken one restarted the resolve.
        // The cost grew quadratically in the number of broken assets.
        var decoded = new List<string>();
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (asset, _) =>
            {
                decoded.Add(asset);
                return asset.EndsWith("-d.hdr", StringComparison.Ordinal)
                    ? throw new InvalidDataException("This asset is malformed.")
                    : Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert("/World/Lights/A", "a.hdr"),
                .. Upsert("/World/Lights/B", "b.hdr"),
                .. Upsert("/World/Lights/C", "c.hdr"),
                .. Upsert("/World/Lights/D", "d-d.hdr"),
            ],
            4,
            1);

        resources.PrepareEnvironmentLighting(scene);

        await Assert.That(resources.EnvironmentLitDomes)
            .IsEquivalentTo(["/World/Lights/A", "/World/Lights/B", "/World/Lights/C"]);
        await Assert.That(decoded.Count).IsEqualTo(4);
        await Assert.That(resources.EnvironmentDecodeCount).IsEqualTo(4);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ARefusedEnvironmentAllocationRetriesAndRecoversWithoutASceneChange(
        int failingTexture)
    {
        // A device that refuses an allocation has not necessarily refused it
        // forever -- a transient exhaustion is the ordinary case -- and the
        // prepared revision used to be committed before the allocation was even
        // attempted. The next frame then saw nothing to redo, so the scene stayed
        // on the mean-radiance fallback until some unrelated authoring happened to
        // move the environment revision.
        var device = new EnvironmentDevice { FailTextureOrdinal = failingTexture };
        try
        {
            using SilkSceneGpuResources resources = CreateResources(device);
            var scene = new SilkSceneState();
            _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

            resources.PrepareEnvironmentLighting(scene);
            await Assert.That(resources.EnvironmentBinding.Enabled).IsFalse();
            await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
                .Contains(SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable);

            int createdWhileFailing = device.CreatedTextureCount;
            int disposedWhileFailing = device.DisposedTextureCount;

            // Everything the failed transaction constructed was disposed. The
            // allocation that threw was counted but never constructed.
            await Assert.That(disposedWhileFailing).IsEqualTo(failingTexture - 1);

            // The device recovers. No command arrives and the scene revision does
            // not move.
            device.FailTextureOrdinal = 0;
            resources.PrepareEnvironmentLighting(scene);

            await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
            await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);

            // Exactly three new textures: the irradiance map, the specular atlas
            // and the BRDF table. Nothing the first attempt left behind is
            // rebound, and nothing is allocated twice.
            await Assert.That(device.CreatedTextureCount)
                .IsEqualTo(createdWhileFailing + 3);
            await Assert.That(device.DisposedTextureCount).IsEqualTo(disposedWhileFailing);

            // And the retry costs allocations, not a second convolution: the
            // prefiltered payload was retained under its identity throughout.
            await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);

            // The unavailable diagnostic is retracted, because it is no longer
            // true.
            await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
                .DoesNotContain(SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable);
        }
        finally
        {
            device.Dispose();
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ARefusedEnvironmentSamplerRetriesAndRecoversWithoutASceneChange(
        int failingSampler)
    {
        var device = new EnvironmentDevice { FailSamplerOrdinal = failingSampler };
        try
        {
            using SilkSceneGpuResources resources = CreateResources(device);
            var scene = new SilkSceneState();
            _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

            resources.PrepareEnvironmentLighting(scene);
            await Assert.That(resources.EnvironmentBinding.Enabled).IsFalse();
            int disposedWhileFailing = device.DisposedTextureCount;
            await Assert.That(disposedWhileFailing).IsEqualTo(3);
            await Assert.That(device.DisposedSamplerCount).IsEqualTo(failingSampler - 1);

            device.FailSamplerOrdinal = 0;
            resources.PrepareEnvironmentLighting(scene);

            await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();
            await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
            await Assert.That(device.DisposedTextureCount).IsEqualTo(disposedWhileFailing);
            await Assert.That(resources.EnvironmentPrefilterBuilds).IsEqualTo(1);
        }
        finally
        {
            device.Dispose();
        }
    }

    [Test]
    public async Task ADirectionalLossStaysDiagnosedWhenTheMeanFallbackSucceeds()
    {
        // The two layers report the same codes about the same dome to say
        // different things: the prefilter, that the dome lost its directional
        // response; the fallback, that it could not even be reduced to a colour.
        // Clearing by code let the second erase the first, so a dome refused by
        // the aggregate budget was diagnosed, fell back successfully, and the
        // successful fallback wiped the record of the loss -- leaving a scene that
        // silently lost its directionality with no diagnostic at all.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) => Constant(8, 4, 1f),
            describer: _ => new SilkImageDescription(8, 4, SilkTextureFormat.Rgba32Float),
            options: new SilkEnvironmentPrefilterOptions
            {
                MaximumSourceBytes = 1024,
                MaximumAggregateSourceBytes = 768,
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Upsert(DomePath, TexturePath),
                .. Upsert(SecondDomePath, SecondTexturePath),
            ],
            2,
            1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        // The first dome is carried by the environment; the second is not.
        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);

        // The second dome's mean fallback *succeeded* -- it contributes its full
        // ambient -- and that is exactly the case in which the loss used to go
        // unreported.
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded);

        // And it survives a second resolve, so it is not merely present on the
        // frame that produced it.
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);
        resources.PrepareEnvironmentLighting(scene);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded);
    }

    [Test]
    public async Task AnUnreadableDomeStaysDiagnosedWhileItsFallbackReadsTheFile()
    {
        // The same rule with a different cause. The prefilter cannot read the
        // asset at all, so the dome loses its directional response and is named
        // for it; the fallback then resolves the same dome's untextured emission
        // without incident and used to erase that name.
        int decodes = 0;
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, _) =>
            {
                decodes++;
                return decodes == 1
                    ? throw new InvalidDataException("This asset is malformed.")
                    : Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X)
            .IsEqualTo(UnitDomeAmbient)
            .Within(1e-5f);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);

        // The fallback read the very asset the prefilter refused, in the same
        // revision. Those two verdicts cannot both be right about the same bytes,
        // so the refusal was transient and the loss of directionality must not be
        // settled on it: the next prepare retries with no scene change at all.
        resources.PrepareEnvironmentLighting(scene);
        ISilkGraphicsBuffer retried = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
        await Assert.That(resources.EnvironmentBinding.Enabled).IsTrue();

        // The dome is now carried by the environment, so it contributes nothing to
        // the ambient term -- it is never counted both as an image and as an
        // untextured approximation of itself.
        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(retried).X)
            .IsEqualTo(0f)
            .Within(1e-5f);

        // And the diagnostic that named the loss is retracted, because it is no
        // longer true.
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);

        // The retry is bounded. A third prepare against unchanged bytes must not
        // decode again: a source that reproduced the contradiction would otherwise
        // be re-read on every frame forever.
        int decodesAfterRetry = decodes;
        resources.PrepareEnvironmentLighting(scene);
        await Assert.That(decodes).IsEqualTo(decodesAfterRetry);
        await Assert.That(resources.EnvironmentLitDomes).IsEquivalentTo([DomePath]);
    }

    [Test]
    public async Task ARepeatedPrefilterRefusalIsSettledRatherThanRetriedForever()
    {
        // The other half of the same rule. A source the prefilter always refuses
        // and the fallback always reads is a real disagreement between the two
        // paths rather than a transient one, so it is retried exactly once and
        // then settled -- otherwise the image would be decoded on every frame for
        // as long as the scene held still.
        int prefilterAttempts = 0;
        int fallbackReads = 0;
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(
            device,
            decoder: (_, prefilter) =>
            {
                // The prefilter opens its sources through the streaming path and
                // the fallback through the mean cache, but both call the same
                // decoder, so they are told apart by counting rather than by the
                // flag -- which carries the transfer-function request, not the
                // caller.
                if (prefilterAttempts + fallbackReads == 0 ||
                    prefilterAttempts <= fallbackReads)
                {
                    prefilterAttempts++;
                    throw new InvalidDataException("This asset is malformed.");
                }
                fallbackReads++;
                return Constant(8, 4, 1f);
            });
        var scene = new SilkSceneState();
        _ = scene.Apply(Upsert(DomePath, TexturePath), 1, 1);

        for (int frame = 0; frame < 4; frame++)
        {
            resources.PrepareEnvironmentLighting(scene);
            _ = resources.RequireFrameBuffer(
                scene,
                RenderOutputTransform.Identity,
                exposure: 1f);
        }

        // Two prefilter attempts and no more: the first resolve, and the single
        // retry the fallback's success bought.
        await Assert.That(prefilterAttempts).IsEqualTo(2);
        await Assert.That(resources.EnvironmentLitDomes).IsEmpty();
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed);
    }

    [Test]
    public async Task AnUntexturedDomeCountsAsAuthoredSceneLighting()
    {
        // An untextured dome publishes no environment record at all: hdSilk folds
        // it into the frame's ambient term and sets the ambient intensity to one
        // to say that it did. That bit is the only evidence the dome exists, and
        // the managed writer repurposes the ambient slot's w component as the
        // direct-light count -- so without folding it into the environment block
        // it was discarded, and a dome authored black or with zero diffuse
        // acquired a headlight nobody placed.
        using var device = new EnvironmentDevice();
        using SilkSceneGpuResources resources = CreateResources(device);
        var scene = new SilkSceneState();
        _ = scene.Apply(
            SilkEnvironmentLightingTests.CreateFrameWithAmbient(0f, 0f, 0f, 1f),
            1,
            1);

        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(
            scene,
            RenderOutputTransform.Identity,
            exposure: 1f);

        await Assert.That(SilkEnvironmentLightingTests.ReadAmbient(frame).X).IsEqualTo(0f);
        await Assert.That(
            SilkEnvironmentLightingTests.ReadAuthoredSceneLighting(frame)).IsEqualTo(1f);

        // A frame with no dome at all does not make the claim.
        var undomed = new SilkSceneState();
        _ = undomed.Apply(
            SilkEnvironmentLightingTests.CreateFrameWithAmbient(0f, 0f, 0f, 0f),
            1,
            1);
        using SilkSceneGpuResources bare = CreateResources(device);
        ISilkGraphicsBuffer without = bare.RequireFrameBuffer(
            undomed,
            RenderOutputTransform.Identity,
            exposure: 1f);
        await Assert.That(
            SilkEnvironmentLightingTests.ReadAuthoredSceneLighting(without)).IsEqualTo(0f);
    }

    private static SilkSceneGpuResources CreateResources(
        EnvironmentDevice device,
        Func<string, bool, SilkDecodedImage>? decoder = null,
        SilkEnvironmentPrefilterOptions? options = null,
        Func<string, SilkEnvironmentAssetStamp>? stampReader = null,
        Func<string, SilkImageDescription>? describer = null,
        ulong decodeByteBudget = 256UL * 1024 * 1024) =>
        new(
            device,
            decoder ?? ((_, _) => Constant(8, 4, 1f)),
            udimResolver: _ => [],
            residencyOptions: null,
            environmentDecodeByteBudget: decodeByteBudget,
            imageDescriber: describer,
            environmentPrefilterOptions: options,
            environmentStampReader: stampReader ?? (_ => new SilkEnvironmentAssetStamp(1, 1)));

    private static SilkDecodedImage Constant(uint width, uint height, float value)
    {
        float[] values = new float[width * height * 4];
        for (int index = 0; index < values.Length; index += 4)
        {
            values[index] = value;
            values[index + 1] = value;
            values[index + 2] = value;
            values[index + 3] = 1f;
        }
        return new SilkDecodedImage(
            width,
            height,
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray(),
            SilkTextureFormat.Rgba32Float);
    }

    private static byte[] Upsert(
        string path,
        string texture,
        SilkDomeTextureFormat format = SilkDomeTextureFormat.Latlong,
        float specular = 0f,
        SilkEnvironmentUnsupportedFeatures unsupported =
            SilkEnvironmentUnsupportedFeatures.None,
        uint domeIndex = SilkEnvironmentUpsertCommand.NoDomeIndex) =>
        SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            path,
            texture,
            format,
            SilkColorSpace.Auto,
            unsupported,
            specular: specular,
            domeIndex: domeIndex);

    private static byte[] Remove(string path)
    {
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(SilkEnvironmentLightingTests.ComputeStableHash(path)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. pathBytes,
        ];
        List<byte> command =
        [
            .. BitConverter.GetBytes((uint)SilkCommandType.EnvironmentRemove),
            .. BitConverter.GetBytes((uint)(payload.Count + 8)),
            .. payload,
        ];
        return [.. command];
    }

    private sealed class EnvironmentDevice
        : ISilkGraphicsDevice, ISilkSelectionOutlineGraphicsDevice, ISilkDeviceLossGraphicsDevice
    {
        public ulong DeviceLossGeneration { get; set; }

        internal bool RefuseTextures { get; init; }

        /// <summary>
        /// The one-based ordinal of the texture allocation that fails, or zero.
        /// </summary>
        /// <remarks>
        /// The environment allocates its resources in a fixed order under one
        /// guard, so selecting the n-th allocation is how each failure inside that
        /// transaction is injected separately. A guard that only rolled back the
        /// first failure would look correct until the third one happened.
        /// </remarks>
        internal int FailTextureOrdinal { get; set; }

        /// <summary>The one-based ordinal of the sampler allocation that fails.</summary>
        internal int FailSamplerOrdinal { get; set; }

        internal int CreatedTextureCount { get; private set; }

        internal int DisposedTextureCount { get; set; }

        internal int CreatedSamplerCount { get; private set; }

        internal int DisposedSamplerCount { get; set; }

        public ulong SelectionOutlineDeviceGeneration { get; set; }

        public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities => new(false, false);

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Environment retention test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            CreateTexture2D(new SilkTextureDescriptor(
                width,
                height,
                format,
                SilkTextureUsage.Sampled));

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
        {
            descriptor.Validate();
            if (RefuseTextures)
            {
                throw new InvalidOperationException("This device allocates no textures.");
            }
            CreatedTextureCount++;
            if (FailTextureOrdinal == CreatedTextureCount)
            {
                throw new InvalidOperationException(
                    $"This device refuses texture {CreatedTextureCount}.");
            }
            return new EnvironmentTexture(descriptor, this);
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new EnvironmentBuffer(size, usage);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
        {
            CreatedSamplerCount++;
            if (FailSamplerOrdinal == CreatedSamplerCount)
            {
                throw new InvalidOperationException(
                    $"This device refuses sampler {CreatedSamplerCount}.");
            }
            return new EnvironmentSampler(descriptor, this);
        }

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) => throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) => throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) => throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => new EnvironmentCommandList();

        public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
            SilkSelectionMaskPipelineDescriptor descriptor) => throw new NotSupportedException();

        public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
            SilkSelectionOutlinePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
            SilkSelectionOutlineBindingDescriptor descriptor) => throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class EnvironmentTexture(
        SilkTextureDescriptor descriptor,
        EnvironmentDevice device)
        : SilkGraphicsTextureBase(descriptor)
    {
        public override void ReadbackForTesting(Span<byte> destination) =>
            throw new NotSupportedException();

        public override void ReadbackForTesting(Span<float> destination) =>
            throw new NotSupportedException();

        protected override void ReleaseNative() => device.DisposedTextureCount++;
    }

    private sealed class EnvironmentSampler(
        SilkSamplerDescriptor descriptor,
        EnvironmentDevice device)
        : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor => descriptor;

        public void Dispose() => device.DisposedSamplerCount++;
    }

    private sealed class EnvironmentBuffer(nuint size, SilkBufferUsage usage)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
        }
    }

    private sealed class EnvironmentCommandList : ISilkGraphicsCommandList
    {
        internal List<(ISilkGraphicsTexture Texture, int ByteCount)> Uploads { get; } = [];

        internal Dictionary<uint, ISilkGraphicsTexture> Textures { get; } = [];

        internal Dictionary<uint, SilkSamplerDescriptor> Samplers { get; } = [];

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            Uploads.Add((texture, source.Length));

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            Textures[binding] = texture;

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            Samplers[binding] = sampler.Descriptor;

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void DrawIndexed(uint indexCount)
        {
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
        }

        public void EndRendering()
        {
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline)
        {
        }

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetComputeUniformBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispatch(uint elementCount)
        {
        }

        public void BufferBarrier(ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispose()
        {
        }
    }
}
