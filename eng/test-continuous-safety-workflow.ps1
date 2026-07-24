#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

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

function Assert-DoesNotContain
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Unexpected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value.Contains($Unexpected, [StringComparison]::Ordinal))
    {
        throw "$Context unexpectedly contains '$Unexpected'."
    }
}

$ciWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
$nativeWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/native.yml') -Raw
$performanceWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/performance.yml') -Raw
$releaseWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/release.yml') -Raw
$rhiLinuxWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/rhi-linux-aot.yml') -Raw
$shaderWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/shaders.yml') -Raw
$directoryBuild = Get-Content (
    Join-Path $repoRoot 'Directory.Build.props') -Raw
$performanceRunner = Get-Content (
    Join-Path $repoRoot 'eng/run-performance.ps1') -Raw
$solution = Get-Content (Join-Path $repoRoot 'OpenUsd.slnx') -Raw

Assert-Contains $ciWorkflow 'dotnet-version: 10.0.301' 'Managed CI workflow'
Assert-DoesNotContain $ciWorkflow '10.0.x' 'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    'eng/shaders/scripts/checked_payload.py' `
    'Managed CI workflow'
Assert-Contains $ciWorkflow './eng/test-documentation.ps1' 'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    './eng/run-vulkan-conformance-tests.ps1' `
    'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    "`$_.BaseName -cne 'OpenUsd.Rendering.ConformanceTests'" `
    'Managed CI workflow'
Assert-Contains $ciWorkflow '--managed-safety' 'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    'actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5 # v4' `
    'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    'actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4' `
    'Managed CI workflow'
Assert-Contains `
    $ciWorkflow `
    'actions/setup-python@a26af69be951a213d495a4c3e4e4022e16d87065 # v5' `
    'Managed CI workflow'
Assert-DoesNotContain $ciWorkflow 'actions/checkout@v4' 'Managed CI workflow'
Assert-DoesNotContain $ciWorkflow 'actions/setup-dotnet@v4' 'Managed CI workflow'
Assert-DoesNotContain $ciWorkflow 'actions/setup-python@v5' 'Managed CI workflow'
Assert-Contains `
    $directoryBuild `
    'Microsoft.CodeAnalysis.PublicApiAnalyzers' `
    'Public API analyzer configuration'
Assert-Contains `
    $directoryBuild `
    'PublicAPI.Shipped.txt' `
    'Public API analyzer configuration'
Assert-Contains `
    $nativeWorkflow `
    './eng/run-native-fuzz.ps1 -SelfTest' `
    'Native fuzz workflow'
Assert-Contains `
    $nativeWorkflow `
    './eng/run-native-fuzz.ps1 -MaxTotalTime 60' `
    'Native fuzz workflow'
Assert-Contains $performanceWorkflow 'workflow_call:' 'Performance workflow'
Assert-DoesNotContain $performanceWorkflow 'github.event_name' 'Performance workflow'
Assert-Contains `
    $performanceWorkflow `
    './eng/run-performance.ps1' `
    'Performance workflow'
Assert-Contains `
    $performanceRunner `
    "[StringComparison]::OrdinalIgnoreCase" `
    'Performance runner'
Assert-Contains `
    $performanceRunner `
    'Deterministic safety run $run failed' `
    'Performance runner'
Assert-Contains `
    $performanceRunner `
    '*PackedStringBenchmarks.PackUtf8Strings*' `
    'Performance runner'
Assert-Contains `
    $performanceRunner `
    '*SilkCommandBenchmarks.ParseCommandPage*' `
    'Performance runner'
Assert-DoesNotContain $performanceRunner '--anyCategories' 'Performance runner'
Assert-Contains `
    $solution `
    'tests/OpenUsd.Performance.Tests/OpenUsd.Performance.Tests.csproj' `
    'Solution'
Assert-Contains `
    $shaderWorkflow `
    "'.github/workflows/performance.yml'" `
    'Shader workflow'
Assert-Contains `
    $releaseWorkflow `
    'uses: ./.github/workflows/performance.yml' `
    'Release gate workflow'
Assert-Contains `
    $releaseWorkflow `
    'needs.performance.result' `
    'Release gate evidence'
Assert-Contains $rhiLinuxWorkflow 'fetch-depth: 0' 'Linux RHI workflow'
Assert-Contains `
    $rhiLinuxWorkflow `
    './eng/run-rhi-probe.ps1' `
    'Linux RHI workflow'
Assert-Contains `
    $rhiLinuxWorkflow `
    'actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5 # v4' `
    'Linux RHI workflow'
Assert-Contains `
    $rhiLinuxWorkflow `
    'actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4' `
    'Linux RHI workflow'
Assert-DoesNotContain `
    $rhiLinuxWorkflow `
    'actions/checkout@v4' `
    'Linux RHI workflow'
Assert-DoesNotContain `
    $rhiLinuxWorkflow `
    'actions/setup-dotnet@v4' `
    'Linux RHI workflow'

foreach ($relativePath in @(
    'eng/prepare-vulkan-test-runtime.ps1',
    'eng/run-rhi-probe.ps1',
    'eng/run-native-fuzz.ps1',
    'eng/run-performance.ps1',
    'eng/run-vulkan-conformance-tests.ps1',
    'eng/test-vulkan-test-runtime.ps1',
    'eng/test-continuous-safety-workflow.ps1'))
{
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repoRoot $relativePath),
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -ne 0)
    {
        throw "$relativePath has syntax errors: $($parseErrors -join [Environment]::NewLine)"
    }
}

$performanceTests = Get-ChildItem (
    Join-Path $repoRoot 'tests/OpenUsd.Performance.Tests') -Filter '*.cs' |
    ForEach-Object { Get-Content $_.FullName -Raw } |
    Out-String
Assert-DoesNotContain $performanceTests 'Stopwatch' 'Deterministic performance tests'
Assert-DoesNotContain $performanceTests '.Elapsed' 'Deterministic performance tests'

Write-Output 'Continuous safety workflow source contract passed.'
