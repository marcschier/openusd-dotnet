#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SdkRoot,
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..\artifacts\render-deferred-native\pack-provenance-proof')
)

$ErrorActionPreference = 'Stop'
$SdkRoot = [IO.Path]::GetFullPath($SdkRoot)
$work = Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) ([Guid]::NewGuid().ToString('N'))
$copy = Join-Path $work 'sdk'
$verifier = Join-Path $PSScriptRoot 'sdk-storage-admission-metadata.ps1'
$sidecar = Join-Path $copy '.openusd-storage-admission.json'
$original = Get-Content (Join-Path $SdkRoot '.openusd-storage-admission.json') -Raw
$required = @(
    'include\pxr\usd\sdf\storageAdmission.h',
    'include\pxr\base\vt\arrayEdit.h',
    'lib\usd_ms.dll',
    'lib\usd_ms.lib'
)
$results = [Collections.Generic.List[object]]::new()
New-Item -ItemType Directory $copy -Force | Out-Null

function Invoke-Case([string]$Name, [object]$Metadata, [bool]$ExpectedSuccess, [string]$Rid = 'win-x64')
{
    $inputPath = Join-Path $work "$Name.metadata.json"
    $Metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $inputPath -Encoding utf8NoBOM
    Copy-Item -LiteralPath $inputPath $sidecar -Force
    $log = Join-Path $work "$Name.log"
    & pwsh -NoProfile -File $verifier -Operation Verify -SdkRoot $copy -Rid $Rid > $log 2>&1
    $code = $LASTEXITCODE
    $passed = ($code -eq 0) -eq $ExpectedSuccess
    $results.Add([ordered]@{
        name = $Name
        expectedSuccess = $ExpectedSuccess
        rid = $Rid
        exitCode = $code
        passed = $passed
        metadata = $inputPath
        log = $log
    })
    Write-Output "$Name`: expectedSuccess=$ExpectedSuccess exitCode=$code passed=$passed"
}

try
{
    foreach ($relative in $required)
    {
        $destination = Join-Path $copy $relative
        New-Item -ItemType Directory (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SdkRoot $relative) $destination
    }
    Invoke-Case 'valid-inventory' ($original | ConvertFrom-Json) $true
    $metadata = $original | ConvertFrom-Json
    $metadata.files = @()
    Invoke-Case 'empty-inventory' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files = @($metadata.files | Where-Object { $_.path -like 'include/*' })
    Invoke-Case 'headers-only' $metadata $false
    foreach ($relative in $required)
    {
        $metadata = $original | ConvertFrom-Json
        $metadata.files = @($metadata.files | Where-Object { $_.path -ne $relative.Replace('\', '/') })
        Invoke-Case ("omitted-" + [IO.Path]::GetFileName($relative)) $metadata $false
    }
    $metadata = $original | ConvertFrom-Json
    $metadata.files = @($metadata.files | Where-Object { $_.path -like 'lib/*' })
    Invoke-Case 'binaries-only' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files = $null
    Invoke-Case 'null-inventory' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files = $metadata.files[0]
    Invoke-Case 'non-array-inventory' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files += $metadata.files[0].PSObject.Copy()
    $metadata.files[-1].path = $metadata.files[-1].path.Replace('/', '\')
    Invoke-Case 'duplicate-path-alias' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files[0].path = 'include/../' + $metadata.files[0].path
    Invoke-Case 'traversal-path' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files[0].PSObject.Properties.Remove('sha256')
    Invoke-Case 'missing-hash' $metadata $false
    $metadata = $original | ConvertFrom-Json
    $metadata.files[0].sha256 = '01234567'
    Invoke-Case 'invalid-hash' $metadata $false
    Invoke-Case 'wrong-linux-target' ($original | ConvertFrom-Json) $false 'linux-x64'
    Invoke-Case 'wrong-macos-target' ($original | ConvertFrom-Json) $false 'osx-arm64'
    foreach ($relative in @('lib\usd_ms.dll', 'lib\usd_ms.lib'))
    {
        $path = Join-Path $copy $relative
        Move-Item -LiteralPath $path ($path + '.held')
        try { Invoke-Case ("missing-file-" + [IO.Path]::GetFileName($relative)) ($original | ConvertFrom-Json) $false }
        finally { Move-Item -LiteralPath ($path + '.held') $path }
    }
    $binary = Join-Path $copy 'lib\usd_ms.dll'
    (Get-Item -LiteralPath $binary).IsReadOnly = $false
    $stream = [IO.File]::Open($binary, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
    try { $first = $stream.ReadByte(); $stream.Position = 0; $stream.WriteByte($first -bxor 1) }
    finally { $stream.Dispose() }
    Invoke-Case 'binary-hash-drift' ($original | ConvertFrom-Json) $false
    $stream = [IO.File]::Open($binary, [IO.FileMode]::Open, [IO.FileAccess]::Write)
    try { $stream.WriteByte($first) } finally { $stream.Dispose() }
    Invoke-Case 'restored-inventory' ($original | ConvertFrom-Json) $true
}
finally
{
    $results.ToArray() | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding utf8NoBOM
    Remove-Item -LiteralPath $copy -Recurse -Force
}

if (@($results | Where-Object { -not $_.passed }).Count -ne 0)
{
    throw "SDK pack provenance contract failed; evidence=$work"
}
Write-Output "SDK_PACK_PROVENANCE_OK: $($results.Count) cases; evidence=$work"
