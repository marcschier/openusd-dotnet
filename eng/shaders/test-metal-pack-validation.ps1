#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputRoot = Join-Path $PSScriptRoot 'out/metal-pack-validation-tests'
$fixtureRoot = Join-Path $outputRoot 'fixtures'
$projectPath = Join-Path `
    $repoRoot `
    'src/OpenUsd.Rendering.Silk.Metal/OpenUsd.Rendering.Silk.Metal.csproj'
$validatorPath = Join-Path $PSScriptRoot 'scripts/metal_sidecar.py'
$sourceManifestPath = Join-Path $PSScriptRoot 'shader-manifest.json'
$lockPath = Join-Path $PSScriptRoot 'toolchain.lock.json'
$pythonPath = (Get-Command python -ErrorAction Stop).Source

Remove-Item $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
try
{
    & $pythonPath `
        (Join-Path $PSScriptRoot 'tests/metal_pack_fixture.py') `
        --repository-root $repoRoot `
        --output-root $fixtureRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not create Metal pack validation fixtures.'
    }

    $validLibrary = Join-Path $fixtureRoot 'mesh.metallib'
    $validManifest = Join-Path $fixtureRoot 'valid.json'
    & $pythonPath $validatorPath `
        --sidecar $validManifest `
        --library $validLibrary `
        --manifest $sourceManifestPath `
        --lock $lockPath `
        --repository-root $repoRoot `
        --verify-checked-files
    if ($LASTEXITCODE -ne 0)
    {
        throw 'The positive Metal pack validation fixture was rejected.'
    }

    & dotnet restore $projectPath --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not restore the Metal package validation project.'
    }

    & dotnet build `
        $projectPath `
        -c Release `
        --nologo `
        --no-restore `
        -p:OpenUsdRequireMetalShaderLibrary=false
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not build the Metal package validation project.'
    }

    $cases = @(
        @{
            Name = 'absent'
            Library = (Join-Path $fixtureRoot 'missing.metallib')
            Manifest = (Join-Path $fixtureRoot 'missing.json')
            Expected = 'requires the Xcode-validated ten-entry'
        },
        @{
            Name = 'corrupt-library'
            Library = (Join-Path $fixtureRoot 'corrupt.metallib')
            Manifest = $validManifest
            Expected = 'Combined Metal library size does not match'
        },
        @{
            Name = 'wrong-hash'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'wrong-hash.json')
            Expected = 'Combined Metal library hash does not match'
        },
        @{
            Name = 'wrong-size'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'wrong-size.json')
            Expected = 'Combined Metal library size does not match'
        },
        @{
            Name = 'missing-compute'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'missing-compute.json')
            Expected = 'record counts must match the contract'
        },
        @{
            Name = 'stale-source'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'stale-source.json')
            Expected = 'source mesh.vertex hash does not match'
        },
        @{
            Name = 'malformed-record'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'malformed-record.json')
            Expected = 'sources[0] keys do not match'
        },
        @{
            Name = 'extra-record'
            Library = $validLibrary
            Manifest = (Join-Path $fixtureRoot 'extra-record.json')
            Expected = 'record counts must match the contract'
        }
    )

    foreach ($case in $cases)
    {
        $packageRoot = Join-Path $outputRoot $case.Name
        New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
        $arguments = @(
            'pack',
            $projectPath,
            '-c', 'Release',
            '--nologo',
            '--no-build',
            '--no-restore',
            "-p:OpenUsdRequireMetalShaderLibrary=true",
            "-p:OpenUsdMetalShaderLibraryPath=$($case.Library)",
            "-p:OpenUsdMetalShaderManifestPath=$($case.Manifest)",
            "-p:OpenUsdMetalPythonExecutable=$pythonPath",
            "-p:OpenUsdMetalRepositoryRoot=$repoRoot",
            "-p:PackageOutputPath=$packageRoot"
        )
        $output = (& dotnet @arguments 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0)
        {
            throw "Metal pack unexpectedly accepted $($case.Name)."
        }
        if (-not $output.Contains($case.Expected))
        {
            throw (
                "Metal pack rejected $($case.Name) for the wrong reason. " +
                "Expected '$($case.Expected)'. Output:`n$output")
        }
        if (Get-ChildItem $packageRoot -Filter '*.nupkg' -Recurse)
        {
            throw "Metal pack emitted a package for $($case.Name)."
        }
    }

    if (-not $IsMacOS)
    {
        $packageRoot = Join-Path $outputRoot 'valid-non-macos'
        New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
        $arguments = @(
            'pack',
            $projectPath,
            '-c', 'Release',
            '--nologo',
            '--no-build',
            '--no-restore',
            '-p:OpenUsdRequireMetalShaderLibrary=true',
            "-p:OpenUsdMetalShaderLibraryPath=$validLibrary",
            "-p:OpenUsdMetalShaderManifestPath=$validManifest",
            "-p:OpenUsdMetalPythonExecutable=$pythonPath",
            "-p:OpenUsdMetalRepositoryRoot=$repoRoot",
            "-p:PackageOutputPath=$packageRoot"
        )
        $output = (& dotnet @arguments 2>&1 | Out-String)
        if (
            $LASTEXITCODE -eq 0 -or
            -not $output.Contains('supported only on macOS with Xcode 16.4')
        )
        {
            throw (
                'A valid Metal pair did not reach the non-macOS package gate. ' +
                "Output:`n$output")
        }
        if (Get-ChildItem $packageRoot -Filter '*.nupkg' -Recurse)
        {
            throw 'The non-macOS Metal pack emitted a package.'
        }
    }

    # GitHub Actions exits pwsh with the last expected failing dotnet pack code.
    $global:LASTEXITCODE = 0
    Write-Host (
        "Validated positive schema-v4 input and rejected $($cases.Count) " +
        'Metal pack failure cases before package emission.')
}
finally
{
    Remove-Item $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
}
