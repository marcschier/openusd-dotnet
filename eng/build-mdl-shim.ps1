#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Builds the optional openusd_mdl material adapter for one RID.

.DESCRIPTION
    The MDL preset configures the whole native project, so cmake --install would write every shim it
    built, not only openusd_mdl. That is why this script never installs: it builds in place, reports
    the built adapter path, and leaves native/install/shim/<rid> untouched. That prefix is the one
    the verified native archive is extracted into and the one packaging reads, and no base package
    may ever contain an MDL binary.

    Nothing here downloads, builds, or links the NVIDIA MDL SDK. eng/mdl.lock.json pins a verified
    SDK baseline as provenance for a future SDK-backed adapter; the adapter this script builds is an
    authored-value distillation foundation that links nothing at all.

    Only win-x64 is gated by a workflow. The linux-x64-mdl and osx-arm64-mdl presets build the
    identical dependency-free source and this script accepts them, but no workflow builds them, so
    those RIDs are buildable rather than proven.

    The MDL presets turn Vulkan off. Nothing in this slice touches Vulkan, and the default win-x64
    preset already covers the MaterialX Vulkan generation path, so requiring a Vulkan SDK here would
    add a prerequisite that gates nothing.

    Deploying the adapter is deliberately a separate, explicit act: copy the reported library beside
    the hdSilk library, or point OPENUSD_MDL_ADAPTER_PATH at its absolute path. The loader accepts
    no relative path and never loads by bare library name.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,

    # Runs the hdSilk native probe from the MDL build tree. That probe is the only place the
    # distillation half of the MDL slice is proven: without OPENUSD_WITH_MDL the probe compiles the
    # adapter-absent checks alone, because asserting distillation with no adapter asserts nothing.
[switch]$RunProbe,

# Also builds the SDK-backed sibling against an MDL SDK acquisition, and runs the adapter
# probes against both adapters. Point this at what eng/fetch-mdl-sdk.ps1 produced. Nothing
# about this path is packaged: the SDK runtime is user-supplied and is never redistributed.
[string]$MdlSdkRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$openUsdInstallRoot = Join-Path $repoRoot "native/install/$Rid"
$preset = "$Rid-mdl"
$buildRoot = Join-Path 'build/shim' $preset

if (-not (Test-Path $openUsdInstallRoot))
{
    throw "The OpenUSD native install is missing at '$openUsdInstallRoot'."
}

$env:OPENUSD_ROOT = $openUsdInstallRoot
Push-Location (Join-Path $repoRoot 'native')
try
{
    $configureArguments = @('--preset', $preset)
    if ($MdlSdkRoot)
    {
        $resolvedSdkRoot = [System.IO.Path]::GetFullPath($MdlSdkRoot).Replace('\', '/')
        if (-not (Test-Path (Join-Path $resolvedSdkRoot 'include/mi/neuraylib/ineuray.h')))
        {
            throw (
                "'$MdlSdkRoot' is not an MDL SDK acquisition: it has no " +
                'include/mi/neuraylib/ineuray.h. Run ./eng/fetch-mdl-sdk.ps1 first.')
        }
        $configureArguments += "-DOPENUSD_MDL_SDK_ROOT=$resolvedSdkRoot"
        $runtimeDirectory = Join-Path $resolvedSdkRoot 'runtime'
        if (Test-Path $runtimeDirectory)
        {
            # The probe needs a runtime to load; without one it reports the
            # unavailable state and still passes, which is the point.
            $configureArguments +=
                "-DOPENUSD_MDL_SDK_RUNTIME_DIR=$($runtimeDirectory.Replace('\', '/'))"
        }
    }
    if ($Rid -eq 'linux-x64')
    {
        $configureArguments += @(
            '-DCMAKE_BUILD_RPATH_USE_ORIGIN=ON',
            '-DCMAKE_INSTALL_RPATH=$ORIGIN')
    }
    elseif ($Rid -eq 'osx-arm64')
    {
        $configureArguments += @(
            '-DCMAKE_INSTALL_NAME_DIR=@rpath',
            '-DCMAKE_INSTALL_RPATH=@loader_path',
            '-DCMAKE_BUILD_WITH_INSTALL_RPATH=OFF')
    }

    & cmake @configureArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    & cmake --build --preset $preset
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    if ($RunProbe)
    {
        # The adapter probes cover the C ABI boundary and, with an SDK, the
        # module evaluation; the hdSilk probe covers the end-to-end path through
        # UsdImaging and the page wire format. Both are needed: neither implies
        # the other.
        $probeFilter = if ($MdlSdkRoot) { 'hdsilk_probe|mdl_.*probe' } else { 'hdsilk_probe' }
        # --no-tests=error is what turns this filtered run into evidence. CTest reports
        # success when a -R expression selects nothing, so a renamed, unregistered or
        # never-configured hdsilk_probe would otherwise leave the only adapter-present
        # MDL check green having executed no test at all.
        & ctest --test-dir $buildRoot -C Release -R $probeFilter `
            --no-tests=error --output-on-failure
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }
}
finally
{
    Pop-Location
}

$adapter = switch ($Rid)
{
    'win-x64' { Join-Path $repoRoot "native/$buildRoot/openusd_mdl/openusd_mdl.dll" }
    'linux-x64' { Join-Path $repoRoot "native/$buildRoot/openusd_mdl/libopenusd_mdl.so" }
    'osx-arm64' { Join-Path $repoRoot "native/$buildRoot/openusd_mdl/libopenusd_mdl.dylib" }
}
if (-not (Test-Path $adapter))
{
    throw "The MDL adapter build did not produce '$adapter'."
}

Write-Host "Built optional MDL adapter: $adapter"
Write-Host (
    'It is not installed and is in no package. Point OPENUSD_MDL_ADAPTER_PATH at this absolute ' +
    'path, or copy it beside the hdSilk library, to enable accepted-subset MDL distillation. ' +
    'The loader refuses a relative path and never loads by bare library name.')
