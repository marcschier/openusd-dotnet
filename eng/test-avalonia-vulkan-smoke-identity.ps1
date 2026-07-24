# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'avalonia-vulkan-smoke-identity.ps1')
$workRoot = Join-Path $repoRoot 'artifacts/avalonia-vulkan-smoke/identity-test'
Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $workRoot -ItemType Directory -Force | Out-Null
$executable = Join-Path $workRoot 'probe.exe'
try
{
    [IO.File]::WriteAllBytes($executable, [byte[]](1, 2, 3, 4))
    $source = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot $repoRoot
    $binary = Get-AvaloniaVulkanSmokeExecutableIdentity -ExecutablePath $executable
    $expected = [pscustomobject]@{
        sourceSha256 = $source.sourceSha256
        sourceFileCount = $source.sourceFileCount
        latestSourceWriteUtc = $source.latestSourceWriteUtc
        executableSha256 = $binary.executableSha256
        executableLength = $binary.executableLength
        executableLastWriteUtc = $binary.executableLastWriteUtc
        buildCompletedUtc = [DateTimeOffset]::UtcNow.AddSeconds(1).ToString('O')
    }
    Assert-AvaloniaVulkanSmokeIdentity `
        -Expected $expected -Source $source -Executable $binary

    [IO.File]::AppendAllText($executable, 'stale')
    $changedBinary = Get-AvaloniaVulkanSmokeExecutableIdentity -ExecutablePath $executable
    $binaryRejected = $false
    try
    {
        Assert-AvaloniaVulkanSmokeIdentity `
            -Expected $expected -Source $source -Executable $changedBinary
    }
    catch
    {
        $binaryRejected = $_.Exception.Message.Contains('Stale Vulkan smoke evidence')
    }
    if (-not $binaryRejected)
    {
        throw 'A modified executable was not rejected as stale.'
    }

    $changedSource = $source.PSObject.Copy()
    $changedSource.sourceSha256 = '0' * 64
    $sourceRejected = $false
    try
    {
        Assert-AvaloniaVulkanSmokeIdentity `
            -Expected $expected -Source $changedSource -Executable $binary
    }
    catch
    {
        $sourceRejected = $_.Exception.Message.Contains('Stale Vulkan smoke evidence')
    }
    if (-not $sourceRejected)
    {
        throw 'A modified source identity was not rejected as stale.'
    }

    Write-Output 'AVALONIA_VULKAN_IDENTITY_TEST passed source=true executable=true'
}
finally
{
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
