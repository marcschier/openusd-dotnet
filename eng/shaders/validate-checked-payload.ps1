#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'osx-arm64', 'win-x64')]
    [string]$Rid,
    [string]$CheckedRoot = (Join-Path $PSScriptRoot 'checked'),
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [string]$OutputRoot,
    [switch]$SkipManifestHashes
)

$ErrorActionPreference = 'Stop'
$CheckedRoot = [System.IO.Path]::GetFullPath($CheckedRoot)
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputRoot)
{
    $OutputRoot = Join-Path $PSScriptRoot "out/checked-payload/$Rid"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$lockPath = Join-Path $PSScriptRoot 'toolchain.lock.json'
$manifestPath = Join-Path $PSScriptRoot 'shader-manifest.json'
$stagedLibraryPath = Join-Path $CheckedRoot 'mesh.metallib'
$stagedManifestPath = Join-Path $CheckedRoot 'mesh.metallib.manifest.json'

function Convert-ToRepositoryRelativePath
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $relativePath = [System.IO.Path]::GetRelativePath(
        $repoRoot,
        $fullPath).Replace('\', '/')
    if (
        [System.IO.Path]::IsPathRooted($relativePath) -or
        $relativePath -eq '..' -or
        $relativePath.StartsWith('../', [System.StringComparison]::Ordinal)
    )
    {
        throw "$Description must be inside the repository: $fullPath"
    }
    if (
        $relativePath.Split('/') |
            Where-Object { $_ -eq '..' }
    )
    {
        throw "$Description contains a parent path: $relativePath"
    }

    return $relativePath
}

$checkedRootRelative = Convert-ToRepositoryRelativePath `
    -Path $CheckedRoot `
    -Description 'CheckedRoot'
$outputRootRelative = Convert-ToRepositoryRelativePath `
    -Path $OutputRoot `
    -Description 'OutputRoot'

if ($Rid -eq 'osx-arm64')
{
    Remove-Item $stagedLibraryPath -Force -ErrorAction SilentlyContinue
    Remove-Item $stagedManifestPath -Force -ErrorAction SilentlyContinue
}

$modelJson = & python (Join-Path $PSScriptRoot 'scripts/shader-commands.py') `
    --lock $lockPath `
    --manifest $manifestPath `
    --model-only
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the locked shader toolchain model.'
}
$model = $modelJson | ConvertFrom-Json
$checkedManifest = Get-Content (Join-Path $CheckedRoot 'manifest.json') -Raw | ConvertFrom-Json
$structuralArguments = @(
    (Join-Path $PSScriptRoot 'scripts/checked_payload.py'),
    '--checked-root', $CheckedRoot,
    '--lock', $lockPath,
    '--manifest', $manifestPath
)
if ($SkipManifestHashes)
{
    $structuralArguments += '--skip-hashes'
}
& python @structuralArguments
if ($LASTEXITCODE -ne 0)
{
    throw 'Checked shader payload structural validation failed.'
}

if ($Rid -eq 'linux-x64')
{
    if (-not $IsLinux)
    {
        throw 'linux-x64 checked payload validation requires Linux.'
    }
    $validator = Join-Path $ToolRoot 'linux-x64/spirv-tools/bin/spirv-val'
    if (-not (Test-Path $validator))
    {
        throw "Pinned spirv-val is missing at $validator."
    }
    $validatorVersion = (
        & $validator --version 2>&1 |
            Select-Object -First 1
    ).ToString()
    if ($validatorVersion -notlike "*$($model.spirvToolsCommit)*")
    {
        throw "spirv-val is not stamped with $($model.spirvToolsCommit)."
    }
    foreach ($program in $checkedManifest.programs)
    {
        & $validator `
            --target-env $model.spirvTargetEnv `
            (Join-Path $CheckedRoot "$($program.name).spv")
        if ($LASTEXITCODE -ne 0)
        {
            throw "spirv-val rejected checked payload $($program.name).spv."
        }
    }
    Write-Host "Validated $($checkedManifest.programs.Count) checked SPIR-V payloads."
    return
}

if ($Rid -eq 'win-x64')
{
    if (-not $IsWindows)
    {
        throw 'win-x64 checked payload validation requires Windows.'
    }
    Write-Host "Validated $($checkedManifest.programs.Count) checked payload programs."
    return
}

if (-not $IsMacOS)
{
    throw 'osx-arm64 checked payload validation requires macOS.'
}
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($architecture -ne 'Arm64')
{
    throw "osx-arm64 checked payload validation requires arm64, got $architecture."
}
$actualXcode = (& xcodebuild -version | Select-Object -First 1).Trim()
if ($actualXcode -ne "Xcode $($model.xcodeVersion)")
{
    throw "Expected Xcode $($model.xcodeVersion), got '$actualXcode'."
}
& xcrun -f metal-objdump | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw 'Xcode metal-objdump is required.'
}

Remove-Item $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$contractJson = & python `
    (Join-Path $PSScriptRoot 'scripts/checked_payload.py') `
    --print-metal-library-contract `
    --manifest $manifestPath
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the locked Metal library entry-point contract.'
}
$libraryContracts = @($contractJson | ConvertFrom-Json)
$libraryPrograms = foreach ($contract in $libraryContracts)
{
    $matches = @(
        $manifest.programs |
            Where-Object name -EQ $contract.programName
    )
    if ($matches.Count -ne 1)
    {
        throw "Expected exactly one $($contract.programName) shader program."
    }
    $program = $matches[0]
    if (
        $program.entryPoint -ne $contract.entryPoint -or
        $program.stage -ne $contract.stage
    )
    {
        throw (
            "$($contract.programName) must declare $($contract.stage) " +
            "entry point $($contract.entryPoint).")
    }
    $program
}

