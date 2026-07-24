#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [int]$MaxLength = 120,
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'
$folders = @('src', 'tests', 'samples', 'benchmarks') |
    ForEach-Object { Join-Path $Root $_ } |
    Where-Object { Test-Path $_ }
$violations = New-Object System.Collections.Generic.List[string]

foreach ($folder in $folders)
{
    $files = Get-ChildItem -Path $folder -Recurse -File -Filter '*.cs' |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            $_.Name -notmatch '\.(g|g\.i|Designer)\.cs$'
        }

    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName))
        {
            $lineNumber++
            if ($line.Length -gt $MaxLength)
            {
                $relative = $file.FullName.Substring($Root.Path.Length).TrimStart('\', '/')
                $violations.Add(("{0}:{1}: {2} chars (max {3})" -f
                    $relative, $lineNumber, $line.Length, $MaxLength))
            }
        }
    }
}

if ($violations.Count -gt 0)
{
    Write-Host "Line-length violations (max $MaxLength):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Line-length check passed (max $MaxLength)." -ForegroundColor Green
