#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$testRoot = Join-Path $repoRoot 'artifacts/shader-archive-self-test'
$sourceRoot = Join-Path $testRoot 'source'
$zipPath = Join-Path $testRoot 'test.zip'
$tarPath = Join-Path $testRoot 'test.tar.gz'
$expandScript = Join-Path $PSScriptRoot 'expand-verified-archive.ps1'

try
{
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (
        Join-Path $sourceRoot 'bin') | Out-Null
    New-Item -ItemType Directory -Force -Path (
        Join-Path $sourceRoot 'lib') | Out-Null
    Set-Content -LiteralPath (Join-Path $sourceRoot 'bin/tool') -Value 'tool'
    Set-Content -LiteralPath (Join-Path $sourceRoot 'lib/runtime') -Value 'runtime'
    Set-Content -LiteralPath (Join-Path $sourceRoot 'LICENSE') -Value 'license'
    Set-Content -LiteralPath (Join-Path $sourceRoot 'excluded') -Value 'excluded'

    Compress-Archive -Path (Join-Path $sourceRoot '*') -DestinationPath $zipPath
    & tar -czf $tarPath -C $sourceRoot bin lib LICENSE excluded
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not create the shader archive self-test tarball.'
    }

    foreach ($archive in @($zipPath, $tarPath))
    {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($archive)
        $destination = Join-Path $testRoot "expanded-$name"
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        & $expandScript `
            -Archive $archive `
            -Destination $destination `
            -Sha256 $hash `
            -IncludePaths @('bin', 'lib', 'LICENSE') | Out-Null
        foreach ($required in @('bin/tool', 'lib/runtime', 'LICENSE'))
        {
            if (-not (Test-Path -LiteralPath (Join-Path $destination $required)))
            {
                throw "$archive did not extract required path '$required'."
            }
        }
        if (Test-Path -LiteralPath (Join-Path $destination 'excluded'))
        {
            throw "$archive extracted content outside IncludePaths."
        }
    }

    $rejected = $false
    try
    {
        & $expandScript `
            -Archive $zipPath `
            -Destination (Join-Path $testRoot 'bad-hash') `
            -Sha256 ('0' * 64) | Out-Null
    }
    catch
    {
        $rejected = $_.Exception.Message -like '*Hash mismatch*'
    }
    if (-not $rejected)
    {
        throw 'The shader archive helper did not reject an incorrect SHA-256.'
    }

    Write-Output 'SHADER_ARCHIVE_SELF_TEST passed'
}
finally
{
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
