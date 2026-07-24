#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Write', 'Verify')]
    [string]$Operation,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,

    [string]$InstallRoot = (Join-Path $PSScriptRoot '../native/install'),

    [switch]$RequireVulkanRuntime
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$lockPath = Join-Path $PSScriptRoot 'openusd.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
$openUsdRoot = Join-Path $InstallRoot $Rid
$shimRoot = Join-Path $InstallRoot "shim/$Rid"
$manifestPath = Join-Path $openUsdRoot '.openusd-install-metadata.json'

function Get-SourceAbiVersion
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $content = Get-Content $Path -Raw
    $match = [regex]::Match($content, $Pattern)
    if (-not $match.Success)
    {
        throw "Could not read $Name from $Path."
    }

    return [int]$match.Groups[1].Value
}

function Get-SourceCapabilityMask
{
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$HeaderPath
    )

    $sourceContent = Get-Content $SourcePath -Raw
    $expressionMatch = [regex]::Match(
        $sourceContent,
        'DataCapabilities\s*=\s*(?<expression>.*?);',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $expressionMatch.Success)
    {
        throw "Could not read shim data capabilities from $SourcePath."
    }

    $capabilityNames = @(
        [regex]::Matches(
            $expressionMatch.Groups['expression'].Value,
            'OPENUSD_CAPABILITY_[A-Z0-9_]+') |
            ForEach-Object Value |
            Sort-Object -Unique)
    if ($capabilityNames.Count -eq 0)
    {
        throw "The shim data capability expression in $SourcePath is empty."
    }

    $headerContent = Get-Content $HeaderPath -Raw
    [UInt64]$mask = 0
    foreach ($capabilityName in $capabilityNames)
    {
        $bitMatch = [regex]::Match(
            $headerContent,
            "#define\s+$([regex]::Escape($capabilityName))\s+" +
                '\(UINT64_C\(1\)\s*<<\s*(\d+)\)')
        if (-not $bitMatch.Success)
        {
            throw "Could not resolve $capabilityName from $HeaderPath."
        }
        $mask = $mask -bor ([UInt64]1 -shl [int]$bitMatch.Groups[1].Value)
    }

    return $mask
}

function Assert-RequiredPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path $Path))
    {
        throw "Native install metadata validation failed: missing $Description at $Path."
    }
}

function Get-VerifiedHeaderHash
{
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$InstalledPath,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-RequiredPath $SourcePath "source $Description"
    Assert-RequiredPath $InstalledPath "installed $Description"
    $sourceHash = (Get-FileHash $SourcePath -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash $InstalledPath -Algorithm SHA256).Hash
    if ($installedHash -cne $sourceHash)
    {
        throw "The installed $Description does not match the source header."
    }
    return $sourceHash
}

Assert-RequiredPath $openUsdRoot "$Rid OpenUSD install"
Assert-RequiredPath $shimRoot "$Rid shim install"

$dataAbiSource = Join-Path $repoRoot 'native/openusd_dotnet/src/openusd_dotnet.cpp'
$dataHeader = Join-Path $repoRoot 'native/openusd_dotnet/include/openusd_dotnet.h'
$hydraHeader = Join-Path $repoRoot 'native/openusd_hydra/include/openusd_hydra.h'
$pageAbiSource = Join-Path $repoRoot 'native/hdSilk/include/openusd_hdsilk.h'
$renderCameraHeader = Join-Path $repoRoot 'native/include/openusd_render_camera.h'
$renderPickHeader = Join-Path $repoRoot 'native/include/openusd_render_pick.h'
$stormChildHeader = Join-Path $repoRoot 'native/openusd_storm_child/include/openusd_storm_child.h'
$stormChildSource = Join-Path $repoRoot 'native/openusd_storm_child/src/openusd_storm_child.cpp'
$dataAbi = Get-SourceAbiVersion `
    -Path $dataAbiSource `
    -Pattern 'DataAbiVersion\s*=\s*(\d+)' `
    -Name 'shim data ABI version'
$dataCapabilities = Get-SourceCapabilityMask `
    -SourcePath $dataAbiSource `
    -HeaderPath $dataHeader
$stormAbi = Get-SourceAbiVersion `
    -Path $hydraHeader `
    -Pattern 'OPENUSD_STORM_ABI_VERSION\s+(\d+)u?' `
    -Name 'Storm ABI version'
