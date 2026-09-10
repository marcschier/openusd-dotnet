// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

public sealed partial class McpSessionWorkspace
{
    /// <summary>Prepares an authored product at an exact session/native revision without writing output.</summary>
    public async ValueTask<RenderProductJobPlan> PrepareRenderProductAsync(
        WorkspaceSessionRevision revision, IReadOnlyList<double> timeCodes,
        string? settingsPath = null, string? productPath = null, RenderProductOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(timeCodes);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken).ConfigureAwait(false);
            if (active.Backend is not IWorkspaceRenderProductBackend backend)
            {
                throw new NotSupportedException("The workspace backend cannot prepare authored render products.");
            }
            return await backend.PrepareRenderProductAsync(timeCodes, settingsPath, productPath, overrides,
                revision.StageRevision, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
