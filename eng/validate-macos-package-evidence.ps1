#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$EvidencePath = 'artifacts/package-macos-storm-child/package-evidence.json',
    [string]$NativeSourceMetadataPath = 'artifacts/native-input/osx-arm64/source.json',
    [switch]$RequireArchiveSource,
    [switch]$RunParserTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stormChildHeader = Join-Path $repoRoot 'native/openusd_storm_child/include/openusd_storm_child.h'
$stormChildHeaderText = Get-Content $stormChildHeader -Raw
$stormChildAbiMatch = [regex]::Match(
    $stormChildHeaderText,
    'OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u?')
if (-not $stormChildAbiMatch.Success)
{
    throw "Could not read the Storm child ABI from $stormChildHeader."
}
$stormChildAbiVersion = [int]$stormChildAbiMatch.Groups[1].Value
if ($stormChildAbiVersion -ne 8)
{
    throw "macOS package evidence requires ABI 8, got $stormChildAbiVersion."
}

function Assert-FullSha256
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ($Value -notmatch '^[0-9A-F]{64}$')
    {
        throw "$Name is not an uppercase full SHA-256 digest."
    }
}

function Get-ZipEntryHash
{
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )
    $stream = $Entry.Open()
    try
    {
        return [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($stream))
    }
    finally
    {
        $stream.Dispose()
    }
}

function Assert-StormInstallNameEvidence
{
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceInstallName,
        [Parameter(Mandatory = $true)][string]$ValidatedInstallName
    )
    if ($EvidenceInstallName -cne $ValidatedInstallName)
    {
        throw (
            "Storm child install-name evidence '$EvidenceInstallName' does not " +
            "match native validation '$ValidatedInstallName'.")
    }
}

function Assert-StormIdentityEvidence
{
    param(
        [Parameter(Mandatory = $true)]$StormChild,
        [Parameter(Mandatory = $true)][string]$ActualPackageEntrySha256,
        [string]$SignatureSha256 = ''
    )
    $hashes = [ordered]@{
        'stormChild.sha256' = [string]$StormChild.sha256
        'stormChild.packageEntrySha256' = [string]$StormChild.packageEntrySha256
        'stormChild.nativeInstallSha256' = [string]$StormChild.nativeInstallSha256
        'stormChild.publishedPreSignSha256' =
            [string]$StormChild.publishedPreSignSha256
        'stormChild.publishedPostSignSha256' =
            [string]$StormChild.publishedPostSignSha256
    }
    foreach ($hash in $hashes.GetEnumerator())
    {
        Assert-FullSha256 -Value $hash.Value -Name $hash.Key
    }
    if ($hashes['stormChild.sha256'] -cne $ActualPackageEntrySha256 -or
        $hashes['stormChild.packageEntrySha256'] -cne
            $ActualPackageEntrySha256 -or
        $hashes['stormChild.nativeInstallSha256'] -cne
            $ActualPackageEntrySha256 -or
        $hashes['stormChild.publishedPreSignSha256'] -cne
            $ActualPackageEntrySha256)
    {
        throw (
            'The package, native install, and published pre-sign Storm child ' +
            'hashes do not establish a single identity.')
    }
    if (-not [string]::IsNullOrEmpty($SignatureSha256) -and
        $hashes['stormChild.publishedPostSignSha256'] -cne $SignatureSha256)
    {
        throw (
            'The Storm child post-sign hash does not match its signature evidence.')
    }
}

