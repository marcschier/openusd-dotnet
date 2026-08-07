#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [string]$OutputRoot = 'artifacts/viewer-distribution-smoke',
    [ValidateRange(10, 600)]
    [int]$SmokeSeconds = 120,
    [string]$ExpectedStatusPattern = 'frame rendered'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bundle = (Resolve-Path $BundlePath).Path
if (-not (Test-Path $bundle))
{
    throw "Viewer bundle not found: $bundle"
}

$checksumPath = "$bundle.sha256"
if (-not (Test-Path $checksumPath))
{
    throw "Viewer bundle checksum not found: $checksumPath"
}
$checksumLine = (Get-Content $checksumPath -Raw).Trim()
$parts = $checksumLine -split '\s+', 2
if ($parts.Count -ne 2)
{
    throw "Invalid checksum file: $checksumPath"
}
$expected = $parts[0].ToUpperInvariant()
$actual = (Get-FileHash $bundle -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -cne $expected)
{
    throw "Checksum verification failed for $bundle. Expected $expected; actual $actual."
}

$outputRoot = if ([IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot
}
else
{
    Join-Path $repoRoot $OutputRoot
}
$installRoot = Join-Path $outputRoot $Rid
Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
if ($bundle.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
{
    Expand-Archive -Path $bundle -DestinationPath $installRoot -Force
}
else
{
    & tar -xzf $bundle -C $installRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "tar failed while extracting $bundle."
    }
}

foreach ($requiredPath in @(
    (Join-Path $installRoot 'plugin/usd/plugInfo.json'),
    (Join-Path $installRoot 'plugin/usd/hdStorm/resources/plugInfo.json')))
{
    if (-not (Test-Path $requiredPath))
    {
        throw "The installed Viewer bundle is missing required native asset: $requiredPath"
    }
}

$stage = [IO.Path]::GetFullPath($StagePath)
if (-not (Test-Path $stage))
{
    throw "Stage file not found: $stage"
}
$stagedStage = Join-Path $installRoot ([IO.Path]::GetFileName($stage))
Copy-Item $stage $stagedStage -Force

$executable = Join-Path $installRoot 'OpenUsd.Viewer.App'
if ($IsWindows)
{
    $executable += '.exe'
}
if (-not (Test-Path $executable))
{
    throw "Viewer executable not found: $executable"
}

$statusFile = Join-Path $installRoot 'viewer-status.txt'
$logFile = Join-Path $installRoot 'viewer.log'
$stdoutFile = Join-Path $installRoot 'viewer.stdout.log'
$stderrFile = Join-Path $installRoot 'viewer.stderr.log'
$crashReportRoot = Join-Path $installRoot 'viewer-crash-reports'
$dumpPattern = Join-Path $installRoot 'viewer-crash-%p.dmp'
Remove-Item $statusFile, $logFile, $stdoutFile, $stderrFile `
    -Force `
    -ErrorAction SilentlyContinue
Remove-Item $crashReportRoot -Recurse -Force -ErrorAction SilentlyContinue

function Write-ViewerDiagnostics
{
    foreach ($diagnostic in @(
        @{ Name = 'status'; Path = $statusFile },
        @{ Name = 'Avalonia trace'; Path = $logFile },
        @{ Name = 'stdout'; Path = $stdoutFile },
        @{ Name = 'stderr'; Path = $stderrFile }))
    {
        Write-Host "----- viewer $($diagnostic.Name) -----"
        if (Test-Path $diagnostic.Path)
        {
            Get-Content $diagnostic.Path | Write-Host
        }
        else
        {
            Write-Host '(not produced)'
        }
    }

    Write-Host '----- viewer crash dumps -----'
    $dumps = @(Get-ChildItem $installRoot -Filter 'viewer-crash-*.dmp' -ErrorAction SilentlyContinue)
    if ($dumps.Count -eq 0)
    {
        Write-Host '(not produced)'
    }
    foreach ($dump in $dumps)
    {
        Write-Host "$($dump.FullName) ($($dump.Length) bytes)"
    }

    Write-Host '----- viewer macOS crash reports -----'
    $reports = @(Get-ChildItem $crashReportRoot -File -ErrorAction SilentlyContinue)
    if ($reports.Count -eq 0)
    {
        Write-Host '(not produced)'
    }
    foreach ($report in $reports)
    {
        Write-Host "----- $($report.Name) -----"
        Get-Content $report.FullName -TotalCount 200 | Write-Host
    }
}

function Copy-MacOSCrashReports
{
    param([DateTime]$SinceUtc)

    if (-not $IsMacOS)
    {
        return
    }

    $diagnosticRoot = Join-Path $HOME 'Library/Logs/DiagnosticReports'
    if (-not (Test-Path $diagnosticRoot))
    {
        return
    }

    $reports = @(
        Get-ChildItem $diagnosticRoot -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -match '^OpenUsd\.Viewer(\.App)?_' -and
                $_.LastWriteTimeUtc -ge $SinceUtc.AddSeconds(-10)
            } |
            Sort-Object LastWriteTimeUtc -Descending
    )
    if ($reports.Count -eq 0)
    {
        return
    }

    New-Item -ItemType Directory -Force -Path $crashReportRoot | Out-Null
    foreach ($report in $reports)
    {
        Copy-Item $report.FullName (Join-Path $crashReportRoot $report.Name) -Force
    }
}

