#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid = 'win-x64',
    [switch]$AotProbe,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'avalonia-vulkan-smoke-identity.ps1')
$sourceIdentity = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot $repoRoot
$buildStartedUtc = [DateTimeOffset]::UtcNow
$project = Join-Path $repoRoot `
    'tests/OpenUsd.AvaloniaVulkanSmoke/OpenUsd.AvaloniaVulkanSmoke.csproj'
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $suffix = if ($AotProbe) { "$Rid-aot" } else { $Rid }
    Join-Path $repoRoot "artifacts/avalonia-vulkan-smoke/$suffix"
}
else
{
    [System.IO.Path]::GetFullPath($OutputPath)
}

if (-not (Test-Path $openUsdRoot))
{
    throw "The OpenUSD native runtime is unavailable for $Rid at $openUsdRoot."
}
if (Test-Path $publishRoot)
{
    Remove-Item $publishRoot -Recurse -Force
}

$publishArguments = @(
    'publish', $project,
    '-c', 'Release',
    '-f', 'net10.0',
    '-r', $Rid,
    '-o', $publishRoot
)
if ($AotProbe)
{
    if ($Rid -ne 'win-x64')
    {
        throw 'The NativeAOT presenter probe is currently configured for win-x64.'
    }
    $publishArguments += @('--self-contained', 'true', '-p:AotProbe=true')
}
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$binTarget = Join-Path $publishRoot 'bin'
$libTarget = Join-Path $publishRoot 'lib'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget | Out-Null
foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path $layout.Source)
    {
        Get-ChildItem $layout.Source -File |
            Where-Object Name -Match '\.dll$|\.so($|\.)' |
            Copy-Item -Destination $layout.Target -Force
    }
}

$pluginPath = Join-Path $publishRoot 'plugin/usd'
New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
foreach ($pluginSource in @(
    (Join-Path $openUsdRoot 'lib/usd'),
    (Join-Path $openUsdRoot 'plugin/usd'),
    (Join-Path $shimRoot 'plugin/usd')))
{
    if (Test-Path $pluginSource)
    {
        Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
    }
}

Copy-Item (Join-Path $repoRoot 'test-assets/managed-authored.usda') `
    (Join-Path $publishRoot 'avalonia-vulkan-smoke.usda') -Force

$nativeLibrary = if ($Rid -eq 'win-x64')
{
    Join-Path $binTarget 'openusd_hdsilk.dll'
}
else
{
    Join-Path $libTarget 'libopenusd_hdsilk.so'
}
$managedExecutable = if ($Rid -eq 'win-x64')
{
    'OpenUsd.AvaloniaVulkanSmoke.exe'
}
else
{
    'OpenUsd.AvaloniaVulkanSmoke'
}
$identityExecutable = if ($AotProbe)
{
    $managedExecutable
}
else
{
    'OpenUsd.AvaloniaVulkanSmoke.dll'
}
$capability = [ordered]@{
    rid = $Rid
    publishRoot = $publishRoot
    managedApp = Test-Path (Join-Path $publishRoot $managedExecutable)
    nativeAot = [bool]$AotProbe
    nativeRuntime = Test-Path $nativeLibrary
    pluginMetadata = Test-Path (Join-Path $pluginPath 'hdSilk/resources/plugInfo.json')
}
$capability | ConvertTo-Json | Set-Content `
    (Join-Path $publishRoot 'publish-capability.json')
$finalSourceIdentity = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot $repoRoot
if ($sourceIdentity.sourceSha256 -cne $finalSourceIdentity.sourceSha256 -or
    $sourceIdentity.latestSourceWriteUtc -cne $finalSourceIdentity.latestSourceWriteUtc -or
    $sourceIdentity.sourceFileCount -ne $finalSourceIdentity.sourceFileCount)
{
    throw 'Vulkan smoke sources changed while publishing; stale output was rejected.'
}
$executableIdentity = Get-AvaloniaVulkanSmokeExecutableIdentity `
    -ExecutablePath (Join-Path $publishRoot $identityExecutable)
$buildIdentity = [ordered]@{
    schemaVersion = 1
    rid = $Rid
    nativeAot = [bool]$AotProbe
    executableFile = $identityExecutable
    sourceSha256 = $sourceIdentity.sourceSha256
    sourceFileCount = $sourceIdentity.sourceFileCount
    latestSourceWriteUtc = $sourceIdentity.latestSourceWriteUtc
    executableSha256 = $executableIdentity.executableSha256
    executableLength = $executableIdentity.executableLength
    executableLastWriteUtc = $executableIdentity.executableLastWriteUtc
    buildStartedUtc = $buildStartedUtc.ToString('O')
    buildCompletedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceManifest = $sourceIdentity.manifest
}
Assert-AvaloniaVulkanSmokeIdentity `
    -Expected ([pscustomobject]$buildIdentity) `
    -Source $finalSourceIdentity `
    -Executable $executableIdentity
$buildIdentity | ConvertTo-Json -Depth 5 | Set-Content `
    (Join-Path $publishRoot 'build-identity.json')
$capability | ConvertTo-Json
$buildIdentity | ConvertTo-Json -Depth 3
Write-Output $publishRoot