function Assert-LoadedImageEvidence
{
    param(
        [Parameter(Mandatory = $true)]$Image,
        [Parameter(Mandatory = $true)][string]$AppPrefix
    )
    $path = ([string]$Image.path).Replace('\', '/')
    $derivedUnderAppBase =
        $path.StartsWith($AppPrefix, [StringComparison]::Ordinal) -and
        $path -notmatch '/(native/(install|build)|src|source)/'
    if ([bool]$Image.underAppBase -ne $derivedUnderAppBase -or
        -not $derivedUnderAppBase)
    {
        throw "A project dylib escaped the package application base: $path"
    }
}

function Assert-SignatureEvidence
{
    param([Parameter(Mandatory = $true)]$Signature)
    $path = [string]$Signature.path
    Assert-FullSha256 -Value ([string]$Signature.sha256) -Name "signature sha256 ($path)"
    if ([System.IO.Path]::IsPathRooted($path) -or
        $path.Contains('..', [StringComparison]::Ordinal) -or
        -not [bool]$Signature.verified -or
        -not [bool]$Signature.hardened)
    {
        throw "Invalid strict/hardened codesign evidence: $path"
    }
}

function Assert-EvidenceCompleteness
{
    param(
        [Parameter(Mandatory = $true)][object[]]$LoadedImages,
        [Parameter(Mandatory = $true)][object[]]$Signatures
    )
    $imagePaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $imageNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($image in $LoadedImages)
    {
        $path = [string]$image.path
        $name = [IO.Path]::GetFileName($path)
        if (-not $imagePaths.Add($path) -or -not $imageNames.Add($name))
        {
            throw "Duplicate loaded-image evidence is not allowed: $path"
        }
    }

    $signaturePaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $signatureNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($signature in $Signatures)
    {
        $path = [string]$signature.path
        $name = [IO.Path]::GetFileName($path)
        if (-not $signaturePaths.Add($path))
        {
            throw "Duplicate signature evidence is not allowed: $path"
        }
        if (($name -eq 'Consumer' -or
                $name.StartsWith('libopenusd_', [StringComparison]::Ordinal) -or
                $name.StartsWith('libusd_', [StringComparison]::Ordinal)) -and
            -not $signatureNames.Add($name))
        {
            throw "Duplicate project signature evidence is not allowed: $name"
        }
    }

    $requiredNames = @(
        'Consumer',
        'libusd_ms.dylib',
        'libopenusd_dotnet.dylib',
        'libopenusd_storm_child.dylib')
    foreach ($requiredName in $requiredNames)
    {
        if (-not $imageNames.Contains($requiredName))
        {
            throw "macOS loaded-image evidence is missing $requiredName."
        }
        if (-not $signatureNames.Contains($requiredName))
        {
            throw "macOS signature evidence is missing $requiredName."
        }
    }

    foreach ($imageName in $imageNames)
    {
        $isProjectEvidence =
            $imageName -eq 'Consumer' -or
            $imageName.StartsWith(
                'libopenusd_',
                [StringComparison]::Ordinal) -or
            $imageName.StartsWith('libusd_', [StringComparison]::Ordinal)
        if ($isProjectEvidence -and -not $signatureNames.Contains($imageName))
        {
            throw "macOS signature evidence omitted loaded image $imageName."
        }
    }
    foreach ($signatureName in $signatureNames)
    {
        if (-not $imageNames.Contains($signatureName))
        {
            throw "macOS loaded-image evidence omitted signed project $signatureName."
        }
    }
}

function Assert-Throws
{
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Name
    )
    try
    {
        & $Action
    }
    catch
    {
        return
    }
    throw "Expected negative parser test '$Name' to fail."
}

