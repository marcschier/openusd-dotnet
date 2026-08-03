#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '../native/downloads'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../native/src'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$lockPath = Join-Path $PSScriptRoot 'openusd.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)

New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $SourceRoot | Out-Null

function Get-VerifiedDownload
{
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    $path = Join-Path $CacheRoot $FileName
    if (Test-Path $path)
    {
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash
        if ($actual -eq $Sha256)
        {
            Write-Host "Verified $FileName"
            return $path
        }

        if (-not $Force)
        {
            throw "Hash mismatch for $path. Expected $Sha256, got $actual. Use -Force to replace it."
        }

        Remove-Item $path -Force
    }

    $partial = "$path.partial"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $partial
    $downloadHash = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($downloadHash -ne $Sha256)
    {
        Remove-Item $partial -Force
        throw "Hash mismatch for $Uri. Expected $Sha256, got $downloadHash."
    }

    Move-Item $partial $path
    return $path
}

$openUsdArchive = Get-VerifiedDownload `
    -Uri $lock.openUsd.archiveUrl `
    -FileName $lock.openUsd.archiveName `
    -Sha256 $lock.openUsd.archiveSha256

foreach ($dependency in $lock.dependencies)
{
    if ($dependency.platforms -notcontains $Rid)
    {
        continue
    }

    Get-VerifiedDownload `
        -Uri $dependency.url `
        -FileName $dependency.archiveName `
        -Sha256 $dependency.sha256 | Out-Null
}

$openUsdSource = Join-Path $SourceRoot $lock.openUsd.extractDirectory
if (-not (Test-Path $openUsdSource))
{
    Write-Host "Extracting $($lock.openUsd.archiveName)"
    & tar -xzf $openUsdArchive -C $SourceRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to extract $openUsdArchive."
    }
}

$buildScript = Join-Path $openUsdSource $lock.openUsd.buildScript
if (-not (Test-Path $buildScript))
{
    throw "OpenUSD build script was not found at $buildScript."
}

$buildScriptHash = (Get-FileHash $buildScript -Algorithm SHA256).Hash
$knownUnpatchedHashes = @(
    $lock.openUsd.buildScriptSha256,
    'FFC713F58159AAF18682438FC686BABB47484A3FFA94D16EC016FE33EE367194',
    '328341514C4218AD12D16E9ACF09BBAD289C92FDAF413BF12AB89911E9F71242',
    'F6FD234EE167F2695BA8F62C159B5E29F5073DE64F09D97D426F9850EC9448E9'
)
if ($knownUnpatchedHashes -contains $buildScriptHash)
{
    $content = [System.IO.File]::ReadAllText($buildScript)
    $oldBootstrap = '            bootstrap = "bootstrap.bat"'
    $previousBootstrap = '            bootstrap = "cmd.exe /d /s /c bootstrap.bat"'
    $secondBootstrap = '            bootstrap = "cmd.exe /d /s /c .\\bootstrap.bat vc143"'
    $newBootstrap = '            bootstrap = "cmd.exe /d /s /c bootstrap.bat vc143"'
    if (-not $content.Contains($newBootstrap))
    {
        if ($content.Contains($oldBootstrap))
        {
            $content = $content.Replace($oldBootstrap, $newBootstrap)
        }
        elseif ($content.Contains($previousBootstrap))
        {
            $content = $content.Replace($previousBootstrap, $newBootstrap)
        }
        elseif ($content.Contains($secondBootstrap))
        {
            $content = $content.Replace($secondBootstrap, $newBootstrap)
        }
        else
        {
            throw 'The expected Windows Boost bootstrap command was not found.'
        }
    }

    $compilerBindingMarker = '            PatchFile("project-config.jam",'
    if (-not $content.Contains($compilerBindingMarker))
    {
        $runBootstrap = '        Run(bootstrapCmd)'
        $runBootstrapPatched = @(
            '        Run(bootstrapCmd)',
            '        if Windows():',
            '            PatchFile("project-config.jam",',
            '                      [("using msvc : 14.3 ;",',
            '                        "using msvc : 14.3 : cl ;")])'
        ) -join "`n"
        if (-not $content.Contains($runBootstrap))
        {
            throw 'The expected Boost bootstrap invocation was not found.'
        }
        $content = $content.Replace($runBootstrap, $runBootstrapPatched)
    }

    [System.IO.File]::WriteAllText(
        $buildScript,
        $content,
        [System.Text.UTF8Encoding]::new($false))
    $buildScriptHash = (Get-FileHash $buildScript -Algorithm SHA256).Hash
}

if ($buildScriptHash -ne $lock.openUsd.patchedBuildScriptSha256)
{
    throw "OpenUSD patched build script hash mismatch. Expected $($lock.openUsd.patchedBuildScriptSha256), got $buildScriptHash."
}

[pscustomobject]@{
    Rid = $Rid
    RepositoryRoot = $repoRoot.Path
    CacheRoot = $CacheRoot
    SourceRoot = $openUsdSource
    BuildScript = $buildScript
}
