#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

function Get-OpenUsdSoakSourceFiles
{
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $inputs = @(
        '.editorconfig',
        '.github/workflows/render.yml',
        'Directory.Build.props',
        'Directory.Packages.props',
        'OpenUsd.slnx',
        'global.json',
        'nuget.config',
        'version.json',
        'eng/',
        'native/CMakeLists.txt',
        'native/CMakePresets.json',
        'native/hdSilk/',
        'native/openusd_dotnet/',
        'native/openusd_hydra/',
        'native/private/',
        'native/openusd_storm_child/',
        'src/',
        'test-assets/',
        'tests/')
    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($inputPath in $inputs)
    {
        $fullInput = Join-Path $RepositoryRoot $inputPath
        if (Test-Path $fullInput -PathType Leaf)
        {
            [void]$files.Add([System.IO.Path]::GetRelativePath(
                $RepositoryRoot,
                $fullInput).Replace('\', '/'))
            continue
        }
        if (Test-Path $fullInput -PathType Container)
        {
            Get-ChildItem $fullInput -File -Recurse |
                Where-Object {
                    $_.FullName -notmatch '\\(bin|obj|build|install|downloads|artifacts)\\'
                } |
                ForEach-Object {
                    [void]$files.Add([System.IO.Path]::GetRelativePath(
                        $RepositoryRoot,
                        $_.FullName).Replace('\', '/'))
                }
        }
    }
    $files = @($files | Sort-Object -CaseSensitive -Unique)
    if ($files.Count -eq 0)
    {
        throw 'Could not enumerate shared-stage soak source files.'
    }
    return $files
}

function Get-OpenUsdSoakSourceHash
{
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $files = @(Get-OpenUsdSoakSourceFiles $RepositoryRoot)

    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try
    {
        foreach ($relativePath in $files)
        {
            $pathBytes = [System.Text.Encoding]::UTF8.GetBytes(
                $relativePath.Replace('\', '/') + "`n")
            $hash.AppendData($pathBytes)
            $fullPath = Join-Path $RepositoryRoot $relativePath
            $stream = [System.IO.File]::OpenRead($fullPath)
            try
            {
                $buffer = [byte[]]::new(65536)
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0)
                {
                    $hash.AppendData($buffer, 0, $read)
                }
            }
            finally
            {
                $stream.Dispose()
            }
        }
        return [Convert]::ToHexString($hash.GetHashAndReset())
    }
    finally
    {
        $hash.Dispose()
    }
}

function Set-OpenUsdSoakIdentityEnvironment
{
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Executable
    )

    $resolvedExecutable = (Resolve-Path $Executable).Path
    $env:OPENUSD_SOAK_SOURCE_HASH = Get-OpenUsdSoakSourceHash $RepositoryRoot
    $env:OPENUSD_SOAK_EXECUTABLE_HASH =
        (Get-FileHash $resolvedExecutable -Algorithm SHA256).Hash.ToUpperInvariant()
    $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC =
        [DateTimeOffset]::new(
            [System.IO.File]::GetLastWriteTimeUtc($resolvedExecutable)).ToString(
            'O',
            [System.Globalization.CultureInfo]::InvariantCulture)
}

function Assert-OpenUsdSoakArtifact
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$RequirePostLossFrame
    )

    if (-not (Test-Path $Path))
    {
        throw "Shared-stage soak artifact was not produced: $Path"
    }
    $artifact = Get-Content $Path -Raw | ConvertFrom-Json
    foreach ($comparison in @(
        @{ Name = 'source hash'; Expected = $env:OPENUSD_SOAK_SOURCE_HASH; Actual = $artifact.sourceHash },
        @{ Name = 'executable hash'; Expected = $env:OPENUSD_SOAK_EXECUTABLE_HASH; Actual = $artifact.executableHash }))
    {
        if (-not [string]::Equals(
            $comparison.Expected,
            $comparison.Actual,
            [StringComparison]::OrdinalIgnoreCase))
        {
            throw "Stale soak $($comparison.Name): expected $($comparison.Expected), actual $($comparison.Actual)."
        }
    }
    $expectedTimestamp = [DateTimeOffset]::Parse(
        $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC,
        [System.Globalization.CultureInfo]::InvariantCulture)
    $actualTimestamp = [DateTimeOffset]$artifact.executableTimestamp
    if ($expectedTimestamp.UtcTicks -ne $actualTimestamp.UtcTicks)
    {
        throw "Stale soak executable timestamp: expected $expectedTimestamp, actual $actualTimestamp."
    }
    if ($artifact.status -ne 'passed' -or
        -not $artifact.resourcesReleased -or
        -not $artifact.targetedColorUpsertObserved -or
        $artifact.rendererFaultCount -ne 0 -or
        @($artifact.memoryCheckpoints).Count -lt 20)
    {
        throw "Shared-stage soak artifact failed its hardened gates: $Path"
    }
    if ($RequirePostLossFrame -and $artifact.postLossStormFrames -lt 1)
    {
        throw "Shared-stage soak artifact has no successful post-loss frame: $Path"
    }
    if ($RequirePostLossFrame -and $artifact.rendererShutdownCompletions -lt 1)
    {
        throw "Shared-stage soak artifact has no observed render-pump shutdown completion: $Path"
    }
    if ($artifact.finalDisplayColorTime -ne 'default')
    {
        throw "Shared-stage soak artifact did not validate the default-time displayColor: $Path"
    }
    $expectedColor = @($artifact.expectedFinalDisplayColor)
    $actualColor = @($artifact.actualFinalDisplayColor)
    $deterministicColor = @(0.92, 0.752, 0.416, 1.0)
    $colorMatches = $expectedColor.Count -eq 4 -and $actualColor.Count -eq 4
    for ($index = 0; $colorMatches -and $index -lt 4; $index++)
    {
        $colorMatches =
            [Math]::Abs([double]$expectedColor[$index] - $deterministicColor[$index]) -le 0.000001 -and
            [Math]::Abs([double]$actualColor[$index] - $deterministicColor[$index]) -le 0.000001
    }
    if (-not $colorMatches)
    {
        throw "Shared-stage soak artifact has an incorrect final displayColor: $Path"
    }
    $expectedMeshes = @($artifact.expectedFinalMeshes)
    $actualMeshes = @($artifact.actualFinalMeshes)
    if ($expectedMeshes.Count -lt 2 -or
        ($expectedMeshes | ConvertTo-Json -Compress) -ne
            ($actualMeshes | ConvertTo-Json -Compress))
    {
        throw "Shared-stage soak artifact has a stale or inexact final mesh identity set: $Path"
    }
    foreach ($meshPath in @('/World/SoakMeshA', '/World/SoakMeshB'))
    {
        $mesh = @($expectedMeshes | Where-Object path -EQ $meshPath)
        if ($mesh.Count -ne 1 -or
            @($artifact.removedMeshIds) -notcontains [string]$mesh[0].id -or
            @($artifact.restoredMeshIds) -notcontains [string]$mesh[0].id)
        {
            throw "Shared-stage soak artifact did not prove removal/restoration for $meshPath`: $Path"
        }
    }
}
