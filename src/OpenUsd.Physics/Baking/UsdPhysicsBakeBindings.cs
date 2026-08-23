// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Binds one stable simulated identity to the extracted stage prim it was produced from.
/// </summary>
/// <param name="Id">The stable identity of the simulated object.</param>
/// <param name="PrimPath">The absolute path of the extracted prim the identity maps to.</param>
/// <param name="InstanceIndex">
/// The point-instancer instance index the identity maps to, or <c>-1</c> when the identity maps to
/// the prim itself.
/// </param>
/// <param name="TopologyRevision">
/// The topology revision the binding was extracted at. A point sample carrying a different revision
/// describes topology that no longer exists and is rejected instead of authored.
/// </param>
public readonly record struct UsdPhysicsBakeBinding(
    UsdPhysicsObjectId Id,
    string PrimPath,
    int InstanceIndex = -1,
    ulong TopologyRevision = 0) : IUsdDetachedResult;

/// <summary>
/// An immutable set of identity bindings shared by every preview apply and bake of one extraction.
/// </summary>
/// <remarks>
/// Bindings are the only thing that translates a simulated identity into a stage path, so a batch
/// whose <see cref="UsdPhysicsResultBatch.IdentityRevision"/> differs from
/// <see cref="IdentityRevision"/> describes objects that no longer exist and is rejected whole.
/// </remarks>
public sealed class UsdPhysicsBakeBindings : IUsdDetachedResult
{
    private readonly FrozenDictionary<ulong, UsdPhysicsBakeBinding> _map;
    private readonly ImmutableArray<UsdPhysicsBakeBinding> _ordered;

    /// <summary>Gets empty bindings that match no identity.</summary>
    public static UsdPhysicsBakeBindings Empty { get; } = new(0, []);

    /// <summary>Initializes bindings by defensively copying entries.</summary>
    /// <param name="identityRevision">The extraction revision the bindings were produced at.</param>
    /// <param name="bindings">The identity bindings, which must not repeat an identity.</param>
    public UsdPhysicsBakeBindings(
        ulong identityRevision,
        IEnumerable<UsdPhysicsBakeBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        IdentityRevision = identityRevision;

        var map = new Dictionary<ulong, UsdPhysicsBakeBinding>();
        foreach (UsdPhysicsBakeBinding binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.PrimPath) || binding.PrimPath[0] != '/')
            {
                throw new ArgumentException(
                    "Every binding must name an absolute prim path.", nameof(bindings));
            }
            if (binding.Id.IsNone)
            {
                throw new ArgumentException(
                    "A binding cannot use the sentinel identity.", nameof(bindings));
            }
            if (!map.TryAdd(binding.Id.Value, binding))
            {
                throw new ArgumentException(
                    $"The identity {binding.Id.Value} is bound more than once.", nameof(bindings));
            }
        }

        // Authoring order is a stable path order so that two bakes of the same batch produce
        // identical layers no matter what order the caller enumerated identities in.
        _ordered = [.. map.Values.Order(BindingComparer.Instance)];
        _map = map.ToFrozenDictionary();
    }

    /// <summary>Gets the extraction revision these bindings were produced at.</summary>
    public ulong IdentityRevision { get; }

    /// <summary>Gets the bindings in deterministic authoring order.</summary>
    public IReadOnlyList<UsdPhysicsBakeBinding> Bindings => _ordered;

    /// <summary>Gets the number of bound identities.</summary>
    public int Count => _ordered.Length;

    /// <summary>Looks up the binding for one identity.</summary>
    /// <param name="id">The identity to resolve.</param>
    /// <param name="binding">The resolved binding when the identity is bound.</param>
    /// <returns><see langword="true"/> when the identity is bound.</returns>
    public bool TryGetBinding(UsdPhysicsObjectId id, out UsdPhysicsBakeBinding binding) =>
        _map.TryGetValue(id.Value, out binding);

    private sealed class BindingComparer : IComparer<UsdPhysicsBakeBinding>
    {
        public static BindingComparer Instance { get; } = new();

        public int Compare(UsdPhysicsBakeBinding x, UsdPhysicsBakeBinding y)
        {
            int byPath = string.CompareOrdinal(x.PrimPath, y.PrimPath);
            if (byPath != 0)
            {
                return byPath;
            }
            int byInstance = x.InstanceIndex.CompareTo(y.InstanceIndex);
            return byInstance != 0 ? byInstance : x.Id.Value.CompareTo(y.Id.Value);
        }
    }
}
