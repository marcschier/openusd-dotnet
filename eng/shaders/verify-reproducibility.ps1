#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [ValidateSet('Full', 'Spirv', 'Metal')]
    [string]$ArtifactScope = 'Full',
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [string]$WorkRoot = (Join-Path $PSScriptRoot '.cache/reproducibility'),
    [switch]$DeterministicIntermediatesOnly
)

$ErrorActionPreference = 'Stop'
$WorkRoot = [System.IO.Path]::GetFullPath($WorkRoot)
$firstRoot = Join-Path $WorkRoot 'first'
$secondRoot = Join-Path $WorkRoot 'second'

Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
try
{
    & (Join-Path $PSScriptRoot 'build-shaders.ps1') `
        -Rid $Rid `
        -ArtifactScope $ArtifactScope `
        -ToolRoot $ToolRoot `
        -OutputRoot $firstRoot | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & (Join-Path $PSScriptRoot 'build-shaders.ps1') `
        -Rid $Rid `
        -ArtifactScope $ArtifactScope `
        -ToolRoot $ToolRoot `
        -OutputRoot $secondRoot | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $firstInput = Get-ChildItem $firstRoot -Recurse -File
    $secondInput = Get-ChildItem $secondRoot -Recurse -File
    if ($ArtifactScope -eq 'Spirv')
    {
        $pattern = '\.spv$'
    }
    elseif ($ArtifactScope -eq 'Metal')
    {
        # The metallib embeds a build identifier, so only the MSL text is
        # comparable when deterministic intermediates are requested.
        $pattern = if ($DeterministicIntermediatesOnly)
        {
            '\.metal$'
        }
        else
        {
            '\.(metal|metallib)$'
        }
    }
    elseif ($DeterministicIntermediatesOnly)
    {
        $pattern = '\.(dxil|spv|metal|reflection\.json)$'
    }
    else
    {
        $pattern = '\.(dxil|spv|metal|reflection\.json|metallib)$'
    }
    $firstInput = $firstInput | Where-Object Name -Match $pattern
    $secondInput = $secondInput | Where-Object Name -Match $pattern

    $firstFiles = $firstInput |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($firstRoot.Length).TrimStart('\', '/')
                Hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Sort-Object Path
    $secondFiles = $secondInput |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($secondRoot.Length).TrimStart('\', '/')
                Hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Sort-Object Path

    $differences = Compare-Object $firstFiles $secondFiles -Property Path,Hash
    if ($differences)
    {
        $differences | Format-Table | Out-String | Write-Host
        throw 'Shader outputs differ between same-host builds.'
    }

    $scope = if ($ArtifactScope -eq 'Spirv')
    {
        'SPIR-V'
    }
    elseif ($DeterministicIntermediatesOnly)
    {
        'deterministic intermediate'
    }
    else
    {
        'generated'
    }
    Write-Host "Reproducibility verified for $($firstFiles.Count) $scope files."
}
finally
{
    Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
}
