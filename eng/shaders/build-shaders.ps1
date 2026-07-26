#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [ValidateSet('Full', 'Spirv', 'Metal')]
    [string]$ArtifactScope = 'Full',
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$lockPath = Join-Path $PSScriptRoot 'toolchain.lock.json'
$manifestPath = Join-Path $PSScriptRoot 'shader-manifest.json'
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

if (-not $Rid)
{
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $Rid = if ($IsWindows -and $architecture -eq 'X64')
    {
        'win-x64'
    }
    elseif ($IsLinux -and $architecture -eq 'X64')
    {
        'linux-x64'
    }
    elseif ($IsMacOS -and $architecture -eq 'Arm64')
    {
        'osx-arm64'
    }
    else
    {
        throw "The shader toolchain does not support this host: $architecture."
    }
}

if ($Rid -eq 'linux-x64' -and $ArtifactScope -ne 'Spirv')
{
    throw 'Linux shader generation is restricted to the SPIR-V artifact scope.'
}

# DXIL emission loads the dxcompiler and dxil libraries, which exist only on Windows.
# macOS therefore builds the Metal scope, and Windows remains the authoritative producer
# of the full checked payload.
if ($Rid -eq 'osx-arm64' -and $ArtifactScope -ne 'Metal')
{
    throw 'macOS shader generation is restricted to the Metal artifact scope.'
}

if ($ArtifactScope -eq 'Metal' -and $Rid -ne 'osx-arm64')
{
    throw 'The Metal artifact scope is restricted to osx-arm64.'
}

$executableSuffix = if ($IsWindows) { '.exe' } else { '' }
$slang = Join-Path $ToolRoot "$Rid/slang/bin/slangc$executableSuffix"
if (-not (Test-Path $slang))
{
    throw "Slang was not found at $slang. Run fetch-toolchain.ps1 first."
}

$relativeOutput = [System.IO.Path]::GetRelativePath(
    $repoRoot.Path,
    $OutputRoot).Replace('\', '/')
if ($relativeOutput -eq '..' -or $relativeOutput.StartsWith('../'))
{
    throw 'Shader output must be inside the repository for reproducible commands.'
}

Remove-Item $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue
$rawReflectionRoot = Join-Path $OutputRoot '.raw-reflection'
$planPath = Join-Path $rawReflectionRoot 'command-plan.json'
New-Item -ItemType Directory -Force -Path $rawReflectionRoot | Out-Null
& python (Join-Path $PSScriptRoot 'scripts/shader-commands.py') `
    --lock $lockPath `
    --manifest $manifestPath `
    --output-root $relativeOutput `
    --artifact-scope $ArtifactScope.ToLowerInvariant() `
    --output $planPath
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
$plan = Get-Content $planPath -Raw | ConvertFrom-Json

$actualVersion = (
    & $slang -version 2>&1 |
        Select-Object -First 1
).ToString().Trim()
if ($LASTEXITCODE -ne 0 -or $actualVersion -ne $plan.toolchain.slangVersion)
{
    throw "Expected Slang $($plan.toolchain.slangVersion), got '$actualVersion'."
}

function Invoke-Slang
{
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $slang @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Push-Location $repoRoot
try
{
    foreach ($program in $plan.programs)
    {
        if ($ArtifactScope -eq 'Spirv')
        {
            Invoke-Slang ([string[]]$program.commands.spirv.arguments)
            continue
        }

        if ($ArtifactScope -eq 'Full')
        {
            Invoke-Slang ([string[]]$program.commands.dxil.arguments)
            Invoke-Slang ([string[]]$program.commands.spirv.arguments)
        }

        Invoke-Slang ([string[]]$program.commands.metal.arguments)

        if ($ArtifactScope -eq 'Full')
        {
            & python ([string[]]$program.commands.reflection.arguments)
            if ($LASTEXITCODE -ne 0)
            {
                exit $LASTEXITCODE
            }
        }

        if ($IsMacOS)
        {
            $xcodeVersion = (& xcodebuild -version | Select-Object -First 1).Trim()
            if ($xcodeVersion -ne "Xcode $($plan.toolchain.xcodeVersion)")
            {
                throw "Expected Xcode $($plan.toolchain.xcodeVersion), got '$xcodeVersion'."
            }

            $outputBase = Join-Path $OutputRoot $program.name
            $airPath = "$outputBase.air"
            & xcrun `
                -sdk macosx `
                metal `
                "-std=$($plan.toolchain.metalStandard)" `
                -c "$outputBase.metal" `
                -o $airPath
            if ($LASTEXITCODE -ne 0)
            {
                exit $LASTEXITCODE
            }
            & xcrun -sdk macosx metallib $airPath -o "$outputBase.metallib"
            if ($LASTEXITCODE -ne 0)
            {
                exit $LASTEXITCODE
            }
            Remove-Item $airPath -Force
        }
    }

    Copy-Item $planPath (Join-Path $OutputRoot 'executed-commands.json')
}
finally
{
    Pop-Location
    Remove-Item $rawReflectionRoot -Recurse -Force -ErrorAction SilentlyContinue
}

& (Join-Path $PSScriptRoot 'verify-shaders.ps1') `
    -Rid $Rid `
    -ArtifactScope $ArtifactScope `
    -ToolRoot $ToolRoot `
    -OutputRoot $OutputRoot
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Output $OutputRoot
