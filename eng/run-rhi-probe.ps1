#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',

    [string]$PublishRoot,

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vulkan-test-runtime-registry.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishRoot = if ([string]::IsNullOrWhiteSpace($PublishRoot))
{
    Join-Path $repoRoot "artifacts/rhi-probe/$Rid"
}
else
{
    [System.IO.Path]::GetFullPath($PublishRoot)
}
$probeProject = Join-Path $repoRoot 'tests/OpenUsd.RhiProbe/OpenUsd.RhiProbe.csproj'

if (-not $SkipPublish)
{
    if (Test-Path -LiteralPath $publishRoot)
    {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
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
}
elseif (-not (Test-Path -LiteralPath $publishRoot -PathType Container))
{
    throw "The published RHI probe directory does not exist: $publishRoot"
}

$executable = Join-Path $publishRoot 'OpenUsd.RhiProbe'
if ($IsWindows)
{
    $executable += '.exe'
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf))
{
    throw "The published RHI probe executable does not exist: $executable"
}

$environmentNames = @(
    'PATH',
    'LD_LIBRARY_PATH',
    'DYLD_LIBRARY_PATH',
    'VK_DRIVER_FILES',
    'VK_ICD_FILENAMES',
    'VK_LOADER_DEBUG',
    'OPENUSD_REQUIRE_SWIFTSHADER',
    'OPENUSD_VULKAN_API_VERSION',
    'OPENUSD_VULKAN_DRIVER_PATH',
    'OPENUSD_VULKAN_DRIVER_SHA256',
    'OPENUSD_VULKAN_LOADER_PATH',
    'OPENUSD_VULKAN_LOADER_SHA256',
    'OPENUSD_VULKAN_MANIFEST_PATH'
)
$oldEnvironment = @{}
$driverRegistration = $null
foreach ($name in $environmentNames)
{
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name,
        'Process')
}
try
{
    $manifestPath = & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
        -Root $publishRoot `
        -Rid $Rid `
        -Activate
    $driverRegistration = Register-VulkanTestRuntimeDriver `
        -ManifestPath $manifestPath

    Write-Host '[vulkan-runtime] Running NativeAOT SwiftShader preflight.'
    $env:VK_LOADER_DEBUG = 'error,warn,driver'
    & $executable --vulkan-runtime-only
    if ($LASTEXITCODE -ne 0)
    {
        throw "The NativeAOT Vulkan runtime preflight exited with code $LASTEXITCODE."
    }
    $env:VK_LOADER_DEBUG = $oldEnvironment['VK_LOADER_DEBUG']

    & $executable
    if ($LASTEXITCODE -ne 0)
    {
        throw "The NativeAOT RHI probe exited with code $LASTEXITCODE."
    }
}
finally
{
    try
    {
        Restore-VulkanTestRuntimeDriver -State $driverRegistration
    }
    finally
    {
        foreach ($name in $environmentNames)
        {
            if ($null -eq $oldEnvironment[$name])
            {
                Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
            }
            else
            {
                [Environment]::SetEnvironmentVariable(
                    $name,
                    $oldEnvironment[$name],
                    'Process')
            }
        }
    }
}
