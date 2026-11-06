// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

public sealed partial class McpSessionWorkspace
{
    /// <summary>Builds a fresh bounded physics world against the exact session revision.</summary>
    public ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        WorkspaceSessionRevision revision, double expectedMetersPerUnit, string expectedUpAxis,
        CancellationToken cancellationToken = default) =>
        PreparePhysicsRecordingAsync(revision, expectedMetersPerUnit, expectedUpAxis, false, cancellationToken);

    /// <summary>Builds a fresh physics world using the explicitly selected scene-capacity profile.</summary>
    public async ValueTask<WorkspacePhysicsRecording> PreparePhysicsRecordingAsync(
        WorkspaceSessionRevision revision, double expectedMetersPerUnit, string expectedUpAxis, bool fullScenePhysics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken).ConfigureAwait(false);
            if (active.Backend is not IWorkspacePhysicsRecordingBackend backend)
            {
                throw new NotSupportedException("The workspace backend cannot prepare a physics recording.");
            }
            return await backend.PreparePhysicsRecordingAsync(
                revision.StageRevision, expectedMetersPerUnit, expectedUpAxis, fullScenePhysics, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
