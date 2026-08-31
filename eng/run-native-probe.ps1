#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [switch]$SkipNativeAbiProbe
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$publishRoot = Join-Path $repoRoot "artifacts/native-probe/$Rid"
$probeProject = Join-Path $repoRoot 'tests/OpenUsd.NativeProbe/OpenUsd.NativeProbe.csproj'

if (-not (Test-Path $openUsdRoot))
{
    throw "OpenUSD runtime was not found at $openUsdRoot."
}
if (-not (Test-Path $shimRoot))
{
    throw "OpenUsd native shim was not found at $shimRoot."
}

if (Test-Path $publishRoot)
{
    Remove-Item $publishRoot -Recurse -Force
}

& dotnet publish $probeProject `
    -c Release `
    -f net10.0 `
    -r $Rid `
    -p:AotProbe=true `
    -o $publishRoot
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$binTarget = Join-Path $publishRoot 'bin'
$libTarget = Join-Path $publishRoot 'lib'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget | Out-Null
foreach ($layout in @(
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path $layout.Source)
    {
        Get-ChildItem $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

$pluginSource = Join-Path (Join-Path $openUsdRoot 'lib') 'usd'
$pluginPath = Join-Path $publishRoot 'plugin/usd'
New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
$rendererPluginSource = Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'
if (Test-Path $rendererPluginSource)
{
    Copy-Item (Join-Path $rendererPluginSource '*') $pluginPath -Recurse -Force
}
$shimPluginSource = Join-Path (Join-Path $shimRoot 'plugin') 'usd'
if (Test-Path $shimPluginSource)
{
    Copy-Item (Join-Path $shimPluginSource '*') $pluginPath -Recurse -Force
}

$stagedStage = Join-Path $publishRoot ([System.IO.Path]::GetFileName($StagePath))
Copy-Item ([System.IO.Path]::GetFullPath($StagePath)) $stagedStage -Force
$stagedImageFixtures = @(
    'test-assets/mcp-monkey-car-city-textures/asphalt_roughness.jpg',
    'test-assets/mcp-monkey-car-city-textures/asphalt_diffuse.jpg',
    'test-assets/native-image-gray-alpha.png',
    'test-assets/ocio-test-config.ocio'
) | ForEach-Object {
    $source = Join-Path $repoRoot $_
    $target = Join-Path $publishRoot ([System.IO.Path]::GetFileName($source))
    Copy-Item $source $target -Force
    $target
}

$nativeProbe = $null
$testResolverPlugInfo = $null
if (-not $SkipNativeAbiProbe)
{
    $nativeProbeSource = Join-Path $repoRoot "native/build/shim/$Rid/tests/openusd_native_probe"
    if ($IsWindows)
    {
        $nativeProbeSource += '.exe'
    }
    if (-not (Test-Path $nativeProbeSource))
    {
        throw "Native ABI probe was not found at $nativeProbeSource."
    }
    $nativeProbe = Join-Path $publishRoot ([System.IO.Path]::GetFileName($nativeProbeSource))
    Copy-Item $nativeProbeSource $nativeProbe -Force

    # The third-party resolver plugin is never installed. It is staged as the tree it was built as,
    # beside the packaged plugin path rather than inside it, so the probe can only find it by
    # registering its own plugInfo.json.
    $testResolverSource = Join-Path $repoRoot "native/build/shim/$Rid/tests/plugins/openusdTestResolver"
    if (-not (Test-Path $testResolverSource))
    {
        throw "Third-party resolver plugin tree was not found at $testResolverSource."
    }
    $vendorPluginRoot = Join-Path $publishRoot 'vendor-plugins'
    New-Item -ItemType Directory -Force -Path $vendorPluginRoot | Out-Null
    Copy-Item $testResolverSource $vendorPluginRoot -Recurse -Force
    $testResolverPlugInfo = Join-Path $vendorPluginRoot 'openusdTestResolver/resources/plugInfo.json'
    if (-not (Test-Path $testResolverPlugInfo))
    {
        throw "Third-party resolver plugInfo.json was not staged at $testResolverPlugInfo."
    }
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $publishRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath

    if ($null -ne $nativeProbe)
    {
        & $nativeProbe $pluginPath $stagedStage @stagedImageFixtures $testResolverPlugInfo
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }

    $executable = Join-Path $publishRoot 'OpenUsd.NativeProbe'
    if ($IsWindows)
    {
        $executable += '.exe'
    }

    & $executable $pluginPath $stagedStage
    exit $LASTEXITCODE
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
}
