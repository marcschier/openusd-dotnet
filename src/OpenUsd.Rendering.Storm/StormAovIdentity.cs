// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Storm;

/// <summary>States whether one native ID pair was resolved to canonical USD identity.</summary>
public enum StormAovIdentityStatus : uint
{
    /// <summary>No published identity record uses this default value.</summary>
    None = 0,
    /// <summary>The native decoder supplied validated canonical identity.</summary>
    Resolved = 1,
    /// <summary>The pair could not be resolved; it is not background or identity zero.</summary>
    Unresolved = 2,
}

/// <summary>One immutable SDK-ordered instancer context level.</summary>
/// <remarks>Its local index is not an authored ids-primvar value or a flattened global ordinal.</remarks>
public sealed class StormAovInstanceContext
{
    internal StormAovInstanceContext(string instancerPath, int instanceIndex)
    {
        InstancerPath = instancerPath;
        InstanceIndex = instanceIndex;
    }

    /// <summary>Gets the canonical absolute USD instancer path.</summary>
    public string InstancerPath { get; }
    /// <summary>Gets the native decoder's local index at this context level.</summary>
    public int InstanceIndex { get; }
}

/// <summary>Detached identity for one bounded unique native primitive/instance ID pair.</summary>
/// <remarks>
/// Raw IDs and the optional flattened decoder ordinal are snapshot-local, not stable authored identity.
/// Context indices are preserved independently and need not equal the flattened ordinal.
/// </remarks>
public sealed class StormAovIdentity
{
    internal StormAovIdentity(
        in StormAovNative.Identity identity,
        string? primPath,
        string? instancerPath,
        StormAovInstanceContext[] context)
    {
        Status = (StormAovIdentityStatus)identity.Status;
        RawPrimId = identity.PrimId;
        RawInstanceId = identity.InstanceId;
        DecodedInstanceIndex = identity.InstanceIndex < 0 ? null : identity.InstanceIndex;
        PrimPath = primPath;
        InstancerPath = instancerPath;
        InstancerContext = new OwnedReadOnlyList<StormAovInstanceContext>(context);
    }

    /// <summary>Gets explicit resolved/unresolved status.</summary>
    public StormAovIdentityStatus Status { get; }
    /// <summary>Gets the snapshot-local Hydra primitive ID.</summary>
    public int RawPrimId { get; }
    /// <summary>Gets the snapshot-local Hydra instance ID, not an authored instance ID.</summary>
    public int RawInstanceId { get; }
    /// <summary>Gets the optional native flattened decoder ordinal, not a context-local index.</summary>
    public int? DecodedInstanceIndex { get; }
    /// <summary>Gets the canonical USD prim path, or null for unresolved identity.</summary>
    public string? PrimPath { get; }
    /// <summary>Gets the optional canonical flat instancer path.</summary>
    public string? InstancerPath { get; }
    /// <summary>Gets immutable instancer levels in exactly the native decoder order.</summary>
    public IReadOnlyList<StormAovInstanceContext> InstancerContext { get; }
}
