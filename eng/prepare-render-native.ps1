#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [ValidateSet('build', 'archive')]
    [string]$Source = 'build',
    [string]$ArchiveUri,
    [string]$ArchivePath,
    [string]$ArchiveSha256,
    [int]$Jobs = [Environment]::ProcessorCount,
    [string]$InstallRoot,
    [string]$WorkRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw |
    ConvertFrom-Json
$defaultInstallRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot 'native/install'))
$InstallRoot = if ([string]::IsNullOrWhiteSpace($InstallRoot))
{
    $defaultInstallRoot
}
else
{
    [System.IO.Path]::GetFullPath($InstallRoot)
}
$WorkRoot = if ([string]::IsNullOrWhiteSpace($WorkRoot))
{
    Join-Path $repoRoot "artifacts/native-input/$Rid"
}
else
{
    [System.IO.Path]::GetFullPath($WorkRoot)
}
$openUsdRoot = Join-Path $InstallRoot $Rid
$shimRoot = Join-Path $InstallRoot "shim/$Rid"
$metadataPath = Join-Path $WorkRoot 'source.json'
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

function Invoke-CheckedTar
{
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & tar @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw $FailureMessage
    }
}

function Get-ValidatedTarMembers
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$Compressed,
        [string[]]$AllowedRoots
    )

    $listArguments = if ($Compressed) { @('-tzf', $Path) } else { @('-tf', $Path) }
    $members = @(& tar @listArguments)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not inspect native artifact $Path."
    }

    foreach ($member in $members)
    {
        $normalized = $member.Replace('\', '/')
        while ($normalized.StartsWith('./', [StringComparison]::Ordinal))
        {
            $normalized = $normalized.Substring(2)
        }
        $normalized = $normalized.TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($normalized))
        {
            continue
        }
        $segments = $normalized.Split('/', [StringSplitOptions]::RemoveEmptyEntries)
        if ($normalized.StartsWith('/', [StringComparison]::Ordinal) -or
            $normalized -match '^[A-Za-z]:' -or
            $segments -contains '..')
        {
            throw "Native artifact contains an unsafe path: $member"
        }
        if ($null -ne $AllowedRoots)
        {
            $allowed = $false
            foreach ($allowedRoot in $AllowedRoots)
            {
                if ($normalized -eq $allowedRoot -or
                    $normalized.StartsWith(
                        "$allowedRoot/",
                        [StringComparison]::Ordinal))
                {
                    $allowed = $true
                    break
                }
            }
            if (-not $allowed)
            {
                throw "Validated transfer contains an unexpected path: $member"
            }
        }
    }
    return $members
}

function Assert-SafeLinks
{
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$SearchRoots
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $rootPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows)
    {
        [StringComparison]::OrdinalIgnoreCase
    }
    else
    {
        [StringComparison]::Ordinal
    }

    foreach ($searchRoot in $SearchRoots)
    {
        $searchRootItem = Get-Item $searchRoot -Force
        if ($searchRootItem.LinkType -eq 'SymbolicLink')
        {
            throw "Native artifact contains a directory symlink: $searchRoot"
        }
        foreach ($item in Get-ChildItem $searchRoot -Recurse -Force)
        {
            if ($item.LinkType -ne 'SymbolicLink')
            {
                continue
            }
            # A directory symlink is held to the same rules as a file symlink
            # rather than rejected outright. The macOS install ships intra-tree
            # directory links, and what makes a link unsafe is being absolute or
            # resolving outside the install tree, not what it points at.
            # Get-ChildItem does not follow reparse points, so this cannot loop.
            $container = if ($item.PSIsContainer)
            {
                $item.Parent.FullName
            }
            else
            {
                $item.DirectoryName
            }
            foreach ($target in @($item.Target))
            {
                if ([string]::IsNullOrWhiteSpace($target) -or
                    [System.IO.Path]::IsPathRooted($target))
                {
                    throw "Native artifact contains an unsafe symlink: $($item.FullName)"
                }
                $targetPath = [System.IO.Path]::GetFullPath(
                    (Join-Path $container $target))
                if (-not $targetPath.StartsWith($rootPrefix, $comparison))
                {
                    throw "Native artifact symlink escapes its install tree: $($item.FullName)"
                }
            }
        }
    }
}

