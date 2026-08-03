#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$CoverageFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$coveredLines = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$validLines = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($file in $CoverageFile)
{
    if (-not (Test-Path -LiteralPath $file -PathType Leaf))
    {
        throw "Coverage file not found: $file"
    }

    [xml]$report = Get-Content -LiteralPath $file
    foreach ($class in $report.coverage.packages.package.classes.class)
    {
        $fileName = [string]$class.filename
        foreach ($line in $class.lines.line)
        {
            $key = $fileName + ':' + [string]$line.number
            [void]$validLines.Add($key)
            if ([int]$line.hits -gt 0)
            {
                [void]$coveredLines.Add($key)
            }
        }
    }
}

if ($validLines.Count -eq 0)
{
    throw 'Coverage reports did not contain any instrumented lines.'
}

$lineRate = $coveredLines.Count / $validLines.Count
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$directory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($directory))
{
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$writer = [System.Xml.XmlWriter]::Create($OutputPath, $settings)
try
{
    $writer.WriteStartDocument()
    $writer.WriteStartElement('coverage')
    $writer.WriteAttributeString('line-rate', $lineRate.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
    $writer.WriteAttributeString('lines-covered', $coveredLines.Count.ToString([Globalization.CultureInfo]::InvariantCulture))
    $writer.WriteAttributeString('lines-valid', $validLines.Count.ToString([Globalization.CultureInfo]::InvariantCulture))
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally
{
    $writer.Dispose()
}

Write-Host ("Merged line coverage: {0:P2} ({1}/{2})" -f $lineRate, $coveredLines.Count, $validLines.Count)
