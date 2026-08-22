#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$OutputRoot = 'artifacts/mcp-distribution',
    [string]$Configuration = 'Release',
    [switch]$NoArchive,
    [string]$NativeRoot,
    [string]$ShimRoot,
    [string]$DotNetCommand = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-AbsolutePath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path))
    {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-RequiredPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Container', 'Leaf')]
        [string]$PathType,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType $PathType))
    {
        throw "The MCP $Rid native preflight is missing $Description`: $Path"
    }
}

function Get-NativeLayout
{
    switch ($Rid)
    {
        'win-x64'
        {
            return @{
                Directory = 'bin'
                OpenUsdLibrary = 'lib/usd_ms.dll'
                ShimLibraries = @(
                    'openusd_dotnet.dll',
                    'openusd_hydra.dll',
                    'openusd_hdsilk.dll')
                Executable = 'OpenUsd.Mcp.exe'
            }
        }
        'linux-x64'
        {
            return @{
                Directory = 'lib'
                OpenUsdLibrary = 'lib/libusd_ms.so'
                ShimLibraries = @(
                    'libopenusd_dotnet.so',
                    'libopenusd_hydra.so',
                    'libopenusd_hdsilk.so')
                Executable = 'OpenUsd.Mcp'
            }
        }
        'osx-arm64'
        {
            return @{
                Directory = 'lib'
                OpenUsdLibrary = 'lib/libusd_ms.dylib'
                ShimLibraries = @(
                    'libopenusd_dotnet.dylib',
                    'libopenusd_hydra.dylib',
                    'libopenusd_hdsilk.dylib')
                Executable = 'OpenUsd.Mcp'
            }
        }
    }
}

function Assert-NativePreflight
{
    param([Parameter(Mandatory = $true)][hashtable]$NativeLayout)

    foreach ($requiredRoot in @($nativeRoot, $shimRoot))
    {
        Assert-RequiredPath $requiredRoot Container 'native install directory'
    }

    Assert-RequiredPath `
        (Join-Path $nativeRoot $NativeLayout.OpenUsdLibrary) `
        Leaf `
        'Core OpenUSD native library'
    foreach ($nativeName in $NativeLayout.ShimLibraries)
    {
        Assert-RequiredPath `
            (Join-Path $shimRoot "$($NativeLayout.Directory)/$nativeName") `
            Leaf `
            "Core/Imaging shim '$nativeName'"
    }

    foreach ($required in @(
        @{
            Path = (Join-Path $nativeRoot 'lib/usd')
            Type = 'Container'
            Description = 'Core plugin directory'
        },
        @{
            Path = (Join-Path $nativeRoot 'lib/usd/plugInfo.json')
            Type = 'Leaf'
            Description = 'Core plugin metadata'
        },
        @{
            Path = (Join-Path $nativeRoot 'plugin/usd')
            Type = 'Container'
            Description = 'Imaging plugin directory'
        },
        @{
            Path = (Join-Path $nativeRoot 'plugin/usd/plugInfo.json')
            Type = 'Leaf'
            Description = 'Imaging plugin metadata'
        },
        @{
            Path = (Join-Path $nativeRoot 'plugin/usd/hdStorm/resources')
            Type = 'Container'
            Description = 'Imaging hdStorm plugin resource directory'
        },
        @{
            Path = (Join-Path $nativeRoot 'plugin/usd/hdStorm/resources/plugInfo.json')
            Type = 'Leaf'
            Description = 'Imaging hdStorm plugin metadata'
        },
        @{
            Path = (Join-Path $shimRoot 'plugin/usd')
            Type = 'Container'
            Description = 'hdSilk plugin directory'
        },
        @{
            Path = (Join-Path $shimRoot 'plugin/usd/hdSilk/resources')
            Type = 'Container'
            Description = 'hdSilk plugin resource directory'
        },
        @{
            Path = (Join-Path $shimRoot 'plugin/usd/hdSilk/resources/plugInfo.json')
            Type = 'Leaf'
            Description = 'hdSilk plugin metadata'
        }))
    {
        Assert-RequiredPath $required.Path $required.Type $required.Description
    }
}

