#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\native\build\shim\win-x64'),
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot '..\artifacts\windows-native-tests\mesa'),
    [string]$Configuration = 'Release',
    [string]$TestFilter
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'This runner requires Windows.'
}
$build = (Resolve-Path -LiteralPath $BuildRoot).Path
$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$ctest = (Get-Command ctest -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$testJson = & $ctest --test-dir $build -C $Configuration --show-only=json-v1
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not discover the native CTest executables.'
}
$tests = ($testJson -join "`n" | ConvertFrom-Json).tests
$stormTests = @($tests | Where-Object { $_.name -like 'openusd_storm_*' -and $_.command.Count -gt 0 })
foreach ($required in @('openusd_storm_child_aov_probe', 'openusd_storm_child_aov_resize_probe'))
{
    if ($required -notin $stormTests.name)
    {
        throw "The build tree is missing required Storm AOV test '$required'."
    }
}
$directories = @($stormTests | ForEach-Object {
    $executable = [IO.Path]::GetFullPath($_.command[0])
    $relative = [IO.Path]::GetRelativePath($build, $executable)
    if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..') -or
        -not (Test-Path -LiteralPath $executable -PathType Leaf))
    {
        throw "Storm probe is missing or outside the selected build tree: $executable"
    }
    Split-Path -Parent $executable
} | Sort-Object -Unique)

$names = @('PATH', 'OPENUSD_MESA_WGL_OPENGL32_PATH', 'OPENUSD_MESA_WGL_OPENGL32_SHA256',
    'OPENUSD_MESA_WGL_ARCHIVE_URL', 'OPENUSD_MESA_WGL_ARCHIVE_SHA256',
    'GALLIUM_DRIVER', 'LIBGL_ALWAYS_SOFTWARE', 'LP_NUM_THREADS', 'MESA_SHADER_CACHE_DISABLE')
