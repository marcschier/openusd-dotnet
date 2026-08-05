#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [string[]]$TestArguments = @(),

    [string]$RuntimeRoot,

    [string]$ProbePath,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumExpectedTests = 1
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vulkan-test-runtime-registry.ps1')
if (-not $IsWindows)
{
    throw 'The hosted Windows Vulkan conformance runner requires Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($RuntimeRoot))
{
    Join-Path $repoRoot (
        "tests/OpenUsd.Rendering.ConformanceTests/bin/$Configuration/net10.0")
}
else
{
    [System.IO.Path]::GetFullPath($RuntimeRoot)
}
$probe = if ([string]::IsNullOrWhiteSpace($ProbePath))
{
    Join-Path $repoRoot (
        "tests/OpenUsd.RhiProbe/bin/$Configuration/net10.0/OpenUsd.RhiProbe.dll")
}
else
{
    [System.IO.Path]::GetFullPath($ProbePath)
}
$testDll = Join-Path $runtimeRoot 'OpenUsd.Rendering.ConformanceTests.dll'

foreach ($path in @($testDll, $probe))
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "The Windows Vulkan conformance input does not exist: $path"
    }
}

$environmentNames = @(
    'DOTNET_STARTUP_HOOKS',
    'PATH',
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
        -Root $runtimeRoot `
        -Rid win-x64 `
        -Activate `
        -StartupHookPath $probe
    $localNativePaths = @(
        (Join-Path $repoRoot 'native/install/shim/win-x64/bin'),
        (Join-Path $repoRoot 'native/install/win-x64/lib'),
        (Join-Path $repoRoot 'native/install/win-x64/bin')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    if ($localNativePaths.Count -ne 0)
    {
        $env:PATH = ($localNativePaths -join [System.IO.Path]::PathSeparator) +
            [System.IO.Path]::PathSeparator +
            $env:PATH
    }
    $driverRegistration = Register-VulkanTestRuntimeDriver `
        -ManifestPath $manifestPath

    Write-Host '[vulkan-runtime] Running managed SwiftShader preflight.'
    $env:VK_LOADER_DEBUG = 'error,warn,driver'
    & dotnet $probe --vulkan-runtime-only
    if ($LASTEXITCODE -ne 0)
    {
        throw "The managed Vulkan runtime preflight exited with code $LASTEXITCODE."
    }
    $env:VK_LOADER_DEBUG = $oldEnvironment['VK_LOADER_DEBUG']

    $arguments = @(
        $testDll,
        '--minimum-expected-tests',
        $MinimumExpectedTests,
        '--no-ansi',
        '--progress',
        'off'
    )
    $arguments += $TestArguments
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "The Vulkan conformance tests exited with code $LASTEXITCODE."
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