function Assert-NativeLayout
{
    param(
        [Parameter(Mandatory = $true)][string]$OpenUsdRoot,
        [Parameter(Mandatory = $true)][string]$ShimRoot
    )

    $requiredPaths = @(
        (Join-Path $OpenUsdRoot 'lib/usd/plugInfo.json'),
        (Join-Path $OpenUsdRoot 'plugin/usd/hdStorm/resources/plugInfo.json'),
        (Join-Path $ShimRoot 'plugin/usd/hdSilk/resources/plugInfo.json'))
    if ($Rid -eq 'win-x64')
    {
        $requiredPaths += Join-Path $ShimRoot 'bin/openusd_hydra.dll'
    }
    elseif ($Rid -eq 'linux-x64')
    {
        $requiredPaths += @(
            (Join-Path $ShimRoot 'lib/libopenusd_hydra.so'),
            (Join-Path $ShimRoot 'lib/libopenusd_storm_child.so'))
    }
    else
    {
        $requiredPaths += @(
            (Join-Path $ShimRoot 'lib/libopenusd_hydra.dylib'),
            (Join-Path $ShimRoot 'lib/libopenusd_storm_child.dylib'))
    }
    foreach ($requiredPath in $requiredPaths)
    {
        if (-not (Test-Path $requiredPath))
        {
            throw "Native artifact layout is incomplete: $requiredPath"
        }
    }
}

