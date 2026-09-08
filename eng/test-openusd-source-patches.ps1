#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $repoRoot ("artifacts\sdk-patch-contract\" + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$patchPath = Join-Path $work 'fixture.patch'
$manifestPath = Join-Path $work 'fixture.lock.json'
$file = Join-Path $source 'input.cpp'
$apply = Join-Path $PSScriptRoot 'apply-openusd-source-patches.ps1'
New-Item -ItemType Directory $source -Force | Out-Null

function Assert-Refused
{
    param([scriptblock]$Action, [string]$Expected)
    try
    {
        & $Action | Out-Null
    }
    catch
    {
        if ($_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal))
        {
            return
        }
        throw
    }
    throw "SDK patch contract expected refusal: $Expected"
}

try
{
    [IO.File]::WriteAllText($file, "int before = 1;`n")
    $before = (Get-FileHash $file -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($file, "int after = 2;`n")
    $after = (Get-FileHash $file -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($file, "int before = 1;`n")
    [IO.File]::WriteAllText($patchPath, "--- a/input.cpp`n+++ b/input.cpp`n@@ -1 +1 @@`n-int before = 1;`n+int after = 2;`n")
    $lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
    $manifest = @{
        sourceCommit = $lock.openUsd.commit
        accessorVersion = 1
        patches = @(@{
            id = 'fixture'
            path = [IO.Path]::GetRelativePath($repoRoot, $patchPath)
            sha256 = (Get-FileHash $patchPath -Algorithm SHA256).Hash
            files = @(@{ path = 'input.cpp'; beforeSha256 = $before; afterSha256 = $after })
        })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    & $apply -SourceRoot $source -PatchLockPath $manifestPath | Out-Null
    if ([IO.File]::ReadAllText($file) -cne "int after = 2;`n") { throw 'Patch did not produce expected source.' }
    & $apply -SourceRoot $source -PatchLockPath $manifestPath | Out-Null

    [IO.File]::AppendAllText($file, "// changed outside patch`n")
    Assert-Refused { & $apply -SourceRoot $source -PatchLockPath $manifestPath } 'preimages or postimages'
    [IO.File]::WriteAllText($file, "int after = 2;`n")
    [IO.File]::AppendAllText($patchPath, "`n")
    Assert-Refused { & $apply -SourceRoot $source -PatchLockPath $manifestPath } 'patch identity mismatch'
    [IO.File]::WriteAllText($patchPath, "--- a/input.cpp`n+++ b/input.cpp`n@@ -1 +1 @@`n-int before = 1;`n+int after = 2;`n")

    $manifest.sourceCommit = 'not-the-pinned-commit'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    Assert-Refused { & $apply -SourceRoot $source -PatchLockPath $manifestPath } 'source commit'
    $manifest.sourceCommit = $lock.openUsd.commit
    $manifest.patches[0].files[0].path = '../escape.cpp'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    Assert-Refused { & $apply -SourceRoot $source -PatchLockPath $manifestPath } 'invalid relative file path'
    Write-Output 'SDK_PATCH_CONTRACT_OK: exact apply/idempotence/source drift/patch drift/commit/path guards'
}
finally
{
    Remove-Item -LiteralPath $work -Recurse -Force
}
