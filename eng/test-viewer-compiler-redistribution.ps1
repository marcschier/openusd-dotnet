#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'openusd-viewer-compiler-' + [Guid]::NewGuid().ToString('N'))
$nested = Join-Path $root 'nested'
$guard = Join-Path $PSScriptRoot 'assert-viewer-compiler-redistribution.ps1'
New-Item -ItemType Directory -Path $nested -Force | Out-Null
try
{
    & $guard -BundleRoot $root
    $unrelated = Join-Path $nested 'spirv-cross-notice.txt'
    [IO.File]::WriteAllText($unrelated, 'fixture')
    try
    {
        & $guard -BundleRoot $root
    }
    finally
    {
        Remove-Item -LiteralPath $unrelated -Force
    }
    foreach ($name in @(
        'spirv-cross.dll',
        'libspirv-cross.so',
        'libspirv-cross.so.1',
        'libspirv-cross.dylib',
        'SPIRV-CROSS.DLL',
        'dxc.exe',
        'dxcompiler.dll',
        'dxil.dll',
        'libdxcompiler.so',
        'libdxcompiler.so.3.8',
        'libdxil.so.1',
        'libdxil.dylib'))
    {
        $path = Join-Path $nested $name
        [IO.File]::WriteAllText($path, 'fixture')
        try
        {
            $rejected = $false
            try
            {
                & $guard -BundleRoot $root
            }
            catch
            {
                if (-not $_.Exception.Message.Contains($name))
                {
                    throw
                }
                $rejected = $true
            }
            if (-not $rejected)
            {
                throw "The Viewer archive accepted the unresolved compiler fixture '$name'."
            }
        }
        finally
        {
            Remove-Item -LiteralPath $path -Force
        }
    }
    $missingRejected = $false
    try
    {
        & $guard -BundleRoot (Join-Path $root 'missing')
    }
    catch
    {
        if (-not $_.Exception.Message.Contains('Viewer layout directory not found'))
        {
            throw
        }
        $missingRejected = $true
    }
    if (-not $missingRejected)
    {
        throw 'A missing Viewer layout was accepted.'
    }

    $publisher = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'publish-viewer-bundle.ps1') -Raw
    $publishIndex = $publisher.IndexOf('dotnet publish', [StringComparison]::Ordinal)
    $guardIndex = $publisher.IndexOf(
        'assert-viewer-compiler-redistribution.ps1', [StringComparison]::Ordinal)
    $signIndex = $publisher.IndexOf('sign-viewer-bundle.ps1', [StringComparison]::Ordinal)
    if ($publishIndex -lt 0 -or $guardIndex -le $publishIndex -or $signIndex -le $guardIndex -or
        $publisher -notmatch '(?s)if \(-not \$NoArchive\)\s*\{\s*& \(Join-Path \$PSScriptRoot ''assert-viewer-compiler-redistribution\.ps1''\)')
    {
        throw 'The Viewer archive guard must run after publish and before signing, except for -NoArchive.'
    }
    Write-Host 'Viewer compiler redistribution guard passed.'
}
finally
{
    Remove-Item -LiteralPath $nested -Force
    Remove-Item -LiteralPath $root -Force
}
