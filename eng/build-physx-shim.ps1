#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Builds and installs the optional PhysX simulation shim for one RID.

.DESCRIPTION
    The physics preset configures the whole native project, so cmake --install writes every shim it
    built, not only openusd_physx. That is why the install prefix is native/install/shim/<rid>-physx
    and never native/install/shim/<rid>: the latter is the prefix the verified native archive is
    extracted into, and installing over it would replace archive-verified core, Hydra, hdSilk and
    Storm child binaries with locally rebuilt ones at exactly the paths packaging reads.

    The physics packages therefore read only their own named asset out of this prefix. Everything
    else it contains is either a duplicate of a payload another package already publishes, or an
    NVIDIA GPU module this project has no right to redistribute.
#>
[CmdletBinding()]
param(
    # The pinned vcpkg PhysX port declares
    # "(windows & x64 & !mingw & !uwp) | (linux & x64) | (linux & arm64)". There is no arm64-osx
    # build of the simulation SDK, so macOS is absent here rather than failing later inside cmake.
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$openUsdInstallRoot = Join-Path $repoRoot "native/install/$Rid"
$physxInstallRoot = Join-Path $repoRoot "native/install/physx/$Rid"
$shimInstallRoot = Join-Path $repoRoot "native/install/shim/$Rid-physx"
$preset = "$Rid-physx"

if (-not (Test-Path $openUsdInstallRoot))
{
    throw "The OpenUSD native install is missing at '$openUsdInstallRoot'."
}
if (-not (Test-Path $physxInstallRoot))
{
    throw (
        "The PhysX native install is missing at '$physxInstallRoot'. " +
        "Run ./eng/build-physx-native.ps1 -Rid $Rid first.")
}

$env:OPENUSD_ROOT = $openUsdInstallRoot
Push-Location (Join-Path $repoRoot 'native')
try
{
    $configureArguments = @('--preset', $preset)
    if ($Rid -eq 'linux-x64')
    {
        $configureArguments += @(
            '-DCMAKE_BUILD_RPATH_USE_ORIGIN=ON',
            '-DCMAKE_INSTALL_RPATH=$ORIGIN')
    }

    & cmake @configureArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & cmake --build --preset $preset
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $buildRoot = Join-Path 'build/shim' $preset
    & cmake --install $buildRoot --prefix $shimInstallRoot
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}
finally
{
    Pop-Location
}

$library = switch ($Rid)
{
    'win-x64' { Join-Path $shimInstallRoot 'bin/openusd_physx.dll' }
    'linux-x64' { Join-Path $shimInstallRoot 'lib/libopenusd_physx.so' }
}
if (-not (Test-Path $library))
{
    throw "PhysX shim install did not produce '$library'."
}

# The GPU modules are reported rather than required, and are never packaged. They are packman
# blobs under NVIDIA proprietary terms that the vcpkg port downloads for local use, so staging
# them here enables GPU domains on this machine and in CI probes only. A machine without them
# still produces a complete CPU package; nothing about the published set changes either way.
$moduleDirectory = if ($Rid -eq 'win-x64')
{
    Join-Path $shimInstallRoot 'bin'
}
else
{
    Join-Path $shimInstallRoot 'lib'
}
$gpuModules = @(
    Get-ChildItem -LiteralPath $moduleDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(lib)?PhysX(Gpu|Device)' } |
        Select-Object -ExpandProperty Name)

Write-Host "Installed PhysX shim: $library"
if ($gpuModules.Count -gt 0)
{
    Write-Host (
        "Staged NVIDIA proprietary GPU module(s) for local use only: $($gpuModules -join ', '). " +
        'These are never redistributed in an OpenUsd package.')
}
else
{
    Write-Host 'No GPU module was staged; CPU domains only on this machine.'
}
