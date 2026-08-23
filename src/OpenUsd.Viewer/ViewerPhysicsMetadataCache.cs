// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Caches the renderer-neutral capability and diagnostic rows a physics transport describes.
/// </summary>
/// <remarks>
/// Both lists are read once per painted frame - around a hundred times a second while a simulation
/// plays. Rebuilding them on every read allocated a list plus a row per entry at that rate, and
/// handed the controller and the inspector a new reference each time, so every downstream identity
/// check (the controller's capability cache and the inspector's <c>ItemsSource</c> guard) missed and
/// the whole inspector was rebuilt under the operator. The cache returns the same instance until the
/// content actually changes, and decides that by comparing the content itself: reducing it to a hash
/// could collide and hide a diagnostic the operator needs to see.
/// </remarks>
internal sealed class ViewerPhysicsMetadataCache
{
    /// <summary>
    /// Every declared capability flag, resolved once for the process.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.GetValues{TEnum}"/> allocates a fresh array on every call, which is why the
    /// enumeration is resolved once rather than per read.
    /// </remarks>
    private static readonly UsdPhysicsCapability[] Features = BuildFeatures();

    private CapabilityCache? _capabilities;
    private DiagnosticCache? _diagnostics;

    /// <summary>Gets the capability rows for one set of capability flags.</summary>
    /// <param name="features">The flags the built world reports.</param>
    /// <returns>The cached rows, rebuilt only when a flag moved.</returns>
    internal IReadOnlyList<ViewerPhysicsCapabilitySupport> GetCapabilities(
        UsdPhysicsCapability features)
    {
        CapabilityCache? cached = Volatile.Read(ref _capabilities);
        if (cached is not null && cached.Features == features)
        {
            return cached.Rows;
        }

        var rows = new ViewerPhysicsCapabilitySupport[Features.Length];
        for (int index = 0; index < Features.Length; index++)
        {
            UsdPhysicsCapability feature = Features[index];
            bool supported = (features & feature) != 0;
            rows[index] = new ViewerPhysicsCapabilitySupport(
                feature.ToString(),
                supported,
                MapDomain(feature),
                supported
                    ? $"The built world simulates {feature}."
                    : $"The built world does not provide {feature}.");
        }

        Volatile.Write(ref _capabilities, new CapabilityCache(features, rows));
        return rows;
    }

    /// <summary>Gets the diagnostic rows for one retained diagnostic set.</summary>
    /// <param name="diagnostics">The diagnostics the most recent operation produced.</param>
    /// <returns>The cached rows, rebuilt only when an entry changed.</returns>
    internal IReadOnlyList<ViewerPhysicsDiagnosticRow> GetDiagnostics(
        UsdPhysicsDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        DiagnosticCache? cached = Volatile.Read(ref _diagnostics);
        if (cached is not null && ReferenceEquals(cached.Source, diagnostics))
        {
            return cached.Rows;
        }

        if (cached is not null && cached.Source.Equals(diagnostics))
        {
            // A rebuilt set carrying the same entries must not churn the inspector, so the rows are
            // kept and only the identity the next read compares against is refreshed. Refreshing it
            // in place keeps this path allocation free for a transport that rebuilds every read.
            cached.Source = diagnostics;
            return cached.Rows;
        }

        IReadOnlyList<UsdPhysicsDiagnostic> entries = diagnostics.Entries;
        var rows = new ViewerPhysicsDiagnosticRow[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            UsdPhysicsDiagnostic entry = entries[index];
            rows[index] = new ViewerPhysicsDiagnosticRow(
                entry.Severity.ToString(),
                entry.Category.ToString(),
                entry.Code,
                entry.Message);
        }

        Volatile.Write(ref _diagnostics, new DiagnosticCache(diagnostics, rows));
        return rows;
    }

    private static PhysicsRenderDomain? MapDomain(UsdPhysicsCapability feature) => feature switch
    {
        UsdPhysicsCapability.RigidBodies => PhysicsRenderDomain.RigidBody,
        UsdPhysicsCapability.Articulations => PhysicsRenderDomain.Articulation,
        UsdPhysicsCapability.Controllers => PhysicsRenderDomain.Controller,
        UsdPhysicsCapability.Vehicles => PhysicsRenderDomain.Vehicle,
        UsdPhysicsCapability.Particles => PhysicsRenderDomain.Particles,
        UsdPhysicsCapability.Cloth => PhysicsRenderDomain.Cloth,
        UsdPhysicsCapability.Deformables => PhysicsRenderDomain.Deformable,
        _ => null,
    };

    private static UsdPhysicsCapability[] BuildFeatures()
    {
        UsdPhysicsCapability[] declared = Enum.GetValues<UsdPhysicsCapability>();
        int count = 0;
        foreach (UsdPhysicsCapability feature in declared)
        {
            if (IsSingleCapability(feature))
            {
                count++;
            }
        }

        var features = new UsdPhysicsCapability[count];
        int index = 0;
        foreach (UsdPhysicsCapability feature in declared)
        {
            if (IsSingleCapability(feature))
            {
                features[index++] = feature;
            }
        }

        return features;
    }

    private static bool IsSingleCapability(UsdPhysicsCapability feature)
    {
        uint value = (uint)feature;
        return value != 0 && (value & (value - 1)) == 0;
    }

    /// <summary>Holds the capability rows built from one set of capability flags.</summary>
    private sealed class CapabilityCache(
        UsdPhysicsCapability features,
        ViewerPhysicsCapabilitySupport[] rows)
    {
        internal UsdPhysicsCapability Features { get; } = features;

        internal ViewerPhysicsCapabilitySupport[] Rows { get; } = rows;
    }

    /// <summary>Holds the diagnostic rows built from one retained diagnostic set.</summary>
    private sealed class DiagnosticCache(
        UsdPhysicsDiagnostics source,
        ViewerPhysicsDiagnosticRow[] rows)
    {
        private UsdPhysicsDiagnostics _source = source;

        /// <summary>Gets or sets the set the rows were built from, or one equal to it.</summary>
        internal UsdPhysicsDiagnostics Source
        {
            get => Volatile.Read(ref _source);
            set => Volatile.Write(ref _source, value);
        }

        internal ViewerPhysicsDiagnosticRow[] Rows { get; } = rows;
    }
}
