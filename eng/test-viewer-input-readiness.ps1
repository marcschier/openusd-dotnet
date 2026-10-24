#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

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

Write-Output 'Viewer input readiness tests passed: 9 cases.'
