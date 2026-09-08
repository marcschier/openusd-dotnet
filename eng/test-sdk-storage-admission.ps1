#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SdkRoot,
    [Parameter(Mandatory = $true)][string]$CMake,
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..\artifacts\render-deferred-native\sdk-guard-proof')
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$work = Join-Path $EvidenceRoot ([Guid]::NewGuid().ToString('N'))
$copy = Join-Path $work 'sdk'
$project = Join-Path $work 'project'
$build = Join-Path $work 'build'
$patchLock = Join-Path $repository 'eng\openusd-storage-admission.lock.json'
$guard = Join-Path $repository 'native\openusd_dotnet\cmake\VerifyStorageAdmissionSdk.cmake'
New-Item -ItemType Directory $copy, $project -Force | Out-Null
$sourceMetadata = Join-Path $SdkRoot '.openusd-storage-admission.json'
$metadata = Get-Content $sourceMetadata -Raw | ConvertFrom-Json
Copy-Item -LiteralPath $sourceMetadata (Join-Path $copy '.openusd-storage-admission.json')
foreach ($file in $metadata.files)
{
    $destination = Join-Path $copy $file.path
    New-Item -ItemType Directory (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $SdkRoot $file.path) $destination
}
$cmakeSource = @'
cmake_minimum_required(VERSION 3.28)
project(StorageAdmissionIdentityContract NONE)
include("${GUARD}")
openusd_verify_storage_admission_sdk("${SDK_ROOT}" "${PATCH_LOCK}")
add_custom_target(check ALL
    COMMAND "${CMAKE_COMMAND}" "-DSTORAGE_ADMISSION_SDK_ROOT=${SDK_ROOT}"
        "-DSTORAGE_ADMISSION_PATCH_LOCK=${PATCH_LOCK}" -P "${GUARD}" VERBATIM)
'@
Set-Content -LiteralPath (Join-Path $project 'CMakeLists.txt') -Value $cmakeSource -Encoding utf8NoBOM

function Invoke-Configure([string]$Name, [bool]$Success)
{
    $log = Join-Path $work "$Name-configure.log"
    & $CMake -S $project -B $build -G Ninja "-DGUARD=$guard" "-DSDK_ROOT=$copy" "-DPATCH_LOCK=$patchLock" > $log 2>&1
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne $Success) { throw "Unexpected SDK guard configure result: $log" }
    if (-not $Success -and (Get-Content $log -Raw) -notmatch 'SHA256 mismatch|identity mismatch|exact tracked patch|Missing or invalid|requires verified')
    {
        throw "Configure did not reject the intended SDK identity: $log"
    }
}
function Invoke-Build([string]$Name, [bool]$Success)
{
    $log = Join-Path $work "$Name-build.log"
    & $CMake --build $build > $log 2>&1
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne $Success) { throw "Unexpected SDK guard build result: $log" }
    if (-not $Success -and (Get-Content $log -Raw) -notmatch 'SHA256 mismatch|identity mismatch|exact tracked patch|Missing or invalid|requires verified')
    {
        throw "Every-build guard did not reject the intended SDK identity: $log"
    }
}

Invoke-Configure 'baseline' $true
Invoke-Build 'baseline' $true
foreach ($relative in @('include\pxr\usd\sdf\storageAdmission.h', 'include\pxr\base\vt\arrayEdit.h'))
{
    $path = Join-Path $copy $relative
    $original = [IO.File]::ReadAllBytes($path)
    [IO.File]::AppendAllText($path, "`n// same-version drift`n")
    $name = [IO.Path]::GetFileNameWithoutExtension($relative)
    Invoke-Configure $name $false
    Invoke-Build $name $false
    [IO.File]::WriteAllBytes($path, $original)
    Invoke-Configure "$name-restored" $true
    Invoke-Build "$name-restored" $true
}
$binary = $metadata.files | Where-Object { $_.path -match '\.(dll|so|dylib)$' } | Select-Object -First 1
$stream = [IO.File]::Open((Join-Path $copy $binary.path), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
try { $first = $stream.ReadByte(); $stream.Position = 0; $stream.WriteByte($first -bxor 1) }
finally { $stream.Dispose() }
Invoke-Configure 'binary-drift' $false
Invoke-Build 'binary-drift' $false
$stream = [IO.File]::Open((Join-Path $copy $binary.path), [IO.FileMode]::Open, [IO.FileAccess]::Write)
try { $stream.WriteByte($first) } finally { $stream.Dispose() }
Invoke-Configure 'binary-restored' $true
Invoke-Build 'binary-restored' $true
$copyMetadata = Join-Path $copy '.openusd-storage-admission.json'
$originalMetadata = [IO.File]::ReadAllBytes($copyMetadata)
$metadata.accessorVersion = 9999
$metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $copyMetadata -Encoding utf8NoBOM
Invoke-Configure 'accessor-version' $false
Invoke-Build 'accessor-version' $false
[IO.File]::WriteAllBytes($copyMetadata, $originalMetadata)
Move-Item -LiteralPath $copyMetadata ($copyMetadata + '.held')
Invoke-Configure 'missing-provenance' $false
Invoke-Build 'missing-provenance' $false
Move-Item -LiteralPath ($copyMetadata + '.held') $copyMetadata
Invoke-Configure 'all-restored' $true
Invoke-Build 'all-restored' $true
Remove-Item -LiteralPath $copy -Recurse -Force
Write-Output "SDK_STORAGE_GUARDS_OK: two exact same-version headers and actual SDK binary; configure/every-build refusal and restore; evidence=$work"
