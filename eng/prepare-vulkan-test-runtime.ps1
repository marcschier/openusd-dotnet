#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

    [string]$OutputPath,

    [switch]$Activate,

    [string]$StartupHookPath
)

$ErrorActionPreference = 'Stop'
$rootPath = [System.IO.Path]::GetFullPath($Root)
$lockPath = Join-Path $PSScriptRoot 'vulkan-test-runtime.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$runtime = $lock.runtimes.$Rid
if ($null -eq $runtime)
{
    throw "The Vulkan test runtime lock does not contain '$Rid'."
}

function Resolve-NativeAsset
{
    param([Parameter(Mandatory = $true)][string]$FileName)

    $candidates = @(
        (Join-Path $rootPath $FileName),
        (Join-Path $rootPath "runtimes/$Rid/native/$FileName")
    )
    foreach ($candidate in $candidates)
    {
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "The Vulkan test runtime asset '$FileName' was not found under '$rootPath'."
}

function Assert-AssetHash
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -cne $Expected)
    {
        throw "Vulkan test runtime hash mismatch for '$Path'. Expected $Expected, got $actual."
    }
}

function Add-EnvironmentPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $current = [Environment]::GetEnvironmentVariable($Name, 'Process')
    $entries = if ([string]::IsNullOrWhiteSpace($current))
    {
        @()
    }
    else
    {
        @($current -split [System.IO.Path]::PathSeparator)
    }
    $entries = @($entries | Where-Object { $_ -ne $Path })
    [Environment]::SetEnvironmentVariable(
        $Name,
        (@($Path) + $entries) -join [System.IO.Path]::PathSeparator,
        'Process')
}

$loaderPath = Resolve-NativeAsset ([string]$runtime.loader)
$driverPath = Resolve-NativeAsset ([string]$runtime.driver)
Assert-AssetHash $loaderPath ([string]$runtime.loaderSha256)
Assert-AssetHash $driverPath ([string]$runtime.driverSha256)

$manifestPath = if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    Join-Path $rootPath "openusd-swiftshader-$Rid.json"
}
else
{
    [System.IO.Path]::GetFullPath($OutputPath)
}
$manifestDirectory = Split-Path -Parent $manifestPath
New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
$manifest = [ordered]@{
    file_format_version = '1.0.0'
    ICD = [ordered]@{
        library_path = $driverPath
        api_version = [string]$lock.apiVersion
    }
}
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 3),
    [System.Text.UTF8Encoding]::new($false))

if ($Activate)
{
    if (-not [string]::IsNullOrWhiteSpace($StartupHookPath))
    {
        $hookPath = [System.IO.Path]::GetFullPath($StartupHookPath)
        if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf))
        {
            throw "The Vulkan loader startup hook does not exist: $hookPath"
        }
        Add-EnvironmentPath -Name 'DOTNET_STARTUP_HOOKS' -Path $hookPath
    }

    $loaderDirectory = Split-Path -Parent $loaderPath
    if ($Rid -eq 'win-x64')
    {
        Add-EnvironmentPath -Name 'PATH' -Path $loaderDirectory
    }
    elseif ($Rid -eq 'linux-x64')
    {
        Add-EnvironmentPath -Name 'LD_LIBRARY_PATH' -Path $loaderDirectory
    }
    elseif ($Rid -eq 'osx-arm64')
    {
        Add-EnvironmentPath -Name 'DYLD_LIBRARY_PATH' -Path $loaderDirectory
    }

    $env:VK_DRIVER_FILES = $manifestPath
    $env:VK_ICD_FILENAMES = $manifestPath
    $env:OPENUSD_REQUIRE_SWIFTSHADER = '1'
    $env:OPENUSD_VULKAN_LOADER_PATH = $loaderPath
    $env:OPENUSD_VULKAN_LOADER_SHA256 = [string]$runtime.loaderSha256
    $env:OPENUSD_VULKAN_DRIVER_PATH = $driverPath
    $env:OPENUSD_VULKAN_DRIVER_SHA256 = [string]$runtime.driverSha256
    $env:OPENUSD_VULKAN_MANIFEST_PATH = $manifestPath
    $env:OPENUSD_VULKAN_API_VERSION = [string]$lock.apiVersion

    Write-Host (
        "[vulkan-runtime] rid=$Rid cwd=$((Get-Location).Path) " +
        "os=$([Environment]::OSVersion.VersionString)")
    Write-Host (
        "[vulkan-runtime] processArch=" +
        "$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) " +
        "cpu=$env:PROCESSOR_IDENTIFIER")
    Write-Host (
        "[vulkan-runtime] loader=$loaderPath " +
        "sha256=$([string]$runtime.loaderSha256)")
    Write-Host (
        "[vulkan-runtime] driver=$driverPath " +
        "sha256=$([string]$runtime.driverSha256)")
    Write-Host (
        "[vulkan-runtime] manifest=$manifestPath " +
        "api=$([string]$lock.apiVersion)")
}

Write-Output $manifestPath
