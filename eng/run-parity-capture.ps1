#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid = $(if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else { '' }),

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [string]$NativeInstallRoot,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Rid))
{
    throw 'Parity capture is supported only on Windows and Linux.'
}

$installRoot = if ([string]::IsNullOrWhiteSpace($NativeInstallRoot))
{
    Join-Path $repoRoot 'native/install'
}
else
{
    [System.IO.Path]::GetFullPath($NativeInstallRoot)
}
$openUsdRoot = Join-Path $installRoot $Rid
$shimRoot = Join-Path $installRoot "shim/$Rid"
foreach ($required in @($openUsdRoot, $shimRoot))
{
    if (-not (Test-Path -LiteralPath $required -PathType Container))
    {
        throw "Parity capture native runtime is missing: $required"
    }
}

$stageRoot = Join-Path $repoRoot "artifacts/parity-capture/$Rid"
$runtimeRoot = Join-Path $stageRoot 'runtime'
$binTarget = Join-Path $runtimeRoot 'bin'
$libTarget = Join-Path $runtimeRoot 'lib'
$pluginPath = Join-Path $runtimeRoot 'plugin/usd'
Remove-Item $runtimeRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget, $pluginPath | Out-Null

foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path -LiteralPath $layout.Source -PathType Container)
    {
        Get-ChildItem -LiteralPath $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

foreach ($pluginSource in @(
    (Join-Path (Join-Path $openUsdRoot 'lib') 'usd'),
    (Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'),
    (Join-Path (Join-Path $shimRoot 'plugin') 'usd')))
{
    if (Test-Path -LiteralPath $pluginSource -PathType Container)
    {
        Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $pluginPath 'plugInfo.json') -PathType Leaf))
{
    throw "Parity capture could not stage an OpenUSD plugin root at $pluginPath."
}

if (-not $SkipBuild)
{
    & dotnet build `
        (Join-Path $repoRoot 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj') `
        -c $Configuration `
        -f net10.0
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldCaptureRequired = $env:OPENUSD_PARITY_CAPTURE_REQUIRED
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_PARITY_CAPTURE_REQUIRED = '1'

    if ($Rid -eq 'win-x64')
    {
        & (Join-Path $PSScriptRoot 'prepare-mesa-wgl-test-runtime.ps1') `
            -Root (Join-Path $stageRoot 'mesa-wgl-runtime') `
            -Rid $Rid `
            -Activate `
            -Preflight
    }

    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
        -Project tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj `
        -Framework net10.0 `
        -Configuration $Configuration `
        -MinimumExpectedTests 2 `
        -TestArguments @(
            '--treenode-filter',
            '/*/*/StormSilkParityCaptureDriverTests/*')
    exit $LASTEXITCODE
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_PARITY_CAPTURE_REQUIRED = $oldCaptureRequired
}
