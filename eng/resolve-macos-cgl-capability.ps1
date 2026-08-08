#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath = 'artifacts/render-capability/macos-cgl.json'
)

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS)
{
    throw 'The macOS CGL capability resolver can run only on macOS.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath
}
else
{
    Join-Path $repoRoot $OutputPath
}

$source = @'
using System;
using System.Runtime.InteropServices;

public static class OpenUsdCglCapability
{
    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    [DllImport(OpenGl, EntryPoint = "CGLChoosePixelFormat")]
    public static extern int CGLChoosePixelFormat(int[] attributes, ref IntPtr pixelFormat, out int count);

    [DllImport(OpenGl, EntryPoint = "CGLDestroyPixelFormat")]
    public static extern int CGLDestroyPixelFormat(IntPtr pixelFormat);
}
'@
Add-Type -TypeDefinition $source

$cglSuccess = 0
$kCGLBadPixelFormat = 10002
$CglPfaColorSize = 8
$CglPfaAlphaSize = 11
$CglPfaDepthSize = 12
$CglPfaStencilSize = 13
$CglPfaAccelerated = 73
$CglPfaAllowOfflineRenderers = 96
$CglPfaOpenGlProfile = 99
$CglOglPVersion32Core = 0x3200

$attributes = @(
    $CglPfaOpenGlProfile, $CglOglPVersion32Core,
    $CglPfaAccelerated,
    $CglPfaAllowOfflineRenderers,
    $CglPfaColorSize, 32,
    $CglPfaAlphaSize, 8,
    $CglPfaDepthSize, 24,
    $CglPfaStencilSize, 8,
    0)

$pixelFormat = [IntPtr]::Zero
$pixelFormatCount = 0
$error = [OpenUsdCglCapability]::CGLChoosePixelFormat(
    $attributes,
    [ref]$pixelFormat,
    [ref]$pixelFormatCount)

try
{
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
    if ($error -eq $cglSuccess -and $pixelFormat -ne [IntPtr]::Zero -and $pixelFormatCount -gt 0)
    {
        [ordered]@{
            schemaVersion = 1
            status = 'available'
            completedAt = [DateTimeOffset]::UtcNow.ToString('O')
            platform = 'macos-cgl'
            cglError = $error
            pixelFormatCount = $pixelFormatCount
            allowOfflineRenderers = $true
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutput
        Write-Host "[macos-cgl] Available: accelerated offline-capable OpenGL 3.2 core pixel format."
        if ($env:GITHUB_OUTPUT)
        {
            "available=true" >> $env:GITHUB_OUTPUT
        }
        return
    }

    if ($error -eq $kCGLBadPixelFormat -or $pixelFormatCount -le 0)
    {
        # Render run 31263500952 reached package success and then failed here:
        # CGLChoosePixelFormat failed with CGL error 10002 on hosted macOS arm64.
        $reason = if ($error -eq $kCGLBadPixelFormat)
        {
            'CGLChoosePixelFormat failed with CGL error 10002 for an accelerated ' +
                'offline-capable OpenGL 3.2 core pixel format on this headless host.'
        }
        else
        {
            'CGLChoosePixelFormat found no accelerated offline-capable OpenGL ' +
                '3.2 core pixel format on this headless host.'
        }
        [ordered]@{
            schemaVersion = 1
            status = 'skipped'
            completedAt = [DateTimeOffset]::UtcNow.ToString('O')
            platform = 'macos-cgl'
            reason = $reason
            cglError = $error
            pixelFormatCount = $pixelFormatCount
            allowOfflineRenderers = $true
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutput
        Write-Host "[macos-cgl] Skipped: $reason"
        if ($env:GITHUB_OUTPUT)
        {
            "available=false" >> $env:GITHUB_OUTPUT
        }
        return
    }

    throw "CGLChoosePixelFormat failed with unexpected CGL error $error."
}
finally
{
    if ($pixelFormat -ne [IntPtr]::Zero)
    {
        [void][OpenUsdCglCapability]::CGLDestroyPixelFormat($pixelFormat)
    }
}
