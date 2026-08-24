#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$NativeRoot,
    [string]$ShimRoot,
    [string]$RuntimeRoot,
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

function Assert-StagedRuntime
{
    param(
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [Parameter(Mandatory = $true)][hashtable]$NativeLayout
    )

    $pluginRoot = Join-Path $StageRoot 'plugin/usd'
    foreach ($requiredPath in @(
        (Join-Path $pluginRoot 'plugInfo.json'),
        (Join-Path $pluginRoot 'hdStorm/resources/plugInfo.json'),
        (Join-Path $pluginRoot 'hdSilk/resources/plugInfo.json')))
    {
        Assert-RequiredPath $requiredPath Leaf 'staged plugin asset'
    }

    $requiredNativeNames = @(
        [IO.Path]::GetFileName($NativeLayout.OpenUsdLibrary)) +
        $NativeLayout.ShimLibraries
    foreach ($nativeName in $requiredNativeNames)
    {
        $nativeFile = Get-ChildItem `
            -LiteralPath $StageRoot `
            -Recurse `
            -File `
            -Filter $nativeName |
            Select-Object -First 1
        if ($null -eq $nativeFile)
        {
            throw "The staged MCP runtime is missing required native library '$nativeName'."
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $StageRoot -Recurse -File)
    {
        $relativePath = [IO.Path]::GetRelativePath($StageRoot, $file.FullName)
        $parts = @($relativePath -split '[\\/]')
        if ($file.Name -like 'OpenUsd.Viewer*' -or
            $file.Name -like 'OpenUsd.Runtime.Cesium*' -or
            $parts -contains 'samples' -or
            $parts -contains 'tests')
        {
            throw "The staged MCP runtime contains excluded asset '$relativePath'."
        }
    }
}

function Install-StagedRuntime
{
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $backup = Join-Path `
        ([IO.Path]::GetDirectoryName($Target)) `
        ".$([IO.Path]::GetFileName($Target)).backup.$(
            [Guid]::NewGuid().ToString('N'))"
    $hadTarget = $false
    try
    {
        if (Test-Path -LiteralPath $Target)
        {
            Move-Item -LiteralPath $Target -Destination $backup
            $hadTarget = $true
        }
        Move-Item -LiteralPath $Stage -Destination $Target
    }
    catch
    {
        $commitError = $_
        try
        {
            if (Test-Path -LiteralPath $Target)
            {
                Remove-Item -LiteralPath $Target -Recurse -Force
            }
            if ($hadTarget -and (Test-Path -LiteralPath $backup))
            {
                Move-Item -LiteralPath $backup -Destination $Target
            }
        }
        catch
        {
            throw (
                "MCP runtime replacement failed: $($commitError.Exception.Message) " +
                "Rollback also failed: $($_.Exception.Message)")
        }
        throw $commitError
    }

    if ($hadTarget)
    {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}

function Join-LoaderPath
{
    param(
        [AllowNull()]
        [AllowEmptyCollection()]
        [string[]]$Path,
        [char]$Separator = [IO.Path]::PathSeparator
    )

    $entries = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($component in @($Path))
    {
        if ([string]::IsNullOrWhiteSpace($component))
        {
            continue
        }
        foreach ($entry in @(
            $component -split [regex]::Escape([string]$Separator)))
        {
            if (-not [string]::IsNullOrWhiteSpace($entry) -and
                $seen.Add($entry))
            {
                $entries.Add($entry)
            }
        }
    }
    return [string]::Join([string]$Separator, $entries)
}

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
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($RuntimeRoot))
{
    Join-Path $repoRoot "artifacts/mcp-source-runtime/$Rid"
}
else
{
    Get-AbsolutePath $RuntimeRoot
}
$runtimeParent = [IO.Path]::GetDirectoryName($runtimeRoot)
$stageRoot = Join-Path `
    $runtimeParent `
    ".$([IO.Path]::GetFileName($runtimeRoot)).tmp.$(
        [Guid]::NewGuid().ToString('N'))"
$project = Join-Path $repoRoot 'src/OpenUsd.Mcp/OpenUsd.Mcp.csproj'
$nativeLayout = Get-NativeLayout

# Preflight every source dependency and target shape before creating staging output.
Assert-NativePreflight $nativeLayout
Assert-NativeInstallMetadata
Get-Command $DotNetCommand -ErrorAction Stop | Out-Null
if ((Test-Path -LiteralPath $runtimeRoot) -and
    -not (Test-Path -LiteralPath $runtimeRoot -PathType Container))
{
    throw "The MCP runtime target is not a directory: $runtimeRoot"
}

try
{
    New-Item -ItemType Directory -Force -Path $runtimeParent | Out-Null
    New-Item -ItemType Directory -Path $stageRoot | Out-Null

    $binRoot = Join-Path $stageRoot 'bin'
    $libRoot = Join-Path $stageRoot 'lib'
    $pluginRoot = Join-Path $stageRoot 'plugin/usd'
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

    if ($Rid -eq 'win-x64')
    {
        foreach ($source in @(
            (Join-Path $nativeRoot 'lib'),
            (Join-Path $shimRoot 'lib')))
        {
            if (Test-Path -LiteralPath $source -PathType Container)
            {
                Get-ChildItem -LiteralPath $source -File -Filter '*.dll' |
                    Copy-Item -Destination $binRoot -Force
            }
        }
    }

    foreach ($pluginSource in @(
        (Join-Path $nativeRoot 'lib/usd'),
        (Join-Path $nativeRoot 'plugin/usd'),
        (Join-Path $shimRoot 'plugin/usd')))
    {
        Copy-DirectoryContents $pluginSource $pluginRoot
    }

    Assert-StagedRuntime $stageRoot $nativeLayout
    Install-StagedRuntime $stageRoot $runtimeRoot
}
finally
{
    if (Test-Path -LiteralPath $stageRoot)
    {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}

$binRoot = Join-Path $runtimeRoot 'bin'
$libRoot = Join-Path $runtimeRoot 'lib'
$pluginRoot = Join-Path $runtimeRoot 'plugin/usd'
$env:OPENUSD_PLUGIN_PATH = $pluginRoot
if ($Rid -eq 'win-x64')
{
    $env:PATH = @($runtimeRoot, $binRoot, $libRoot, $env:PATH) -join
        [IO.Path]::PathSeparator
}
elseif ($Rid -eq 'linux-x64')
{
    $env:LD_LIBRARY_PATH = Join-LoaderPath -Path @(
        $runtimeRoot,
        $binRoot,
        $libRoot,
        $env:LD_LIBRARY_PATH)
}
else
{
    $env:DYLD_LIBRARY_PATH = Join-LoaderPath -Path @(
        $runtimeRoot,
        $binRoot,
        $libRoot,
        $env:DYLD_LIBRARY_PATH)
}

& $DotNetCommand run `
    --project $project `
    --configuration $Configuration `
    --framework net10.0 `
    --no-build
exit $LASTEXITCODE
