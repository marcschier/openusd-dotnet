#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [switch]$PackageConsumer
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$openUsdInstallRoot = Join-Path $repoRoot "native/install/$Rid"
$cesiumInstallRoot = Join-Path $repoRoot "native/install/cesium/$Rid"
$shimInstallRoot = Join-Path $repoRoot "native/install/shim/$Rid-cesium"
$preset = "$Rid-cesium"

if (-not (Test-Path $openUsdInstallRoot))
{
    throw "The OpenUSD native install is missing at '$openUsdInstallRoot'."
}
if (-not (Test-Path $cesiumInstallRoot))
{
    throw "The Cesium native install is missing at '$cesiumInstallRoot'."
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
    elseif ($Rid -eq 'osx-arm64')
    {
        $configureArguments += @(
            '-DCMAKE_INSTALL_NAME_DIR=@rpath',
            '-DCMAKE_INSTALL_RPATH=@loader_path',
            '-DCMAKE_BUILD_WITH_INSTALL_RPATH=OFF')
    }

    & cmake @configureArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $buildArguments = @('--build', '--preset', $preset)
    if ($PackageConsumer)
    {
        $buildArguments += @('--target', 'openusd_cesium')
        Write-Host 'Building the Cesium package-consumer target and its declared dependencies.'
    }
    & cmake @buildArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $buildRoot = Join-Path 'build/shim' $preset
    $installRoot = if ($PackageConsumer)
    {
        Join-Path $buildRoot 'openusd_cesium'
    }
    else
    {
        $buildRoot
    }
    & cmake --install $installRoot --prefix $shimInstallRoot
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
    'win-x64' { Join-Path $shimInstallRoot 'bin/openusd_cesium.dll' }
    'linux-x64' { Join-Path $shimInstallRoot 'lib/libopenusd_cesium.so' }
    'osx-arm64' { Join-Path $shimInstallRoot 'lib/libopenusd_cesium.dylib' }
}
if (-not (Test-Path $library))
{
    throw "Cesium shim install did not produce '$library'."
}
if ($PackageConsumer)
{
    $installed = Join-Path $shimInstallRoot 'include/openusd_cesium.h'
    $source = Join-Path $repoRoot 'native/openusd_cesium/include/openusd_cesium.h'
    if ((Get-FileHash -LiteralPath $installed).Hash -cne (Get-FileHash -LiteralPath $source).Hash)
    {
        throw 'The installed Cesium header does not match the current project source.'
    }
}

Write-Host "Installed Cesium shim: $library"
