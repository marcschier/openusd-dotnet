// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies a platform for pure backend selection.
/// </summary>
public enum RenderPlatform
{
    /// <summary>An unsupported or unknown platform.</summary>
    Unknown,

    /// <summary>Microsoft Windows.</summary>
    Windows,

    /// <summary>Linux.</summary>
    Linux,

    /// <summary>Apple macOS.</summary>
    MacOS
}

/// <summary>
/// Identifies automatic or explicit backend selection.
/// </summary>
public enum RenderBackendSelectionMode
{
    /// <summary>Use platform policy and failover ordering.</summary>
    Automatic,

    /// <summary>Use only the explicitly requested backend.</summary>
    Manual
}

/// <summary>
/// Identifies why backend selection produced no candidate.
/// </summary>
public enum RenderBackendSelectionFailureKind
{
    /// <summary>Selection produced at least one candidate.</summary>
    None,

    /// <summary>The platform has no defined automatic order.</summary>
    UnsupportedPlatform,

    /// <summary>The requested backend is unsupported on the platform.</summary>
    RequestedBackendUnsupported,

    /// <summary>The requested backend is unavailable or already failed initialization.</summary>
    RequestedBackendUnavailable,

    /// <summary>No automatic candidate remains available.</summary>
    NoBackendAvailable
}

/// <summary>
/// Describes a pure backend selection request.
/// </summary>
/// <param name="Platform">The target platform.</param>
/// <param name="RequestedBackend">The explicit backend, or null for automatic selection.</param>
public readonly record struct RenderBackendSelectionRequest(
    RenderPlatform Platform,
    RenderBackendKind? RequestedBackend)
{
    /// <summary>Gets the selection mode.</summary>
    public RenderBackendSelectionMode Mode =>
        RequestedBackend.HasValue
            ? RenderBackendSelectionMode.Manual
            : RenderBackendSelectionMode.Automatic;
}

/// <summary>
/// Contains immutable ordered backend candidates.
/// </summary>
public sealed class RenderBackendSelectionResult : IEquatable<RenderBackendSelectionResult>
{
    private readonly ImmutableArray<RenderBackendKind> _candidates;

    internal RenderBackendSelectionResult(
        RenderBackendSelectionMode mode,
        RenderBackendSelectionFailureKind failure,
        IEnumerable<RenderBackendKind> candidates)
    {
        Mode = mode;
        Failure = failure;
        _candidates = [.. candidates];
    }

    /// <summary>Gets the selection mode.</summary>
    public RenderBackendSelectionMode Mode { get; }

    /// <summary>Gets the selection failure category.</summary>
    public RenderBackendSelectionFailureKind Failure { get; }

    /// <summary>Gets ordered backend candidates.</summary>
    public IReadOnlyList<RenderBackendKind> Candidates => _candidates;

    /// <summary>Gets a value indicating whether a candidate is available.</summary>
    public bool IsSuccess => _candidates.Length != 0;

    /// <inheritdoc />
    public bool Equals(RenderBackendSelectionResult? other) =>
        other is not null
        && Mode == other.Mode
        && Failure == other.Failure
        && _candidates.AsSpan().SequenceEqual(other._candidates.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is RenderBackendSelectionResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(Failure);
        foreach (RenderBackendKind candidate in _candidates)
        {
            hash.Add(candidate);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Provides deterministic renderer-neutral backend ordering.
/// </summary>
public static class RenderBackendSelectionPolicy
{
    /// <summary>
    /// Selects ordered candidates after removing unavailable and failed backends.
    /// </summary>
    public static RenderBackendSelectionResult Select(
        RenderBackendSelectionRequest request,
        IEnumerable<RenderBackendKind> availableBackends,
        IEnumerable<RenderBackendKind>? failedInitializations = null)
    {
        ArgumentNullException.ThrowIfNull(availableBackends);

        var available = new HashSet<RenderBackendKind>(availableBackends);
        HashSet<RenderBackendKind> failed = failedInitializations is null
            ? []
            : new HashSet<RenderBackendKind>(failedInitializations);

        if (request.Platform == RenderPlatform.Unknown)
        {
            return Failure(
                request.Mode,
                RenderBackendSelectionFailureKind.UnsupportedPlatform);
        }

        if (request.RequestedBackend is { } requested)
        {
            if (!IsSupported(request.Platform, requested))
            {
                return Failure(
                    RenderBackendSelectionMode.Manual,
                    RenderBackendSelectionFailureKind.RequestedBackendUnsupported);
            }
            if (!available.Contains(requested) || failed.Contains(requested))
            {
                return Failure(
                    RenderBackendSelectionMode.Manual,
                    RenderBackendSelectionFailureKind.RequestedBackendUnavailable);
            }
            return new RenderBackendSelectionResult(
                RenderBackendSelectionMode.Manual,
                RenderBackendSelectionFailureKind.None,
                [requested]);
        }

        RenderBackendKind[] order = request.Platform switch
        {
            RenderPlatform.Windows =>
            [
                RenderBackendKind.Storm,
                RenderBackendKind.D3D12,
                RenderBackendKind.Vulkan
            ],
            RenderPlatform.Linux => [RenderBackendKind.Storm, RenderBackendKind.Vulkan],
            RenderPlatform.MacOS => [RenderBackendKind.Storm, RenderBackendKind.Metal],
            _ => []
        };
        RenderBackendKind[] candidates =
            [.. order.Where(candidate => available.Contains(candidate) && !failed.Contains(candidate))];
        return candidates.Length == 0
            ? Failure(
                RenderBackendSelectionMode.Automatic,
                RenderBackendSelectionFailureKind.NoBackendAvailable)
            : new RenderBackendSelectionResult(
                RenderBackendSelectionMode.Automatic,
                RenderBackendSelectionFailureKind.None,
                candidates);
    }

    private static bool IsSupported(RenderPlatform platform, RenderBackendKind backend) =>
        backend == RenderBackendKind.Storm
        || (platform == RenderPlatform.Windows && backend == RenderBackendKind.D3D12)
        || ((platform == RenderPlatform.Windows || platform == RenderPlatform.Linux)
            && backend == RenderBackendKind.Vulkan)
        || (platform == RenderPlatform.MacOS && backend == RenderBackendKind.Metal);

    private static RenderBackendSelectionResult Failure(
        RenderBackendSelectionMode mode,
        RenderBackendSelectionFailureKind failure) =>
        new(mode, failure, []);
}
