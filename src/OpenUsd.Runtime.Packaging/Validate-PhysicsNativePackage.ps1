#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Validates the physics runtime package payload and writes its ABI evidence.

.DESCRIPTION
    The physics package carries a native solver whose C ABI is negotiated exactly at run time: a
    managed mirror that disagrees with the shipped library in any record size refuses to use it.
    That negotiation happens inside a consumer application, which is the most expensive place to
    discover a mismatch, so the version the package ships is asserted here against the same lock
    every other ABI generation in this repository is asserted against, and the result is embedded
    in the package as evidence.

    The asset names are asserted rather than discovered, for two independent reasons. The physics
    shim install directory also holds the Core and Imaging shims, because the physics CMake preset
    configures the whole native tree, and publishing a second copy of those at an application root
    masks the current binary rather than being ignored. The same directory also holds the PhysXGpu
    and PhysXDevice modules, which are packman blobs under NVIDIA proprietary terms rather than the
    BSD-3-Clause PhysX source; this project has no agreement to redistribute them, so a package that
    contains one is a licensing defect and not merely a layout defect. Both are refused here.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$WorldHeader,

    [Parameter(Mandatory = $true)]
    [string]$ExtractHeader,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    # A single delimited string rather than an array parameter: the caller is an MSBuild Exec that
    # runs through cmd.exe on Windows and /bin/sh elsewhere, and an array of quoted paths does not
    # survive both shells identically.
    [string]$Assets = ''
)

$ErrorActionPreference = 'Stop'

# The lock is the single place every ABI generation in this repository is stated, so it is read
# here rather than restated. A physics bump that updates the header and forgets the lock, or the
# reverse, fails packing instead of shipping a package whose evidence is fiction.
$lockPath = Join-Path $PSScriptRoot '../../eng/openusd.lock.json'
if (-not (Test-Path -LiteralPath $lockPath))
{
    throw "The ABI lock is missing at '$lockPath'."
}
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json

function Get-HeaderValue
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path))
    {
        throw "The $Name header is missing at '$Path'."
    }
    $match = [regex]::Match((Get-Content -LiteralPath $Path -Raw), $Pattern)
    if (-not $match.Success)
    {
        throw "The $Name header at '$Path' does not define $Name."
    }
    return [int]$match.Groups[1].Value
}

$worldAbi = Get-HeaderValue `
    -Path $WorldHeader `
    -Pattern 'OPENUSD_PHYSX_WORLD_ABI_VERSION\s+(\d+)u?' `
    -Name 'OPENUSD_PHYSX_WORLD_ABI_VERSION'
$extractAbi = Get-HeaderValue `
    -Path $ExtractHeader `
    -Pattern 'OPENUSD_PHYSICS_EXTRACT_ABI_VERSION\s+(\d+)u?' `
    -Name 'OPENUSD_PHYSICS_EXTRACT_ABI_VERSION'

$requiredWorldAbi = [int]$lock.abi.physics
$requiredExtractAbi = [int]$lock.abi.physicsExtract
if ($worldAbi -ne $requiredWorldAbi)
{
    throw "Physics world ABI $worldAbi does not match lock ABI $requiredWorldAbi."
}
if ($extractAbi -ne $requiredExtractAbi)
{
    throw "Physics extraction ABI $extractAbi does not match lock ABI $requiredExtractAbi."
}

$expectedAssets = switch ($Rid)
{
    'win-x64' { @('openusd_physx.dll') }
    'linux-x64' { @('libopenusd_physx.so') }
    default
    {
        throw "No physics package payload is defined for $Rid."
    }
}

$actualAssets = @($Assets -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$actualNames = @($actualAssets | ForEach-Object { Split-Path -Leaf $_ })
if (($actualNames -join ';') -cne ($expectedAssets -join ';'))
{
    throw (
        "$PackageId would publish '$($actualNames -join ';')' but must publish exactly " +
        "'$($expectedAssets -join ';')'.")
}

# Independent of the name comparison above, because the two rules protect different things and a
# future edit to the expected list must not be able to silently authorise a proprietary blob.
$proprietary = @($actualNames | Where-Object { $_ -match '(?i)^(lib)?PhysX(Gpu|Device)' })
if ($proprietary.Count -gt 0)
{
    throw (
        "$PackageId would publish NVIDIA proprietary module(s) '$($proprietary -join ', ')'. " +
        'The PhysXGpu and PhysXDevice modules are packman blobs under NVIDIA proprietary terms, ' +
        'not the BSD-3-Clause PhysX source, and this project has no agreement to redistribute ' +
        'them. A licensed user supplies them beside the runtime instead.')
}

$records = @()
foreach ($path in $actualAssets)
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "The physics package asset is missing at '$path'."
    }
    $item = Get-Item -LiteralPath $path
    $records += [ordered]@{
        name = $item.Name
        size = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

$evidence = [ordered]@{
    packageId = $PackageId
    rid = $Rid
    kind = 'Physics'
    physicsWorldAbiVersion = $worldAbi
    physicsExtractAbiVersion = $extractAbi
    # Recorded per package rather than left to be inferred from an absence: no OpenUsd package ever
    # contains a PhysX GPU or device module, on any RID.
    cudaModulesIncluded = $false
    assets = $records
}

$evidenceDirectory = Split-Path -Parent $EvidencePath
if ($evidenceDirectory -and -not (Test-Path -LiteralPath $evidenceDirectory))
{
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
}
[System.IO.File]::WriteAllText(
    $EvidencePath,
    ($evidence | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Physics package evidence: $EvidencePath (world ABI $worldAbi, extraction ABI $extractAbi)"
