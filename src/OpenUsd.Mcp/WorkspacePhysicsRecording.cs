// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

/// <summary>Opt-in preparation of a fresh, bounded physics world at an exact workspace revision.</summary>
public interface IWorkspacePhysicsRecordingBackend
{
    /// <summary>Extracts and builds a supported world without changing the authored stage.</summary>
    ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        ulong expectedStageRevision, double expectedMetersPerUnit, string expectedUpAxis, CancellationToken cancellationToken);

    /// <summary>Optionally admits a complete rigid scene without weakening the default bounded profile.</summary>
    ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        ulong expectedStageRevision, double expectedMetersPerUnit, string expectedUpAxis,
        bool fullScenePhysics, CancellationToken cancellationToken) =>
        fullScenePhysics
            ? throw new NotSupportedException("This workspace backend does not expose the full-scene physics profile.")
            : PreparePhysicsRecordingAsync(expectedStageRevision, expectedMetersPerUnit, expectedUpAxis, cancellationToken);
}

/// <summary>Owns one prepared recording world; the caller must dispose it after capture drains.</summary>
public sealed class WorkspacePhysicsRecording : IAsyncDisposable
{
    private int _disposed;

    /// <summary>Creates the owning recording handle with detached render bindings.</summary>
    public WorkspacePhysicsRecording(
        UsdPhysicsTransport transport, PhysicsRenderBindingTable bindings,
        PhysicsRenderCapacities capacities, ulong sourceRevision)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(bindings);
        Transport = transport;
        Bindings = bindings;
        Capacities = capacities;
        SourceRevision = sourceRevision;
    }

    /// <summary>Gets the exclusively owned transport.</summary>
    public UsdPhysicsTransport Transport { get; }
    /// <summary>Gets bindings that remain unchanged throughout recording.</summary>
    public PhysicsRenderBindingTable Bindings { get; }
    /// <summary>Gets complete renderer-neutral projection capacities.</summary>
    public PhysicsRenderCapacities Capacities { get; }
    /// <summary>Gets the pinned native stage change serial.</summary>
    public ulong SourceRevision { get; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await Transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
