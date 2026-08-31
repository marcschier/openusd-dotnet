#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
#
# Stages a runnable hdSilk runtime from a verified native install tree and reports the
# loader and plugin paths a managed test host needs.
#
# Extracted from run-parity-capture.ps1 rather than reused from it, because the parity
# capture both stages the runtime and performs the Storm capture: on macOS it runs only
# when resolve-macos-cgl-capability.ps1 proves the runner can create a CGL pixel format.
# The Metal volume gates need the same staged runtime but have nothing to do with CGL, so
# tying them to that capability would make a Metal-only gate silently disappear whenever
# hosted OpenGL regressed.
#
# The merge is the reason a staging step exists at all. OpenUSD's own plugins and the
# hdSilk delegate live in separate install trees, and OPENUSD_PLUGIN_PATH names exactly
# one directory, so both have to be copied into one plugin root before a test host can
# discover the delegate and the hioOpenVDB field reader together.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = $(
        if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { '' }),

    [string]$NativeInstallRoot,

    [string]$Root
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Rid))
{
    throw 'Runtime staging is supported only on Windows, Linux, and macOS arm64.'
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
        throw "The hdSilk runtime staging input is missing: $required"
    }
}

$stageRoot = if ([string]::IsNullOrWhiteSpace($Root))
{
    Join-Path $repoRoot "artifacts/hdsilk-runtime/$Rid"
}
else
{
    [System.IO.Path]::GetFullPath($Root)
}
$binTarget = Join-Path $stageRoot 'bin'
$libTarget = Join-Path $stageRoot 'lib'
$pluginPath = Join-Path $stageRoot 'plugin/usd'
Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
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
    throw "The hdSilk runtime staging produced no OpenUSD plugin root at $pluginPath."
}

# The hdSilk delegate and OpenUSD's hioOpenVDB field reader are reported separately, and
# neither is fatal here. A missing delegate is a real staging fault the caller should see
# named; a missing hioOpenVDB reader is a native-profile fact that the volume gates
# already turn into an explicit capability skip, so failing here would only replace a
# precise skip with a vague staging error.
$delegates = @(Get-ChildItem -LiteralPath $pluginPath -Recurse -File -Filter 'plugInfo.json' |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match '"hdSilk"' })
$hasDelegate = $delegates.Count -gt 0
$hasOpenVdb = (Get-ChildItem -LiteralPath $pluginPath -Recurse -File -Filter 'plugInfo.json' |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match 'hioOpenVDB' }).Count -gt 0

$libraryPath = ($libTarget, $binTarget) -join [System.IO.Path]::PathSeparator
Write-Host "[hdsilk-runtime] rid=$Rid root=$stageRoot"
Write-Host "[hdsilk-runtime] plugin=$pluginPath hdSilk=$hasDelegate hioOpenVDB=$hasOpenVdb"
Write-Host "[hdsilk-runtime] libraryPath=$libraryPath"
if (-not $hasDelegate)
{
    Write-Host (
        '::warning::The staged plugin root declares no hdSilk delegate; the volume gates ' +
        'will report a capability skip instead of rendering.')
}

if ($env:GITHUB_OUTPUT)
{
    "root=$stageRoot" | Out-File $env:GITHUB_OUTPUT -Append
    "plugin=$pluginPath" | Out-File $env:GITHUB_OUTPUT -Append
    "bin=$binTarget" | Out-File $env:GITHUB_OUTPUT -Append
    "lib=$libTarget" | Out-File $env:GITHUB_OUTPUT -Append
    "library-path=$libraryPath" | Out-File $env:GITHUB_OUTPUT -Append
    "hdsilk=$($hasDelegate.ToString().ToLowerInvariant())" | Out-File $env:GITHUB_OUTPUT -Append
    "openvdb=$($hasOpenVdb.ToString().ToLowerInvariant())" | Out-File $env:GITHUB_OUTPUT -Append
}

[ordered]@{
    schemaVersion = 1
    rid = $Rid
    root = $stageRoot
    plugin = $pluginPath
    bin = $binTarget
    lib = $libTarget
    libraryPath = $libraryPath
    hdSilk = $hasDelegate
    hioOpenVdb = $hasOpenVdb
} | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $stageRoot 'runtime.json')
