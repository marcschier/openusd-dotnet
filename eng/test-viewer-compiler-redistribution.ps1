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

    $bundleFixtureRoot = Join-Path $root 'bundle-fixture'
    $publisherPath = Join-Path $PSScriptRoot 'publish-viewer-bundle.ps1'
    function dotnet
    {
        if ($args[0] -cne 'publish')
        {
            throw 'The Viewer fixture only supports dotnet publish.'
        }

        [xml]$project = Get-Content -LiteralPath $args[1] -Raw
        $outputIndex = [Array]::IndexOf($args, '-o')
        if ($outputIndex -lt 0 -or $outputIndex + 1 -ge $args.Count)
        {
            throw 'The Viewer fixture did not receive a publish output directory.'
        }
        $output = $args[$outputIndex + 1]
        $usd = Join-Path $output 'plugin/usd'
        $storm = Join-Path $usd 'hdStorm/resources'
        New-Item -ItemType Directory -Force -Path $storm | Out-Null
        [IO.File]::WriteAllText((Join-Path $usd 'plugInfo.json'), '{}')
        [IO.File]::WriteAllText((Join-Path $storm 'plugInfo.json'), '{}')
        [IO.File]::WriteAllText((Join-Path $output 'fixture.pdb'), 'fixture')

        $reference = @($project.Project.ItemGroup.PackageReference | Where-Object {
            $_.Include -ceq 'Silk.NET.SPIRV.Cross.Native'
        })
        $excluded = $reference.Count -eq 1 -and
            @($reference[0].ExcludeAssets -split '[,;]' | Where-Object {
                $_.Trim() -ieq 'native'
            }).Count -eq 1
        if (-not $excluded)
        {
            [IO.File]::WriteAllText((Join-Path $output 'libspirv-cross.so'), 'fixture')
        }
        if ($global:openUsdViewerFixtureIncludeDxc)
        {
            [IO.File]::WriteAllText((Join-Path $output 'dxc.exe'), 'fixture')
        }
        $global:LASTEXITCODE = 0
    }

    try
    {
        $global:openUsdViewerFixtureIncludeDxc = $false
        & $publisherPath -Rid linux-x64 -OutputRoot $bundleFixtureRoot -PackageVersion '0.0.0'
        $archiveLayout = Join-Path $bundleFixtureRoot 'layout/linux-x64'
        if (Test-Path -LiteralPath (Join-Path $archiveLayout 'libspirv-cross.so'))
        {
            throw 'The Viewer archive layout included the external SPIRV-Cross library.'
        }

        & $publisherPath -Rid linux-x64 -OutputRoot $bundleFixtureRoot `
            -PackageVersion '0.0.0' -NoArchive
        if (-not (Test-Path -LiteralPath (Join-Path $archiveLayout 'libspirv-cross.so')))
        {
            throw 'A non-archive Viewer publish no longer includes its native dependency.'
        }

        $global:openUsdViewerFixtureIncludeDxc = $true
        $unexpectedRejected = $false
        try
        {
            & $publisherPath -Rid linux-x64 -OutputRoot $bundleFixtureRoot -PackageVersion '0.0.0'
        }
        catch
        {
            if (-not $_.Exception.Message.Contains('dxc.exe'))
            {
                throw
            }
            $unexpectedRejected = $true
        }
        if (-not $unexpectedRejected)
        {
            throw 'The Viewer archive accepted an unexpected compiler binary.'
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $bundleFixtureRoot)
        {
            Remove-Item -LiteralPath $bundleFixtureRoot -Recurse -Force
        }
        Remove-Item Function:\dotnet
        Remove-Variable openUsdViewerFixtureIncludeDxc -Scope Global
    }
    Write-Host 'Viewer compiler redistribution guard passed.'
}
finally
{
    Remove-Item -LiteralPath $nested -Force
    Remove-Item -LiteralPath $root -Force
}
