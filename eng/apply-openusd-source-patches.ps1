#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [Parameter(Mandatory = $true)]
    [string]$PatchLockPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$PatchLockPath = [IO.Path]::GetFullPath($PatchLockPath)
$pin = Get-Content -LiteralPath $PatchLockPath -Raw | ConvertFrom-Json
$usdLock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
if ($pin.sourceCommit -cne $usdLock.openUsd.commit)
{
    throw 'The SDK patch set does not match the locked OpenUSD source commit.'
}
$relativeRoot = [IO.Path]::GetRelativePath($repoRoot, $SourceRoot)
if ([IO.Path]::IsPathRooted($relativeRoot) -or $relativeRoot.StartsWith('..', [StringComparison]::Ordinal))
{
    throw 'The patched SDK source must be in the repository-owned source or artifact graph.'
}
function Assert-NoReparseSource
{
    param([string]$Path)
    $current = $Path
    while ($current.Length -gt $repoRoot.Length)
    {
        if (Test-Path -LiteralPath $current)
        {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw 'SDK patches cannot be applied through a shared source junction or reparse point.'
            }
        }
        $current = Split-Path $current -Parent
    }
}
Assert-NoReparseSource $SourceRoot
foreach ($patch in $pin.patches)
{
    if ([IO.Path]::IsPathRooted($patch.path) -or $patch.path -match '(^|[\\/])\.\.([\\/]|$)')
    {
        throw 'SDK patch path must be repository-relative.'
    }
    $patchPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $patch.path))
    if ((Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -cne $patch.sha256)
    {
        throw "SDK source patch identity mismatch: $($patch.path)"
    }
    $allBefore = $true
    $allAfter = $true
    foreach ($file in $patch.files)
    {
        if ([IO.Path]::IsPathRooted($file.path) -or $file.path -match '(^|[\\/])\.\.([\\/]|$)')
        {
            throw 'SDK patch contains an invalid relative file path.'
        }
        $path = Join-Path $SourceRoot $file.path
        Assert-NoReparseSource $path
        $hash = if (Test-Path -LiteralPath $path -PathType Leaf)
        {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
        else { $null }
        $allBefore = $allBefore -and ($hash -ceq $file.beforeSha256)
        $allAfter = $allAfter -and ($hash -ceq $file.afterSha256)
    }
    if ($allAfter)
    {
        Write-Host "Verified applied SDK patch $($patch.id)"
        continue
    }
    if (-not $allBefore)
    {
        throw "SDK source does not match complete preimages or postimages for $($patch.id)."
    }
    Push-Location $repoRoot
    try
    {
        & git --no-pager apply --check "--directory=$relativeRoot" --whitespace=nowarn $patchPath
        if ($LASTEXITCODE -ne 0) { throw "SDK patch preflight failed: $($patch.id)" }
        & git --no-pager apply "--directory=$relativeRoot" --whitespace=nowarn $patchPath
        if ($LASTEXITCODE -ne 0) { throw "SDK patch application failed: $($patch.id)" }
    }
    finally
    {
        Pop-Location
    }
    foreach ($file in $patch.files)
    {
        $path = Join-Path $SourceRoot $file.path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $file.afterSha256)
        {
            throw "SDK patched file identity mismatch: $($file.path)"
        }
    }
    Write-Host "Applied verified SDK patch $($patch.id)"
}

[pscustomobject]@{
    SourceCommit = $pin.sourceCommit
    AccessorVersion = $pin.accessorVersion
    PatchLockPath = $PatchLockPath
    PatchSetSha256 = (Get-FileHash -LiteralPath $PatchLockPath -Algorithm SHA256).Hash
}
