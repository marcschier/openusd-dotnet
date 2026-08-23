// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Manages a removable simulation overlay on a stage's session layer.
/// </summary>
/// <remarks>
/// <para>
/// When normalized, the session layer becomes a pure container with two anonymous sublayers:
/// a strong physics overlay (index 0) where simulation results are authored, and a weaker
/// user-edit layer (index 1) where viewer/session edits are directed. This ensures physics
/// results always compose above user edits without overwriting them.
/// </para>
/// <para>
/// Disposal removes only the physics overlay and releases all handles. The user-edit layer
/// and any pre-existing session content that was migrated into it are preserved. Removal
/// errors are propagated from <see cref="Dispose"/>; handles are always released in a
/// <c>finally</c> block to avoid leaks even when removal fails.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdSessionOverlay : IDisposable
{
    private readonly WeakReference<UsdStage>? _ownerStage;
    private OpenUsdNativeStage? _retainedStage;
    private OpenUsdNativeLayer? _physicsLayer;
    private OpenUsdNativeLayer? _userLayer;
    private string? _physicsLayerIdentifier;
    private string? _userLayerIdentifier;
    private bool _disposed;

    private UsdSessionOverlay(
        OpenUsdNativeStage retainedStage,
        OpenUsdNativeLayer physicsLayer,
        OpenUsdNativeLayer userLayer,
        UsdStage? ownerStage)
    {
        _retainedStage = retainedStage;
        _physicsLayer = physicsLayer;
        _userLayer = userLayer;
        _physicsLayerIdentifier = physicsLayer.Identifier;
        _userLayerIdentifier = userLayer.Identifier;
        _ownerStage = ownerStage is not null ? new WeakReference<UsdStage>(ownerStage) : null;
    }

    /// <summary>Gets the physics overlay layer identifier.</summary>
    public string PhysicsLayerIdentifier =>
        _physicsLayerIdentifier ?? throw new ObjectDisposedException(nameof(UsdSessionOverlay));

    /// <summary>Gets the user-edit layer identifier.</summary>
    public string UserLayerIdentifier =>
        _userLayerIdentifier ?? throw new ObjectDisposedException(nameof(UsdSessionOverlay));

    /// <summary>Gets whether the overlay has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets an owned handle to the physics overlay layer.</summary>
    internal OpenUsdNativeLayer PhysicsLayer =>
        _physicsLayer ?? throw new ObjectDisposedException(nameof(UsdSessionOverlay));

    /// <summary>Gets an owned handle to the user-edit layer.</summary>
    internal OpenUsdNativeLayer UserLayer =>
        _userLayer ?? throw new ObjectDisposedException(nameof(UsdSessionOverlay));

    /// <summary>
    /// Normalizes the stage's session layer into a simulation overlay topology.
    /// </summary>
    internal static UsdSessionOverlay Normalize(OpenUsdNativeStage stage, UsdStage? owner = null)
    {
        ArgumentNullException.ThrowIfNull(stage);

        OpenUsdNativeStage retainedStage = OpenUsdNativeRuntime.RetainStage(stage);
        try
        {
            var (physicsLayer, userLayer) =
                OpenUsdNativeRuntime.SessionOverlayNormalize(retainedStage);
            try
            {
                return new UsdSessionOverlay(retainedStage, physicsLayer, userLayer, owner);
            }
            catch
            {
                try
                {
                    string physicsId = physicsLayer.Identifier;
                    OpenUsdNativeRuntime.SessionOverlayRemove(retainedStage, physicsId);
                }
                catch (OpenUsdNativeException)
                {
                    // Best-effort rollback; original exception propagates.
                }
                try
                {
                    string userId = userLayer.Identifier;
                    OpenUsdNativeRuntime.SessionOverlayRemove(retainedStage, userId);
                }
                catch (OpenUsdNativeException)
                {
                    // Best-effort rollback.
                }
                physicsLayer.Dispose();
                userLayer.Dispose();
                throw;
            }
        }
        catch
        {
            retainedStage.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Detects whether external code has added direct opinions to the session container.
    /// </summary>
    public bool DetectContamination()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return OpenUsdNativeRuntime.SessionOverlayDetectContamination(
            _retainedStage!, _physicsLayerIdentifier!, _userLayerIdentifier!);
    }

    /// <summary>
    /// Migrates any detected session container contamination into the user-edit layer
    /// using SdfCopySpec with overwrite semantics (preserving original strength).
    /// </summary>
    public void MigrateContamination()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OpenUsdNativeRuntime.SessionOverlayMigrateContamination(
            _retainedStage!, _userLayerIdentifier!);
    }

    /// <summary>
    /// Redirects the stage edit target to the user-edit layer.
    /// </summary>
    public void SetEditTargetToUserLayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OpenUsdNativeRuntime.SetEditTarget(_retainedStage!, _userLayer!);
    }

    /// <summary>
    /// Resolves whether a SetEditTargetToSessionLayer call should be redirected
    /// to the user-edit layer while this overlay is active.
    /// </summary>
    internal bool TryRedirectSessionEditTarget(OpenUsdNativeStage stage)
    {
        if (_disposed || _retainedStage is null || _userLayer is null)
        {
            return false;
        }
        OpenUsdNativeRuntime.SetEditTarget(stage, _userLayer);
        return true;
    }

    /// <summary>
    /// Removes the physics overlay from the session layer and releases all handles.
    /// Idempotent: subsequent calls are no-ops. Removal errors are propagated;
    /// handles are always released in a <c>finally</c> block.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Clear stage registration to break the strong reference cycle.
        ClearStageRegistration();

        try
        {
            if (_physicsLayerIdentifier is not null && _retainedStage is not null)
            {
                OpenUsdNativeRuntime.SessionOverlayRemove(
                    _retainedStage, _physicsLayerIdentifier);
            }
        }
        finally
        {
            ReleaseHandles();
        }
    }

    private void ClearStageRegistration()
    {
        if (_ownerStage is not null &&
            _ownerStage.TryGetTarget(out UsdStage? stage))
        {
            stage.ClearActiveOverlay(this);
        }
    }

    private void ReleaseHandles()
    {
        _physicsLayer?.Dispose();
        _physicsLayer = null;
        _userLayer?.Dispose();
        _userLayer = null;
        _retainedStage?.Dispose();
        _retainedStage = null;
        _physicsLayerIdentifier = null;
        _userLayerIdentifier = null;
    }
}