$sessionAbi = Get-SourceAbiVersion `
    -Path $pageAbiSource `
    -Pattern 'OPENUSD_SILK_SESSION_ABI_VERSION\s+(\d+)u?' `
    -Name 'hdSilk session ABI version'
$pageAbi = Get-SourceAbiVersion `
    -Path $pageAbiSource `
    -Pattern 'OPENUSD_SILK_PAGE_ABI_VERSION\s+(\d+)u?' `
    -Name 'hdSilk page ABI version'
$stormChildAbi = Get-SourceAbiVersion `
    -Path $stormChildHeader `
    -Pattern 'OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u?' `
    -Name 'Storm child ABI version'
$cameraStateVersion = Get-SourceAbiVersion `
    -Path $dataHeader `
    -Pattern 'OPENUSD_GEOM_CAMERA_STATE_VERSION\s+UINT32_C\((\d+)\)' `
    -Name 'camera state version'
$stormChildNavigationInputVersion = Get-SourceAbiVersion `
    -Path $stormChildHeader `
    -Pattern 'OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION\s+(\d+)u?' `
    -Name 'Storm child navigation input version'

if ($dataAbi -ne [int]$lock.abi.data)
{
    throw "Shim data ABI $dataAbi does not match lock ABI $($lock.abi.data)."
}
if ($dataCapabilities -ne [UInt64]$lock.abi.dataCapabilities)
{
    throw (
        "Shim data capabilities 0x$($dataCapabilities.ToString('X')) do not match " +
        "lock capabilities 0x$(([UInt64]$lock.abi.dataCapabilities).ToString('X')).")
}
if ($pageAbi -ne [int]$lock.abi.renderCommands)
{
    throw "hdSilk page ABI $pageAbi does not match lock ABI $($lock.abi.renderCommands)."
}
if ($stormAbi -ne 5)
{
    throw "Storm ABI $stormAbi does not match the package ABI 5 contract."
}
if ($sessionAbi -ne 4)
{
    throw "hdSilk session ABI $sessionAbi does not match the package ABI 4 contract."
}
if ($stormChildAbi -ne 7)
{
    throw "Storm child ABI $stormChildAbi does not match the package ABI 7 contract."
}
if ($cameraStateVersion -ne 1)
{
    throw "Camera state version $cameraStateVersion does not match the package version 1 contract."
}
if ($stormChildNavigationInputVersion -ne 1)
{
    throw (
        "Storm child navigation input version $stormChildNavigationInputVersion " +
        'does not match the package version 1 contract.')
}

$nativeLayout = switch ($Rid)
{
    'win-x64'
    {
        @{
            Directory = 'bin'
            Names = @(
                'openusd_dotnet.dll',
                'openusd_hydra.dll',
                'openusd_hdsilk.dll',
                'openusd_storm_child.dll'
            )
            OpenUsdLibrary = 'lib/usd_ms.dll'
        }
    }
    'linux-x64'
    {
        @{
            Directory = 'lib'
            Names = @(
                'libopenusd_dotnet.so',
                'libopenusd_hydra.so',
                'libopenusd_hdsilk.so',
                'libopenusd_storm_child.so'
            )
            OpenUsdLibrary = 'lib/libusd_ms.so'
        }
    }
    'osx-arm64'
    {
        @{
            Directory = 'lib'
            Names = @(
                'libopenusd_dotnet.dylib',
                'libopenusd_hydra.dylib',
                'libopenusd_hdsilk.dylib',
                'libopenusd_storm_child.dylib'
            )
            OpenUsdLibrary = 'lib/libusd_ms.dylib'
        }
    }
}

Assert-RequiredPath `
    (Join-Path $openUsdRoot $nativeLayout.OpenUsdLibrary) `
    'OpenUSD monolithic library'
foreach ($nativeName in $nativeLayout.Names)
{
    Assert-RequiredPath `
        (Join-Path $shimRoot "$($nativeLayout.Directory)/$nativeName") `
        $nativeName
}
$dataLibraryName = @(
    $nativeLayout.Names |
        Where-Object { $_ -like '*openusd_dotnet*' })
$hydraLibraryName = @(
    $nativeLayout.Names |
        Where-Object { $_ -like '*openusd_hydra*' })
$hdSilkLibraryName = @(
    $nativeLayout.Names |
        Where-Object { $_ -like '*openusd_hdsilk*' })
$stormChildLibraryNames = @(
    $nativeLayout.Names |
        Where-Object { $_ -like '*openusd_storm_child*' })
foreach ($identifiedLibrary in @(
    @{ Name = 'data shim'; Values = $dataLibraryName },
    @{ Name = 'Hydra shim'; Values = $hydraLibraryName },
    @{ Name = 'hdSilk shim'; Values = $hdSilkLibraryName },
    @{ Name = 'Storm child'; Values = $stormChildLibraryNames }))
{
    if ($identifiedLibrary.Values.Count -ne 1)
    {
        throw "Could not identify exactly one $($identifiedLibrary.Name) library for $Rid."
    }
}
$dataLibraryPath = Join-Path `
    $shimRoot `
    "$($nativeLayout.Directory)/$($dataLibraryName[0])"
$hydraLibraryPath = Join-Path `
    $shimRoot `
    "$($nativeLayout.Directory)/$($hydraLibraryName[0])"
$hdSilkLibraryPath = Join-Path `
    $shimRoot `
    "$($nativeLayout.Directory)/$($hdSilkLibraryName[0])"
$stormChildLibraryName = $stormChildLibraryNames[0]
$stormChildLibraryPath = Join-Path `
    $shimRoot `
    "$($nativeLayout.Directory)/$stormChildLibraryName"
Assert-RequiredPath (Join-Path $openUsdRoot 'lib/usd/plugInfo.json') `
    'OpenUSD plugin metadata'
