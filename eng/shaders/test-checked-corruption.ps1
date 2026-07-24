#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Complete-ExpectedCorruptionRejection
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('linux-x64', 'osx-arm64')]
        [string]$TargetRid,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord]$Failure
    )

    $expectedMessage = if ($TargetRid -eq 'linux-x64')
    {
        'spirv-val rejected checked payload *.spv.'
    }
    else
    {
        'Metal rejected checked payload *.metal.'
    }
    if ($Failure.Exception.Message -notlike $expectedMessage)
    {
        throw $Failure
    }

    # The GitHub Actions pwsh wrapper exits with the global native exit code.
    $global:LASTEXITCODE = 0
    Write-Host "Expected corruption rejection: $($Failure.Exception.Message)"
}

function Test-CorruptionRejectionExitContract
{
    $global:LASTEXITCODE = 17
    try
    {
        throw 'spirv-val rejected checked payload mesh.vertex.spv.'
    }
    catch
    {
        Complete-ExpectedCorruptionRejection `
            -TargetRid linux-x64 `
            -Failure $_
    }
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Expected native corruption rejection was not normalized.'
    }

    $global:LASTEXITCODE = 23
    $unexpectedFailurePropagated = $false
    try
    {
        try
        {
            throw 'Unexpected validator setup failure.'
        }
        catch
        {
            Complete-ExpectedCorruptionRejection `
                -TargetRid linux-x64 `
                -Failure $_
        }
    }
    catch
    {
        $unexpectedFailurePropagated =
            $_.Exception.Message -eq 'Unexpected validator setup failure.'
    }
    if (-not $unexpectedFailurePropagated)
    {
        throw 'Unexpected validator failure was hidden.'
    }
    if ($LASTEXITCODE -ne 23)
    {
        throw 'Unexpected validator failure exit code was normalized.'
    }

    $global:LASTEXITCODE = 0
    Write-Output 'Checked corruption exit contract passed.'
}

if ($SelfTest)
{
    Test-CorruptionRejectionExitContract
    return
}

$testRoot = Join-Path $PSScriptRoot ".cache/corruption/$Rid"
Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'checked/*') $testRoot -Recurse

try
{
    if ($Rid -eq 'linux-x64')
    {
        $path = Get-ChildItem $testRoot -Filter '*.spv' | Select-Object -First 1
        $bytes = [System.IO.File]::ReadAllBytes($path.FullName)
        if ($bytes.Length -lt 24)
        {
            throw "SPIR-V corruption fixture is too small: $($path.FullName)"
        }
        $bytes[20] = 0xff
        $bytes[21] = 0xff
        $bytes[22] = 0xff
        $bytes[23] = 0xff
        [System.IO.File]::WriteAllBytes($path.FullName, $bytes)
    }
    else
    {
        $path = Get-Item (Join-Path $testRoot 'mesh.vertex.metal')
        $content = Get-Content $path.FullName -Raw
        if (-not $content.Contains('#include'))
        {
            throw "MSL corruption fixture has no include: $($path.FullName)"
        }
        $content = $content.Replace('#include', '#error  ')
        [System.IO.File]::WriteAllText(
            $path.FullName,
            $content,
            [System.Text.UTF8Encoding]::new($false))
    }

    $rejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'validate-checked-payload.ps1') `
            -Rid $Rid `
            -CheckedRoot $testRoot `
            -ToolRoot $ToolRoot `
            -OutputRoot (Join-Path $testRoot 'output') `
            -SkipManifestHashes
    }
    catch
    {
        Complete-ExpectedCorruptionRejection `
            -TargetRid $Rid `
            -Failure $_
        $rejected = $true
    }
    if (-not $rejected)
    {
        throw "The $Rid checked payload validator accepted corruption."
    }
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Verified $Rid checked payload corruption rejection."
