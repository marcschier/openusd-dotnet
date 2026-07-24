#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$WorkRoot = (
        Join-Path $PSScriptRoot '../artifacts/linux-native-prerequisite-preflight')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if (-not $IsLinux)
{
    throw 'The Linux native prerequisite preflight must run on Linux.'
}

$installHint = @(
    'sudo apt-get update && sudo apt-get install -y',
    'git tar python3 python-is-python3 build-essential cmake ninja-build',
    'pkg-config libx11-dev libxt-dev'
) -join ' '
$requiredCommands = @(
    'git',
    'tar',
    'python',
    'cmake',
    'ninja',
    'gcc',
    'g++',
    'pkg-config'
)
$missingCommands = @(
    foreach ($command in $requiredCommands)
    {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue))
        {
            $command
        }
    }
)
if ($missingCommands.Count -ne 0)
{
    throw @(
        "Linux native build prerequisite preflight failed. Missing executable(s):",
        ($missingCommands -join ', '),
        "Install the required Ubuntu packages with: $installHint"
    ) -join ' '
}

$pkgConfigModules = @('x11', 'xt')
& pkg-config --exists @pkgConfigModules
if ($LASTEXITCODE -ne 0)
{
    $details = @(& pkg-config --print-errors --exists @pkgConfigModules 2>&1)
    throw @(
        'Linux native build prerequisite preflight failed.',
        'pkg-config could not resolve both x11 and xt.',
        "Install the required Ubuntu packages with: $installHint",
        ($details -join [Environment]::NewLine)
    ) -join [Environment]::NewLine
}

$WorkRoot = [System.IO.Path]::GetFullPath($WorkRoot)
$probeRoot = Join-Path $WorkRoot 'cmake-x11-xt'
$sourceRoot = Join-Path $probeRoot 'source'
$buildRoot = Join-Path $probeRoot 'build'
Remove-Item $probeRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $sourceRoot 'CMakeLists.txt'),
    @'
cmake_minimum_required(VERSION 3.28)
project(OpenUsdLinuxNativePrerequisiteProbe LANGUAGES C CXX)

find_package(X11 REQUIRED)
if(NOT X11_Xt_FOUND)
    message(FATAL_ERROR "Xt was not found by CMake FindX11.")
endif()
if(NOT TARGET X11::X11)
    message(FATAL_ERROR "CMake did not create the X11::X11 target.")
endif()
if(NOT TARGET X11::Xt)
    message(FATAL_ERROR "CMake did not create the X11::Xt target.")
endif()

add_executable(openusd_linux_x11_xt_probe probe.c)
target_link_libraries(
    openusd_linux_x11_xt_probe
    PRIVATE
        X11::X11
        X11::Xt)
'@,
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $sourceRoot 'probe.c'),
    @'
#include <X11/Intrinsic.h>

int main(void)
{
    XtToolkitInitialize();
    return 0;
}
'@,
    [System.Text.UTF8Encoding]::new($false))

$cmakeOutput = @(
    & cmake -S $sourceRoot -B $buildRoot -G Ninja 2>&1
)
$cmakeExitCode = $LASTEXITCODE
if ($cmakeExitCode -ne 0)
{
    throw @(
        'Linux native build prerequisite preflight failed.',
        'CMake could not configure a project requiring X11::X11 and X11::Xt.',
        "Install the required Ubuntu packages with: $installHint",
        ($cmakeOutput -join [Environment]::NewLine)
    ) -join [Environment]::NewLine
}
$buildOutput = @(& cmake --build $buildRoot --parallel 2 2>&1)
if ($LASTEXITCODE -ne 0)
{
    throw @(
        'Linux native build prerequisite preflight failed.',
        'The X11/Xt CMake probe did not compile and link.',
        "Install the required Ubuntu packages with: $installHint",
        ($buildOutput -join [Environment]::NewLine)
    ) -join [Environment]::NewLine
}

$x11Version = (@(& pkg-config --modversion x11) -join '').Trim()
$xtVersion = (@(& pkg-config --modversion xt) -join '').Trim()
Remove-Item $probeRoot -Recurse -Force
Write-Output "Linux native prerequisite preflight passed (x11 $x11Version, xt $xtVersion)."
