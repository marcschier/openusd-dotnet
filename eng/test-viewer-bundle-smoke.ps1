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

$statusFile = Join-Path $installRoot 'viewer-status.txt'
$logFile = Join-Path $installRoot 'viewer.log'
$stdoutFile = Join-Path $installRoot 'viewer.stdout.log'
$stderrFile = Join-Path $installRoot 'viewer.stderr.log'
$crashReportRoot = Join-Path $installRoot 'viewer-crash-reports'
$dumpPattern = Join-Path $installRoot 'viewer-crash-%p.dmp'
$hangStackFile = Join-Path $installRoot 'viewer-hang-stack.txt'
$nativeStackFile = Join-Path $installRoot 'viewer-native-stack.txt'
$hangDumpFile = Join-Path $installRoot 'viewer-hang.dmp'
$createdumpOutputFile = Join-Path $installRoot 'viewer-createdump.log'
Remove-Item $statusFile, $logFile, $stdoutFile, $stderrFile `
    -Force `
    -ErrorAction SilentlyContinue
Remove-Item $hangStackFile, $nativeStackFile, $hangDumpFile, $createdumpOutputFile `
    -Force `
    -ErrorAction SilentlyContinue
Remove-Item $crashReportRoot -Recurse -Force -ErrorAction SilentlyContinue

# Job 93147442138 sat past twenty minutes in a step whose only intended bound
# was a 120-second render wait; keep live hang capture, then fail far below the
# 120-minute job timeout.
$overallSmokeTimeoutSeconds = $SmokeSeconds + 180
$overallDeadlineUtc = [DateTime]::UtcNow.AddSeconds($overallSmokeTimeoutSeconds)
$processExitTimeoutSeconds = 5

function Get-RemainingWaitMilliseconds
{
    param(
        [string]$WaitName,
        [int]$RequestedSeconds
    )

    $remainingMilliseconds = [int][Math]::Floor(
        ($overallDeadlineUtc - [DateTime]::UtcNow).TotalMilliseconds)
    if ($remainingMilliseconds -le 0)
    {
        throw "Viewer bundle smoke overall ceiling expired before wait '$WaitName'. " +
            "The ceiling is $overallSmokeTimeoutSeconds seconds."
    }

    return [Math]::Max(1, [Math]::Min($RequestedSeconds * 1000, $remainingMilliseconds))
}

function Wait-ProcessExitBounded
{
    param(
        [Diagnostics.Process]$Process,
        [string]$WaitName,
        [int]$TimeoutSeconds
    )

    $timeoutMilliseconds = Get-RemainingWaitMilliseconds `
        -WaitName $WaitName `
        -RequestedSeconds $TimeoutSeconds
    if ($Process.WaitForExit($timeoutMilliseconds))
    {
        return $true
    }

    Write-Warning "Wait '$WaitName' expired after $timeoutMilliseconds ms for PID $($Process.Id)."
    return $false
}

function Stop-ProcessBounded
{
    param(
        [Diagnostics.Process]$Process,
        [string]$Reason
    )

    $Process.Refresh()
    if ($Process.HasExited)
    {
        return $true
    }

    Write-Host "Stopping PID $($Process.Id) because $Reason."
    Stop-Process -Id $Process.Id -ErrorAction SilentlyContinue
    if (Wait-ProcessExitBounded `
            -Process $Process `
            -WaitName "$Reason graceful exit after Stop-Process" `
            -TimeoutSeconds $processExitTimeoutSeconds)
    {
        return $true
    }

    $Process.Refresh()
    if ($Process.HasExited)
    {
        return $true
    }

    if ($IsWindows)
    {
        Write-Warning "PID $($Process.Id) ignored Stop-Process for '$Reason'; escalating with Stop-Process -Force."
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        $killWaitName = "$Reason forced exit after Stop-Process -Force"
    }
    else
    {
        Write-Warning "PID $($Process.Id) ignored Stop-Process for '$Reason'; escalating with SIGKILL."
        & kill -KILL $Process.Id 2>$null
        $killWaitName = "$Reason forced exit after SIGKILL"
    }

    if (Wait-ProcessExitBounded `
            -Process $Process `
            -WaitName $killWaitName `
            -TimeoutSeconds $processExitTimeoutSeconds)
    {
        return $true
    }

    Write-Warning "PID $($Process.Id) still did not exit after '$killWaitName'; continuing with diagnostics."
    return $false
}

