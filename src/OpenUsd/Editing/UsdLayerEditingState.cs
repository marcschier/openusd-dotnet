// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>Opaque process-local identities, never pointers or portable document identifiers.</summary>
/// <param name="StageId">Logical stage identity.</param>
/// <param name="LayerId">Logical layer identity.</param>
/// <param name="Generation">Generation invalidated by replacement, reload or local detachment.</param>
public readonly record struct UsdLayerIdentity(ulong StageId, ulong LayerId, ulong Generation) : IUsdDetachedResult;

/// <summary>The layer's native-known role in its document.</summary>
public enum UsdLayerRole
{
    /// <summary>The source root layer; generic resident layers are capture-only.</summary>
    Root = 0,
    /// <summary>The session stack container, not the review authoring layer.</summary>
    SessionContainer = 1,
    /// <summary>The currently registered user-review layer.</summary>
    UserReview = 2,
    /// <summary>The separately registered simulation overlay.</summary>
    Physics = 3,
    /// <summary>Another stage-local layer.</summary>
    Local = 4
}

/// <summary>Immutable, detached layer identity, lifecycle and permission information.</summary>
public sealed class UsdLayerEditingState : IUsdDetachedResult
{
    internal UsdLayerEditingState(
        UsdLayerIdentity identity, ulong revision, UsdLayerRole role, uint flags,
        string identifier, string realPath, string resolvedPath, string assetAnchor)
    {
        Identity = identity;
        Revision = revision;
        Role = role;
        Identifier = identifier;
        RealPath = realPath;
        ResolvedPath = resolvedPath;
        AssetAnchor = assetAnchor;
        IsAnonymous = (flags & 1) != 0;
        IsDirty = (flags & 2) != 0;
        NativeIsDirty = (flags & 4) != 0;
        PermissionToEdit = (flags & 8) != 0;
        PermissionToSave = (flags & 16) != 0;
        CurrentlyLocal = (flags & 32) != 0;
    }

    /// <summary>Gets the logical stage/layer identity and generation.</summary>
    public UsdLayerIdentity Identity { get; }
    /// <summary>Gets the layer revision; disjoint per-edit comparisons do not require it to match.</summary>
    public ulong Revision { get; }
    /// <summary>Gets the native-known layer role.</summary>
    public UsdLayerRole Role { get; }
    /// <summary>Gets the exact native identifier.</summary>
    public string Identifier { get; }
    /// <summary>Gets the native real path, or an empty string.</summary>
    public string RealPath { get; }
    /// <summary>Gets the native resolved path, or an empty string.</summary>
    public string ResolvedPath { get; }
    /// <summary>Gets the source layer's asset anchor, never a guessed root-layer anchor.</summary>
    public string AssetAnchor { get; }
    /// <summary>Gets whether this is an anonymous layer.</summary>
    public bool IsAnonymous { get; }
    /// <summary>Gets logical editing dirtiness relative to the conditionally acknowledged saved revision.</summary>
    public bool IsDirty { get; }
    /// <summary>Gets native Sdf dirtiness, independent of the logical saved baseline.</summary>
    public bool NativeIsDirty { get; }
    /// <summary>Gets native edit permission; this does not establish bounded editing support.</summary>
    public bool PermissionToEdit { get; }
    /// <summary>Gets native save permission; this does not establish checkpoint/export support.</summary>
    public bool PermissionToSave { get; }
    /// <summary>Gets whether the layer is currently local to its document.</summary>
    public bool CurrentlyLocal { get; }
    /// <summary>Gets whether role, locality and permission admit an authored editing attempt.</summary>
    /// <remarks>Native calls additionally require the owned counted backing and validate all content budgets.</remarks>
    public bool CanAttemptAuthoredEdits =>
        Role == UsdLayerRole.UserReview && CurrentlyLocal && PermissionToEdit;
    /// <summary>Gets the known restriction, or null when an attempt may proceed to native backing validation.</summary>
    public string? EditingRestriction => !CurrentlyLocal
        ? "The layer is detached from its document."
        : Role != UsdLayerRole.UserReview
            ? "Only the registered user-review layer is admitted; generic and simulation layers are capture-only."
            : !PermissionToEdit
                ? "Native edit permission is disabled."
                : null;
}
