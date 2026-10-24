#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vhacd.lock.json') -Raw | ConvertFrom-Json
if (-not $Destination)
{
    $Destination = [IO.Path]::Combine(
        (Split-Path $PSScriptRoot -Parent), 'native', 'src', "V-HACD-$($lock.commit)")
}
$null = New-Item -ItemType Directory -Path $Destination -Force
foreach ($file in $lock.files)
{
    $path = Join-Path $Destination $file.name
    if (Test-Path -LiteralPath $path)
    {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256)
        {
            throw "The existing V-HACD input '$path' does not match the source lock."
        }
        continue
    }
    $pending = "$path.$([Guid]::NewGuid().ToString('N')).download"
    try
    {
        Invoke-WebRequest -Uri $file.url -OutFile $pending
        if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash -ne $file.sha256)
        {
            throw "The downloaded V-HACD input '$($file.name)' does not match the source lock."
        }
        Move-Item -LiteralPath $pending -Destination $path
    }
    finally
    {
        if (Test-Path -LiteralPath $pending)
        {
            Remove-Item -LiteralPath $pending
        }
    }
}
Write-Host "Verified V-HACD $($lock.version) inputs at '$Destination'."
