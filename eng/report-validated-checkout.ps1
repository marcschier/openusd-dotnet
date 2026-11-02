#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label
)

$ErrorActionPreference = 'Stop'
$validated = (git rev-parse HEAD).Trim()
$branch = $env:VALIDATED_BRANCH
$isPullRequest = $env:GITHUB_EVENT_NAME -eq 'pull_request'
$headRepository = $env:VALIDATED_PULL_REQUEST_REPOSITORY
$branchHead = $null
$branchDescription = 'fetched branch head'
if (-not $isPullRequest -or
    (-not [string]::IsNullOrWhiteSpace($headRepository) -and
        $headRepository -ieq $env:GITHUB_REPOSITORY))
{
    $branchHead = git rev-parse --verify --quiet "refs/remotes/origin/$branch"
}
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$branchHead"))
{
    $branchHead = $env:CHECKOUT_FALLBACK_SHA
    $branchDescription = 'triggering branch revision'
}
$global:LASTEXITCODE = 0
$branchHead = "$branchHead".Trim()
$eventHead = "$($env:CHECKOUT_FALLBACK_SHA)".Trim()
$comparison = $validated
$validatedDescription = "commit $validated"
if ($isPullRequest -and $eventHead -and $validated -ne $eventHead)
{
    $secondParent = git rev-parse --verify --quiet "$validated^2"
    if ($LASTEXITCODE -eq 0 -and $secondParent -and ("$secondParent").Trim() -ieq $eventHead)
    {
        $comparison = $eventHead
        $validatedDescription = "pull request merge commit $validated (head $comparison)"
    }
    $global:LASTEXITCODE = 0
}

Write-Host (
    "::notice::WORKFLOW_CHECKOUT_VALIDATION: $Label is validating $validatedDescription " +
    "from '$branch'; $branchDescription is $branchHead.")
if ($comparison -ne $branchHead)
{
    Write-Host (
        "::warning::WORKFLOW_CHECKOUT_STALE: $Label is validating $validatedDescription, " +
        "not '$branch' $branchDescription $branchHead. For workflow_run, " +
        "this is the native artifact pipeline commit and can lag behind the branch head.")
}
