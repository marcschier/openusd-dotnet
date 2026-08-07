#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [int]$Jobs = [Environment]::ProcessorCount,
    [switch]$ForceFetch,
    [switch]$PlanOnly,
    [switch]$SkipSmokeProbe
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cacheRoot = Join-Path $repoRoot 'native/downloads'
$sourceRoot = Join-Path $repoRoot 'native/src'
$buildRoot = Join-Path $repoRoot "native/build/cesium/$Rid"
$installRoot = Join-Path $repoRoot "native/install/cesium/$Rid"
$probeBuildRoot = Join-Path $repoRoot "native/build/cesium-probe/$Rid"
$lock = Get-Content (Join-Path $PSScriptRoot 'cesium.lock.json') -Raw | ConvertFrom-Json
$cesiumSource = Join-Path $sourceRoot $lock.cesiumNative.extractDirectory
$vcpkgRoot = Join-Path $repoRoot "native/.cache/vcpkg-$($lock.vcpkg.baseline.Substring(0, 12))"
$vcpkgToolchain = Join-Path $vcpkgRoot 'scripts/buildsystems/vcpkg.cmake'

$triplet = switch ($Rid)
{
    'win-x64' { $lock.vcpkg.windowsTriplet }
    'linux-x64' { $lock.vcpkg.linuxTriplet }
    'osx-arm64' { $lock.vcpkg.macosTriplet }
}

$configureArguments = @(
    '-S', $cesiumSource,
    '-B', $buildRoot,
    '-G', 'Ninja',
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_INSTALL_PREFIX=$installRoot",
    # Linux links cesium-native static archives into libopenusd_cesium.so; without
    # PIC, thread_local statics can emit executable-only R_X86_64_TPOFF32 relocations.
    "-DCMAKE_POSITION_INDEPENDENT_CODE=ON",
    "-DCMAKE_TOOLCHAIN_FILE=$vcpkgToolchain",
    "-DVCPKG_TARGET_TRIPLET=$triplet",
    "-DVCPKG_HOST_TRIPLET=$triplet",
    "-DVCPKG_MANIFEST_MODE=ON",
    "-DVCPKG_MANIFEST_DIR=$cesiumSource",
    "-DVCPKG_INSTALL_OPTIONS=--clean-buildtrees-after-build",
    "-DCESIUM_USE_EZVCPKG=OFF",
    "-DCESIUM_TESTS_ENABLED=OFF",
    "-DCESIUM_ENABLE_CLANG_TIDY=OFF",
    "-DCESIUM_INSTALL_STATIC_LIBS=ON",
    "-DCESIUM_INSTALL_HEADERS=ON",
    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5"
)

if ($Rid -eq 'osx-arm64')
{
    $configureArguments += @(
        '-DCMAKE_OSX_ARCHITECTURES=arm64',
        '-DCMAKE_OSX_DEPLOYMENT_TARGET=13.0')
}

if ($PlanOnly)
{
    Write-Output "cesium-native $($lock.cesiumNative.tag) ($($lock.cesiumNative.commit))"
    Write-Output "vcpkg baseline: $($lock.vcpkg.baseline)"
    Write-Output "RID: $Rid"
    Write-Output "Triplet: $triplet"
    Write-Output "Source: $cesiumSource"
    Write-Output "Build: $buildRoot"
    Write-Output "Install: $installRoot"
    Write-Output ("Configure: cmake {0}" -f ($configureArguments -join ' '))
    Write-Output "Build: cmake --build $buildRoot --parallel $Jobs"
    Write-Output "Install: cmake --install $buildRoot"
    return
}

$layout = & (Join-Path $PSScriptRoot 'fetch-cesium-native.ps1') `
    -Rid $Rid `
    -CacheRoot $cacheRoot `
    -SourceRoot $sourceRoot `
    -Force:$ForceFetch

if ($Rid -eq 'win-x64' -and -not $IsWindows)
{
    throw 'win-x64 must be built on Windows.'
}
if ($Rid -eq 'linux-x64' -and -not $IsLinux)
{
    throw 'linux-x64 must be built on Linux.'
}
if ($Rid -eq 'osx-arm64' -and -not $IsMacOS)
{
    throw 'osx-arm64 must be built on macOS.'
}

