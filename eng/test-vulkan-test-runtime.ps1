#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vulkan-test-runtime-registry.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = [System.IO.Path]::GetFullPath($Root)
$workRoot = Join-Path $repoRoot "artifacts/vulkan-test-runtime-self-test/$Rid"
$lock = Get-Content (Join-Path $PSScriptRoot 'vulkan-test-runtime.lock.json') -Raw |
    ConvertFrom-Json
$runtime = $lock.runtimes.$Rid
if ($null -eq $runtime)
{
    throw "The Vulkan test runtime lock does not contain '$Rid'."
}
$environmentNames = @(
    'PATH',
    'LD_LIBRARY_PATH',
    'DYLD_LIBRARY_PATH',
    'VK_DRIVER_FILES',
    'VK_ICD_FILENAMES',
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

function Resolve-SourceAsset
{
    param([Parameter(Mandatory = $true)][string]$FileName)

    foreach ($candidate in @(
        (Join-Path $sourceRoot $FileName),
        (Join-Path $sourceRoot "runtimes/$Rid/native/$FileName")))
    {
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return $candidate
        }
    }
    throw "The self-test could not find '$FileName' under '$sourceRoot'."
}

function Test-RegistryRegistrationRoundTrip
{
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not $IsWindows)
    {
        Write-Host '[vulkan-runtime] registry-self-test skipped platform=non-Windows'
        return
    }

    $hive = [Microsoft.Win32.RegistryHive]::CurrentUser
    $view = [Microsoft.Win32.RegistryView]::Registry64
    $testRoot = "SOFTWARE\OpenUsd.VulkanRuntimeSelfTest-$([Guid]::NewGuid())"
    $driversPath = "$testRoot\Drivers"
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
    try
    {
        $missingState = Register-VulkanTestRuntimeDriver `
            -ManifestPath $ManifestPath `
            -Hive $hive `
            -View $view `
            -SubKeyPath $driversPath `
            -ForceRegistration
        try
        {
            $active = Get-VulkanTestRuntimeDriverRegistration `
                -ManifestPath $ManifestPath `
                -Hive $hive `
                -View $view `
                -SubKeyPath $driversPath
            if (-not $active.Exists -or
                $active.Kind -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
                [int]$active.Value -ne 0)
            {
                throw 'The registry self-test did not activate a missing value.'
            }
        }
        finally
        {
            Restore-VulkanTestRuntimeDriver -State $missingState
        }

        $remaining = $baseKey.OpenSubKey($testRoot, $false)
        if ($null -ne $remaining)
        {
            $remaining.Dispose()
            throw 'The registry self-test left newly created keys behind.'
        }

        $key = $baseKey.CreateSubKey($driversPath, $true)
        try
        {
            $key.SetValue(
                $ManifestPath,
                'prior-value',
                [Microsoft.Win32.RegistryValueKind]::String)
        }
        finally
        {
            $key.Dispose()
        }

        $priorState = Register-VulkanTestRuntimeDriver `
            -ManifestPath $ManifestPath `
            -Hive $hive `
            -View $view `
            -SubKeyPath $driversPath `
            -ForceRegistration
        try
        {
            $active = Get-VulkanTestRuntimeDriverRegistration `
                -ManifestPath $ManifestPath `
                -Hive $hive `
                -View $view `
                -SubKeyPath $driversPath
            if (-not $active.Exists -or
                $active.Kind -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
                [int]$active.Value -ne 0)
            {
                throw 'The registry self-test did not replace a prior value.'
            }
        }
        finally
        {
            Restore-VulkanTestRuntimeDriver -State $priorState
        }

        $restored = Get-VulkanTestRuntimeDriverRegistration `
            -ManifestPath $ManifestPath `
            -Hive $hive `
            -View $view `
            -SubKeyPath $driversPath
        if (-not $restored.Exists -or
            $restored.Kind -ne [Microsoft.Win32.RegistryValueKind]::String -or
            [string]$restored.Value -cne 'prior-value')
        {
            throw 'The registry self-test did not restore the exact prior value.'
        }
        Write-Host '[vulkan-runtime] registry-self-test passed'
    }
    finally
    {
        $baseKey.DeleteSubKeyTree($testRoot, $false)
        $baseKey.Dispose()
    }
}

Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
try
{
    $loader = Resolve-SourceAsset ([string]$runtime.loader)
    $driver = Resolve-SourceAsset ([string]$runtime.driver)
    Copy-Item -LiteralPath $loader -Destination $workRoot
    Copy-Item -LiteralPath $driver -Destination $workRoot

    $manifestPath = & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
        -Root $workRoot `
        -Rid $Rid `
        -Activate
    $driverRegistration = Register-VulkanTestRuntimeDriver `
        -ManifestPath $manifestPath
    Test-RegistryRegistrationRoundTrip -ManifestPath $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (-not [System.IO.Path]::IsPathFullyQualified([string]$manifest.ICD.library_path) -or
        [string]$manifest.ICD.api_version -cne [string]$lock.apiVersion -or
        $env:VK_DRIVER_FILES -cne $manifestPath -or
        $env:VK_ICD_FILENAMES -cne $manifestPath -or
        $env:OPENUSD_VULKAN_LOADER_SHA256 -cne
            [string]$runtime.loaderSha256 -or
        $env:OPENUSD_VULKAN_DRIVER_SHA256 -cne
            [string]$runtime.driverSha256 -or
        $env:OPENUSD_VULKAN_API_VERSION -cne [string]$lock.apiVersion)
    {
        throw 'The activated Vulkan test runtime does not match the lock.'
    }

    Add-Content -LiteralPath (Join-Path $workRoot ([string]$runtime.driver)) -Value 'tamper'
    $rejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
            -Root $workRoot `
            -Rid $Rid | Out-Null
    }
    catch
    {
        $rejected = $_.Exception.Message -like '*hash mismatch*'
    }
    if (-not $rejected)
    {
        throw 'The Vulkan test runtime self-test did not reject a modified driver.'
    }

    Copy-Item -LiteralPath $driver -Destination $workRoot -Force
    Copy-Item -LiteralPath $loader -Destination $workRoot -Force
    Add-Content -LiteralPath (Join-Path $workRoot ([string]$runtime.loader)) -Value 'tamper'
    $rejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
            -Root $workRoot `
            -Rid $Rid | Out-Null
    }
    catch
    {
        $rejected = $_.Exception.Message -like '*hash mismatch*'
    }
    if (-not $rejected)
    {
        throw 'The Vulkan test runtime self-test did not reject a modified loader.'
    }

    Write-Output "VULKAN_TEST_RUNTIME passed rid=$Rid"
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
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
