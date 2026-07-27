#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Packs the exact set of NuGet packages this repository publishes.

.DESCRIPTION
    The published set is enumerated here rather than inferred from the solution so that
    adding a project cannot silently ship it to a public feed. A package pushed to
    nuget.org can be unlisted but never withdrawn or replaced, so the set is asserted
    after packing: producing a package that is not on the list, or missing one that is,
    fails the run instead of publishing the difference.

    The runtime packages embed the locked native install for their RID, so every RID has
    to be staged before this runs. OpenUsd.Rendering.Silk.Metal additionally requires the
    validated mesh.metallib, which only a macOS host can produce.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = 'artifacts/nupkg',
    [string]$Configuration = 'Release',
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')),
    [switch]$SkipMetal
)

$ErrorActionPreference = 'Stop'

# The published set. Renderer-neutral and per-backend managed libraries first, then the
# per-RID runtime packages that carry the native payload.
$published = @(
    'OpenUsd'
    'OpenUsd.Interop'
    'OpenUsd.Rendering'
    'OpenUsd.Rendering.Silk'
    'OpenUsd.Rendering.Silk.D3D12'
    'OpenUsd.Rendering.Silk.Metal'
    'OpenUsd.Rendering.Silk.Vulkan'
    'OpenUsd.Rendering.Storm'
    'OpenUsd.Viewer'
    'OpenUsd.Runtime.Core.win-x64'
    'OpenUsd.Runtime.Core.linux-x64'
    'OpenUsd.Runtime.Core.osx-arm64'
    'OpenUsd.Runtime.Imaging.win-x64'
    'OpenUsd.Runtime.Imaging.linux-x64'
    'OpenUsd.Runtime.Imaging.osx-arm64'
)

# Retained before the SkipMetal filter so the src/ classification check below always
# covers the full set.
$allPublished = $published

if ($SkipMetal)
{
    # Only for local verification on a non-macOS host. A release must never skip a
    # package, so the workflow does not pass this.
    $published = $published | Where-Object { $_ -ne 'OpenUsd.Rendering.Silk.Metal' }
    Write-Host 'Skipping OpenUsd.Rendering.Silk.Metal (requires a macOS host).'
}

# Projects under src/ that are deliberately not published. Listing them explicitly
# forces a decision when a project is added: a new library that is neither published nor
# named here fails this script instead of quietly never shipping. This is checked
# against the full set so it holds even when Metal is skipped locally.
$notPublished = @{
    'OpenUsd.Viewer.App' = 'Desktop application shell, distributed separately from NuGet.'
}

$srcProjects = Get-ChildItem (Join-Path $Root 'src') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } |
    Select-Object -ExpandProperty Name

$unclassified = $srcProjects | Where-Object {
    $allPublished -notcontains $_ -and -not $notPublished.ContainsKey($_)
}

if ($unclassified)
{
    throw ("Projects under src/ are neither published nor listed as unpublished: " +
        "$($unclassified -join ', '). Add them to the published set or record why not.")
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath
}
else
{
    Join-Path $Root $OutputPath
}

if (Test-Path $outputRoot)
{
    Remove-Item $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($id in $published)
{
    $project = Join-Path $Root "src/$id/$id.csproj"
    if (-not (Test-Path $project))
    {
        throw "The published package '$id' has no project at '$project'."
    }

    Write-Host "Packing $id" -ForegroundColor Cyan
    $arguments = @(
        'pack'
        $project
        '-c'
        $Configuration
        '-o'
        $outputRoot
        '-p:PublicRelease=true'
    )

    if ($id -eq 'OpenUsd.Rendering.Silk.Metal')
    {
        # Fail the pack rather than emit a Metal package with no shader library.
        $arguments += '-p:OpenUsdRequireMetalShaderLibrary=true'
    }

    dotnet @arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "Packing '$id' failed with exit code $LASTEXITCODE."
    }
}

$produced = Get-ChildItem $outputRoot -Filter '*.nupkg' |
    ForEach-Object { $_.Name -replace '\.\d+\.\d+\.\d+.*$', '' } |
    Sort-Object -Unique

$expected = $published | Sort-Object -Unique
$unexpected = $produced | Where-Object { $expected -notcontains $_ }
$missing = $expected | Where-Object { $produced -notcontains $_ }

if ($unexpected)
{
    throw "Packing produced packages that are not published: $($unexpected -join ', ')."
}

if ($missing)
{
    throw "Packing did not produce every published package; missing: $($missing -join ', ')."
}

Write-Host "Packed $($produced.Count) package(s) into $outputRoot." -ForegroundColor Green
