#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid,
    [int]$Jobs = [Environment]::ProcessorCount,
    [switch]$ForceFetch,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cacheRoot = Join-Path $repoRoot 'native/downloads'
$buildRoot = Join-Path $repoRoot "native/build/physx/$Rid"
$installRoot = Join-Path $repoRoot "native/install/physx/$Rid"
$lock = Get-Content (Join-Path $PSScriptRoot 'physx.lock.json') -Raw | ConvertFrom-Json
$triplet = switch ($Rid)
{
    'win-x64' { $lock.vcpkg.windowsTriplet }
    'linux-x64' { $lock.vcpkg.linuxTriplet }
}

$vcpkgRoot = Join-Path $repoRoot "native/.cache/vcpkg-$($lock.vcpkg.baseline.Substring(0, 12))"
$vcpkgExe = if ($IsWindows) { Join-Path $vcpkgRoot 'vcpkg.exe' } else { Join-Path $vcpkgRoot 'vcpkg' }
$vcpkgInstalledRoot = Join-Path $buildRoot 'vcpkg_installed'
$installArguments = @(
    'install',
    "$($lock.physx.vcpkgPackage):$triplet",
    "--x-install-root=$vcpkgInstalledRoot",
    '--clean-buildtrees-after-build',
    '--disable-metrics'
)

if ($PlanOnly)
{
    Write-Output "PhysX $($lock.physx.tag) via vcpkg baseline $($lock.vcpkg.baseline)"
    Write-Output "RID: $Rid"
    Write-Output "Triplet: $triplet"
    Write-Output "Build: $buildRoot"
    Write-Output "Install: $installRoot"
    Write-Output "Install package: $vcpkgExe $($installArguments -join ' ')"
    return
}

$layout = & (Join-Path $PSScriptRoot 'fetch-physx-native.ps1') `
    -Rid $Rid `
    -CacheRoot $cacheRoot `
    -Force:$ForceFetch

if ($Rid -eq 'win-x64' -and -not $IsWindows)
{
    throw 'win-x64 must be built on Windows.'
}
if ($Rid -eq 'linux-x64' -and -not $IsLinux)
{
    throw 'linux-x64 must be built on Linux.'
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
$env:VCPKG_ROOT = $layout.VcpkgRoot
$env:VCPKG_DISABLE_METRICS = '1'
$env:VCPKG_MAX_CONCURRENCY = [string]$Jobs
Write-Host "Building PhysX $($lock.physx.tag) for $Rid ($triplet)"
& $layout.VcpkgExecutable @installArguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$vcpkgTripletRoot = Join-Path $vcpkgInstalledRoot $triplet
if (-not (Test-Path $vcpkgTripletRoot))
{
    throw "Could not find installed PhysX tree at $vcpkgTripletRoot."
}
Copy-Item (Join-Path $vcpkgTripletRoot '*') $installRoot -Recurse -Force

$noticePath = Join-Path $installRoot 'THIRD-PARTY-PHYSX.md'
# The notice separates what is redistributed from what is not, on purpose. The SDK sources this
# port compiles are BSD-3-Clause and are statically linked into openusd_physx, so they are
# redistributed. The GPU and device modules are separate packman blobs the port downloads under
# NVIDIA proprietary terms; nothing in this repository redistributes them, and a notice that
# named only the BSD licence would imply otherwise to anyone reading a package.
$noticeLines = @(
    '# PhysX third-party notices',
    '',
    "PhysX $($lock.physx.tag) is licensed under $($lock.physx.license).",
    "Repository: $($lock.physx.repository)",
    "vcpkg baseline: $($lock.vcpkg.baseline)",
    '',
    '## Redistributed here',
    '',
    'The PhysX SDK static libraries built from the repository above, including PhysXVehicle2,',
    'linked into the `openusd_physx` C ABI shim. These are covered by the licence named above.',
    '',
    '## Not redistributed',
    '',
    "The optional GPU acceleration modules ($($lock.physx.gpuModules -join ', ')) are separate",
    'binary packages that the vcpkg port downloads from NVIDIA rather than building from the',
    "BSD-3-Clause sources. They are licensed under $($lock.physx.gpuModuleLicense) and are not",
    'included in any OpenUsd package. A user with the appropriate NVIDIA licence supplies them',
    'beside the runtime to enable GPU domains; without them the runtime reports no CUDA',
    'capability and skips every GPU-only object with a diagnostic.',
    ''
)
$copyrightPath = Join-Path $installRoot 'share/physx/copyright'
if (Test-Path $copyrightPath)
{
    $noticeLines += Get-Content $copyrightPath -Raw
}
[System.IO.File]::WriteAllLines($noticePath, $noticeLines, [System.Text.UTF8Encoding]::new($false))