Assert-RequiredPath (Join-Path $shimRoot 'plugin/usd/hdSilk/resources/plugInfo.json') `
    'hdSilk plugin metadata'
if ($RequireVulkanRuntime -and $Rid -eq 'win-x64')
{
    Assert-RequiredPath `
        (Join-Path $InstallRoot "vulkan-sdk-$($lock.vulkanSdk.version)/bin/vulkan-1.dll") `
        'locked Vulkan loader'
}

$expected = [ordered]@{
    schemaVersion = 3
    rid = $Rid
    openUsdCommit = [string]$lock.openUsd.commit
    lockSha256 = (Get-FileHash $lockPath -Algorithm SHA256).Hash
    dataSourceSha256 = (Get-FileHash $dataAbiSource -Algorithm SHA256).Hash
    stormChildSourceSha256 = (Get-FileHash $stormChildSource -Algorithm SHA256).Hash
    shimDataAbiVersion = $dataAbi
    shimDataCapabilities = $dataCapabilities
    dataCameraStateVersion = $cameraStateVersion
    stormAbiVersion = $stormAbi
    silkSessionAbiVersion = $sessionAbi
    shimPageAbiVersion = $pageAbi
    vulkanSdkVersion = [string]$lock.vulkanSdk.version
    stormChildAbiVersion = $stormChildAbi
    stormChildNavigationInputVersion = $stormChildNavigationInputVersion
}
$installedDataHeader = Join-Path $shimRoot 'include/openusd_dotnet.h'
$installedHydraHeader = Join-Path $shimRoot 'include/openusd_hydra.h'
$installedHdSilkHeader = Join-Path $shimRoot 'include/openusd_hdsilk.h'
$installedStormChildHeader = Join-Path $shimRoot 'include/openusd_storm_child.h'
$installedRenderCameraHeader = Join-Path $shimRoot 'include/openusd_render_camera.h'
$installedRenderPickHeader = Join-Path $shimRoot 'include/openusd_render_pick.h'
$expected.dataHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $dataHeader `
    -InstalledPath $installedDataHeader `
    -Description 'data shim header'
$expected.hydraHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $hydraHeader `
    -InstalledPath $installedHydraHeader `
    -Description 'Hydra shim header'
$expected.hdSilkHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $pageAbiSource `
    -InstalledPath $installedHdSilkHeader `
    -Description 'hdSilk shim header'
$expected.renderCameraHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $renderCameraHeader `
    -InstalledPath $installedRenderCameraHeader `
    -Description 'render camera header'
$expected.renderPickHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $renderPickHeader `
    -InstalledPath $installedRenderPickHeader `
    -Description 'render pick header'
$expected.stormChildHeaderSha256 = Get-VerifiedHeaderHash `
    -SourcePath $stormChildHeader `
    -InstalledPath $installedStormChildHeader `
    -Description 'Storm child header'
$installedStormAbiVersion = Get-SourceAbiVersion `
    -Path $installedHydraHeader `
    -Pattern 'OPENUSD_STORM_ABI_VERSION\s+(\d+)u?' `
    -Name 'installed Storm ABI version'
$installedSessionAbiVersion = Get-SourceAbiVersion `
    -Path $installedHdSilkHeader `
    -Pattern 'OPENUSD_SILK_SESSION_ABI_VERSION\s+(\d+)u?' `
    -Name 'installed hdSilk session ABI version'
