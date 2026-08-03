#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [int]$Jobs = [Environment]::ProcessorCount,
    [switch]$ForceFetch,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cacheRoot = Join-Path $repoRoot 'native/downloads'
$sourceRoot = Join-Path $repoRoot 'native/src'
$buildRoot = Join-Path $repoRoot "native/build/$Rid"
$installRoot = Join-Path $repoRoot "native/install/$Rid"
$lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
$openUsdSource = Join-Path $sourceRoot $lock.openUsd.extractDirectory
$buildScript = Join-Path $openUsdSource $lock.openUsd.buildScript

$arguments = @(
    $installRoot,
    '--src', $cacheRoot,
    '--build', $buildRoot,
    '--inst', $installRoot,
    '--build-variant', 'release',
    '--generator', 'Ninja',
    '--cmake-build-args=-DCMAKE_POLICY_VERSION_MINIMUM=3.5',
    '--build-monolithic',
    '--no-tests',
    '--no-examples',
    '--no-tutorials',
    '--no-tools',
    '--no-docs',
    '--no-python-docs',
    '--no-python',
    '--usdValidation',
    '--usd-imaging',
    '--no-usdview',
    '--ptex',
    '--openvdb',
    '--no-embree',
    '--no-prman',
    '--openimageio',
    '--opencolorio',
    '--alembic',
    '--draco',
    '--materialx',
    '--onetbb',
    '-j', $Jobs
)

switch ($Rid)
{
    'win-x64'
    {
        $arguments += @('--zlib', '--vulkan')
    }
    'linux-x64'
    {
        $arguments += @('--no-zlib', '--vulkan')
    }
    'osx-arm64'
    {
        $arguments += @('--no-zlib', '--no-vulkan')
    }
}

if ($PlanOnly)
{
    Write-Output "OpenUSD $($lock.openUsd.tag) ($($lock.openUsd.commit))"
    Write-Output "RID: $Rid"
    Write-Output "Source: $openUsdSource"
    Write-Output "Build: $buildRoot"
    Write-Output "Install: $installRoot"
    Write-Output ("Command: python `"{0}`" {1}" -f $buildScript, ($arguments -join ' '))
    return
}

$layout = & (Join-Path $PSScriptRoot 'fetch-native.ps1') `
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
if (($Rid -eq 'win-x64' -or $Rid -eq 'linux-x64') -and -not $env:VULKAN_SDK)
{
    $localVulkanSdk = Join-Path $repoRoot "native/install/vulkan-sdk-$($lock.vulkanSdk.version)"
    $includeDirectory = if ($IsWindows) { 'Include' } else { 'include' }
    $requiredVulkanFiles = @(
        (Join-Path $localVulkanSdk "$includeDirectory/vulkan/vulkan.h"),
        (Join-Path $localVulkanSdk "$includeDirectory/vulkan/vk_enum_string_helper.h"),
        (Join-Path $localVulkanSdk "$includeDirectory/vma/vk_mem_alloc.h")
    )
    $libraryDirectory = if ($IsWindows) { 'Lib' } else { 'lib' }
    $requiredShadercPattern = if ($IsWindows)
    {
        'shaderc_combinedd'
    }
    else
    {
        'shaderc_combined'
    }
    $hasRequiredShaderc = Get-ChildItem `
        (Join-Path $localVulkanSdk $libraryDirectory) `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object Name -Match $requiredShadercPattern
    if ($requiredVulkanFiles.Where({ -not (Test-Path $_) }).Count -gt 0 -or
        -not $hasRequiredShaderc)
    {
        $localVulkanSdk = & (Join-Path $PSScriptRoot 'build-vulkan-sdk.ps1') |
            Select-Object -Last 1
    }

    $env:VULKAN_SDK = $localVulkanSdk
    $binaryDirectory = if ($IsWindows) { 'Bin' } else { 'bin' }
    $env:PATH = (Join-Path $localVulkanSdk $binaryDirectory) + [System.IO.Path]::PathSeparator + $env:PATH
}

if (-not (Get-Command python -ErrorAction SilentlyContinue))
{
    throw 'Python is required to run the pinned OpenUSD build script.'
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
    throw 'Run this script from a Visual Studio 2022 17.14 developer shell so cl.exe is available.'
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

Write-Host "Building OpenUSD $($lock.openUsd.tag) for $Rid"
$env:CMAKE_POLICY_VERSION_MINIMUM = '3.5'
Remove-Item Env:NoDefaultCurrentDirectoryInExePath -ErrorAction SilentlyContinue
& python $layout.BuildScript @arguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$shimInstallRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$env:OPENUSD_ROOT = $installRoot
Push-Location (Join-Path $repoRoot 'native')
try
{
    $shimConfigureArguments = @('--preset', $Rid)
    if ($Rid -eq 'linux-x64')
    {
        $shimConfigureArguments += @(
            '-DCMAKE_BUILD_RPATH_USE_ORIGIN=ON',
            '-DCMAKE_INSTALL_RPATH=$ORIGIN')
    }
    elseif ($Rid -eq 'osx-arm64')
    {
        $shimConfigureArguments += @(
            '-DCMAKE_INSTALL_NAME_DIR=@rpath',
            '-DCMAKE_INSTALL_RPATH=@loader_path',
            '-DCMAKE_BUILD_WITH_INSTALL_RPATH=OFF')
    }
    & cmake @shimConfigureArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & cmake --build --preset $Rid
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & cmake --install "build/shim/$Rid" --prefix $shimInstallRoot
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}
finally
{
    Pop-Location
}

& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
    -Operation Write `
    -Rid $Rid
& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
    -Operation Verify `
    -Rid $Rid
