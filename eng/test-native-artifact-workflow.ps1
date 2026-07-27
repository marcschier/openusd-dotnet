#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$nativeWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/native.yml') -Raw
$packageWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/package.yml') -Raw
$renderWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/render.yml') -Raw
$releaseWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/release.yml') -Raw

function Assert-Contains
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if (-not $Value.Contains($Expected, [StringComparison]::Ordinal))
    {
        throw "$Context does not contain '$Expected'."
    }
}

foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64'))
{
    Assert-Contains $nativeWorkflow "rid: $rid" 'Native artifact workflow'
    Assert-Contains `
        $nativeWorkflow `
        'name: openusd-native-${{ matrix.rid }}' `
        'Native artifact workflow'
}
foreach ($required in @(
    'runs-on: ${{ matrix.runner }}',
    './eng/test-render-native-archive.ps1',
    './eng/native-install-metadata.ps1',
    './eng/run-native-probe.ps1',
    './eng/run-silk-probe.ps1',
    './eng/create-native-archive.ps1',
    'retention-days: 30'))
{
    Assert-Contains $nativeWorkflow $required 'Native artifact workflow'
}

foreach ($workflow in @($packageWorkflow, $renderWorkflow))
{
    Assert-Contains $workflow 'native-pipeline-run-id:' 'Native archive consumer workflow'
    Assert-Contains $workflow 'actions: read' 'Native archive consumer workflow'
    Assert-Contains `
        $workflow `
        'actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093' `
        'Native archive consumer workflow'
    Assert-Contains `
        $workflow `
        './eng/prepare-workflow-native-input.ps1' `
        'Native archive consumer workflow'
}
foreach ($workflow in @(
    $nativeWorkflow,
    $packageWorkflow,
    $renderWorkflow,
    (Get-Content (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw),
    (Get-Content (Join-Path $repoRoot '.github/workflows/shaders.yml') -Raw)))
{
    Assert-Contains $workflow 'workflow_call:' 'Reusable release workflow'
}
foreach ($calledWorkflow in @(
    'ci.yml',
    'shaders.yml',
    'native.yml',
    'package.yml',
    'render.yml'))
{
    Assert-Contains `
        $releaseWorkflow `
        "uses: ./.github/workflows/$calledWorkflow" `
        'Release gate workflow'
}
# The release gate still consumes the native archives produced by the native job
# of the same run, but does it in the publish job, which is the one that produces
# what is actually released. The packages and render gates build their own native
# inputs because archive consumption fails on macOS and Linux, so binding the run
# id there would not have been honoured anyway.
Assert-Contains `
    $releaseWorkflow `
    'run-id: ${{ github.run_id }}' `
    'Release gate workflow'
foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64'))
{
    Assert-Contains `
        $releaseWorkflow `
        "name: openusd-native-$rid" `
        'Release gate workflow'
}

foreach ($script in @(
    'create-native-archive.ps1',
    'prepare-workflow-native-input.ps1'))
{
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot $script),
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -ne 0)
    {
        throw "$script has syntax errors: $($parseErrors -join [Environment]::NewLine)"
    }
}

Write-Output 'Native artifact workflow source contract passed.'