function Get-NativeInstallRoot
{
    $installRoot = [IO.Path]::GetFullPath(
        [IO.Path]::GetDirectoryName($nativeRoot))
    $expectedNativeRoot = [IO.Path]::GetFullPath(
        (Join-Path $installRoot $Rid))
    $expectedShimRoot = [IO.Path]::GetFullPath(
        (Join-Path $installRoot "shim/$Rid"))
    $comparison = if ($IsWindows)
    {
        [StringComparison]::OrdinalIgnoreCase
    }
    else
    {
        [StringComparison]::Ordinal
    }

    if (-not $expectedNativeRoot.Equals(
            [IO.Path]::GetFullPath($nativeRoot),
            $comparison) -or
        -not $expectedShimRoot.Equals(
            [IO.Path]::GetFullPath($shimRoot),
            $comparison))
    {
        throw (
            "The MCP $Rid native roots must use the metadata install topology " +
            "'<install>/$Rid' and '<install>/shim/$Rid'. Got native root " +
            "'$nativeRoot' and shim root '$shimRoot'.")
    }

    return $installRoot
}

function Assert-NativeInstallMetadata
{
    $installRoot = Get-NativeInstallRoot
    & (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
        -Operation Verify `
        -Rid $Rid `
        -InstallRoot $installRoot |
        Out-Null
}

function Copy-DirectoryContents
{
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Target
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container))
    {
        return
    }
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force)
    {
        Copy-Item `
            -LiteralPath $item.FullName `
            -Destination $Target `
            -Recurse `
            -Force
    }
}