$oldPath = $env:PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldStagePath = $env:OPENUSD_STAGE_PATH
$oldStatusFile = $env:OPENUSD_STATUS_FILE
$oldLogFile = $env:OPENUSD_LOG_FILE
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldDotnetDump = $env:DOTNET_DbgEnableMiniDump
$oldDotnetDumpType = $env:DOTNET_DbgMiniDumpType
$oldDotnetDumpName = $env:DOTNET_DbgMiniDumpName
$oldComPlusDump = $env:COMPlus_DbgEnableMiniDump
$oldComPlusDumpType = $env:COMPlus_DbgMiniDumpType
$oldComPlusDumpName = $env:COMPlus_DbgMiniDumpName
$process = $null
$processStartUtc = [DateTime]::UtcNow
try
{
    $env:PATH = $installRoot + [IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $installRoot + [IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DOTNET_DbgEnableMiniDump = '1'
    $env:DOTNET_DbgMiniDumpType = '4'
    $env:DOTNET_DbgMiniDumpName = $dumpPattern
    $env:COMPlus_DbgEnableMiniDump = '1'
    $env:COMPlus_DbgMiniDumpType = '4'
    $env:COMPlus_DbgMiniDumpName = $dumpPattern

    # Deliberately NOT set on macOS. DYLD_LIBRARY_PATH overrides dylib lookup by
    # leaf name for the whole process, not just for this bundle, and macOS ships
    # its own private OpenUSD inside ModelIO at /usr/lib/usd/libusd_ms.dylib.
    # Pointing it at a directory holding our libusd_ms.dylib made dyld hand ours
    # to ModelIO, which then found its symbols missing, poisoned them with
    # 0xBAD4007 and took the process down with SIGSEGV before the Viewer drew a
    # frame. A self-contained bundle must not need it: the macOS packaging
    # already builds with CMAKE_INSTALL_NAME_DIR=@rpath and
    # CMAKE_INSTALL_RPATH=@loader_path, so the bundle resolves its own
    # dependencies relative to itself. If it ever cannot, that is a packaging
    # defect to fix rather than to paper over with a process-wide override --
    # any consumer that set the same variable would hit the same crash.
    if (-not $IsMacOS)
    {
        $env:DYLD_LIBRARY_PATH = $installRoot + [IO.Path]::PathSeparator + $oldDyldLibraryPath
    }
    $env:OPENUSD_PLUGIN_PATH = Join-Path $installRoot 'plugin/usd'
    $env:OPENUSD_STAGE_PATH = $stagedStage
    $env:OPENUSD_STATUS_FILE = $statusFile
    $env:OPENUSD_LOG_FILE = $logFile

    $arguments = if ($IsWindows) { @('--windows-rendering=angle') } else { @() }
    $processStartUtc = [DateTime]::UtcNow
    $process = Start-Process $executable -PassThru `
        -ArgumentList $arguments `
        -RedirectStandardOutput $stdoutFile `
        -RedirectStandardError $stderrFile
    $deadline = [DateTime]::UtcNow.AddSeconds($SmokeSeconds)
    $renderedStatus = $null
    while ([DateTime]::UtcNow -lt $deadline)
    {
        $process.Refresh()
        if (Test-Path $statusFile)
        {
            $statuses = @(Get-Content $statusFile)
            $renderedStatus = $statuses |
                Where-Object { $_ -match $ExpectedStatusPattern } |
                Select-Object -Last 1
            if ($null -ne $renderedStatus)
            {
                break
            }
            $failure = $statuses |
                Where-Object {
                    $_ -match '^(Renderer initialization failed|Renderer frame failed|' +
                        'Viewer diagnostic sequence failed|Renderer failed|' +
                        'Renderer unavailable|Renderer lost)'
                } |
                Select-Object -Last 1
            if ($null -ne $failure)
            {
                throw "Viewer reported a renderer failure: $failure"
            }
        }
        if ($process.HasExited)
        {
            throw "Viewer exited before rendering a frame with code $($process.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $renderedStatus)
    {
        throw "Viewer did not report pattern '$ExpectedStatusPattern' within $SmokeSeconds seconds."
    }
    if ($process.HasExited)
    {
        throw "Viewer exited after reporting a frame with code $($process.ExitCode)."
    }
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    $process.WaitForExit()
    Write-Output "VIEWER_BUNDLE_SMOKE_RENDERED rid=$Rid status=$renderedStatus"
}
catch
{
    Copy-MacOSCrashReports -SinceUtc $processStartUtc
    Write-ViewerDiagnostics
    throw
}
finally
{
    if ($process -is [Diagnostics.Process] -and -not $process.HasExited)
    {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }
    $env:PATH = $oldPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_STAGE_PATH = $oldStagePath
    $env:OPENUSD_STATUS_FILE = $oldStatusFile
    $env:OPENUSD_LOG_FILE = $oldLogFile
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:DOTNET_DbgEnableMiniDump = $oldDotnetDump
    $env:DOTNET_DbgMiniDumpType = $oldDotnetDumpType
    $env:DOTNET_DbgMiniDumpName = $oldDotnetDumpName
    $env:COMPlus_DbgEnableMiniDump = $oldComPlusDump
    $env:COMPlus_DbgMiniDumpType = $oldComPlusDumpType
    $env:COMPlus_DbgMiniDumpName = $oldComPlusDumpName
}
