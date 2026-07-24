#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installDir = Join-Path $repoRoot '.dotnet'
$version = '10.0.301'
$installScript = Join-Path $env:TEMP "dotnet-install-$([guid]::NewGuid()).ps1"
$errorMessage = (
    'Required .NET SDK 10.0.301 not found. Run ./eng/install-dotnet.ps1 ' +
    'or ./eng/install-dotnet.sh.')

Push-Location $repoRoot
try
{
    Invoke-WebRequest `
        -Uri 'https://dot.net/v1/dotnet-install.ps1' `
        -OutFile $installScript
    & $installScript -Version $version -InstallDir $installDir
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $installedVersion = & (Join-Path $installDir 'dotnet.exe') --version
    if ($installedVersion -cne $version)
    {
        throw "Installed SDK '$installedVersion' does not match '$version'."
    }

    Copy-Item -LiteralPath 'global.json' -Destination 'global.json.bak' -Force
    $globalJson = Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json
    $updates = [ordered]@{
        version = $version
        rollForward = 'disable'
        allowPrerelease = $false
        paths = @('.dotnet', '$host$')
        errorMessage = $errorMessage
    }
    foreach ($entry in $updates.GetEnumerator())
    {
        $property = $globalJson.sdk.PSObject.Properties[$entry.Key]
        if ($property)
        {
            $property.Value = $entry.Value
        }
        else
        {
            $globalJson.sdk | Add-Member `
                -MemberType NoteProperty `
                -Name $entry.Key `
                -Value $entry.Value
        }
    }
    $globalJson |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath 'global.json' -Encoding utf8NoBOM

    Write-Output "Installed repository-local .NET SDK $installedVersion."
}
finally
{
    Pop-Location
    Remove-Item -LiteralPath $installScript -Force -ErrorAction SilentlyContinue
}
