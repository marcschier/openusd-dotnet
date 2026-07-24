#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../native/src/vulkan-sdk-1.4.321.0'),
    [string]$BuildRoot = (Join-Path $PSScriptRoot '../native/build/vulkan-sdk-1.4.321.0'),
    [string]$InstallRoot = (Join-Path $PSScriptRoot '../native/install/vulkan-sdk-1.4.321.0'),
    [int]$Jobs = [Environment]::ProcessorCount
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
$BuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)

if (-not (Get-Command git -ErrorAction SilentlyContinue))
{
    throw 'Git is required.'
}
if (-not (Get-Command python -ErrorAction SilentlyContinue))
{
    throw 'Python is required.'
}
if (-not (Get-Command cmake -ErrorAction SilentlyContinue))
{
    throw 'CMake is required.'
}
if (-not (Get-Command ninja -ErrorAction SilentlyContinue))
{
    throw 'Ninja is required.'
}
if ($IsWindows -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue))
{
    throw 'Run this script from a Visual Studio developer shell so cl.exe is available.'
}

function Sync-Repository
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Commit
    )

    $path = Join-Path $SourceRoot $Name
    if (-not (Test-Path (Join-Path $path '.git')))
    {
        New-Item -ItemType Directory -Force -Path $SourceRoot | Out-Null
        & git clone --filter=blob:none --no-checkout $Url $path
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }

    & git -C $path fetch --depth 1 origin $Commit
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
    & git -C $path checkout --detach $Commit
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

foreach ($repository in $lock.vulkanSdk.repositories)
{
    $directoryName = if ($repository.name -eq 'shaderc-source-manifest')
    {
        'shaderc'
    }
    else
    {
        $repository.name
    }

    Sync-Repository -Name $directoryName -Url $repository.url -Commit $repository.commit
}

$shadercManifest = Join-Path $SourceRoot 'shaderc'
$shadercSource = Join-Path $shadercManifest 'src'
if (-not (Test-Path (Join-Path $shadercSource 'CMakeLists.txt')))
{
    Push-Location $shadercManifest
    try
    {
        & python (Join-Path $shadercManifest 'update_shaderc_sources.py')
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }
    finally
    {
        Pop-Location
    }
}

New-Item -ItemType Directory -Force -Path $BuildRoot,$InstallRoot | Out-Null

function Invoke-CMake
{
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & cmake @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

function Invoke-CMakeBuild
{
    param([Parameter(Mandatory = $true)][string]$Directory)

    & cmake --build $Directory --target install -j $Jobs
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

$headersBuild = Join-Path $BuildRoot 'Vulkan-Headers'
Invoke-CMake @(
    '-S', (Join-Path $SourceRoot 'Vulkan-Headers'),
    '-B', $headersBuild,
    '-G', 'Ninja',
    '-DCMAKE_BUILD_TYPE=Release',
    "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
    '-DVULKAN_HEADERS_ENABLE_TESTS=OFF'
)
Invoke-CMakeBuild $headersBuild

$loaderBuild = Join-Path $BuildRoot 'Vulkan-Loader'
Invoke-CMake @(
    '-S', (Join-Path $SourceRoot 'Vulkan-Loader'),
    '-B', $loaderBuild,
    '-G', 'Ninja',
    '-DCMAKE_BUILD_TYPE=RelWithDebInfo',
    "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
    "-DVULKAN_HEADERS_INSTALL_DIR=$InstallRoot",
    '-DBUILD_TESTS=OFF',
    '-DUPDATE_DEPS=OFF'
)
Invoke-CMakeBuild $loaderBuild

$shadercBuild = Join-Path $BuildRoot 'shaderc'
Invoke-CMake @(
    '-S', $shadercSource,
    '-B', $shadercBuild,
    '-G', 'Ninja',
    '-DCMAKE_BUILD_TYPE=Release',
    "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
    '-DSHADERC_SKIP_TESTS=ON',
    '-DSHADERC_ENABLE_SHARED_CRT=ON',
    '-DSHADERC_SKIP_COPYRIGHT_CHECK=ON'
)
Invoke-CMakeBuild $shadercBuild

if ($IsWindows)
{
    $shadercDebugBuild = Join-Path $BuildRoot 'shaderc-debug'
    Invoke-CMake @(
        '-S', $shadercSource,
        '-B', $shadercDebugBuild,
        '-G', 'Ninja',
        '-DCMAKE_BUILD_TYPE=Debug',
        '-DCMAKE_DEBUG_POSTFIX=d',
        "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
        '-DSHADERC_SKIP_TESTS=ON',
        '-DSHADERC_ENABLE_SHARED_CRT=ON',
        '-DSHADERC_SKIP_COPYRIGHT_CHECK=ON'
    )
    Invoke-CMakeBuild $shadercDebugBuild
}

$includeDirectory = if ($IsWindows) { 'Include' } else { 'include' }
$libraryDirectory = if ($IsWindows) { 'Lib' } else { 'lib' }
$vmaInstallDirectory = Join-Path $InstallRoot "$includeDirectory/vma"
New-Item -ItemType Directory -Force -Path $vmaInstallDirectory | Out-Null
Copy-Item `
    (Join-Path $SourceRoot 'VulkanMemoryAllocator/include/vk_mem_alloc.h') `
    (Join-Path $vmaInstallDirectory 'vk_mem_alloc.h') `
    -Force

$vulkanIncludeDirectory = Join-Path $InstallRoot "$includeDirectory/vulkan"
Copy-Item `
    (Join-Path $SourceRoot 'Vulkan-Utility-Libraries/include/vulkan/vk_enum_string_helper.h') `
    (Join-Path $vulkanIncludeDirectory 'vk_enum_string_helper.h') `
    -Force

$vulkanHeader = Join-Path $InstallRoot "$includeDirectory/vulkan/vulkan.h"
$vmaHeader = Join-Path $vmaInstallDirectory 'vk_mem_alloc.h'
$enumStringHeader = Join-Path $vulkanIncludeDirectory 'vk_enum_string_helper.h'
$vulkanLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'vulkan'
$shadercLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'shaderc_combined'
$shadercDebugLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'shaderc_combinedd'

$hasRequiredShaderc = $shadercLibrary -and
    (-not $IsWindows -or $shadercDebugLibrary)
if (-not (Test-Path $vulkanHeader) -or
    -not (Test-Path $vmaHeader) -or
    -not (Test-Path $enumStringHeader) -or
    -not $vulkanLibrary -or
    -not $hasRequiredShaderc)
{
    throw "The local Vulkan SDK is incomplete at $InstallRoot."
}

Write-Output $InstallRoot
