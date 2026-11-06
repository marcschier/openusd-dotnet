#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param([switch]$SkipProcessExitCheck)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'viewer-input-readiness.ps1')

function New-ReadinessFixture
{
    $source = 'a' * 40
    $run = @{ id = 101; head_sha = $source; repository = @{ id = 102 }; head_repository = @{ id = 103 } }
    $jobs = @()
    $artifacts = @()
    $index = 0
    foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64'))
    {
        $index++
        $steps = @(
            @{ name = 'Pack the runtime packages for this RID'; conclusion = 'success' }
            @{ name = 'Upload packed Viewer inputs'; conclusion = 'success' }
            @{ name = 'Report a deferred smoke when the native tree is not published yet'; conclusion = 'skipped' }
        )
        if ($rid -eq 'linux-x64')
        {
            $steps += @{ name = 'Pack the platform-neutral packages'; conclusion = 'success' }
        }
        elseif ($rid -eq 'osx-arm64')
        {
            $steps += @{ name = 'Pack the Metal package'; conclusion = 'success' }
        }
        $jobs += @{
            id = 200 + $index; run_id = $run.id; head_sha = $source
            name = "pack Viewer inputs $rid"; status = 'completed'; conclusion = 'success'; steps = $steps
        }
        $artifacts += @{
            id = 300 + $index; name = "openusd-viewer-input-nupkg-$rid"; size_in_bytes = 123
            expired = $false; digest = 'sha256:' + ('b' * 64)
            workflow_run = @{
                id = $run.id; head_sha = $source
                repository_id = $run.repository.id; head_repository_id = $run.head_repository.id
            }
        }
    }
    @{
        WorkflowRun = [pscustomobject]$run
        JobsResponse = [pscustomobject]@{ total_count = 3; jobs = $jobs }
        ArtifactsResponse = [pscustomobject]@{ total_count = 3; artifacts = $artifacts }
    } | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable
}

function Assert-Rejected
{
    param([string]$Name, [hashtable]$Fixture, [string[]]$Expected)
    $message = $null
    try { Get-ViewerInputReadiness @Fixture | Out-Null }
    catch { $message = $_.Exception.Message }
    if ($null -eq $message) { throw "$Name accepted invalid evidence." }
    foreach ($text in $Expected)
    {
        if (!$message.Contains($text, [StringComparison]::Ordinal))
        {
            throw "$Name did not report '$text': $message"
        }
    }
    Write-Output "PASS $Name"
}

$fixture = New-ReadinessFixture
$ready = Get-ViewerInputReadiness @fixture
if (!$ready.ready -or ($ready.readyRids -join ',') -cne 'win-x64,linux-x64,osx-arm64' -or
    $ready.explicitlyDeferredRids.Count -ne 0 -or $ready.runId -ne 101 -or $ready.source -cne ('a' * 40) -or
    ($ready.artifacts.id -join ',') -cne '301,302,303')
{
    throw 'CompleteSameSourceInputsAreReady did not require all three RIDs.'
}
Write-Output 'PASS CompleteSameSourceInputsAreReady'

$fixture = New-ReadinessFixture
$fixture.ArtifactsResponse.artifacts = @()
$fixture.ArtifactsResponse.total_count = 0
foreach ($job in $fixture.JobsResponse.jobs)
{
    foreach ($step in $job.steps)
    {
        $step.conclusion = if ($step.name.StartsWith('Report a deferred')) { 'success' } else { 'skipped' }
    }
}
$deferred = Get-ViewerInputReadiness @fixture
if ($deferred.ready -or $deferred.readyRids.Count -ne 0 -or
    ($deferred.explicitlyDeferredRids -join ',') -cne 'win-x64,linux-x64,osx-arm64' -or
    $deferred.artifacts.Count -ne 0)
{
    throw 'ExplicitSuccessfulDeferralsAreNotReady accepted absent packages.'
}
Write-Output 'PASS ExplicitSuccessfulDeferralsAreNotReady'

