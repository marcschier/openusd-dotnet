#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$modelJson = & python (Join-Path $PSScriptRoot 'scripts/shader-commands.py') `
    --lock (Join-Path $PSScriptRoot 'toolchain.lock.json') `
    --manifest (Join-Path $PSScriptRoot 'shader-manifest.json') `
    --model-only
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the locked shader toolchain model.'
}
$model = $modelJson | ConvertFrom-Json

function Get-CommandVersion
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$Arguments = @('--version'),
        [switch]$AllowNonZero
    )

    $output = & $Name @Arguments 2>&1
    if ((-not $AllowNonZero -and $LASTEXITCODE -ne 0) -or -not $output)
    {
        throw "Could not query $Name version."
    }
    return ($output | ForEach-Object { $_.ToString() }) -join "`n"
}

$compiler = if ($IsWindows)
{
    Get-CommandVersion -Name 'cl.exe' -Arguments @('/Bv') -AllowNonZero
}
elseif ($IsMacOS)
{
    Get-CommandVersion -Name 'clang' -Arguments @('--version')
}
else
{
    Get-CommandVersion -Name 'c++' -Arguments @('--version')
}
$xcode = if ($IsMacOS)
{
    Get-CommandVersion -Name 'xcodebuild' -Arguments @('-version')
}
else
{
    $null
}

$result = [ordered]@{
    schemaVersion = 1
    rid = $Rid
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    compiler = $compiler
    cmake = Get-CommandVersion -Name 'cmake'
    ninja = Get-CommandVersion -Name 'ninja'
    python = Get-CommandVersion -Name 'python'
    xcode = $xcode
    lockedShaderToolchain = $model
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$result |
    ConvertTo-Json -Depth 6 |
    Set-Content $OutputPath -Encoding utf8

Write-Host "Recorded host toolchain at $OutputPath."