function Assert-StagedLayout
{
    param(
        [Parameter(Mandatory = $true)][string]$LayoutRoot,
        [Parameter(Mandatory = $true)][hashtable]$NativeLayout
    )

    $pluginRoot = Join-Path $LayoutRoot 'plugin/usd'
    foreach ($requiredPath in @(
        (Join-Path $LayoutRoot $NativeLayout.Executable),
        (Join-Path $pluginRoot 'plugInfo.json'),
        (Join-Path $pluginRoot 'hdStorm/resources/plugInfo.json'),
        (Join-Path $pluginRoot 'hdSilk/resources/plugInfo.json')))
    {
        Assert-RequiredPath $requiredPath Leaf 'staged application/plugin asset'
    }

    $requiredNativeNames = @(
        [IO.Path]::GetFileName($NativeLayout.OpenUsdLibrary)) +
        $NativeLayout.ShimLibraries
    foreach ($nativeName in $requiredNativeNames)
    {
        $nativeFile = Get-ChildItem `
            -LiteralPath $LayoutRoot `
            -Recurse `
            -File `
            -Filter $nativeName |
            Select-Object -First 1
        if ($null -eq $nativeFile)
        {
            throw "The staged MCP bundle is missing required native library '$nativeName'."
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $LayoutRoot -Recurse -File)
    {
        $relativePath = [IO.Path]::GetRelativePath($LayoutRoot, $file.FullName)
        $parts = @($relativePath -split '[\\/]')
        if ($file.Name -like 'OpenUsd.Viewer*' -or
            $file.Name -like 'OpenUsd.Runtime.Cesium*' -or
            $parts -contains 'samples' -or
            $parts -contains 'tests')
        {
            throw "The staged MCP bundle contains excluded asset '$relativePath'."
        }
    }
}

function Install-StagedDirectories
{
    param([Parameter(Mandatory = $true)][object[]]$Directories)

    $transactionId = [Guid]::NewGuid().ToString('N')
    foreach ($directory in $Directories)
    {
        $directory.Backup = Join-Path `
            ([IO.Path]::GetDirectoryName($directory.Target)) `
            ".$([IO.Path]::GetFileName($directory.Target)).backup.$transactionId"
        $directory.HadTarget = $false
        $directory.Installed = $false
    }

    try
    {
        foreach ($directory in $Directories)
        {
            if (Test-Path -LiteralPath $directory.Target)
            {
                Move-Item `
                    -LiteralPath $directory.Target `
                    -Destination $directory.Backup
                $directory.HadTarget = $true
            }
        }
        foreach ($directory in $Directories)
        {
            Move-Item `
                -LiteralPath $directory.Stage `
                -Destination $directory.Target
            $directory.Installed = $true
        }
    }
    catch
    {
        $commitError = $_
        $rollbackFailures = [Collections.Generic.List[string]]::new()
        for ($index = $Directories.Count - 1; $index -ge 0; $index--)
        {
            $directory = $Directories[$index]
            try
            {
                if ($directory.Installed -and
                    (Test-Path -LiteralPath $directory.Target))
                {
                    Remove-Item -LiteralPath $directory.Target -Recurse -Force
                }
                if ($directory.HadTarget -and
                    (Test-Path -LiteralPath $directory.Backup))
                {
                    Move-Item `
                        -LiteralPath $directory.Backup `
                        -Destination $directory.Target
                }
            }
            catch
            {
                $rollbackFailures.Add($_.Exception.Message)
            }
        }
        if ($rollbackFailures.Count -ne 0)
        {
            throw (
                "MCP output replacement failed: $($commitError.Exception.Message) " +
                "Rollback also failed: $($rollbackFailures -join '; ')")
        }
        throw $commitError
    }

    foreach ($directory in $Directories)
    {
        if ($directory.HadTarget)
        {
            Remove-Item -LiteralPath $directory.Backup -Recurse -Force
        }
    }
}

$outputRoot = Get-AbsolutePath $OutputRoot
$nativeRoot = if ([string]::IsNullOrWhiteSpace($NativeRoot))
{
    Join-Path $repoRoot "native/install/$Rid"
}
else
{
    Get-AbsolutePath $NativeRoot
}
$shimRoot = if ([string]::IsNullOrWhiteSpace($ShimRoot))
{
    Join-Path $repoRoot "native/install/shim/$Rid"
}
else
{
    Get-AbsolutePath $ShimRoot
}
$project = Join-Path $repoRoot 'src/OpenUsd.Mcp/OpenUsd.Mcp.csproj'
$layoutRoot = Join-Path $outputRoot "layout/$Rid"
$artifactRoot = Join-Path $outputRoot "artifacts/$Rid"
$layoutParent = [IO.Path]::GetDirectoryName($layoutRoot)
$artifactParent = [IO.Path]::GetDirectoryName($artifactRoot)
$stageId = [Guid]::NewGuid().ToString('N')
$stagedLayoutRoot = Join-Path $layoutParent ".$Rid.tmp.$stageId"
$stagedArtifactRoot = Join-Path $artifactParent ".$Rid.tmp.$stageId"
$nativeLayout = Get-NativeLayout

# Preflight every source dependency and target shape before creating staging output.
Assert-NativePreflight $nativeLayout
Assert-NativeInstallMetadata
Get-Command $DotNetCommand -ErrorAction Stop | Out-Null
foreach ($target in @($layoutRoot, $artifactRoot))
{
    if ((Test-Path -LiteralPath $target) -and
        -not (Test-Path -LiteralPath $target -PathType Container))
    {
        throw "The MCP output target is not a directory: $target"
    }
}

try
{
    New-Item -ItemType Directory -Force -Path $layoutParent, $artifactParent |
        Out-Null
    New-Item `
        -ItemType Directory `
        -Path $stagedLayoutRoot, $stagedArtifactRoot |
        Out-Null

    & $DotNetCommand publish $project `
        -c $Configuration `
        -f net10.0 `
        -r $Rid `
        --self-contained true `
        -o $stagedLayoutRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "OpenUsd.Mcp publish failed for $Rid."
    }

    $binRoot = Join-Path $stagedLayoutRoot 'bin'
    $libRoot = Join-Path $stagedLayoutRoot 'lib'
    $pluginRoot = Join-Path $stagedLayoutRoot 'plugin/usd'
    New-Item -ItemType Directory -Force -Path $binRoot, $libRoot, $pluginRoot |
        Out-Null

    foreach ($layout in @(
        @{ Source = (Join-Path $nativeRoot 'bin'); Target = $binRoot },
        @{ Source = (Join-Path $nativeRoot 'lib'); Target = $libRoot },
        @{ Source = (Join-Path $shimRoot 'bin'); Target = $binRoot },
        @{ Source = (Join-Path $shimRoot 'lib'); Target = $libRoot }))
    {
        Copy-DirectoryContents $layout.Source $layout.Target
    }

    foreach ($pluginSource in @(
        (Join-Path $nativeRoot 'lib/usd'),
        (Join-Path $nativeRoot 'plugin/usd'),
        (Join-Path $shimRoot 'plugin/usd')))
    {
        Copy-DirectoryContents $pluginSource $pluginRoot
    }

    Assert-StagedLayout $stagedLayoutRoot $nativeLayout

    $extension = if ($Rid -eq 'win-x64') { '.zip' } else { '.tar.gz' }
    $archiveName = "OpenUsd.Mcp.$Rid$extension"
    $stagedArchivePath = Join-Path $stagedArtifactRoot $archiveName
    $archivePath = Join-Path $artifactRoot $archiveName
    if (-not $NoArchive)
    {
        if ($Rid -eq 'win-x64')
        {
            Compress-Archive `
                -Path (Join-Path $stagedLayoutRoot '*') `
                -DestinationPath $stagedArchivePath
        }
        else
        {
            Push-Location $stagedLayoutRoot
            try
            {
                & tar -czf $stagedArchivePath .
                if ($LASTEXITCODE -ne 0)
                {
                    throw "tar failed while creating $stagedArchivePath."
                }
            }
            finally
            {
                Pop-Location
            }
        }

        $hash = (Get-FileHash $stagedArchivePath -Algorithm SHA256).Hash
        "$hash  $archiveName" |
            Set-Content "$stagedArchivePath.sha256"
    }

    $manifestName = "OpenUsd.Mcp.$Rid.manifest.json"
    $stagedManifestPath = Join-Path $stagedArtifactRoot $manifestName
    $manifestPath = Join-Path $artifactRoot $manifestName
    $manifest = [ordered]@{
        schemaVersion = 1
        rid = $Rid
        targetFramework = 'net10.0'
        archivePath = if ($NoArchive) { $null } else { $archivePath }
        includes = @('mcp-app', 'core-runtime', 'imaging-runtime', 'render-backends')
        excludes = @('viewer-app', 'samples', 'tests', 'cesium-runtime')
        files = @(Get-ChildItem $stagedLayoutRoot -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    path = [IO.Path]::GetRelativePath(
                        $stagedLayoutRoot,
                        $_.FullName)
                    sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
                    length = $_.Length
                }
            })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content $stagedManifestPath

    Assert-RequiredPath $stagedManifestPath Leaf 'staged manifest'
    if (-not $NoArchive)
    {
        Assert-RequiredPath $stagedArchivePath Leaf 'staged archive'
        Assert-RequiredPath "$stagedArchivePath.sha256" Leaf 'staged archive checksum'
    }

    $directories = @(
        [pscustomobject]@{
            Stage = $stagedLayoutRoot
            Target = $layoutRoot
            Backup = $null
            HadTarget = $false
            Installed = $false
        },
        [pscustomobject]@{
            Stage = $stagedArtifactRoot
            Target = $artifactRoot
            Backup = $null
            HadTarget = $false
            Installed = $false
        })
    Install-StagedDirectories $directories

    Write-Output (
        "MCP_BUNDLE_PUBLISHED rid=$Rid layout=$layoutRoot " +
        "manifest=$manifestPath")
}
finally
{
    foreach ($temporaryPath in @($stagedLayoutRoot, $stagedArtifactRoot))
    {
        if (Test-Path -LiteralPath $temporaryPath)
        {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force
        }
    }
}