foreach ($property in @('run_id', 'head_sha', 'status', 'conclusion'))
{
    $fixture = New-ReadinessFixture
    $actual = switch ($property)
    {
        'run_id' { 999 }
        'head_sha' { 'c' * 40 }
        'status' { 'in_progress' }
        'conclusion' { 'failure' }
    }
    $fixture.JobsResponse.jobs[0][$property] = $actual
    $encoded = ConvertTo-Json -InputObject $actual -Compress
    Assert-Rejected "RejectedJobReportsObservedIdentity_$property" $fixture @(
        'Expected run=101', ('source=' + ('a' * 40)), 'observed=', "`"$property`":$encoded"
    )
}

$fixture = New-ReadinessFixture
$fixture.JobsResponse.jobs[0].steps[0].conclusion = 'failure'
Assert-Rejected 'SuccessfulJobCannotHideFailedStep' $fixture @('actual win-x64 Viewer pack job')

$fixture = New-ReadinessFixture
$fixture.ArtifactsResponse.artifacts[0].workflow_run.head_sha = 'c' * 40
Assert-Rejected 'ArtifactMustMatchExactSource' $fixture @('different run/source')

$fixture = New-ReadinessFixture
$fixture.JobsResponse.total_count = 4
Assert-Rejected 'IncompleteMetadataIsRejected' $fixture @('metadata is incomplete')

$script:currentFixture = New-ReadinessFixture
$script:queuedFixture = New-ReadinessFixture
$script:queuedFixture.JobsResponse.jobs[0].status = 'queued'
$script:queuedFixture.JobsResponse.jobs[0].conclusion = $null
$script:missingArtifactFixture = New-ReadinessFixture
$script:missingArtifactFixture.ArtifactsResponse.artifacts = @(
    $script:missingArtifactFixture.ArtifactsResponse.artifacts | Where-Object name -CNE 'openusd-viewer-input-nupkg-win-x64'
)
$script:missingArtifactFixture.ArtifactsResponse.total_count = 2
$script:failedFixture = New-ReadinessFixture
$script:failedFixture.JobsResponse.jobs[0].conclusion = 'failure'
$script:jobRoutes = @{}
$script:artifactRoutes = @{}
$script:jobFetches = 0
$script:artifactFetches = 0
$script:mode = 'stale-jobs'
function gh
{
    $route = [string]$args[-1]
    if ($args[0] -cne 'api' -or $args -cnotcontains '-H' -or $args -cnotcontains 'Cache-Control: no-cache')
    {
        throw 'Viewer metadata requests must explicitly revalidate GitHub API responses.'
    }
    $global:LASTEXITCODE = 0
    if ($route -match '/jobs\?')
    {
        $script:jobFetches++
        if ($script:mode -eq 'job-error')
        {
            $global:LASTEXITCODE = 1
            return '{}'
        }
        if ($script:mode -eq 'always-stale')
        {
            return $script:queuedFixture.JobsResponse | ConvertTo-Json -Depth 20 -Compress
        }
        if ($script:mode -eq 'failed')
        {
            return $script:failedFixture.JobsResponse | ConvertTo-Json -Depth 20 -Compress
        }
        if ($script:mode -ne 'stale-jobs')
        {
            return $script:currentFixture.JobsResponse | ConvertTo-Json -Depth 20 -Compress
        }
        if (!$script:jobRoutes.ContainsKey($route))
        {
            $script:jobRoutes[$route] = if ($script:jobRoutes.Count -eq 0)
            {
                $script:queuedFixture.JobsResponse
            }
            else
            {
                $script:currentFixture.JobsResponse
            }
        }
        return $script:jobRoutes[$route] | ConvertTo-Json -Depth 20 -Compress
    }
    if ($route -match '/artifacts\?')
    {
        $script:artifactFetches++
        if ($script:mode -eq 'artifact-error')
        {
            $global:LASTEXITCODE = 1
            return '{}'
        }
        if ($script:mode -eq 'never-published')
        {
            return $script:missingArtifactFixture.ArtifactsResponse | ConvertTo-Json -Depth 20 -Compress
        }
        if ($script:mode -eq 'missing-artifact')
        {
            if (!$script:artifactRoutes.ContainsKey($route))
            {
                $script:artifactRoutes[$route] = if ($script:artifactRoutes.Count -eq 0)
                {
                    $script:missingArtifactFixture.ArtifactsResponse
                }
                else
                {
                    $script:currentFixture.ArtifactsResponse
                }
            }
            return $script:artifactRoutes[$route] | ConvertTo-Json -Depth 20 -Compress
        }
        return $script:currentFixture.ArtifactsResponse | ConvertTo-Json -Depth 20 -Compress
    }
    throw "Unexpected GitHub API route: $route"
}
function Start-Sleep { param([int]$Seconds) }
function Reset-MetadataMock
{
    param([string]$Mode)
    $script:mode = $Mode
    $script:jobRoutes = @{}
    $script:artifactRoutes = @{}
    $script:jobFetches = 0
    $script:artifactFetches = 0
}

$metadata = Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
    -MaxAttempts 3 -RetryDelaySeconds 0
$result = Get-ViewerInputReadiness -WorkflowRun $script:currentFixture.WorkflowRun `
    -JobsResponse $metadata.jobs -ArtifactsResponse $metadata.artifacts
if (!$result.ready -or $script:jobFetches -ne 2)
{
    throw 'FreshJobSnapshotsAreRetried did not recover from stale queued job evidence.'
}
Write-Output 'PASS FreshJobSnapshotsAreRetried'

Reset-MetadataMock 'always-stale'
$metadata = Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
    -MaxAttempts 3 -RetryDelaySeconds 0
if ($script:jobFetches -ne 3) { throw 'Persistently stale jobs did not exhaust the bounded retry.' }
Assert-Rejected 'PersistentlyStalePackEvidenceIsRejected' @{
    WorkflowRun = $script:currentFixture.WorkflowRun; JobsResponse = $metadata.jobs; ArtifactsResponse = $metadata.artifacts
} @('"status":"queued"')

Reset-MetadataMock 'missing-artifact'
$metadata = Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
    -MaxAttempts 3 -RetryDelaySeconds 0
$result = Get-ViewerInputReadiness -WorkflowRun $script:currentFixture.WorkflowRun `
    -JobsResponse $metadata.jobs -ArtifactsResponse $metadata.artifacts
if (!$result.ready -or $script:jobFetches -ne 2 -or $script:artifactFetches -ne 2)
{
    throw 'UploadedArtifactMetadataIsRetried did not wait for the missing same-run artifact.'
}
Write-Output 'PASS UploadedArtifactMetadataIsRetried'

Reset-MetadataMock 'never-published'
$metadata = Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
    -MaxAttempts 3 -RetryDelaySeconds 0
if ($script:jobFetches -ne 3 -or $script:artifactFetches -ne 3)
{
    throw 'A persistently missing uploaded artifact did not exhaust the bounded retry.'
}
Assert-Rejected 'NeverPublishedUploadedArtifactIsRejected' @{
    WorkflowRun = $script:currentFixture.WorkflowRun; JobsResponse = $metadata.jobs; ArtifactsResponse = $metadata.artifacts
} @('Viewer input availability disagrees with the actual pack step: win-x64')

Reset-MetadataMock 'failed'
$metadata = Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
    -MaxAttempts 3 -RetryDelaySeconds 0
if ($script:jobFetches -ne 1) { throw 'A completed failed pack job was retried.' }
Assert-Rejected 'FailedPackEvidenceIsRejectedImmediately' @{
    WorkflowRun = $script:currentFixture.WorkflowRun; JobsResponse = $metadata.jobs; ArtifactsResponse = $metadata.artifacts
} @('"conclusion":"failure"')

foreach ($mode in @('job-error', 'artifact-error'))
{
    Reset-MetadataMock $mode
    $previousExitCode = $global:LASTEXITCODE
    $message = $null
    try
    {
        Get-ViewerInputMetadata -RunApi 'repos/example/project/actions/runs/101' `
            -MaxAttempts 3 -RetryDelaySeconds 0 | Out-Null
    }
    catch { $message = $_.Exception.Message }
    finally { $global:LASTEXITCODE = $previousExitCode }
    $expected = if ($mode -eq 'job-error') { 'pack-job evidence' } else { 'input-artifact evidence' }
    if ($null -eq $message -or !$message.Contains($expected, [StringComparison]::Ordinal))
    {
        throw "$mode did not report the failed metadata request: $message"
    }
    Write-Output "PASS MetadataApiFailure_$mode"
}

if (!$SkipProcessExitCheck)
{
    $scriptPath = $PSCommandPath.Replace("'", "''")
    $command = "`$ErrorActionPreference = 'Stop'; & '$scriptPath' -SkipProcessExitCheck; " +
        'if (Test-Path variable:\LASTEXITCODE) { exit $LASTEXITCODE }'
    $output = & ([Environment]::ProcessPath) -NoLogo -NoProfile -NonInteractive -Command $command 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        throw "The GitHub Actions PowerShell wrapper exited $exitCode after the readiness tests.`n$($output -join "`n")"
    }
    Write-Output 'PASS GitHubActionsRunnerExitCode'
}

Write-Output 'Viewer input readiness tests passed.'
