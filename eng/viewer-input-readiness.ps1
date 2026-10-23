# Copyright (c) marcschier. Licensed under the MIT License.

function Get-ViewerInputReadiness
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$WorkflowRun,
        [Parameter(Mandatory = $true)][object]$JobsResponse,
        [Parameter(Mandatory = $true)][object]$ArtifactsResponse
    )

    Set-StrictMode -Version Latest
    $rids = @('win-x64', 'linux-x64', 'osx-arm64')
    $prefix = 'openusd-viewer-input-nupkg-'
    if ($WorkflowRun.id -le 0 -or $WorkflowRun.repository.id -le 0 -or
        $WorkflowRun.head_repository.id -le 0 -or $WorkflowRun.head_sha -cnotmatch '^[0-9a-f]{40}$')
    {
        throw 'Viewer input readiness requires a valid exact workflow-run/source identity.'
    }
    $jobs = @($JobsResponse.jobs)
    $artifacts = @($ArtifactsResponse.artifacts)
    if ($JobsResponse.total_count -ne $jobs.Count -or $ArtifactsResponse.total_count -ne $artifacts.Count)
    {
        throw 'Viewer input metadata is incomplete; retrieve every API page before deciding readiness.'
    }
    $packJobs = @($jobs | Where-Object {
        $_.name.StartsWith('pack Viewer inputs ', [StringComparison]::Ordinal)
    })
    if ($packJobs.Count -ne 3 -or @($packJobs.id | Sort-Object -Unique).Count -ne 3)
    {
        throw 'Exactly three distinct supported-RID pack jobs must complete before Viewer readiness.'
    }
    $inputs = @($artifacts | Where-Object {
        $_.name.StartsWith($prefix, [StringComparison]::Ordinal)
    })
    foreach ($artifact in $inputs)
    {
        $rid = $artifact.name.Substring($prefix.Length)
        if ($rid -cnotin $rids -or $artifact.id -le 0 -or $artifact.size_in_bytes -le 0 -or
            $artifact.expired -isnot [bool] -or $artifact.expired -or
            $artifact.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
            $artifact.workflow_run.id -ne $WorkflowRun.id -or
            $artifact.workflow_run.head_sha -cne $WorkflowRun.head_sha -or
            $artifact.workflow_run.repository_id -ne $WorkflowRun.repository.id -or
            $artifact.workflow_run.head_repository_id -ne $WorkflowRun.head_repository.id)
        {
            throw "A Viewer input artifact is invalid, expired or bound to a different run/source: $($artifact.name)"
        }
    }
    if (@($inputs | Select-Object -ExpandProperty id | Sort-Object -Unique).Count -ne $inputs.Count)
    {
        throw 'Viewer input artifact identities are duplicated.'
    }
    $readyRids = @()
    $deferredRids = @()
    foreach ($rid in $rids)
    {
        $job = @($packJobs | Where-Object name -CEQ "pack Viewer inputs $rid")
        if ($job.Count -ne 1 -or $job[0].id -le 0 -or $job[0].run_id -ne $WorkflowRun.id -or
            $job[0].head_sha -cne $WorkflowRun.head_sha -or $job[0].status -cne 'completed' -or
            $job[0].conclusion -cne 'success' -or
            @($job[0].steps | Where-Object conclusion -CEQ 'failure').Count -ne 0)
        {
            $observed = @($job | Select-Object id, run_id, head_sha, status, conclusion) |
                ConvertTo-Json -Depth 3 -Compress
            throw ("The actual $rid Viewer pack job failed, was cancelled or has mismatched evidence. " +
                "Expected run=$($WorkflowRun.id), source=$($WorkflowRun.head_sha), completed/success; " +
                "observed=$observed. Inspect the retained readiness metadata for step conclusions.")
        }
        $members = @($inputs | Where-Object name -CEQ "$prefix$rid")
        if ($members.Count -gt 1) { throw "Viewer inputs contain duplicate artifacts for $rid." }
        $expected = if ($members.Count -eq 1) { 'success' } else { 'skipped' }
        $deferred = if ($members.Count -eq 1) { 'skipped' } else { 'success' }
        foreach ($stepName in @('Pack the runtime packages for this RID', 'Upload packed Viewer inputs') +
            $(if ($rid -eq 'linux-x64') { @('Pack the platform-neutral packages') }
              elseif ($rid -eq 'osx-arm64') { @('Pack the Metal package') }
              else { @() }))
        {
            if (@($job[0].steps | Where-Object {
                $_.name -ceq $stepName -and $_.conclusion -ceq $expected
            }).Count -ne 1)
            {
                throw "Viewer input availability disagrees with the actual pack step: $rid / $stepName"
            }
        }
        if (@($job[0].steps | Where-Object {
            $_.name -ceq 'Report a deferred smoke when the native tree is not published yet' -and
            $_.conclusion -ceq $deferred
        }).Count -ne 1)
        {
            throw "Missing $rid artifacts do not have explicit successful unavailable-input deferral evidence."
        }
        if ($members.Count -eq 1) { $readyRids += $rid }
        else { $deferredRids += $rid }
    }
    [ordered]@{
        schemaVersion = 1
        status = $(if ($readyRids.Count -eq 3) { 'ready' } else { 'deferred' })
        ready = $readyRids.Count -eq 3
        runId = $WorkflowRun.id
        source = $WorkflowRun.head_sha
        readyRids = $readyRids
        explicitlyDeferredRids = $deferredRids
        artifacts = @($inputs | Select-Object id, name, size_in_bytes, digest)
        actualPackJobs = @($packJobs | Select-Object id, name, conclusion)
    }
}
