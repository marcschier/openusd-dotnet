# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$NativeRoot = (Join-Path $PSScriptRoot '../native')
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path $PSScriptRoot 'openusd.install.lock.json') -Raw | ConvertFrom-Json
$sdk = Join-Path $NativeRoot "install/$Rid"
& (Join-Path $PSScriptRoot 'sdk-runtime-patch-metadata.ps1') -Operation Verify -SdkRoot $sdk -Rid $Rid
& (Join-Path $PSScriptRoot 'sdk-storage-admission-metadata.ps1') -Operation Verify -SdkRoot $sdk -Rid $Rid
& python (Join-Path $PSScriptRoot 'generate-native-notices.py') verify --rid $Rid --install-root $sdk
if ($LASTEXITCODE -ne 0) { throw 'Verified SDK build reuse requires complete current redistribution notices.' }
$required = @(
    (Join-Path $sdk '.openusd-install-metadata.json'),
    (Join-Path $sdk '.openusd-runtime-patches.json'),
    (Join-Path $sdk '.openusd-storage-admission.json'),
    (Join-Path $sdk '.openusd-native-notices.json'),
    (Join-Path $sdk 'THIRD-PARTY.md'))
if ($Rid -eq 'osx-arm64')
{
    $compiler = Join-Path $NativeRoot "install/shaderc/$Rid"
    $required += @(
        (Join-Path $compiler 'include/shaderc/shaderc.hpp'),
        (Join-Path $compiler 'lib/libshaderc_combined.a'),
        (Join-Path $compiler 'shaderc-provenance.json'))
}
else
{
    $compiler = Join-Path $NativeRoot "install/vulkan-sdk-$($lock.vulkanSdk.version)"
    $include = if ($Rid -eq 'win-x64') { 'Include' } else { 'include' }
    $lib = if ($Rid -eq 'win-x64') { 'Lib' } else { 'lib' }
    $required += @(
        (Join-Path $compiler "$include/vulkan/vulkan.h"),
        (Join-Path $compiler "$include/vulkan/vk_enum_string_helper.h"),
        (Join-Path $compiler "$include/vma/vk_mem_alloc.h"))
    $required += if ($Rid -eq 'win-x64')
    {
        @(
            (Join-Path $compiler "$lib/shaderc_combined.lib"),
            (Join-Path $compiler "$lib/shaderc_combinedd.lib"),
            (Join-Path $compiler 'Bin/vulkan-1.dll'))
    }
    else
    {
        @(
            (Join-Path $compiler "$lib/libshaderc_combined.a"),
            (Join-Path $compiler "$lib/libvulkan.so"))
    }
}
foreach ($file in $required)
{
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
        (Get-Item -LiteralPath $file -Force).Length -eq 0)
    {
        throw "The verified SDK build-cache pair is incomplete: $file"
    }
}
if ($Rid -eq 'osx-arm64')
{
    & (Join-Path $PSScriptRoot 'verify-private-shaderc-cache.ps1') -CompilerRoot $compiler
}
Write-Host "SDK_BUILD_INPUT_VERIFIED: rid=$Rid; vendor inputs only, not native qualification."