$libraryPath = Join-Path $OutputRoot 'mesh.metallib'
$symbolDumpPath = Join-Path $OutputRoot 'mesh.symbols.txt'
$libraryRelative = Convert-ToRepositoryRelativePath `
    -Path $libraryPath `
    -Description 'Metallib output'
$symbolDumpRelative = Convert-ToRepositoryRelativePath `
    -Path $symbolDumpPath `
    -Description 'Symbol dump output'
$stagedLibraryRelative = Convert-ToRepositoryRelativePath `
    -Path $stagedLibraryPath `
    -Description 'Staged metallib'
$stagedManifestRelative = Convert-ToRepositoryRelativePath `
    -Path $stagedManifestPath `
    -Description 'Staged metallib manifest'
$sourceRecords = @()
$airRecords = @()
$compileCommands = @()
$airPaths = @()
$airRelativePaths = @()

Push-Location $repoRoot
try
{
    foreach ($program in $libraryPrograms)
    {
        $sourcePath = Join-Path $CheckedRoot "$($program.name).metal"
        $airPath = Join-Path $OutputRoot "$($program.name).air"
        $airPaths += $airPath
        $sourceRelative = Convert-ToRepositoryRelativePath `
            -Path $sourcePath `
            -Description 'Metal source'
        $airRelative = Convert-ToRepositoryRelativePath `
            -Path $airPath `
            -Description 'AIR output'
        $airRelativePaths += $airRelative
        $metalArguments = @(
            '-sdk', 'macosx',
            'metal', "-std=$($model.metalStandard)",
            '-c', $sourceRelative,
            '-o', $airRelative
        )
        & xcrun @metalArguments
        if ($LASTEXITCODE -ne 0)
        {
            throw "Metal rejected checked payload $($program.name).metal."
        }
        $sourceRecords += [ordered]@{
            programName = $program.name
            path = $sourceRelative
            sha256 = (
                Get-FileHash $sourcePath -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            size = (Get-Item $sourcePath).Length
            entryPoint = $program.entryPoint
            stage = $program.stage
        }
        $airRecords += [ordered]@{
            programName = $program.name
            path = $airRelative
            sha256 = (
                Get-FileHash $airPath -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            size = (Get-Item $airPath).Length
            entryPoint = $program.entryPoint
            stage = $program.stage
        }
        $compileCommands += [ordered]@{
            programName = $program.name
            executable = 'xcrun'
            arguments = $metalArguments
        }
    }

    $metallibArguments = @(
        '-sdk', 'macosx',
        'metallib'
    ) + $airRelativePaths + @(
        '-o', $libraryRelative
    )
    & xcrun @metallibArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw 'metallib rejected the combined checked shader AIR inputs.'
    }
    $symbols = & xcrun metal-objdump --syms $libraryRelative 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "metal-objdump could not load $libraryRelative."
    }
    $symbolText = ($symbols | ForEach-Object { $_.ToString() }) -join "`n"
    [System.IO.File]::WriteAllText(
        $symbolDumpPath,
        "$symbolText`n",
        [System.Text.UTF8Encoding]::new($false))
    $symbolValidationCommands = @()
    foreach ($program in $libraryPrograms)
    {
        $arguments = @(
            'eng/shaders/scripts/checked_payload.py',
            '--symbols-input', $symbolDumpRelative,
            '--entry-point', $program.entryPoint
        )
        & python @arguments
        if ($LASTEXITCODE -ne 0)
        {
            throw "Metallib entry point $($program.entryPoint) is missing."
        }
        $symbolValidationCommands += [ordered]@{
            programName = $program.name
            executable = 'python'
            arguments = $arguments
        }
    }
}
finally
{
    Pop-Location
}

