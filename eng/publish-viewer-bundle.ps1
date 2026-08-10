#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$OutputRoot = 'artifacts/viewer-distribution',
    [string]$PackageSource,
    [string]$PackageVersion,
    [string]$Configuration = 'Release',
    [switch]$NoArchive
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = if ([IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot
}
else
{
    Join-Path $repoRoot $OutputRoot
}
$layoutRoot = Join-Path $outputRoot "layout/$Rid"
$objRoot = Join-Path $outputRoot "obj/$Rid"
$artifactRoot = Join-Path $outputRoot "artifacts/$Rid"
$manifestPath = Join-Path $artifactRoot "OpenUsd.Viewer.$Rid.manifest.json"
$signEvidencePath = Join-Path $artifactRoot "OpenUsd.Viewer.$Rid.signing.json"
$notarizationEvidencePath = Join-Path $artifactRoot "OpenUsd.Viewer.$Rid.notarization.json"

function Get-Version
{
    if (-not [string]::IsNullOrWhiteSpace($PackageVersion))
    {
        return $PackageVersion
    }
    $json = Get-Content (Join-Path $repoRoot 'version.json') -Raw | ConvertFrom-Json
    [string]$json.version
}

function Assert-ArchiveChecksum
{
    param(
        [string]$Path,
        [string]$ChecksumPath
    )

    $line = (Get-Content $ChecksumPath -Raw).Trim()
    $parts = $line -split '\s+', 2
    if ($parts.Count -ne 2)
    {
        throw "Invalid checksum file: $ChecksumPath"
    }
    $expected = $parts[0].ToUpperInvariant()
    $actual = (Get-FileHash $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -cne $expected)
    {
        throw "Checksum verification failed for $Path. Expected $expected; actual $actual."
    }
}

function New-SymbolArchive
{
    param(
        [string]$Destination
    )

    $symbolsRoot = Join-Path $artifactRoot 'symbols'
    Remove-Item $symbolsRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $symbolsRoot | Out-Null
    $symbols = @(Get-ChildItem $layoutRoot -Recurse -File |
        Where-Object {
            $_.Extension -in @('.pdb', '.dbg') -or
            $_.FullName -match '\.dSYM[\\/]'
        } |
        Sort-Object FullName)
    if ($symbols.Count -eq 0)
    {
        throw "No symbol files were found in $layoutRoot."
    }
    foreach ($symbol in $symbols)
    {
        $relative = [IO.Path]::GetRelativePath($layoutRoot, $symbol.FullName)
        $target = Join-Path $symbolsRoot $relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) |
            Out-Null
        Copy-Item $symbol.FullName $target -Force
    }

    if ($Destination.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
    {
        Compress-Archive -Path (Join-Path $symbolsRoot '*') -DestinationPath $Destination -Force
    }
    else
    {
        Push-Location $symbolsRoot
        try
        {
            & tar -czf $Destination .
            if ($LASTEXITCODE -ne 0)
            {
                throw "tar failed while creating $Destination."
            }
        }
        finally
        {
            Pop-Location
        }
    }
}

function Assert-NoNativeInstallReference
{
    $textExtensions = @(
        '.config',
        '.deps.json',
        '.json',
        '.ps1',
        '.runtimeconfig.json',
        '.txt',
        '.xml')
    foreach ($file in Get-ChildItem $layoutRoot -Recurse -File |
        Where-Object { $_.Extension -in $textExtensions })
    {
        $content = Get-Content $file.FullName -Raw
        if ($content -match 'native[\\/]install')
        {
            throw "The Viewer bundle contains a native/install reference in $($file.FullName)."
        }
    }
}

$version = Get-Version
Remove-Item $layoutRoot, $objRoot, $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $layoutRoot, $objRoot, $artifactRoot | Out-Null

$generatedProject = Join-Path $objRoot 'OpenUsd.Viewer.Distribution.App.csproj'
$program = Join-Path $objRoot 'Program.cs'
$nugetConfig = Join-Path $objRoot 'nuget.config'

@"
// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Distribution.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => OpenUsd.Viewer.ViewerEntryPoint.Run(args);
}
"@ | Set-Content $program

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>$Rid</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <AssemblyName>OpenUsd.Viewer.App</AssemblyName>
    <RootNamespace>OpenUsd.Viewer.Distribution.App</RootNamespace>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="OpenUsd.Viewer" Version="$version" />
    <PackageVersion Include="OpenUsd.Runtime.Imaging.$Rid" Version="$version" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="OpenUsd.Viewer" />
    <PackageReference Include="OpenUsd.Runtime.Imaging.$Rid" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Wayland" />
  </ItemGroup>