if ($Source -eq 'build')
{
    if ($InstallRoot -ne $defaultInstallRoot)
    {
        throw 'A custom InstallRoot is supported only for archive input.'
    }
    & (Join-Path $PSScriptRoot 'build-native.ps1') -Rid $Rid -Jobs $Jobs
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
    $sourceMetadata = [ordered]@{
        source = 'build'
        rid = $Rid
        openUsdCommit = $lock.openUsd.commit
        openUsdArchiveSha256 = $lock.openUsd.archiveSha256
    }
}
else
{
    $hasArchiveUri = -not [string]::IsNullOrWhiteSpace($ArchiveUri)
    $hasArchivePath = -not [string]::IsNullOrWhiteSpace($ArchivePath)
    if ($hasArchiveUri -eq $hasArchivePath -or
        [string]::IsNullOrWhiteSpace($ArchiveSha256))
    {
        throw 'Provide exactly one of ArchiveUri or ArchivePath plus ArchiveSha256.'
    }
    if ($ArchiveSha256 -notmatch '^[0-9A-Fa-f]{64}$')
    {
        throw 'ArchiveSha256 must be a full SHA-256 digest.'
    }

    $stagedArchivePath = Join-Path $WorkRoot 'native.tar.gz'
    $extractRoot = Join-Path $WorkRoot 'extracted'
    $stagedInstallRoot = Join-Path $WorkRoot 'validated-install'
    $transferArchivePath = Join-Path $WorkRoot 'validated-native.tar'
    Remove-Item $stagedArchivePath -Force -ErrorAction SilentlyContinue
    Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $stagedInstallRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $transferArchivePath -Force -ErrorAction SilentlyContinue

    if ($hasArchiveUri)
    {
        $archiveUriValue = [uri]$ArchiveUri
        if ($archiveUriValue.Scheme -ne 'https')
        {
            throw 'Native artifact downloads must use HTTPS.'
        }
        Invoke-WebRequest -Uri $ArchiveUri -OutFile $stagedArchivePath
        $sourceDescription = $archiveUriValue.GetLeftPart([System.UriPartial]::Path)
    }
    else
    {
        $localArchivePath = [System.IO.Path]::GetFullPath($ArchivePath)
        if (-not (Test-Path $localArchivePath -PathType Leaf))
        {
            throw "Native artifact archive was not found: $localArchivePath"
        }
        [System.IO.File]::Copy($localArchivePath, $stagedArchivePath, $true)
        $sourceDescription = [System.IO.Path]::GetFileName($localArchivePath)
    }

    $actualHash = (Get-FileHash $stagedArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $ArchiveSha256)
    {
        throw "Native artifact hash mismatch. Expected $ArchiveSha256, got $actualHash."
    }

    Get-ValidatedTarMembers -Path $stagedArchivePath -Compressed | Out-Null
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Invoke-CheckedTar `
        -Arguments @('-xzf', $stagedArchivePath, '-C', $extractRoot) `
        -FailureMessage "Could not extract native artifact $stagedArchivePath."
    $archiveInstallRoot = Join-Path $extractRoot 'native/install'
    $archiveOpenUsdRoot = Join-Path $archiveInstallRoot $Rid
    $archiveShimRoot = Join-Path $archiveInstallRoot "shim/$Rid"
    if (-not (Test-Path $archiveOpenUsdRoot -PathType Container) -or
        -not (Test-Path $archiveShimRoot -PathType Container))
    {
        throw "The archive must contain native/install/$Rid and native/install/shim/$Rid."
    }
    Assert-SafeLinks `
        -Root $archiveInstallRoot `
        -SearchRoots @($archiveOpenUsdRoot, $archiveShimRoot)
    Assert-NativeLayout $archiveOpenUsdRoot $archiveShimRoot

    Invoke-CheckedTar `
        -Arguments @(
            '-cf',
            $transferArchivePath,
            '-C',
            $archiveInstallRoot,
            $Rid,
            "shim/$Rid") `
        -FailureMessage 'Could not create the validated native subtree archive.'
    Get-ValidatedTarMembers `
        -Path $transferArchivePath `
        -AllowedRoots @($Rid, "shim/$Rid") |
        Out-Null

    New-Item -ItemType Directory -Force -Path $stagedInstallRoot | Out-Null
    Invoke-CheckedTar `
        -Arguments @('-xf', $transferArchivePath, '-C', $stagedInstallRoot) `
        -FailureMessage 'Could not stage the validated native subtree archive.'
    $stagedOpenUsdRoot = Join-Path $stagedInstallRoot $Rid
    $stagedShimRoot = Join-Path $stagedInstallRoot "shim/$Rid"
    Assert-SafeLinks `
        -Root $stagedInstallRoot `
        -SearchRoots @($stagedOpenUsdRoot, $stagedShimRoot)
    Assert-NativeLayout $stagedOpenUsdRoot $stagedShimRoot

    Remove-Item $openUsdRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $shimRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $InstallRoot |
        Out-Null
    Invoke-CheckedTar `
        -Arguments @('-xf', $transferArchivePath, '-C', $InstallRoot) `
        -FailureMessage 'Could not install the validated native subtrees.'
    Assert-SafeLinks `
        -Root $InstallRoot `
        -SearchRoots @($openUsdRoot, $shimRoot)
    Assert-NativeLayout $openUsdRoot $shimRoot

    $sourceMetadata = [ordered]@{
        source = 'archive'
        rid = $Rid
        artifact = $sourceDescription
        sha256 = $actualHash
        expectedOpenUsdCommit = $lock.openUsd.commit
    }
}

Assert-NativeLayout $openUsdRoot $shimRoot
& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
    -Operation Write `
    -Rid $Rid `
    -InstallRoot $InstallRoot
& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
    -Operation Verify `
    -Rid $Rid `
    -InstallRoot $InstallRoot
$sourceMetadata | ConvertTo-Json | Set-Content $metadataPath
Write-Output "Prepared pinned $Rid native input from $Source."