function Invoke-ProcessWithBoundedWait
{
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WaitName,
        [int]$TimeoutSeconds,
        [string]$StandardOutputPath,
        [string]$StandardErrorPath
    )

    $startProcessArguments = @{
        FilePath = $FilePath
        ArgumentList = $ArgumentList
        PassThru = $true
    }
    if (-not [string]::IsNullOrEmpty($StandardOutputPath))
    {
        $startProcessArguments['RedirectStandardOutput'] = $StandardOutputPath
    }
    if (-not [string]::IsNullOrEmpty($StandardErrorPath))
    {
        $startProcessArguments['RedirectStandardError'] = $StandardErrorPath
    }

    $child = Start-Process @startProcessArguments
    if (Wait-ProcessExitBounded -Process $child -WaitName $WaitName -TimeoutSeconds $TimeoutSeconds)
    {
        return $child.ExitCode
    }

    Stop-ProcessBounded -Process $child -Reason "wait '$WaitName' timed out" | Out-Null
    return $null
}

function ConvertTo-EncodedCommand
{
    param([string]$Command)

    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
}

function Quote-PowerShellLiteral
{
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Invoke-ArchiveExtraction
{
    $waitName = "archive extraction for $bundle"
    $timeoutSeconds = [int][Math]::Ceiling(
        ($overallDeadlineUtc - [DateTime]::UtcNow).TotalSeconds)
    if ($timeoutSeconds -le 0)
    {
        throw "Viewer bundle smoke overall ceiling expired before wait '$waitName'. " +
            "The ceiling is $overallSmokeTimeoutSeconds seconds."
    }

    $extractOutput = Join-Path $installRoot 'viewer-extract.stdout.log'
    $extractError = Join-Path $installRoot 'viewer-extract.stderr.log'
    Remove-Item $extractOutput, $extractError -Force -ErrorAction SilentlyContinue
    if ($bundle.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
    {
        $pwshName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
        $pwsh = Join-Path $PSHOME $pwshName
        $command = @"
`$ErrorActionPreference = 'Stop'
Expand-Archive ``
    -LiteralPath $(Quote-PowerShellLiteral $bundle) ``
    -DestinationPath $(Quote-PowerShellLiteral $installRoot) ``
    -Force
"@
        $exitCode = Invoke-ProcessWithBoundedWait `
            -FilePath $pwsh `
            -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-NonInteractive',
                '-EncodedCommand',
                (ConvertTo-EncodedCommand $command)) `
            -WaitName $waitName `
            -TimeoutSeconds $timeoutSeconds `
            -StandardOutputPath $extractOutput `
            -StandardErrorPath $extractError
    }
    else
    {
        $exitCode = Invoke-ProcessWithBoundedWait `
            -FilePath 'tar' `
            -ArgumentList @('-xzf', $bundle, '-C', $installRoot) `
            -WaitName $waitName `
            -TimeoutSeconds $timeoutSeconds `
            -StandardOutputPath $extractOutput `
            -StandardErrorPath $extractError
    }

    if ($null -eq $exitCode)
    {
        throw "Archive extraction wait '$waitName' exceeded the " +
            "$overallSmokeTimeoutSeconds-second smoke ceiling."
    }
    if ($exitCode -ne 0)
    {
        throw "Archive extraction wait '$waitName' failed with exit code $exitCode."
    }
}

Invoke-ArchiveExtraction

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

    Write-Host '----- viewer hang stack -----'
    if (Test-Path $hangStackFile)
    {
        Get-Content $hangStackFile | Write-Host
    }
    else
    {
        Write-Host '(not produced)'
    }

    Write-Host '----- viewer native stack -----'
    if (Test-Path $nativeStackFile)
    {
        Get-Content $nativeStackFile -TotalCount 250 | Write-Host
        $nativeStackLineCount = (Get-Content $nativeStackFile | Measure-Object -Line).Lines
        if ($nativeStackLineCount -gt 250)
        {
            Write-Host "----- viewer native stack truncated in log; full $nativeStackLineCount lines in artifact -----"
        }
    }
    else
    {
        Write-Host '(not produced)'
    }

    Write-Host '----- viewer createdump output -----'
    if (Test-Path $createdumpOutputFile)
    {
        Get-Content $createdumpOutputFile | Write-Host
    }
    else
    {
        Write-Host '(not produced)'
    }

    Write-Host '----- viewer crash dumps -----'
    $dumps = @(
        Get-ChildItem $installRoot -Filter 'viewer-crash-*.dmp' -ErrorAction SilentlyContinue
        Get-ChildItem $installRoot -Filter 'viewer-hang*.dmp' -ErrorAction SilentlyContinue
    )
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

function Invoke-DiagnosticProcess
{
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$OutputPath,
        [int]$TimeoutSeconds = 30
    )

    $errorPath = "$OutputPath.err"
    Remove-Item $OutputPath, $errorPath -Force -ErrorAction SilentlyContinue
    $exitCode = Invoke-ProcessWithBoundedWait `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WaitName "diagnostic tool '$FilePath'" `
        -TimeoutSeconds $TimeoutSeconds `
        -StandardOutputPath $OutputPath `
        -StandardErrorPath $errorPath
    if ($null -eq $exitCode)
    {
        Add-Content $OutputPath "Diagnostic tool '$FilePath' timed out after $TimeoutSeconds seconds."
    }
    if (Test-Path $errorPath)
    {
        Add-Content $OutputPath '----- stderr -----'
        Get-Content $errorPath | Add-Content $OutputPath
        Remove-Item $errorPath -Force -ErrorAction SilentlyContinue
    }
}

function Capture-HangDiagnostics
{
    param([Diagnostics.Process]$ViewerProcess)

    $ViewerProcess.Refresh()
    if ($ViewerProcess.HasExited)
    {
        return
    }

    Set-Content $hangStackFile "Viewer PID $($ViewerProcess.Id) was still running at timeout."
    $stack = Get-Command dotnet-stack -ErrorAction SilentlyContinue
    if ($null -ne $stack)
    {
        try
        {
            Invoke-DiagnosticProcess `
                -FilePath $stack.Source `
                -ArgumentList @('report', '-p', [string]$ViewerProcess.Id) `
                -OutputPath $hangStackFile
        }
        catch
        {
            Add-Content $hangStackFile "dotnet-stack failed: $($_.Exception.Message)"
        }
    }
    else
    {
        Add-Content $hangStackFile 'dotnet-stack was not available on PATH.'
    }

    Set-Content $nativeStackFile "Viewer PID $($ViewerProcess.Id) was still running at timeout."
    $gdb = Get-Command gdb -ErrorAction SilentlyContinue
    if ($null -ne $gdb)
    {
        try
        {
            Invoke-DiagnosticProcess `
                -FilePath $gdb.Source `
                -ArgumentList @(
                    '-q',
                    '-batch',
                    '-ex=set pagination off',
                    '-ex=set confirm off',
                    '-ex=set debuginfod enabled off',
                    '-ex=info threads',
                    '-ex=thread apply all bt full',
                    '-ex=detach',
                    '-p',
                    [string]$ViewerProcess.Id) `
                -OutputPath $nativeStackFile
        }
        catch
        {
            Add-Content $nativeStackFile "gdb failed: $($_.Exception.Message)"
        }
    }
    else
    {
        Add-Content $nativeStackFile 'gdb was not available on PATH.'
    }

    $createdumpName = if ($IsWindows) { 'createdump.exe' } else { 'createdump' }
    $createdump = Join-Path $installRoot $createdumpName
    if (Test-Path $createdump)
    {
        Invoke-DiagnosticProcess `
            -FilePath $createdump `
            -ArgumentList @('--full', '--name', $hangDumpFile, [string]$ViewerProcess.Id) `
            -OutputPath $createdumpOutputFile
    }
    else
    {
        Set-Content $createdumpOutputFile "createdump was not found at $createdump."
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
    $hitOverallCeiling = $false
    while ($true)
    {
        $now = [DateTime]::UtcNow
        if ($now -ge $overallDeadlineUtc)
        {
            $hitOverallCeiling = $true
            break
        }
        if ($now -ge $deadline)
        {
            break
        }

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
        Capture-HangDiagnostics -ViewerProcess $process
        if ($hitOverallCeiling)
        {
            throw "Viewer bundle smoke overall ceiling expired while waiting for " +
                "pattern '$ExpectedStatusPattern'. The ceiling is " +
                "$overallSmokeTimeoutSeconds seconds."
        }
        throw "Viewer did not report pattern '$ExpectedStatusPattern' within $SmokeSeconds seconds."
    }
    if ($process.HasExited)
    {
        throw "Viewer exited after reporting a frame with code $($process.ExitCode)."
    }
    if (-not (Stop-ProcessBounded -Process $process -Reason "viewer rendered status was observed"))
    {
        throw "Viewer rendered a frame but did not exit after the bounded shutdown waits."
    }
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
        Stop-ProcessBounded -Process $process -Reason "viewer smoke cleanup" | Out-Null
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
