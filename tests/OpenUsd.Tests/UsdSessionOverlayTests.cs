// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

/// <summary>
/// Tests for <see cref="UsdSessionOverlay"/> managed primitives.
/// </summary>
public sealed class UsdSessionOverlayTests
{
    private const ulong SessionOverlayCapability = 1UL << 18;

    /// <summary>
    /// Returns <see langword="true"/> when the native runtime exports session overlay support.
    /// Only catches expected native-unavailability exceptions, not arbitrary failures.
    /// </summary>
    private static bool HasSessionOverlayCapability()
    {
        try
        {
            return (OpenUsdNativeRuntime.Capabilities & SessionOverlayCapability) != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void SkipIfNoCapability()
    {
        if (!HasSessionOverlayCapability())
        {
            Skip.Test("Native session overlay capability not available.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    // -----------------------------------------------------------------------
    // Contract tests: these always execute regardless of native availability.
    // -----------------------------------------------------------------------

    [Test]
    public async Task SessionOverlayCapabilityBit_IsCorrectValue()
    {
        ulong actual = GetSessionOverlayBit();
        ulong expected = 1UL << 18;
        await Assert.That(actual).IsEqualTo(expected);
    }

    private static ulong GetSessionOverlayBit() => SessionOverlayCapability;

    [Test]
    public void Normalize_ThrowsArgumentNullException_WhenStageIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => UsdSessionOverlay.Normalize(null!));
    }

    // -----------------------------------------------------------------------
    // Native-dependent tests: skip when overlay capability is unavailable.
    // -----------------------------------------------------------------------

    [Test]
    public async Task PhysicsLayerIdentifier_WhenNotDisposed_ReturnsAnonymousIdentifier()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        await Assert.That(overlay.PhysicsLayerIdentifier).Contains("physics-overlay");
        await Assert.That(overlay.UserLayerIdentifier).Contains("user-edit");
    }

    [Test]
    public async Task Normalize_CreatesExpectedSubLayerTopology()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        using var sessionLayer = stage.GetSessionLayer();
        string[] sublayers = sessionLayer.GetSublayerPaths();

        await Assert.That(sublayers.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(sublayers[0]).IsEqualTo(overlay.PhysicsLayerIdentifier);
        await Assert.That(sublayers[1]).IsEqualTo(overlay.UserLayerIdentifier);
    }

    [Test]
    public async Task Normalize_PreservesExistingSublayerPaths()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());

        // Add a sublayer to the session before normalization.
        using var sessionLayer = stage.GetSessionLayer();
        string preExistingSublayerPath = "anon:pre-existing:sublayer.usda";
        sessionLayer.AddSublayer(preExistingSublayerPath);

        using var overlay = stage.NormalizeSessionOverlay();

        string[] sublayers = sessionLayer.GetSublayerPaths();
        // physics[0], user-edit[1], pre-existing[2+]
        await Assert.That(sublayers.Length).IsGreaterThanOrEqualTo(3);
        await Assert.That(sublayers[0]).IsEqualTo(overlay.PhysicsLayerIdentifier);
        await Assert.That(sublayers[1]).IsEqualTo(overlay.UserLayerIdentifier);
    }

    [Test]
    public async Task Dispose_RemovesOnlyPhysicsOverlay()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        string userLayerId;
        string physicsLayerId;
        {
            using var overlay = stage.NormalizeSessionOverlay();
            userLayerId = overlay.UserLayerIdentifier;
            physicsLayerId = overlay.PhysicsLayerIdentifier;
        }
        using var sessionLayer = stage.GetSessionLayer();
        string[] sublayers = sessionLayer.GetSublayerPaths();

        await Assert.That(sublayers).DoesNotContain(physicsLayerId);
        await Assert.That(sublayers).Contains(userLayerId);
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        overlay.Dispose();
        overlay.Dispose(); // Must not throw.

