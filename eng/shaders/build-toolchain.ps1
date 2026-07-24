#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '.cache'),
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [int]$Jobs = [Environment]::ProcessorCount
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path $PSScriptRoot 'toolchain.lock.json') -Raw |
    ConvertFrom-Json
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)

if (-not $Rid)
{
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $Rid = if ($IsWindows -and $architecture -eq 'X64')
    {
        'win-x64'
    }
    elseif ($IsLinux -and $architecture -eq 'X64')
    {
        'linux-x64'
    }
    elseif ($IsMacOS -and $architecture -eq 'Arm64')
    {
        'osx-arm64'
    }
    else
    {
        throw "The shader toolchain does not support this host: $architecture."
    }
}

foreach ($commandName in @('cmake', 'ninja', 'python'))
{
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue))
    {
        throw "$commandName is required to build SPIRV-Tools."
    }
}
if ($IsWindows -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue))
{
    throw 'Run from a Visual Studio developer shell so cl.exe is available.'
}

$spirvToolsSource = Join-Path `
    $CacheRoot `
    "sources/spirv-tools/$($lock.spirvTools.extractDirectory)"
$spirvHeadersSource = Join-Path `
    $CacheRoot `
    "sources/spirv-headers/$($lock.spirvHeaders.extractDirectory)"
$buildRoot = Join-Path $CacheRoot "build/$Rid/spirv-tools"
$installRoot = Join-Path $ToolRoot "$Rid/spirv-tools"
$cmakeSpirvHeadersSource = $spirvHeadersSource.Replace('\', '/')

if (-not (Test-Path (Join-Path $spirvToolsSource 'CMakeLists.txt')))
{
    throw "SPIRV-Tools source is missing. Run fetch-toolchain.ps1 first."
}
if (-not (Test-Path (Join-Path $spirvHeadersSource 'CMakeLists.txt')))
{
    throw "SPIRV-Headers source is missing. Run fetch-toolchain.ps1 first."
}

New-Item -ItemType Directory -Force -Path $buildRoot,$installRoot | Out-Null
& cmake `
    -S $spirvToolsSource `
    -B $buildRoot `
    -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    "-DSPIRV-Headers_SOURCE_DIR=$cmakeSpirvHeadersSource" `
    -DSPIRV_SKIP_TESTS=ON `
    -DSPIRV_WERROR=ON `
    -DSPIRV_COLOR_TERMINAL=OFF
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Remove-Item (Join-Path $buildRoot 'build-version.inc') `
    -Force `
    -ErrorAction SilentlyContinue
& cmake -E env `
    "FORCED_BUILD_VERSION_DESCRIPTION=$($lock.spirvTools.commit)" `
    cmake --build $buildRoot --target spirv-val -j $Jobs
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$executableName = if ($IsWindows) { 'spirv-val.exe' } else { 'spirv-val' }
$validator = Get-ChildItem $buildRoot -Recurse -File -Filter $executableName |
    Select-Object -First 1
if (-not $validator)
{
    throw "The SPIR-V validator was not produced under $buildRoot."
}

$binRoot = Join-Path $installRoot 'bin'
New-Item -ItemType Directory -Force -Path $binRoot | Out-Null
Copy-Item $validator.FullName (Join-Path $binRoot $executableName) -Force
Write-Output $installRoot