$installedPageAbiVersion = Get-SourceAbiVersion `
    -Path $installedHdSilkHeader `
    -Pattern 'OPENUSD_SILK_PAGE_ABI_VERSION\s+(\d+)u?' `
    -Name 'installed hdSilk page ABI version'
$installedStormChildAbiVersion = Get-SourceAbiVersion `
    -Path $installedStormChildHeader `
    -Pattern 'OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u?' `
    -Name 'installed Storm child ABI version'
$installedCameraStateVersion = Get-SourceAbiVersion `
    -Path $installedDataHeader `
    -Pattern 'OPENUSD_GEOM_CAMERA_STATE_VERSION\s+UINT32_C\((\d+)\)' `
    -Name 'installed camera state version'
$installedStormChildNavigationInputVersion = Get-SourceAbiVersion `
    -Path $installedStormChildHeader `
    -Pattern 'OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION\s+(\d+)u?' `
    -Name 'installed Storm child navigation input version'
foreach ($installedAbi in @(
    @{
        Name = 'Storm'
        Installed = $installedStormAbiVersion
        Source = $stormAbi
    },
    @{
        Name = 'hdSilk session'
        Installed = $installedSessionAbiVersion
        Source = $sessionAbi
    },
    @{
        Name = 'hdSilk page'
        Installed = $installedPageAbiVersion
        Source = $pageAbi
    },
    @{
        Name = 'Storm child'
        Installed = $installedStormChildAbiVersion
        Source = $stormChildAbi
    }))
{
    if ($installedAbi.Installed -ne $installedAbi.Source)
    {
        throw (
            "Installed $($installedAbi.Name) ABI $($installedAbi.Installed) " +
            "does not match source ABI $($installedAbi.Source). Reinstall the shim.")
    }
}
if ($installedCameraStateVersion -ne $cameraStateVersion)
{
    throw (
        "Installed camera state version $installedCameraStateVersion does not match " +
        "source version $cameraStateVersion. Reinstall the shim.")
}
if ($installedStormChildNavigationInputVersion -ne $stormChildNavigationInputVersion)
{
    throw (
        "Installed Storm child navigation input version " +
        "$installedStormChildNavigationInputVersion does not match source version " +
        "$stormChildNavigationInputVersion. Reinstall the shim.")
}
$expected.dataLibrarySha256 = (
    Get-FileHash $dataLibraryPath -Algorithm SHA256).Hash
$expected.hydraLibrarySha256 = (
    Get-FileHash $hydraLibraryPath -Algorithm SHA256).Hash
$expected.hdSilkLibrarySha256 = (
    Get-FileHash $hdSilkLibraryPath -Algorithm SHA256).Hash
$expected.stormChildLibrarySha256 = (
    Get-FileHash $stormChildLibraryPath -Algorithm SHA256).Hash
if ($Rid -eq 'linux-x64')
{
    . (Join-Path `
        $repoRoot `
        'src/OpenUsd.Runtime.Packaging/LinuxStormChildTopology.ps1')
    $stormChildTopology = Get-OpenUsdStormChildFileTopology `
        -LibraryPath $stormChildLibraryPath
    $expected.stormChildSoname = $stormChildTopology.soname
    $expected.stormChildRealFile = $stormChildTopology.realFile
    $expected.stormChildRealFileSha256 = (
        Get-FileHash $stormChildTopology.realPath -Algorithm SHA256).Hash
}

if ($Operation -eq 'Write')
{
    $expected |
        ConvertTo-Json |
        Set-Content $manifestPath -Encoding utf8NoBOM
    Write-Output "Wrote $Rid native install metadata to $manifestPath."
    return
}

Assert-RequiredPath $manifestPath 'native install metadata manifest'
$actual = Get-Content $manifestPath -Raw | ConvertFrom-Json
foreach ($property in $expected.Keys)
{
    $expectedValue = [string]$expected[$property]
    $actualValue = [string]$actual.$property
    if ($actualValue -cne $expectedValue)
    {
        throw (
            "Native install metadata mismatch for '$property': " +
            "expected '$expectedValue', got '$actualValue'. Rebuild the native install."
        )
    }
}

Write-Output (
    "Verified $Rid native install metadata: OpenUSD $($expected.openUsdCommit), " +
    "lock $($expected.lockSha256), data ABI $dataAbi, " +
    "capabilities 0x$($dataCapabilities.ToString('X')), camera state v$cameraStateVersion, " +
    "Storm ABI $stormAbi, Silk session/page ABI $sessionAbi/$pageAbi, " +
    "Storm child ABI $stormChildAbi/navigation v$stormChildNavigationInputVersion."
)