$previous = @{}
foreach ($name in $names)
{
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$created = [Collections.Generic.List[string]]::new()
$created.Capacity = $directories.Count
$expected = $null
$exitCode = 1

function Write-AdmissionEvidence
{
    param([string]$Root, [string]$Log, [object]$SelectedTest, [object[]]$Observations, [int]$CTestExit,
        [datetime]$ExecutionStarted)

    try
    {
        $lastTest = Join-Path $build 'Testing\Temporary\LastTest.log'
        if (-not (Test-Path -LiteralPath $lastTest -PathType Leaf) -or
            (Get-Item -LiteralPath $lastTest).Length -gt 2MB -or
            (Get-Item -LiteralPath $lastTest).LastWriteTimeUtc -lt $ExecutionStarted)
        {
            throw 'The bounded CTest failure log is missing or exceeds its limit.'
        }
        $text = Get-Content -LiteralPath $lastTest -Raw
        $testName = [regex]::Escape('openusd_render_admission_targets-deleted')
        $match = [regex]::Match($text,
            "(?ms)^\d+/\d+ Testing: $testName\r?`n.*?^`"$testName`" time elapsed: [^\r\n]*")
        if (-not $match.Success) { throw 'The exact owned admission test log block is missing.' }
        $block = $match.Value
        if ([Text.Encoding]::UTF8.GetByteCount($block) -gt 64KB)
        {
            throw 'The exact admission log exceeds its bounded text limit.'
        }
        $block | Set-Content -LiteralPath (Join-Path $Root 'targets-deleted-LastTest.log')
        $modules = [Collections.Generic.List[object]]::new()
        $roles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in [regex]::Matches($block,
            '\[DEBUG-openusd-admission\] module-role=(probe|core|sdk) module-path-utf16hex=([0-9a-f]{4,4096})'))
        {
            if ($modules.Count -ge 3) { break }
            $role = $entry.Groups[1].Value
            if (-not $roles.Add($role)) { throw 'The actual module role was reported more than once.' }
            $path = [Text.UnicodeEncoding]::new($false, $false, $true).GetString(
                [Convert]::FromHexString($entry.Groups[2].Value))
            $allowed = if ($role -eq 'sdk')
            {
                [IO.Path]::GetFullPath((Join-Path $repoRoot 'native\install\win-x64'))
            }
            else { $build }
            $full = [IO.Path]::GetFullPath($path)
            $name = [IO.Path]::GetFileName($full)
            $requiredName = switch ($role)
            {
                'probe' { 'openusd_render_admission_probe.exe' }
                'core' { 'openusd_dotnet.dll' }
                'sdk' { 'usd_ms.dll' }
            }
            if (-not $full.StartsWith($allowed + '\', [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals($full, $path, [StringComparison]::OrdinalIgnoreCase) -or
                $name -cne $requiredName -or -not (Test-Path -LiteralPath $full -PathType Leaf))
            {
                throw 'The native module identity is outside its exact expected build/install root.'
            }
            $ancestor = Get-Item -LiteralPath $full -Force
            for ($depth = 0; $depth -lt 32 -and $ancestor -and
                $ancestor.FullName.Length -ge $allowed.Length; $depth++)
            {
                if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)
                {
                    throw 'The native module identity traverses a reparse point.'
                }
                $ancestor = $ancestor.Parent
            }
            $modules.Add(@{
                role = $role
                path = [IO.Path]::GetRelativePath($repoRoot, $full)
                sha256 = (Get-FileHash -LiteralPath $full).Hash
                bytes = (Get-Item -LiteralPath $full).Length
                modulePathObservation = 'actual-process-registration'
                hashObservation = 'after-original-CTest'
            })
        }
        if ($modules.Count -ne 3)
        {
            Write-Host '[DEBUG-openusd-admission] module-pairing=incomplete reason=registration-not-observed'
        }
        [ordered]@{
            ctestExit = $CTestExit
            build = [IO.Path]::GetRelativePath($repoRoot, $build)
            test = $SelectedTest.name
            originalTimeoutSeconds = 120
            sourceSha = (git -C $repoRoot rev-parse HEAD).Trim()
            modules = @($modules.ToArray())
            observations = $Observations
            log = [IO.Path]::GetFileName($Log)
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Root 'targets-deleted-identity.json')
    }
    catch
    {
        Write-Host ("[DEBUG-openusd-admission] evidence=unknown original-ctest-exit=$CTestExit " +
            "error-type=$($_.Exception.GetType().Name)")
    }
}

function Invoke-ObservedCTest
{
    param([string[]]$Arguments)

    $selected = @($tests | Where-Object name -CEQ 'openusd_render_admission_targets-deleted')
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $buildBase = Join-Path $repoRoot 'native\build\shim'
    if ($selected.Count -ne 1 -or $selected[0].command.Count -ne 4 -or
        $selected[0].command[3] -cne 'targets-deleted' -or
        @($selected[0].properties | Where-Object { $_.name -eq 'TIMEOUT' -and $_.value -eq 120 }).Count -ne 1 -or
        -not $build.StartsWith($buildBase + '\', [StringComparison]::OrdinalIgnoreCase))
    {
        Write-Host '[DEBUG-openusd-admission] observer=refused reason=exact-test-profile'
        & $ctest @Arguments | ForEach-Object { Write-Host $_ }
        return $LASTEXITCODE
    }
    $probe = [IO.Path]::GetFullPath($selected[0].command[0])
    if (-not $probe.StartsWith($build + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $probe -PathType Leaf))
    {
        Write-Host '[DEBUG-openusd-admission] observer=refused reason=owned-probe'
        & $ctest @Arguments | ForEach-Object { Write-Host $_ }
        return $LASTEXITCODE
    }
    $root = Join-Path $repoRoot ('artifacts\native-ctest\' + [IO.Path]::GetFileName($build))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $stdoutPath = Join-Path $root 'ctest.stdout.log'
    $stderrPath = Join-Path $root 'ctest.stderr.log'
    $emptyDebuggerScript = Join-Path $root 'empty-debugger-command.txt'
    [IO.File]::WriteAllText($emptyDebuggerScript, '')
    $oldTrace = $env:OPENUSD_ADMISSION_PHASE_TRACE
    $oldBuildTrace = $env:OPENUSD_ADMISSION_TRACE_BUILD_ROOT
    $oldRepoTrace = $env:OPENUSD_ADMISSION_TRACE_REPO_ROOT
    $env:OPENUSD_ADMISSION_PHASE_TRACE = '1'
    $env:OPENUSD_ADMISSION_TRACE_BUILD_ROOT = $build
    $env:OPENUSD_ADMISSION_TRACE_REPO_ROOT = $repoRoot
    $process = $null
    $started = $false
    $observations = [Collections.Generic.List[object]]::new()
    try
    {
        $start = [Diagnostics.ProcessStartInfo]::new($ctest)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $executionStarted = [datetime]::UtcNow
        if (-not $process.Start()) { throw 'Original CTest could not start.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $sampled = $false
        $probeBegan = $null
        $probePid = $null
        $probeCreated = $null
        $debuggerCommand = Get-Command cdb.exe -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        $debugger = if ($debuggerCommand) { $debuggerCommand.Source }
            else { 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' }
        if (-not (Test-Path -LiteralPath $debugger -PathType Leaf)) { $debugger = $null }
        if ($debugger)
        {
            $helpStart = [Diagnostics.ProcessStartInfo]::new($debugger)
            $helpStart.UseShellExecute = $false
            $helpStart.RedirectStandardOutput = $true
            $helpStart.RedirectStandardError = $true
            $helpStart.ArgumentList.Add('-?')
            $helpProcess = [Diagnostics.Process]::new()
            $helpProcess.StartInfo = $helpStart
            $helpStarted = $false
            try
            {
                if (-not $helpProcess.Start()) { throw 'The installed debugger help did not start.' }
                $helpStarted = $true
                $helpOut = $helpProcess.StandardOutput.ReadToEndAsync()
                $helpError = $helpProcess.StandardError.ReadToEndAsync()
                if (-not $helpProcess.WaitForExit(3000))
                {
                    $helpProcess.Kill()
                    $helpProcess.WaitForExit()
                    $debugger = $null
                }
                $help = $helpOut.GetAwaiter().GetResult() + $helpError.GetAwaiter().GetResult()
                if ([Text.Encoding]::UTF8.GetByteCount($help) -gt 64KB -or $help -cnotmatch '(?:^|\s)-pvr(?:\s|$)')
                {
                    $debugger = $null
                }
            }
            catch
            {
                $debugger = $null
                $observations.Add(@{ stack = 'unknown'; reason = 'installed-debugger-help-unavailable' })
            }
            finally
            {
                if ($helpStarted -and -not $helpProcess.HasExited) { $helpProcess.Kill(); $helpProcess.WaitForExit() }
                $helpProcess.Dispose()
            }
        }
        $observations.Add(@{
            nonSuspendingDebuggerAvailable = [bool]$debugger
            absentOrUnsupportedStack = if ($debugger) { 'not-yet-observed' } else { 'unknown' }
        })
        while (-not $process.WaitForExit(1000))
        {
            try
            {
                if ($sampled) { continue }
                $children = @(Get-CimInstance Win32_Process -Filter (
                    "ParentProcessId=$($process.Id) AND Name='openusd_render_admission_probe.exe'") |
                    Where-Object {
                        $_.Name -eq 'openusd_render_admission_probe.exe' -and
                        [string]::Equals($_.ExecutablePath, $probe, [StringComparison]::OrdinalIgnoreCase) -and
                        $_.CommandLine -match '(?:\s|")targets-deleted"?$' })
                if ($children.Count -ne 1) { continue }
                $child = $children[0]
                if ($null -eq $probePid)
                {
                    $probePid = $child.ProcessId
                    $probeCreated = $child.CreationDate
                    $probeBegan = [Diagnostics.Stopwatch]::StartNew()
                    $observations.Add(@{ pid = $probePid; creation = $probeCreated; probe = 'targets-deleted' })
                }
                if ($child.ProcessId -ne $probePid -or $child.CreationDate -ne $probeCreated) { continue }
                if ($probeBegan.Elapsed.TotalSeconds -lt 60) { continue }
                $sampled = $true
                if (([datetime]::UtcNow - $probeCreated.ToUniversalTime()).TotalSeconds -ge 110)
                {
                    $observations.Add(@{ stack = 'unknown'; reason = 'too-close-to-original-deadline' })
                    continue
                }
                if (-not $debugger)
                {
                    $observations.Add(@{ stack = 'unknown'; reason = 'already-installed-cdb-unavailable' })
                    continue
                }
                $live = Get-CimInstance Win32_Process -Filter "ProcessId=$probePid"
                if (-not $live -or $live.CreationDate -ne $probeCreated -or $live.ParentProcessId -ne $process.Id -or
                    -not [string]::Equals($live.ExecutablePath, $probe, [StringComparison]::OrdinalIgnoreCase) -or
                    $live.CommandLine -notmatch '(?:\s|")targets-deleted"?$')
                {
                    $observations.Add(@{ stack = 'unknown'; reason = 'owned-probe-no-longer-current' })
                    continue
                }
                $debugStart = [Diagnostics.ProcessStartInfo]::new($debugger)
                $debugStart.UseShellExecute = $false
                $debugStart.WorkingDirectory = $root
                $debugStart.RedirectStandardOutput = $true
                $debugStart.RedirectStandardError = $true
                foreach ($argument in @('-pvr', '-p', "$probePid", '-noshell', '-y', $build,
                    '-cf', $emptyDebuggerScript, '-c', '~0s;k 32;qd'))
                {
                    $debugStart.ArgumentList.Add($argument)
                }
                $debugProcess = [Diagnostics.Process]::new()
                $debugProcess.StartInfo = $debugStart
                $debugStarted = $false
                try
                {
                    if (-not $debugProcess.Start()) { throw 'The already-installed non-invasive debugger did not start.' }
                    $debugStarted = $true
                    $debugOut = $debugProcess.StandardOutput.ReadToEndAsync()
                    $debugError = $debugProcess.StandardError.ReadToEndAsync()
                    $finished = $debugProcess.WaitForExit(5000)
                    if (-not $finished)
                    {
                        $debugProcess.Kill()
                        $debugProcess.WaitForExit()
                    }
                    $output = $debugOut.GetAwaiter().GetResult() + $debugError.GetAwaiter().GetResult()
                    if ([Text.Encoding]::UTF8.GetByteCount($output) -le 64KB)
                    {
                        $output | Set-Content -LiteralPath (Join-Path $root 'targets-deleted-stack.txt')
                    }
                    $observations.Add(@{ pid = $probePid; nonInvasive = $true; finished = $finished;
                        debuggerExit = $debugProcess.ExitCode; outputWithinBound = ($output.Length -le 65536) })
                }
                finally
                {
                    if ($debugStarted -and -not $debugProcess.HasExited)
                    {
                        $debugProcess.Kill()
                        $debugProcess.WaitForExit()
                    }
                    $debugProcess.Dispose()
                }
            }
            catch
            {
                $sampled = $true
                $observations.Add(@{ stack = 'unknown'; errorType = $_.Exception.GetType().Name })
            }
        }
        $result = $process.ExitCode
        $outText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        foreach ($entry in @(@{ text = $outText; path = $stdoutPath }, @{ text = $errorText; path = $stderrPath }))
        {
            if ([Text.Encoding]::UTF8.GetByteCount($entry.text) -le 1MB)
            {
                $entry.text | Set-Content -LiteralPath $entry.path
                Write-Host $entry.text
            }
            else { Write-Host '[DEBUG-openusd-admission] full-CTest-log-exceeds-bound; see original-CTest-LastTest' }
        }
        Write-AdmissionEvidence -Root $root -Log $stdoutPath -SelectedTest $selected[0] `
            -Observations $observations.ToArray() -CTestExit $result -ExecutionStarted $executionStarted
        Write-Host "[DEBUG-openusd-admission] authoritative-original-ctest-exit=$result"
        return $result
    }
    catch
    {
        if (-not $started) { throw }
        if (-not $process.HasExited) { $process.WaitForExit() }
        $result = $process.ExitCode
        Write-Host ("[DEBUG-openusd-admission] observation=unknown original-ctest-exit=$result " +
            "error-type=$($_.Exception.GetType().Name)")
        return $result
    }
    finally
    {
        $env:OPENUSD_ADMISSION_PHASE_TRACE = $oldTrace
        $env:OPENUSD_ADMISSION_TRACE_BUILD_ROOT = $oldBuildTrace
        $env:OPENUSD_ADMISSION_TRACE_REPO_ROOT = $oldRepoTrace
        if ($process) { $process.Dispose() }
    }
}

try
{
    $stage = & (Join-Path $PSScriptRoot 'prepare-mesa-wgl-test-runtime.ps1') `
        -Root $runtime -Activate -Preflight
    $source = Join-Path $stage 'opengl32.dll'
    $expected = $env:OPENUSD_MESA_WGL_OPENGL32_SHA256
    foreach ($directory in $directories)
    {
        $destination = Join-Path $directory 'opengl32.dll'
        if (Test-Path -LiteralPath $destination)
        {
            if ((Get-FileHash -LiteralPath $destination).Hash -ne $expected)
            {
                throw "Refusing to replace an existing different OpenGL library: $destination"
            }
        }
        else
        {
            [IO.File]::Copy($source, $destination, $false)
            $created.Add($destination)
            if ((Get-FileHash -LiteralPath $destination).Hash -ne $expected)
            {
                throw "Staged OpenGL library hash mismatch: $destination"
            }
        }
        Write-Host "[windows-native-tests] Mesa WGL: $destination sha256=$expected"
    }
    $arguments = @('--test-dir', $build, '-C', $Configuration, '--no-tests=error', '--output-on-failure')
    if ($TestFilter)
    {
        $arguments += @('-R', $TestFilter)
    }
    if ($env:OPENUSD_ADMISSION_CTEST_TRACE -eq '1')
    {
        $exitCode = Invoke-ObservedCTest -Arguments $arguments
    }
    else
    {
        & $ctest @arguments
        $exitCode = $LASTEXITCODE
    }
}
finally
{
    try
    {
        foreach ($path in $created)
        {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $expected)
            {
                throw "The staged OpenGL library changed during execution; it was not deleted: $path"
            }
            Remove-Item -LiteralPath $path -Force
        }
    }
    finally
    {
        foreach ($name in $names)
        {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }
}
exit $exitCode
