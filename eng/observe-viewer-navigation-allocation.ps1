#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The original allocation observation is Windows-only.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assembly = Join-Path $repo 'tests\OpenUsd.Viewer.Tests\bin\Release\net10.0\OpenUsd.Viewer.Tests.dll'
$report = Join-Path (Split-Path $assembly) 'TestResults\OpenUsd.Viewer.Tests-windows-net10.0.tunit-report.json'
$original = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$failure = @($original.groups | ForEach-Object tests | Where-Object {
    $_.methodName -ceq 'StormNavigationTrackingAllocatesNothingAfterWarmup' -and $_.status -ceq 'failed'
})
if ($failure.Count -ne 1)
{
    Write-Host '[DEBUG-viewer-allocation] no original navigation allocation failure; no diagnostic execution.'
    return
}
$root = Join-Path $repo 'artifacts\viewer-navigation-allocation'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Copy-Item -LiteralPath $report -Destination (Join-Path $root 'original-failed.tunit-report.json')
$toolRoot = Join-Path $env:RUNNER_TEMP "openusd-owned-trace-$($env:GITHUB_RUN_ID)"
$tool = Join-Path $toolRoot 'dotnet-trace.exe'
if (-not (Get-Command dotnet-trace -CommandType Application -ErrorAction SilentlyContinue))
{
    Write-Host '[DEBUG-viewer-allocation] concrete selected tool unavailable; restoring only owned local official tool.'
    & dotnet tool install dotnet-trace --version 10.0.750501 --tool-path $toolRoot --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Owned official diagnostic tool restore failed; original failure retained.' }
}
else
{
    $tool = (Get-Command dotnet-trace -CommandType Application -ErrorAction Stop).Source
}
$hostPath = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$source = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Actual failed source identity is unavailable.' }
$started = [DateTimeOffset]::UtcNow
$trace = Join-Path $root 'original-viewer-context.nettrace'
& $tool collect --profile gc-verbose --buffersize 32 --duration 00:10:00 --show-child-io --output $trace `
    -- $hostPath $assembly --minimum-expected-tests 1 --no-ansi --progress off `
    *> (Join-Path $root 'owned-diagnostic-process.log')
$diagnosticExit = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $trace)) { throw 'Actual diagnostic trace missing; original failure retained.' }
[ordered]@{
    diagnosticOnly = $true
    originalGateVerdict = 'FAILED; never replaced by profiled replay'
    source = $source
    startedAtUtc = $started.ToString('O')
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    originalMethod = $failure[0].methodName
    assemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    originalReportSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'original-failed.tunit-report.json')).Hash
    toolSha256 = (Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash
    host = $hostPath
    runtimes = @(& $hostPath --list-runtimes)
    toolVersion = @(& $tool --version)
    diagnosticExit = $diagnosticExit
    traceSha256 = (Get-FileHash -LiteralPath $trace -Algorithm SHA256).Hash
    limitations = 'GC allocation ticks are sampled; a passing replay does not prove the original 8136-byte cause.'
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'identity.json')
Write-Host "[DEBUG-viewer-allocation] owned diagnostic exit=$diagnosticExit; original gate remains FAILED."