if (-not (Get-Command cmake -ErrorAction SilentlyContinue))
{
    throw 'CMake 3.30.4 or newer is required.'
}
if (-not (Get-Command ninja -ErrorAction SilentlyContinue))
{
    throw 'Ninja is required.'
}
if ($Rid -eq 'win-x64' -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue))
{
    throw 'Run this script from a Visual Studio x64 developer shell so cl.exe is available.'
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

$ninjaPath = (Get-Command ninja).Source
$configureArguments += "-DCMAKE_MAKE_PROGRAM=$ninjaPath"

$env:VCPKG_ROOT = $layout.VcpkgRoot
$env:VCPKG_DISABLE_METRICS = '1'
Write-Host "Building cesium-native $($lock.cesiumNative.tag) for $Rid ($triplet)"
& cmake @configureArguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

& cmake --build $buildRoot --parallel $Jobs
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

& cmake --install $buildRoot
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$vcpkgInstalledRoot = Join-Path $buildRoot "vcpkg_installed/$triplet"
if (-not (Test-Path $vcpkgInstalledRoot))
{
    $vcpkgInstalledRoot = Join-Path $cesiumSource "vcpkg_installed/$triplet"
}
if (-not (Test-Path $vcpkgInstalledRoot))
{
    throw "Could not find the vcpkg installed tree for $triplet."
}

$vcpkgLibraryRoot = Join-Path $vcpkgInstalledRoot 'lib'
$vcpkgHeaderRoot = Join-Path $vcpkgInstalledRoot 'include'
if (Test-Path $vcpkgLibraryRoot)
{
    New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'lib') | Out-Null
    Get-ChildItem $vcpkgLibraryRoot -File | Copy-Item -Destination (Join-Path $installRoot 'lib') -Force
}
if (Test-Path $vcpkgHeaderRoot)
{
    New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'include') | Out-Null
    Copy-Item (Join-Path $vcpkgHeaderRoot '*') (Join-Path $installRoot 'include') -Recurse -Force
}

$noticePath = Join-Path $installRoot 'THIRD-PARTY-CESIUM.md'
$noticeLines = @(
    '# cesium-native third-party notices',
    '',
    "cesium-native $($lock.cesiumNative.tag) is licensed under Apache-2.0.",
    "Source: $($lock.cesiumNative.archiveUrl)",
    "SHA-256: $($lock.cesiumNative.archiveSha256)",
    '',
    "vcpkg baseline: $($lock.vcpkg.baseline)",
    ''
)
$copyrightFiles = Get-ChildItem (Join-Path $vcpkgInstalledRoot 'share') -Filter copyright -Recurse |
    Sort-Object FullName
foreach ($copyrightFile in $copyrightFiles)
{
    $packageName = Split-Path $copyrightFile.DirectoryName -Leaf
    $noticeLines += "## $packageName"
    $noticeLines += ''
    $noticeLines += Get-Content $copyrightFile.FullName -Raw
    $noticeLines += ''
}
[System.IO.File]::WriteAllLines($noticePath, $noticeLines, [System.Text.UTF8Encoding]::new($false))

if (-not $SkipSmokeProbe)
{
    $probeSource = Join-Path $repoRoot 'native/cesium_probe'
    $probeArguments = @(
        '-S', $probeSource,
        '-B', $probeBuildRoot,
        '-G', 'Ninja',
        "-DCMAKE_BUILD_TYPE=Release",
        "-DCMAKE_TOOLCHAIN_FILE=$vcpkgToolchain",
        "-DVCPKG_TARGET_TRIPLET=$triplet",
        "-DVCPKG_HOST_TRIPLET=$triplet",
        "-DCMAKE_PREFIX_PATH=$installRoot;$vcpkgInstalledRoot;$vcpkgInstalledRoot/share",
        "-DZLIB_INCLUDE_DIR=$vcpkgInstalledRoot/include"
    )

    if ($Rid -eq 'win-x64')
    {
        $probeArguments += "-DZLIB_LIBRARY_RELEASE=$vcpkgInstalledRoot/lib/zs.lib"
    }

    if ($Rid -eq 'osx-arm64')
    {
        $probeArguments += @(
            '-DCMAKE_OSX_ARCHITECTURES=arm64',
            '-DCMAKE_OSX_DEPLOYMENT_TARGET=13.0')
    }

    & cmake @probeArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & cmake --build $probeBuildRoot --parallel $Jobs
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & ctest --test-dir $probeBuildRoot --output-on-failure
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}