</Project>
"@ | Set-Content $generatedProject

$sourceLines = @()
$packageSourceMapping = ''
if (-not [string]::IsNullOrWhiteSpace($PackageSource))
{
    $packageSourcePath = if ([IO.Path]::IsPathRooted($PackageSource))
    {
        $PackageSource
    }
    else
    {
        Join-Path $repoRoot $PackageSource
    }
    $sourceLines += "    <add key=`"openusd-local`" value=`"$packageSourcePath`" />"
    $packageSourceMapping = @"
  <packageSourceMapping>
    <packageSource key="openusd-local">
      <package pattern="OpenUsd.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
"@
}
$sourceLines += '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
$($sourceLines -join "`n")
  </packageSources>
$packageSourceMapping
</configuration>
"@ | Set-Content $nugetConfig

dotnet publish $generatedProject `
    -c $Configuration `
    -f net10.0 `
    -r $Rid `
    --self-contained true `
    -o $layoutRoot `
    --configfile $nugetConfig
if ($LASTEXITCODE -ne 0)
{
    throw "Viewer package-only publish failed for $Rid."
}

foreach ($requiredPath in @(
    (Join-Path $layoutRoot 'plugin/usd/plugInfo.json'),
    (Join-Path $layoutRoot 'plugin/usd/hdStorm/resources/plugInfo.json')))
{
    if (-not (Test-Path $requiredPath))
    {
        throw "The package-only Viewer layout is missing $requiredPath."
    }
}

Assert-NoNativeInstallReference

& (Join-Path $PSScriptRoot 'sign-viewer-bundle.ps1') `
    -Rid $Rid `
    -BundleRoot $layoutRoot `
    -EvidencePath $signEvidencePath
if ($LASTEXITCODE -ne 0)
{
    throw "Viewer signing failed for $Rid."
}

$extension = if ($Rid -eq 'win-x64') { '.zip' } else { '.tar.gz' }
$archivePath = Join-Path $artifactRoot "OpenUsd.Viewer.$Rid$extension"
$symbolsPath = Join-Path $artifactRoot "OpenUsd.Viewer.$Rid.symbols$extension"
if (-not $NoArchive)
{
    if ($Rid -eq 'win-x64')
    {
        Compress-Archive -Path (Join-Path $layoutRoot '*') -DestinationPath $archivePath -Force
    }
    else
    {
        Push-Location $layoutRoot
        try
        {
            & tar -czf $archivePath .
            if ($LASTEXITCODE -ne 0)
            {
                throw "tar failed while creating $archivePath."
            }
        }
        finally
        {
            Pop-Location
        }
    }

    New-SymbolArchive -Destination $symbolsPath
    foreach ($path in @($archivePath, $symbolsPath))
    {
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash
        "$hash  $([IO.Path]::GetFileName($path))" |
            Set-Content "$path.sha256"
        Assert-ArchiveChecksum -Path $path -ChecksumPath "$path.sha256"
    }
    if ($Rid -eq 'osx-arm64')
    {
        & (Join-Path $PSScriptRoot 'sign-viewer-bundle.ps1') `
            -Rid $Rid `
            -BundleRoot $layoutRoot `
            -EvidencePath $notarizationEvidencePath `
            -ArchivePath $archivePath `
            -NotarizeOnly
        if ($LASTEXITCODE -ne 0)
        {
            throw "Viewer notarization failed for $Rid."
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    rid = $Rid
    packageVersion = $version
    packageSource = if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        'nuget.org'
    } else {
        $packageSourcePath
    }
    layoutRoot = $layoutRoot
    archivePath = if ($NoArchive) { $null } else { $archivePath }
    symbolsPath = if ($NoArchive) { $null } else { $symbolsPath }
    signingEvidencePath = $signEvidencePath
    notarizationEvidencePath = if ($Rid -eq 'osx-arm64') {
        $notarizationEvidencePath
    } else {
        $null
    }
    nativeInstallReferenceFree = $true
    files = @(Get-ChildItem $layoutRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($layoutRoot, $_.FullName)
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
                length = $_.Length
            }
        })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
Write-Output "VIEWER_BUNDLE_PUBLISHED rid=$Rid archive=$archivePath manifest=$manifestPath"
