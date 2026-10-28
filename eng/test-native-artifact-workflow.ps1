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
    './eng/test-native-download.ps1',
    './eng/test-render-native-archive.ps1',
    './eng/native-install-metadata.ps1',
    './eng/run-native-probe.ps1',
    './eng/run-silk-probe.ps1',
    './eng/create-native-archive.ps1',
    'retention-days: 30'))
{
    Assert-Contains $nativeWorkflow $required 'Native artifact workflow'
}
$ciWorkflow = Get-Content (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
$mdlJob = [regex]::Match(
    $nativeWorkflow, '(?ms)^  mdl-adapter:\r?\n.*?(?=^  [\w-]+:\r?$|\z)').Value
foreach ($required in @(
    'name: ${{ matrix.rid }} standalone MDL adapter',
    'rid: win-x64',
    'rid: linux-x64',
    'rid: osx-arm64',
    'cmake -S native/openusd_mdl -B artifacts/mdl-standalone',
    '--config Release --target mdl_sdk_probe',
    '-R "^mdl_adapter_probe$" --no-tests=error --output-on-failure'))
{
    Assert-Contains $mdlJob $required 'Standalone MDL adapter job'
}
if ($mdlJob -match '(?m)^    needs:' -or $mdlJob.Contains('fetch-native') -or
    $mdlJob.Contains('OPENUSD_MDL_SDK_ROOT'))
{
    throw 'The standalone authored adapter gate must not require a native archive or an MDL SDK.'
}
$mdlSdkJob = [regex]::Match(
    $nativeWorkflow, '(?ms)^  mdl-sdk-adapter:\r?\n.*?(?=^  [\w-]+:\r?$|\z)').Value
foreach ($required in @(
    'name: ${{ matrix.rid }} standalone MDL SDK ${{ matrix.qualification }}',
    'rid: win-x64',
    'rid: linux-x64',
    'rid: osx-arm64',
    'qualification: compile-only',
    'qualification: module tests',
    './eng/fetch-mdl-sdk.ps1 -Rid ${{ matrix.rid }}',
    'python ./eng/fetch_simready_mdl.py --data-root $dataRoot',
    'if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }',
    '-DOPENUSD_MDL_SDK_ROOT=$sdkRoot',
    '-DOPENUSD_MDL_SDK_RUNTIME_DIR=$sdkRoot/runtime',
    '-DOPENUSD_MDL_REAL_MODULE_ROOT=$moduleRoot',
    'cmake --build artifacts/mdl-sdk-standalone --config Release',
    'ctest --test-dir artifacts/mdl-sdk-standalone -C Release',
    '--no-tests=error --output-on-failure'))
{
    Assert-Contains $mdlSdkJob $required 'Standalone MDL SDK adapter job'
}
Assert-Contains $mdlSdkJob @'
      - name: Test standalone MDL SDK adapter and real modules
        if: runner.os == 'macOS'
'@ 'SDK module execution admission'
if ($mdlSdkJob -match '(?m)^    needs:' -or $mdlSdkJob.Contains('fetch-native') -or
    $mdlSdkJob.Contains('upload-artifact'))
{
    throw 'The SDK adapter gate must not require a native archive or publish acquired dependencies.'
}
foreach ($path in @(
    'eng/fetch_simready_mdl.py',
    'eng/simready_warehouse.py',
    'eng/datasets/simready-mdl-core.json',
    'test-assets/mdl/**'))
{
    if ([regex]::Matches($nativeWorkflow, "(?m)^      - '$([regex]::Escape($path))'$").Count -ne 2)
    {
        throw "MDL input '$path' must trigger both native push and pull-request qualification."
    }
}
Assert-Contains $ciWorkflow './eng/test-native-download.ps1' 'Managed CI workflow'
if ([regex]::Matches($nativeWorkflow, "(?m)^      - 'eng/test-native-download\.ps1'$").Count -ne 2)
{
    throw 'Native download contract must trigger both native push and pull-request qualification.'
}

foreach ($workflow in @($packageWorkflow, $renderWorkflow))
{
    Assert-Contains $workflow 'native-pipeline-run-id:' 'Native archive consumer workflow'
    Assert-Contains $workflow 'actions: read' 'Native archive consumer workflow'
    Assert-Contains `
        $workflow `
        'actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333' `
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
# The release gate still consumes the native archives produced by the native job of the
# same run, but does it in the pack jobs, which produce what is actually released. The
# packages and render gates build their own native inputs because archive consumption
# fails on macOS and Linux, so binding the run id there would not have been honoured.
Assert-Contains `
    $releaseWorkflow `
    'run-id: ${{ github.run_id }}' `
    'Release gate workflow'
Assert-Contains `
    $releaseWorkflow `
    'name: openusd-native-${{ matrix.rid }}' `
    'Release gate workflow'
foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64'))
{
    Assert-Contains `
        $releaseWorkflow `
        "rid: $rid" `
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