Copy-Item $libraryPath $stagedLibraryPath -Force
$libraryHash = (
    Get-FileHash $libraryPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$stagedHash = (
    Get-FileHash $stagedLibraryPath -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($libraryHash -ne $stagedHash)
{
    throw 'The staged mesh.metallib differs from the validated output.'
}

$requiredInputsJson = & python `
    (Join-Path $PSScriptRoot 'scripts/checked_payload.py') `
    --print-required-inputs `
    --manifest $manifestPath
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the required checked payload provenance set.'
}
$provenancePaths = @($requiredInputsJson | ConvertFrom-Json)
$provenance = foreach ($relativePath in $provenancePaths)
{
    $fullPath = Join-Path $repoRoot $relativePath
    [ordered]@{
        path = $relativePath
        sha256 = (
            Get-FileHash $fullPath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
    }
}

$payloadManifest = [ordered]@{
    schemaVersion = 4
    rid = 'osx-arm64'
    checkedRoot = $checkedRootRelative
    payloadRoot = $outputRootRelative
    stagedManifestPath = $stagedManifestRelative
    toolchain = $model
    provenance = $provenance
    library = [ordered]@{
        name = 'mesh'
        path = 'mesh.metallib'
        stagedPath = $stagedLibraryRelative
        sha256 = $libraryHash
        size = (Get-Item $libraryPath).Length
        sources = $sourceRecords
        air = $airRecords
        entryPoints = @(
            $libraryPrograms |
                ForEach-Object {
                    [ordered]@{
                        programName = $_.name
                        name = $_.entryPoint
                        stage = $_.stage
                    }
                }
        )
        symbolDump = 'mesh.symbols.txt'
        symbolDumpSha256 = (
            Get-FileHash $symbolDumpPath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        symbolDumpSize = (Get-Item $symbolDumpPath).Length
        commands = [ordered]@{
            compile = $compileCommands
            link = [ordered]@{
                executable = 'xcrun'
                arguments = $metallibArguments
            }
            inspect = [ordered]@{
                executable = 'xcrun'
                arguments = @(
                    'metal-objdump',
                    '--syms',
                    $libraryRelative)
            }
            validateSymbols = $symbolValidationCommands
        }
    }
}
$payloadJson = $payloadManifest | ConvertTo-Json -Depth 10
$repositoryVariants = @(
    $repoRoot,
    $repoRoot.Replace('\', '/')
)
if ($repositoryVariants | Where-Object { $payloadJson.Contains($_) })
{
    throw 'Metallib manifest contains an absolute repository path.'
}
[System.IO.File]::WriteAllText(
    (Join-Path $OutputRoot 'metallib-manifest.json'),
    "$payloadJson`n",
    [System.Text.UTF8Encoding]::new($false))
& python (Join-Path $PSScriptRoot 'scripts/metal_sidecar.py') `
    --sidecar (Join-Path $OutputRoot 'metallib-manifest.json') `
    --manifest $manifestPath `
    --lock $lockPath `
    --repository-root $repoRoot `
    --library $stagedLibraryPath `
    --verify-files `
    --verify-air
if ($LASTEXITCODE -ne 0)
{
    throw 'The generated Metal sidecar failed central validation.'
}
Copy-Item `
    (Join-Path $OutputRoot 'metallib-manifest.json') `
    $stagedManifestPath `
    -Force
foreach ($airPath in $airPaths)
{
    Remove-Item $airPath -Force
}

Write-Host (
    'Validated and staged one combined mesh.metallib with ' +
    "$($libraryPrograms.Count) exact entry points.")