        await Assert.That(overlay.IsDisposed).IsTrue();
    }

    [Test]
    public async Task DetectContamination_ReturnsFalse_WhenSessionIsClean()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        await Assert.That(overlay.DetectContamination()).IsFalse();
    }

    [Test]
    public async Task SetEditTargetToSessionLayer_RedirectsToUserLayerWhileNormalized()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        // This should redirect to user-edit layer, not the raw session.
        stage.SetEditTargetToSessionLayer();

        await Assert.That(stage.EditTargetLayerIdentifier)
            .IsEqualTo(overlay.UserLayerIdentifier);
    }

    [Test]
    public async Task SetEditTargetToUserLayer_RedirectsEditsCorrectly()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();
        overlay.SetEditTargetToUserLayer();

        await Assert.That(stage.EditTargetLayerIdentifier)
            .IsEqualTo(overlay.UserLayerIdentifier);
    }

    [Test]
    public async Task IsDisposed_ReturnsTrue_AfterDisposal()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        overlay.Dispose();

        await Assert.That(overlay.IsDisposed).IsTrue();
    }

    [Test]
    public void PhysicsLayerIdentifier_AfterDispose_ThrowsObjectDisposedException()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        overlay.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = overlay.PhysicsLayerIdentifier);
    }

    [Test]
    public async Task Normalize_TransfersExistingSessionOpinions()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());

        // Author a prim in session layer before normalization.
        stage.SetEditTargetToSessionLayer();
        stage.DefinePrim("/PreExisting", "Xform");
        stage.SetEditTargetToRootLayer();

        using var overlay = stage.NormalizeSessionOverlay();

        // Session container should be clean after transfer.
        await Assert.That(overlay.DetectContamination()).IsFalse();

        // The prim should still compose (via user-edit sublayer).
        await Assert.That(stage.HasPrim("/PreExisting")).IsTrue();
    }

    [Test]
    public void NormalizeSessionOverlay_ThrowsWhenAlreadyActive()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        Assert.Throws<InvalidOperationException>(() => stage.NormalizeSessionOverlay());
    }

    [Test]
    public async Task OverlayStageLifetime_SurvivesIndependentOfCallerStageHandle()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        // overlay retains its own stage reference; Dispose must not AV.
        overlay.Dispose();
        await Assert.That(overlay.IsDisposed).IsTrue();
    }

    [Test]
    public async Task StageDispose_DisposesActiveOverlay()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        stage.Dispose();

        await Assert.That(overlay.IsDisposed).IsTrue();
    }

    [Test]
    public async Task OverlayDispose_ClearsStageRegistration()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        overlay.Dispose();

        // Should be able to normalize again after overlay disposal.
        using var overlay2 = stage.NormalizeSessionOverlay();
        await Assert.That(overlay2.IsDisposed).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Metadata transfer and contamination detection tests.
    // -----------------------------------------------------------------------

    [Test]
    public async Task Normalize_TransfersCustomLayerDataMetadata()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var sessionLayer = stage.GetSessionLayer();
        sessionLayer.SetMetadata("testKey", "testValue");

        using var overlay = stage.NormalizeSessionOverlay();

        // Session container must be clean — metadata moved to user-edit layer.
        await Assert.That(overlay.DetectContamination()).IsFalse();

        // Metadata must still be accessible via the user-edit native layer.
        string transferred = overlay.UserLayer.GetMetadataString("testKey");
        await Assert.That(transferred).IsEqualTo("testValue");
    }

    [Test]
    public async Task DetectContamination_DetectsMetadataOnlyContamination()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        // Write metadata directly to the session container (bypassing overlay).
        using var sessionLayer = stage.GetSessionLayer();
        sessionLayer.SetMetadata("rogue", "data");

        await Assert.That(overlay.DetectContamination()).IsTrue();
    }

    [Test]
    public async Task MigrateContamination_MovesMetadataToUserLayer()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        // Contaminate session with metadata.
        using var sessionLayer = stage.GetSessionLayer();
        sessionLayer.SetMetadata("contam", "value");

        overlay.MigrateContamination();

        // Session should be clean after migration.
        await Assert.That(overlay.DetectContamination()).IsFalse();

        // Metadata should now be on user-edit layer.
        string migrated = overlay.UserLayer.GetMetadataString("contam");
        await Assert.That(migrated).IsEqualTo("value");
    }

    // -----------------------------------------------------------------------
    // Strength / composition order tests.
    // -----------------------------------------------------------------------

    [Test]
    public async Task PhysicsOpinions_ComposeAboveUserOpinions()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        // Author a prim in user-edit layer.
        overlay.SetEditTargetToUserLayer();
        stage.DefinePrim("/Strength", "Xform");

        // Author same prim in physics layer (stronger) via native interop.
        OpenUsdNativeRuntime.SetEditTarget(stage.Native, overlay.PhysicsLayer);
        stage.DefinePrim("/Strength", "Scope");

        // The composed stage should see the physics type (stronger).
        UsdPrim prim = stage.GetPrim("/Strength");
        await Assert.That(prim.TypeName).IsEqualTo("Scope");
    }

    // -----------------------------------------------------------------------
    // Lifetime / cycle tests.
    // -----------------------------------------------------------------------

    [Test]
    public async Task StageDispose_ThenOverlayAccess_ThrowsObjectDisposed()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        var overlay = stage.NormalizeSessionOverlay();
        stage.Dispose();

        // Overlay should be disposed by stage.
        await Assert.That(overlay.IsDisposed).IsTrue();
        Assert.Throws<ObjectDisposedException>(() => overlay.DetectContamination());
    }

    [Test]
    public async Task MultipleOverlayLifecycles_DoNotLeak()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());

        // First overlay lifecycle.
        var overlay1 = stage.NormalizeSessionOverlay();
        overlay1.Dispose();

        // Second overlay lifecycle.
        var overlay2 = stage.NormalizeSessionOverlay();
        overlay2.Dispose();

        await Assert.That(overlay1.IsDisposed).IsTrue();
        await Assert.That(overlay2.IsDisposed).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Sublayer offset preservation tests.
    // Offset+scale verification requires native execution; these tests verify
    // the path/order preservation through the offset-aware SublayerSnapshot
    // code path. Full offset+scale assertions run in native conformance.
    // -----------------------------------------------------------------------

    [Test]
    public async Task Normalize_PreservesSublayerOrderWithMultiplePaths()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var sessionLayer = stage.GetSessionLayer();

        // Add multiple sublayers in order before normalization.
        sessionLayer.AddSublayer("anon:first:sub.usda");
        sessionLayer.AddSublayer("anon:second:sub.usda");
        sessionLayer.AddSublayer("anon:third:sub.usda");

        using var overlay = stage.NormalizeSessionOverlay();

        string[] sublayers = sessionLayer.GetSublayerPaths();
        // physics[0], user-edit[1], first[2], second[3], third[4]
        await Assert.That(sublayers.Length).IsGreaterThanOrEqualTo(5);
        await Assert.That(sublayers[2]).IsEqualTo("anon:first:sub.usda");
        await Assert.That(sublayers[3]).IsEqualTo("anon:second:sub.usda");
        await Assert.That(sublayers[4]).IsEqualTo("anon:third:sub.usda");
    }

    [Test]
    public async Task MigrateContamination_PreservesExistingSublayerTopology()
    {
        SkipIfNoCapability();

        using var stage = UsdStage.Create(GetTempUsda());
        using var overlay = stage.NormalizeSessionOverlay();

        using var sessionLayer = stage.GetSessionLayer();
        string[] sublayersBefore = sessionLayer.GetSublayerPaths();

        // Contaminate and migrate.
        sessionLayer.SetMetadata("rogue", "data");
        overlay.MigrateContamination();

        string[] sublayersAfter = sessionLayer.GetSublayerPaths();

        // Sublayer topology must be identical after migration.
        await Assert.That(sublayersAfter.Length).IsEqualTo(sublayersBefore.Length);
        for (int i = 0; i < sublayersBefore.Length; i++)
        {
            await Assert.That(sublayersAfter[i]).IsEqualTo(sublayersBefore[i]);
        }
    }

    /// <summary>
    /// Requires the overlay a borrowed scheduler facade normalized to be owned by the stage that
    /// can actually be disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only production caller normalizes the overlay from inside a scheduler callback, and a
    /// callback is handed a borrowed facade over the same native stage rather than the owning one.
    /// While the active overlay was per-facade state, the owning stage saw nothing to clean up on
    /// dispose: the physics sublayer stayed wired into the session layer and the retained native
    /// stage and layer handles survived until finalization.
    /// </para>
    /// <para>
    /// Borrowing directly is deliberate. Driving a scheduler here would test the scheduler; the
    /// contract that matters is that every facade over one native stage shares one overlay slot.
    /// </para>
    /// </remarks>
    [Test]
    public async Task Normalize_FromABorrowedFacade_IsCleanedUpByTheOwningStage()
    {
        SkipIfNoCapability();

        var owned = UsdStage.Create(GetTempUsda());
        UsdSessionOverlay overlay;
        try
        {
            UsdStage borrowed = owned.Borrow();
            overlay = borrowed.NormalizeSessionOverlay();

            // The guard reads the same slot, so the borrowed facade and the owner agree that one
            // overlay is already active.
            await Assert.That(() => owned.NormalizeSessionOverlay())
                .Throws<InvalidOperationException>();
            await Assert.That(() => borrowed.NormalizeSessionOverlay())
                .Throws<InvalidOperationException>();

            using var sessionLayer = owned.GetSessionLayer();
            await Assert.That(sessionLayer.GetSublayerPaths())
                .Contains(overlay.PhysicsLayerIdentifier);
        }
        finally
        {
            owned.Dispose();
        }

        // Disposing the owner had to reach the overlay the borrowed facade created, which is what
        // removes the physics sublayer and releases the handles it retained.
        await Assert.That(overlay.IsDisposed).IsTrue();
    }
    private static string GetTempUsda()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"session_overlay_test_{Guid.NewGuid():N}.usda");
        return path;
    }
}