if ($RunParserTests)
{
    Add-Type -AssemblyName System.IO.Compression
    $content = [Text.Encoding]::UTF8.GetBytes('zip-entry-hash-self-test')
    $memory = [IO.MemoryStream]::new()
    try
    {
        $writer = [IO.Compression.ZipArchive]::new(
            $memory,
            [IO.Compression.ZipArchiveMode]::Create,
            $true)
        $entry = $writer.CreateEntry('hash-input')
        $entryStream = $entry.Open()
        $entryStream.Write($content, 0, $content.Length)
        $entryStream.Dispose()
        $writer.Dispose()
        $memory.Position = 0
        $reader = [IO.Compression.ZipArchive]::new(
            $memory,
            [IO.Compression.ZipArchiveMode]::Read,
            $true)
        $actualHash = Get-ZipEntryHash -Entry $reader.GetEntry('hash-input')
        $expectedHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($content))
        $reader.Dispose()
        if ($actualHash -cne $expectedHash)
        {
            throw 'Get-ZipEntryHash returned an unexpected digest.'
        }
    }
    finally
    {
        $memory.Dispose()
    }

    Assert-StormInstallNameEvidence `
        -EvidenceInstallName '@rpath/libopenusd_storm_child.dylib' `
        -ValidatedInstallName '@rpath/libopenusd_storm_child.dylib'
    Assert-Throws -Name 'install-name mismatch' -Action {
        Assert-StormInstallNameEvidence `
            -EvidenceInstallName '/absolute/libopenusd_storm_child.dylib' `
            -ValidatedInstallName '@rpath/libopenusd_storm_child.dylib'
    }
    $validIdentity = [pscustomobject]@{
        sha256 = ('A' * 64)
        packageEntrySha256 = ('A' * 64)
        nativeInstallSha256 = ('A' * 64)
        publishedPreSignSha256 = ('A' * 64)
        publishedPostSignSha256 = ('B' * 64)
    }
    Assert-StormIdentityEvidence `
        -StormChild $validIdentity `
        -ActualPackageEntrySha256 ('A' * 64) `
        -SignatureSha256 ('B' * 64)
    Assert-Throws -Name 'altered published pre-sign dylib' -Action {
        Assert-StormIdentityEvidence `
            -StormChild ([pscustomobject]@{
                sha256 = ('A' * 64)
                packageEntrySha256 = ('A' * 64)
                nativeInstallSha256 = ('A' * 64)
                publishedPreSignSha256 = ('C' * 64)
                publishedPostSignSha256 = ('B' * 64)
            }) `
            -ActualPackageEntrySha256 ('A' * 64) `
            -SignatureSha256 ('B' * 64)
    }
    Assert-Throws -Name 'post-sign hash mismatch' -Action {
        Assert-StormIdentityEvidence `
            -StormChild $validIdentity `
            -ActualPackageEntrySha256 ('A' * 64) `
            -SignatureSha256 ('C' * 64)
    }
    $appPrefix = '/package/consumer/'
    Assert-LoadedImageEvidence `
        -Image ([pscustomobject]@{
            path = '/package/consumer/libopenusd_storm_child.dylib'
            underAppBase = $true
        }) `
        -AppPrefix $appPrefix
    Assert-Throws -Name 'external loaded image' -Action {
        Assert-LoadedImageEvidence `
            -Image ([pscustomobject]@{
                path = '/usr/local/lib/libopenusd_storm_child.dylib'
                underAppBase = $true
            }) `
            -AppPrefix $appPrefix
    }
    Assert-Throws -Name 'forged loaded-image confinement' -Action {
        Assert-LoadedImageEvidence `
            -Image ([pscustomobject]@{
                path = '/package/consumer/libusd_ms.dylib'
                underAppBase = $false
            }) `
            -AppPrefix $appPrefix
    }
    Assert-SignatureEvidence -Signature ([pscustomobject]@{
        path = 'libopenusd_storm_child.dylib'
        sha256 = ('A' * 64)
        verified = $true
        hardened = $true
    })
    Assert-Throws -Name 'unsigned dylib' -Action {
        Assert-SignatureEvidence -Signature ([pscustomobject]@{
            path = 'libopenusd_storm_child.dylib'
            sha256 = ('A' * 64)
            verified = $false
            hardened = $true
        })
    }
    Assert-Throws -Name 'missing post-sign hash' -Action {
        Assert-SignatureEvidence -Signature ([pscustomobject]@{
            path = 'libopenusd_storm_child.dylib'
            sha256 = ''
            verified = $true
            hardened = $true
        })
    }
    $completeImages = @(
        'Consumer',
        'libusd_ms.dylib',
        'libopenusd_dotnet.dylib',
        'libopenusd_storm_child.dylib',
        'libopenusd_hydra.dylib',
        'libopenusd_hdsilk.dylib') |
        ForEach-Object {
            [pscustomobject]@{
                path = "/package/consumer/$_"
                underAppBase = $true
            }
        }
    $completeSignatures = @(
        'Consumer',
        'libusd_ms.dylib',
        'libopenusd_dotnet.dylib',
        'libopenusd_storm_child.dylib',
        'libopenusd_hydra.dylib',
        'libopenusd_hdsilk.dylib') |
        ForEach-Object {
            [pscustomobject]@{
                path = $_
                sha256 = ('A' * 64)
                verified = $true
                hardened = $true
            }
        }
    Assert-EvidenceCompleteness `
        -LoadedImages $completeImages `
        -Signatures $completeSignatures
    foreach ($missingImage in @(
        'Consumer',
        'libusd_ms.dylib',
        'libopenusd_storm_child.dylib'))
    {
        Assert-Throws -Name "missing image $missingImage" -Action {
            Assert-EvidenceCompleteness `
                -LoadedImages @(
                    $completeImages |
                        Where-Object {
                            [IO.Path]::GetFileName([string]$_.path) -ne
                                $missingImage
                        }) `
                -Signatures $completeSignatures
        }
    }
    foreach ($missingSignature in @(
        'Consumer',
        'libusd_ms.dylib',
        'libopenusd_storm_child.dylib'))
    {
        Assert-Throws -Name "missing signature $missingSignature" -Action {
            Assert-EvidenceCompleteness `
                -LoadedImages $completeImages `
                -Signatures @(
                    $completeSignatures |
                        Where-Object {
                            [IO.Path]::GetFileName([string]$_.path) -ne
                                $missingSignature
                        })
        }
    }
    Write-Output 'macOS package evidence parser/hash tests passed.'
    return
}

if (-not (Test-Path $EvidencePath -PathType Leaf))
{
    throw "The required macOS package evidence is missing: $EvidencePath"
}
$artifactRoot = [System.IO.Path]::GetDirectoryName(
    [System.IO.Path]::GetFullPath($EvidencePath))
$evidence = Get-Content $EvidencePath -Raw | ConvertFrom-Json
if ([int]$evidence.schemaVersion -ne 2 -or $evidence.rid -ne 'osx-arm64')
{
    throw 'macOS package evidence must use schema 2 for osx-arm64.'
}

$packagePath = Join-Path $artifactRoot ([string]$evidence.package)
$validationPath = Join-Path $artifactRoot ([string]$evidence.nativeValidation)
foreach ($path in @($packagePath, $validationPath))
{
    if (-not (Test-Path $path -PathType Leaf))
    {
        throw "An evidenced macOS package artifact is missing: $path"
    }
}
Assert-FullSha256 -Value ([string]$evidence.packageSha256) -Name 'packageSha256'
Assert-FullSha256 `
    -Value ([string]$evidence.nativeValidationSha256) `
    -Name 'nativeValidationSha256'
if ((Get-FileHash $packagePath -Algorithm SHA256).Hash -cne
        [string]$evidence.packageSha256 -or
    (Get-Item $packagePath).Length -ne [long]$evidence.packageSize)
{
    throw 'The macOS package hash or size does not match package evidence.'
}
if ((Get-FileHash $validationPath -Algorithm SHA256).Hash -cne
    [string]$evidence.nativeValidationSha256)
{
    throw 'The macOS native-validation hash does not match package evidence.'
}

Add-Type -AssemblyName System.IO.Compression
$package = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try
{
    $validationEntry = $package.GetEntry(
        'build/OpenUsd.Runtime.Imaging.osx-arm64.native-validation.json')
    if ($null -eq $validationEntry -or
        (Get-ZipEntryHash $validationEntry) -cne
            [string]$evidence.nativeValidationSha256)
    {
        throw 'The packaged macOS native-validation manifest is missing or changed.'
    }
    $stormPath = [string]$evidence.stormChild.path
    $stormEntry = $package.GetEntry($stormPath)
    Assert-FullSha256 `
        -Value ([string]$evidence.stormChild.sha256) `
        -Name 'Storm child sha256'
    $packageStormHash = if ($null -eq $stormEntry)
    {
        ''
    }
    else
    {
        Get-ZipEntryHash $stormEntry
    }
    if ($stormPath -cne
            'runtimes/osx-arm64/native/libopenusd_storm_child.dylib' -or
        $null -eq $stormEntry -or
        $stormEntry.Length -ne [long]$evidence.stormChild.size -or
        $packageStormHash -cne [string]$evidence.stormChild.sha256)
    {
        throw 'The packaged macOS Storm child does not match package evidence.'
    }
}
finally
{
    $package.Dispose()
}
Assert-StormIdentityEvidence `
    -StormChild $evidence.stormChild `
    -ActualPackageEntrySha256 $packageStormHash

$validation = Get-Content $validationPath -Raw | ConvertFrom-Json
if ([int]$validation.schemaVersion -ne 2 -or
    $validation.rid -ne 'osx-arm64' -or
    [int]$validation.stormChildAbiVersion -ne $stormChildAbiVersion -or
    @($validation.rpathPolicy.exactAllowlist).Count -ne 1 -or
    @($validation.rpathPolicy.exactAllowlist)[0] -cne '@loader_path' -or
    -not [bool]$validation.rpathPolicy.rejectRootedPaths -or
    -not [bool]$validation.rpathPolicy.rejectSourceBuildInstallPaths)
{
    throw 'macOS native validation has an unexpected ABI or LC_RPATH policy.'
}
$requiredLibraries = @(
    'libopenusd_storm_child.dylib',
    'libopenusd_hydra.dylib',
    'libopenusd_hdsilk.dylib')
$libraries = @($validation.libraries)
if ($libraries.Count -ne $requiredLibraries.Count)
{
    throw 'macOS native validation must contain exactly three project libraries.'
}
foreach ($libraryName in $requiredLibraries)
{
    $library = @($libraries | Where-Object name -eq $libraryName)
    if ($library.Count -ne 1 -or
        $library[0].installName -cne "@rpath/$libraryName" -or
        @($library[0].rpaths).Count -ne 1 -or
        @($library[0].rpaths)[0] -cne '@loader_path')
    {
        throw "macOS native validation is malformed for $libraryName."
    }
}
$validatedStormChild = @(
    $libraries |
        Where-Object name -eq 'libopenusd_storm_child.dylib')
Assert-StormInstallNameEvidence `
    -EvidenceInstallName ([string]$evidence.stormChild.installName) `
    -ValidatedInstallName ([string]$validatedStormChild[0].installName)

$appBase = [string]$evidence.appBaseCanonical
if ([string]::IsNullOrWhiteSpace($appBase) -or
    -not [System.IO.Path]::IsPathRooted($appBase))
{
    throw 'macOS package evidence is missing a canonical application base.'
}
$appPrefix = $appBase.TrimEnd('/') + '/'
$loadedImages = @($evidence.loadedImages)
if ($loadedImages.Count -lt 2)
{
    throw 'macOS package execution must load at least Storm child and OpenUSD.'
}
foreach ($image in $loadedImages)
{
    Assert-LoadedImageEvidence -Image $image -AppPrefix $appPrefix
}

$signatures = @($evidence.signatures)
if ($signatures.Count -lt 2)
{
    throw 'macOS package evidence must inspect every dylib and the executable.'
}
foreach ($signature in $signatures)
{
    Assert-SignatureEvidence -Signature $signature
}
Assert-EvidenceCompleteness `
    -LoadedImages $loadedImages `
    -Signatures $signatures
$stormSignatures = @(
    $signatures |
        Where-Object {
            [System.IO.Path]::GetFileName([string]$_.path) -ceq
                'libopenusd_storm_child.dylib'
        })
if ($stormSignatures.Count -ne 1)
{
    throw 'macOS package evidence must contain one Storm child signature.'
}
Assert-StormIdentityEvidence `
    -StormChild $evidence.stormChild `
    -ActualPackageEntrySha256 $packageStormHash `
    -SignatureSha256 ([string]$stormSignatures[0].sha256)

$requiredExecution = @(
    'PACKAGE_STORM_CHILD_EXECUTION_OK',
    "STORM_CHILD_ABI=$stormChildAbiVersion",
    'STORM_CHILD_CAPTURE_STATUS=1',
    'STORM_CHILD_NAVIGATION_STATUS=1',
    'STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.',
    'STORM_CHILD_NAVIGATION_RESET=true',
    'DYLD_LIBRARY_PATH_PRESENT=false',
    'PROJECT_OPENUSD_DYLD_IMAGES_CONFINED=true',
    'STORM_CHILD_DYLD_PUBLISH_ROOT=true',
    'OPENUSD_DYLD_PUBLISH_ROOT=true',
    'METAL_PACKAGE_PATHS_CONFINED=true',
    'CWD_IS_PUBLISH=true')
foreach ($line in $requiredExecution)
{
    if (@($evidence.execution) -notcontains $line)
    {
        throw "macOS package execution evidence is missing '$line'."
    }
}

if ($RequireArchiveSource)
{
    if (-not (Test-Path $NativeSourceMetadataPath -PathType Leaf))
    {
        throw "macOS archive source metadata is missing: $NativeSourceMetadataPath"
    }
    $source = Get-Content $NativeSourceMetadataPath -Raw | ConvertFrom-Json
    Assert-FullSha256 -Value ([string]$source.sha256) -Name 'archive source sha256'
    if ($source.source -ne 'archive' -or $source.rid -ne 'osx-arm64')
    {
        throw 'macOS package archive mode did not use an immutable osx-arm64 archive.'
    }
}

Write-Output (
    'Validated macOS package evidence schema, hashes, navigation/capture, exact ' +
    'LC_RPATH, dyld images, and per-file strict/hardened signatures.')
