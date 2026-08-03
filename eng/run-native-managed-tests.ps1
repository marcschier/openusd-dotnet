#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Project,

    [string[]]$Framework = @('net10.0'),

    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumExpectedTests = 1,

    [string[]]$TestArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$stageRoot = Join-Path $repoRoot "artifacts/native-managed-tests/$Rid"

function Assert-Directory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container))
    {
        throw (
            "$Description was not found at '$Path'. Restore the verified native cache from " +
            '.github/workflows/native.yml or build the fast shim/native install before running native managed tests.')
    }
}

Assert-Directory -Path $openUsdRoot -Description 'OpenUSD native runtime'
Assert-Directory -Path $shimRoot -Description 'OpenUsd native shim'

if (Test-Path -LiteralPath $stageRoot)
{
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

$binTarget = Join-Path $stageRoot 'bin'
$libTarget = Join-Path $stageRoot 'lib'
$pluginPath = Join-Path $stageRoot 'plugin/usd'
$workRoot = Join-Path $stageRoot 'work'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget, $pluginPath, $workRoot | Out-Null

foreach ($layout in @(
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path -LiteralPath $layout.Source -PathType Container)
    {
        Get-ChildItem -LiteralPath $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

foreach ($source in @(
    (Join-Path (Join-Path $openUsdRoot 'lib') 'usd'),
    (Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'),
    (Join-Path (Join-Path $shimRoot 'plugin') 'usd')))
{
    if (Test-Path -LiteralPath $source -PathType Container)
    {
        Copy-Item -Path (Join-Path $source '*') -Destination $pluginPath -Recurse -Force
    }
}

if (-not (Get-ChildItem -LiteralPath $pluginPath -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1))
{
    throw "No OpenUSD plugins were staged at '$pluginPath'. Native managed tests cannot open schema-backed stages."
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldPluginPath = $env:OPENUSD_TEST_PLUGIN_PATH
$oldWorkRoot = $env:OPENUSD_TEST_WORK_ROOT
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $stageRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath
    $env:OPENUSD_TEST_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_TEST_WORK_ROOT = $workRoot

    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
        -Project $Project `
        -Framework $Framework `
        -Configuration $Configuration `
        -MinimumExpectedTests $MinimumExpectedTests `
        -TestArguments $TestArguments
    exit $LASTEXITCODE
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:OPENUSD_TEST_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_TEST_WORK_ROOT = $oldWorkRoot
}
