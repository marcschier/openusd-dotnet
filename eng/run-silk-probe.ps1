#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [switch]$SharedStageSoak,
    [switch]$MetalComposition,
    [string]$SoakArtifactPath
)

$ErrorActionPreference = 'Stop'
if ($SharedStageSoak -and $MetalComposition)
{
    throw 'SharedStageSoak and MetalComposition are mutually exclusive.'
}
if ($MetalComposition -and -not $IsMacOS)
{
    throw 'The Metal composition probe requires macOS.'
}
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'shared-stage-soak-identity.ps1')
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$publishRoot = Join-Path $repoRoot "artifacts/silk-probe/$Rid"
$probeProject = Join-Path $repoRoot 'tests/OpenUsd.SilkProbe/OpenUsd.SilkProbe.csproj'

if (-not (Test-Path $openUsdRoot) -or -not (Test-Path $shimRoot))
{
    throw "Build the native runtime and hdSilk plugin for $Rid before running the probe."
}
if (Test-Path $publishRoot)
{
    Remove-Item $publishRoot -Recurse -Force
}

& dotnet publish $probeProject `
    -c Release `
    -f net10.0 `
    -r $Rid `
    -p:AotProbe=true `
    -o $publishRoot
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$binTarget = Join-Path $publishRoot 'bin'
$libTarget = Join-Path $publishRoot 'lib'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget | Out-Null
foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path $layout.Source)
    {
        Get-ChildItem $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

$pluginPath = Join-Path $publishRoot 'plugin/usd'
New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
Copy-Item (Join-Path (Join-Path (Join-Path $openUsdRoot 'lib') 'usd') '*') `
    $pluginPath -Recurse -Force
foreach ($pluginSource in @(
    (Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'),
    (Join-Path (Join-Path $shimRoot 'plugin') 'usd')))
{
    if (Test-Path $pluginSource)
    {
        Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
    }
}

$stagedStage = Join-Path $publishRoot ([System.IO.Path]::GetFileName($StagePath))
Copy-Item ([System.IO.Path]::GetFullPath($StagePath)) $stagedStage -Force
$executable = Join-Path $publishRoot 'OpenUsd.SilkProbe'
if ($IsWindows)
{
    $executable += '.exe'
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldIcdFilenames = $env:VK_ICD_FILENAMES
$oldDriverFiles = $env:VK_DRIVER_FILES
$oldRequireSwiftShader = $env:OPENUSD_REQUIRE_SWIFTSHADER
$oldSoakSourceHash = $env:OPENUSD_SOAK_SOURCE_HASH
$oldSoakExecutableHash = $env:OPENUSD_SOAK_EXECUTABLE_HASH
$oldSoakExecutableTimestamp = $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $publishRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath
    if ($IsWindows -or $IsLinux)
    {
        $swiftShaderIcd = & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
            -Root $publishRoot `
            -Rid $Rid
        $env:VK_ICD_FILENAMES = $swiftShaderIcd
        $env:VK_DRIVER_FILES = $swiftShaderIcd
        $env:OPENUSD_REQUIRE_SWIFTSHADER = '1'
    }
    if ($SharedStageSoak)
    {
        $artifactPath = if ([string]::IsNullOrWhiteSpace($SoakArtifactPath))
        {
            Join-Path $publishRoot 'shared-stage-soak.json'
        }
        else
        {
            [System.IO.Path]::GetFullPath($SoakArtifactPath)
        }
        Set-OpenUsdSoakIdentityEnvironment $repoRoot $executable
        & $executable --shared-stage-soak $pluginPath $stagedStage $artifactPath
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0)
        {
            Assert-OpenUsdSoakArtifact $artifactPath $false
        }
        exit $exitCode
    }
    elseif ($MetalComposition)
    {
        $artifactPath = if ([string]::IsNullOrWhiteSpace($SoakArtifactPath))
        {
            Join-Path $repoRoot 'artifacts/metal-composition-probe/osx-arm64/probe.json'
        }
        else
        {
            [System.IO.Path]::GetFullPath($SoakArtifactPath)
        }
        New-Item -ItemType Directory -Force `
            -Path ([System.IO.Path]::GetDirectoryName($artifactPath)) | Out-Null
        & $executable --metal-composition $pluginPath $stagedStage $artifactPath
        exit $LASTEXITCODE
    }
    else
    {
        & $executable $pluginPath $stagedStage
    }
    exit $LASTEXITCODE
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:VK_ICD_FILENAMES = $oldIcdFilenames
    $env:VK_DRIVER_FILES = $oldDriverFiles
    $env:OPENUSD_REQUIRE_SWIFTSHADER = $oldRequireSwiftShader
    $env:OPENUSD_SOAK_SOURCE_HASH = $oldSoakSourceHash
    $env:OPENUSD_SOAK_EXECUTABLE_HASH = $oldSoakExecutableHash
    $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC = $oldSoakExecutableTimestamp
}
