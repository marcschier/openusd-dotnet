#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = Join-Path $repoRoot 'artifacts/archive-preservation-test'
$sourceRoot = Join-Path $testRoot 'source'
$installRoot = Join-Path $testRoot 'install'
$pipelineInstallRoot = Join-Path $testRoot 'pipeline-install'
$workRoot = Join-Path $testRoot 'work'
$pipelineWorkRoot = Join-Path $testRoot 'pipeline-work'
$archivePath = Join-Path $testRoot 'synthetic-native.tar.gz'
$canonicalArchivePath = Join-Path $testRoot 'canonical-native.tar.gz'
$canonicalMetadataPath = Join-Path $testRoot 'native-artifact.json'
$rid = if ($IsWindows)
{
    'win-x64'
}
elseif ($IsMacOS)
{
    'osx-arm64'
}
else
{
    'linux-x64'
}

function Write-TestFile
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Content = '{}'
    )

    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($Path)) |
        Out-Null
    [System.IO.File]::WriteAllText($Path, $Content)
}

try
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    $sourceInstallRoot = Join-Path $sourceRoot 'native/install'
    $sourceOpenUsdRoot = Join-Path $sourceInstallRoot $rid
    $sourceShimRoot = Join-Path $sourceInstallRoot "shim/$rid"
    $sourceStormChildHeader = Join-Path `
        $repoRoot `
        'native/openusd_storm_child/include/openusd_storm_child.h'
    $archiveStormChildHeader = Join-Path `
        $sourceShimRoot `
        'include/openusd_storm_child.h'
    $sourceRenderCameraHeader = Join-Path `
        $repoRoot `
        'native/include/openusd_render_camera.h'
    $archiveRenderCameraHeader = Join-Path `
        $sourceShimRoot `
        'include/openusd_render_camera.h'
    New-Item -ItemType Directory -Force -Path (
        [System.IO.Path]::GetDirectoryName($archiveStormChildHeader)) |
        Out-Null
    if ($rid -ceq 'win-x64')
    {
        # A real win-x64 install carries the locked Vulkan loader beside the
        # per-RID subtree, and the archive now transports it so the runtime
        # packages can be packed away from a Windows build host.
        $sourceVulkanBin = Join-Path $sourceInstallRoot 'vulkan-sdk-1.4.321.0/bin'
        New-Item -ItemType Directory -Force -Path $sourceVulkanBin | Out-Null
        Set-Content `
            -Path (Join-Path $sourceVulkanBin 'vulkan-1.dll') `
            -Value 'synthetic vulkan loader' `
            -NoNewline
    }
    [System.IO.File]::Copy(
        $sourceStormChildHeader,
        $archiveStormChildHeader,
        $true)
    [System.IO.File]::Copy(
        $sourceRenderCameraHeader,
        $archiveRenderCameraHeader,
        $true)
    foreach ($header in @(
        @{
            Source = 'native/openusd_dotnet/include/openusd_dotnet.h'
            Name = 'openusd_dotnet.h'
        },
        @{
            Source = 'native/openusd_hydra/include/openusd_hydra.h'
            Name = 'openusd_hydra.h'
        },
        @{
            Source = 'native/hdSilk/include/openusd_hdsilk.h'
            Name = 'openusd_hdsilk.h'
        },
        @{
            Source = 'native/include/openusd_render_pick.h'
            Name = 'openusd_render_pick.h'
        }))
    {
        [System.IO.File]::Copy(
            (Join-Path $repoRoot $header.Source),
            (Join-Path $sourceShimRoot "include/$($header.Name)"),
            $true)
    }
    Write-TestFile (Join-Path $sourceOpenUsdRoot 'lib/usd/plugInfo.json')
    Write-TestFile (
        Join-Path $sourceOpenUsdRoot 'plugin/usd/hdStorm/resources/plugInfo.json')
    Write-TestFile (
        Join-Path $sourceShimRoot 'plugin/usd/hdSilk/resources/plugInfo.json')
    Write-TestFile (Join-Path $sourceRoot 'unrelated/should-not-install.txt') 'ignored'

    if ($IsWindows)
    {
        Write-TestFile (Join-Path $sourceOpenUsdRoot 'lib/usd_ms.dll') 'synthetic'
        foreach ($name in @(
            'openusd_dotnet.dll',
            'openusd_hydra.dll',
            'openusd_hdsilk.dll',
            'openusd_storm_child.dll'))
        {
            Write-TestFile (Join-Path $sourceShimRoot "bin/$name") 'synthetic'
        }
    }
    elseif ($IsMacOS)
    {
        Write-TestFile (Join-Path $sourceOpenUsdRoot 'lib/libusd_ms.dylib') 'synthetic'
        $versionedLibrary = Join-Path $sourceShimRoot 'lib/libopenusd_hydra.1.dylib'
        $libraryLink = Join-Path $sourceShimRoot 'lib/libopenusd_hydra.dylib'
        $stormChildLibrary =
            Join-Path $sourceShimRoot 'lib/libopenusd_storm_child.dylib'
        Write-TestFile (
            Join-Path $sourceShimRoot 'lib/libopenusd_dotnet.dylib') 'synthetic'
        Write-TestFile (
            Join-Path $sourceShimRoot 'lib/libopenusd_hdsilk.dylib') 'synthetic'
        $executable = Join-Path $sourceShimRoot 'bin/native-tool'
        Write-TestFile $versionedLibrary 'synthetic'
        Write-TestFile $stormChildLibrary 'synthetic'
        Write-TestFile $executable '#!/bin/sh'
        & chmod 755 $versionedLibrary $stormChildLibrary $executable
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not set executable modes on synthetic native files.'
        }
        & ln -s 'libopenusd_hydra.1.dylib' $libraryLink
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not create the synthetic native symlink.'
        }
    }
    else
    {
        Write-TestFile (Join-Path $sourceOpenUsdRoot 'lib/libusd_ms.so') 'synthetic'
        $versionedLibrary = Join-Path $sourceShimRoot 'lib/libopenusd_hydra.so.1'
        $libraryLink = Join-Path $sourceShimRoot 'lib/libopenusd_hydra.so'
        $stormChildLibrary =
            Join-Path $sourceShimRoot 'lib/libopenusd_storm_child.so.7.0.0'
        $stormChildSonameLink =
            Join-Path $sourceShimRoot 'lib/libopenusd_storm_child.so.7'
        $stormChildLink = Join-Path $sourceShimRoot 'lib/libopenusd_storm_child.so'
        Write-TestFile (
            Join-Path $sourceShimRoot 'lib/libopenusd_dotnet.so') 'synthetic'
        Write-TestFile (
            Join-Path $sourceShimRoot 'lib/libopenusd_hdsilk.so') 'synthetic'
        $executable = Join-Path $sourceShimRoot 'bin/native-tool'
        Write-TestFile $versionedLibrary 'synthetic'
        Write-TestFile $stormChildLibrary 'synthetic'
        Write-TestFile $executable '#!/bin/sh'
        & chmod 755 $versionedLibrary $stormChildLibrary $executable
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not set executable modes on synthetic native files.'
        }
        & ln -s 'libopenusd_hydra.so.1' $libraryLink
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not create the synthetic native symlink.'
        }
        & ln -s 'libopenusd_storm_child.so.7' $stormChildLink
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not create the synthetic Storm child SONAME symlink.'
        }
        & ln -s 'libopenusd_storm_child.so.7.0.0' $stormChildSonameLink
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Could not create the synthetic Storm child VERSION symlink.'
        }
    }

    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    & tar -czf $archivePath -C $sourceRoot native unrelated
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not create the synthetic native archive.'
    }
    $archiveHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash
    $hashRejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'prepare-render-native.ps1') `
            -Rid $rid `
            -Source archive `
            -ArchivePath $archivePath `
            -ArchiveSha256 ('0' * 64) `
            -InstallRoot $installRoot `
            -WorkRoot $workRoot
    }
    catch
    {
        if ($_.Exception.Message -notmatch 'hash mismatch')
        {
            throw
        }
        $hashRejected = $true
    }
    if (-not $hashRejected)
    {
        throw 'The synthetic archive was not rejected for an incorrect SHA-256.'
    }

    & (Join-Path $PSScriptRoot 'prepare-render-native.ps1') `
        -Rid $rid `
        -Source archive `
        -ArchivePath $archivePath `
        -ArchiveSha256 $archiveHash `
        -InstallRoot $installRoot `
        -WorkRoot $workRoot
    if (Test-Path (Join-Path $installRoot 'unrelated'))
    {
        throw 'Archive content outside the validated native subtrees was installed.'
    }
    $installedStormChildHeader = Join-Path `
        $installRoot `
        "shim/$rid/include/openusd_storm_child.h"
    if ((Get-FileHash $installedStormChildHeader -Algorithm SHA256).Hash -cne
        (Get-FileHash $sourceStormChildHeader -Algorithm SHA256).Hash)
    {
        throw 'The synthetic archive did not preserve the exact Storm child header bytes.'
    }
    $installedRenderCameraHeader = Join-Path `
        $installRoot `
        "shim/$rid/include/openusd_render_camera.h"
    if ((Get-FileHash $installedRenderCameraHeader -Algorithm SHA256).Hash -cne
        (Get-FileHash $sourceRenderCameraHeader -Algorithm SHA256).Hash)
    {
        throw 'The synthetic archive did not preserve the exact render camera header bytes.'
    }

    & (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
        -Operation Write `
        -Rid $rid `
        -InstallRoot $sourceInstallRoot
    & (Join-Path $PSScriptRoot 'create-native-archive.ps1') `
        -Rid $rid `
        -InstallRoot $sourceInstallRoot `
        -OutputPath $canonicalArchivePath `
        -MetadataOutputPath $canonicalMetadataPath | Out-Null
    $canonicalMetadata = Get-Content $canonicalMetadataPath -Raw | ConvertFrom-Json
    if ([string]$canonicalMetadata.rid -cne $rid -or
        [string]$canonicalMetadata.archiveSha256 -cne
            (Get-FileHash $canonicalArchivePath -Algorithm SHA256).Hash)
    {
        throw 'The canonical native archive sidecar does not match its archive.'
    }
    $canonicalMembers = @(& tar -tf $canonicalArchivePath)
    if ($canonicalMembers | Where-Object { $_ -like 'native/unrelated*' })
    {
        throw 'The canonical native archive included content outside the install subtrees.'
    }
    & (Join-Path $PSScriptRoot 'prepare-workflow-native-input.ps1') `
        -Rid $rid `
        -Source archive `
        -PipelineRoot $testRoot `
        -InstallRoot $pipelineInstallRoot `
        -WorkRoot $pipelineWorkRoot
    if (-not (Test-Path (
            Join-Path $pipelineInstallRoot "shim/$rid/include/openusd_dotnet.h")))
    {
        throw 'The workflow native input helper did not install the pipeline archive.'
    }

    if ($rid -ceq 'win-x64')
    {
        # The loader has to arrive where the runtime package targets look for it,
        # otherwise the win-x64 packages can only be packed on a Windows host that
        # built it, which is what carrying it in the archive exists to avoid.
        $installedLoaders = @(Get-ChildItem `
            -Path (Join-Path $installRoot 'vulkan-sdk-*/bin/vulkan-1.dll') `
            -ErrorAction SilentlyContinue)
        if ($installedLoaders.Count -ne 1)
        {
            throw (
                'The archive did not install exactly one ' +
                "vulkan-sdk-*/bin/vulkan-1.dll; found $($installedLoaders.Count).")
        }
    }

    if ($IsWindows)
    {
        Write-Output 'Synthetic subtree and SHA-256 validation passed.'
        Write-Output 'Windows validation does not prove Unix symlink or executable-mode preservation.'
    }
    else
    {
        $installedShimRoot = Join-Path $installRoot "shim/$rid"
        $libraryLinkName = if ($IsMacOS)
        {
            'lib/libopenusd_hydra.dylib'
        }
        else
        {
            'lib/libopenusd_hydra.so'
        }
        $installedLink = Join-Path $installedShimRoot $libraryLinkName
        $installedExecutable = Join-Path $installedShimRoot 'bin/native-tool'
        & test -L $installedLink
        if ($LASTEXITCODE -ne 0)
        {
            throw 'The installed native library link is not a symbolic link.'
        }
        & test -x $installedExecutable
        if ($LASTEXITCODE -ne 0)
        {
            throw 'The installed synthetic native tool lost its executable mode.'
        }
        $linkTarget = & readlink $installedLink
        $expectedLinkTarget = if ($IsMacOS)
        {
            'libopenusd_hydra.1.dylib'
        }
        else
        {
            'libopenusd_hydra.so.1'
        }
        if ($LASTEXITCODE -ne 0 -or $linkTarget -ne $expectedLinkTarget)
        {
            throw "The installed native library link target is incorrect: $linkTarget"
        }
        if (-not $IsMacOS)
        {
            $installedStormLink = Join-Path `
                $installedShimRoot `
                'lib/libopenusd_storm_child.so'
            & test -L $installedStormLink
            $stormLinkTarget = & readlink $installedStormLink
            if ($LASTEXITCODE -ne 0 -or
                $stormLinkTarget -cne 'libopenusd_storm_child.so.7')
            {
                throw (
                    'The installed Storm child ABI-7 SONAME link target is incorrect: ' +
                    $stormLinkTarget)
            }
            $installedStormSonameLink = Join-Path `
                $installedShimRoot `
                'lib/libopenusd_storm_child.so.7'
            & test -L $installedStormSonameLink
            $stormSonameLinkTarget = & readlink $installedStormSonameLink
            if ($LASTEXITCODE -ne 0 -or
                $stormSonameLinkTarget -cne 'libopenusd_storm_child.so.7.0.0')
            {
                throw (
                    'The installed Storm child VERSION link target is incorrect: ' +
                    $stormSonameLinkTarget)
            }
        }
        Write-Output 'Synthetic SHA-256, subtree, symlink, and executable-mode validation passed.'
    }
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
