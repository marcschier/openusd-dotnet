#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('windows-wgl', 'linux-x11', 'linux-wayland', 'macos-arm64')]
    [string]$Platform,
    [int]$TimeoutSeconds = 120,
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [switch]$SharedStageSoak,
    [ValidateRange(90, 86400)]
    [int]$SoakSeconds = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$diagnosticRoot = Join-Path $repoRoot "artifacts/platform-smoke/$Platform"
$viewerRoot = Join-Path $diagnosticRoot 'viewer'
New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
$ownedProcess = $null
$ownedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$ownedRuntimeRoot = $null
$runDefaultViewer = $true
$oldEnvironment = @{}
foreach ($name in @(
    'DISPLAY',
    'WAYLAND_DISPLAY',
    'XDG_RUNTIME_DIR',
    'XDG_SESSION_TYPE',
    'LIBGL_ALWAYS_SOFTWARE',
    'MESA_GL_VERSION_OVERRIDE',
    'MESA_GLSL_VERSION_OVERRIDE',
    'OPENUSD_RENDERER',
    'OPENUSD_STORM_NATIVE_WAYLAND',
    'OPENUSD_VIEWER_PLATFORM'))
{
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

function Set-EnvironmentVariable
{
    param([string]$Name, [AllowNull()][string]$Value)
    [Environment]::SetEnvironmentVariable($Name, $Value)
}

function Wait-ForX11
{
    param(
        [string]$Display,
        [System.Diagnostics.Process]$Process
    )
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline)
    {
        $Process.Refresh()
        if ($Process.HasExited)
        {
            throw "Xvfb exited with code $($Process.ExitCode)."
        }
        & xdpyinfo -display $Display *> $null
        if ($LASTEXITCODE -eq 0)
        {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Xvfb display $Display did not become ready."
}

function Get-IsolatedDisplayNumber
{
    param([int]$Offset)

    $firstCandidate = 100 + (($PID * 2 + $Offset) % 30000)
    foreach ($candidate in $firstCandidate..($firstCandidate + 99))
    {
        & xdpyinfo -display ":$candidate" *> $null
        $unixDisplayInUse = $LASTEXITCODE -eq 0
        & xdpyinfo -display "localhost:$candidate" *> $null
        $tcpDisplayInUse = $LASTEXITCODE -eq 0
        if (-not $unixDisplayInUse -and -not $tcpDisplayInUse)
        {
            return $candidate
        }
    }
    throw 'Could not reserve an isolated TCP X display number.'
}

function Invoke-ViewerSmoke
{
    param(
        [string]$OutputPath,
        [string]$ExpectedStatusPattern,
        [bool]$EnableSoak = $SharedStageSoak
    )

    $viewerTimeout = if ($EnableSoak)
    {
        [Math]::Max($TimeoutSeconds, $SoakSeconds + 300)
    }
    else
    {
        $TimeoutSeconds
    }
    & (Join-Path $PSScriptRoot 'run-viewer.ps1') `
        -Rid $rid `
        -StagePath $StagePath `
        -SmokeSeconds $viewerTimeout `
        -OutputPath $OutputPath `
        -ExpectedStatusPattern $ExpectedStatusPattern `
        -SharedStageSoak:$EnableSoak `
        -SoakSeconds $SoakSeconds
    if ($LASTEXITCODE -ne 0)
    {
        throw "Viewer smoke failed with exit code $LASTEXITCODE."
    }
}

try
{
    Set-EnvironmentVariable 'OPENUSD_RENDERER' 'Storm'
    Set-EnvironmentVariable 'OPENUSD_STORM_NATIVE_WAYLAND' '0'
    switch ($Platform)
    {
        'windows-wgl'
        {
            if (-not $IsWindows)
            {
                throw 'windows-wgl must run on Windows.'
            }
            $rid = 'win-x64'
            Set-EnvironmentVariable 'DISPLAY' $null
            Set-EnvironmentVariable 'WAYLAND_DISPLAY' $null
            Set-EnvironmentVariable 'XDG_RUNTIME_DIR' $null
            Set-EnvironmentVariable 'XDG_SESSION_TYPE' $null
            Set-EnvironmentVariable 'OPENUSD_VIEWER_PLATFORM' $Platform
        }
        'linux-x11'
        {
            if (-not $IsLinux)
            {
                throw 'linux-x11 must run on Linux.'
            }
            foreach ($command in @('Xvfb', 'xdpyinfo'))
            {
                if (-not (Get-Command $command -ErrorAction SilentlyContinue))
                {
                    throw "$command is required for the Linux X11 smoke."
                }
            }
            $rid = 'linux-x64'
            $xvfbStdout = Join-Path $diagnosticRoot 'xvfb.stdout.log'
            $xvfbStderr = Join-Path $diagnosticRoot 'xvfb.stderr.log'
            Remove-Item $xvfbStdout, $xvfbStderr -Force `
                -ErrorAction SilentlyContinue
            $useTcpX11 = -not [string]::IsNullOrEmpty($env:WSL_INTEROP)
            $displayNumber = Get-IsolatedDisplayNumber 0
            $xvfbArguments = @(":$displayNumber", '-screen', '0',
                '1280x720x24', '-ac')
            if ($useTcpX11)
            {
                $xvfbArguments += @('-nolisten', 'unix', '-listen', 'tcp')
            }
            else
            {
                $xvfbArguments += @('-nolisten', 'tcp')
            }
            $ownedProcess = Start-Process Xvfb -PassThru `
                -ArgumentList $xvfbArguments `
                -RedirectStandardOutput $xvfbStdout `
                -RedirectStandardError $xvfbStderr
            [void]$ownedProcesses.Add($ownedProcess)
            $display = if ($useTcpX11)
            {
                "localhost:$displayNumber"
            }
            else
            {
                ":$displayNumber"
            }
            Wait-ForX11 $display $ownedProcess
            Set-EnvironmentVariable 'DISPLAY' $display
            Set-EnvironmentVariable 'WAYLAND_DISPLAY' $null
            Set-EnvironmentVariable 'XDG_SESSION_TYPE' 'x11'
            Set-EnvironmentVariable 'LIBGL_ALWAYS_SOFTWARE' '1'
            Set-EnvironmentVariable 'MESA_GL_VERSION_OVERRIDE' '4.5COMPAT'
            Set-EnvironmentVariable 'MESA_GLSL_VERSION_OVERRIDE' '450'
            Set-EnvironmentVariable 'OPENUSD_VIEWER_PLATFORM' $Platform
        }
        'linux-wayland'
        {
            if (-not $IsLinux)
            {
                throw 'linux-wayland must run on Linux.'
            }
            foreach ($command in @('weston', 'xdpyinfo'))
            {
                if (-not (Get-Command $command -ErrorAction SilentlyContinue))
                {
                    throw "$command is required for the Linux Wayland smoke."
                }
            }
            $rid = 'linux-x64'
            $runtimeRoot = Join-Path $repoRoot "artifacts/wl-$PID"
            $ownedRuntimeRoot = $runtimeRoot
            Remove-Item $runtimeRoot -Recurse -Force -ErrorAction SilentlyContinue
            New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
            & chmod 700 $runtimeRoot
            if ($LASTEXITCODE -ne 0)
            {
                throw "Could not secure XDG_RUNTIME_DIR $runtimeRoot."
            }
            $socketName = 'openusd-wayland'
            $westonLog = Join-Path $diagnosticRoot 'weston.log'
            $westonStdout = Join-Path $diagnosticRoot 'weston.stdout.log'
            $westonStderr = Join-Path $diagnosticRoot 'weston.stderr.log'
            Set-EnvironmentVariable 'DISPLAY' $null
            Set-EnvironmentVariable 'WAYLAND_DISPLAY' $null
            Set-EnvironmentVariable 'XDG_RUNTIME_DIR' $runtimeRoot
            Set-EnvironmentVariable 'LIBGL_ALWAYS_SOFTWARE' '1'
            Remove-Item $westonLog, $westonStdout, $westonStderr -Force `
                -ErrorAction SilentlyContinue
            $ownedProcess = Start-Process weston -PassThru `
                -ArgumentList @('--backend=headless-backend.so', '--use-gl',
                    "--socket=$socketName", '--idle-time=0', '--width=1280',
                    '--height=720', '--no-config', '--xwayland',
                    "--log=$westonLog") `
                -RedirectStandardOutput $westonStdout `
                -RedirectStandardError $westonStderr
            [void]$ownedProcesses.Add($ownedProcess)
            $socketPath = Join-Path $runtimeRoot $socketName
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while ([DateTime]::UtcNow -lt $deadline -and
                -not (Test-Path $socketPath))
            {
                $ownedProcess.Refresh()
                if ($ownedProcess.HasExited)
                {
                    throw "Weston exited with code $($ownedProcess.ExitCode)."
                }
                Start-Sleep -Milliseconds 250
            }
            if (-not (Test-Path $socketPath))
            {
                throw "Weston socket $socketPath did not become ready."
            }
            Set-EnvironmentVariable 'WAYLAND_DISPLAY' $socketName
            Set-EnvironmentVariable 'XDG_SESSION_TYPE' 'wayland'
            Set-EnvironmentVariable 'MESA_GL_VERSION_OVERRIDE' '4.5COMPAT'
            Set-EnvironmentVariable 'MESA_GLSL_VERSION_OVERRIDE' '450'
            Set-EnvironmentVariable 'OPENUSD_VIEWER_PLATFORM' $Platform

            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            $display = $null
            while ([DateTime]::UtcNow -lt $deadline -and
                [string]::IsNullOrWhiteSpace($display))
            {
                $ownedProcess.Refresh()
                if ($ownedProcess.HasExited)
                {
                    throw "Weston exited before managed XWayland/XWM startup."
                }
                if (Test-Path $westonLog)
                {
                    $match = Get-Content $westonLog |
                        Select-String -Pattern 'display (:[0-9]+)' |
                        Select-Object -Last 1
                    if ($null -ne $match)
                    {
                        $display = $match.Matches[0].Groups[1].Value
                    }
                }
                Start-Sleep -Milliseconds 100
            }
            if ([string]::IsNullOrWhiteSpace($display))
            {
                throw 'Weston did not publish its managed XWayland display.'
            }
            Wait-ForX11 $display $ownedProcess

            Set-EnvironmentVariable 'DISPLAY' $display
            Set-EnvironmentVariable 'OPENUSD_RENDERER' 'Storm'
            Invoke-ViewerSmoke `
                (Join-Path $diagnosticRoot 'viewer-xwayland-storm') `
                '^Renderer frame rendered: Storm / OpenGL / XWayland;' `
                -EnableSoak $SharedStageSoak
            $runDefaultViewer = $false
        }
        'macos-arm64'
        {
            if (-not $IsMacOS -or
                [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
                    [System.Runtime.InteropServices.Architecture]::Arm64)
            {
                throw 'macos-arm64 must run on an Apple Silicon macOS host.'
            }
            $rid = 'osx-arm64'
            Set-EnvironmentVariable 'OPENUSD_VIEWER_PLATFORM' $Platform
        }
    }

    if ($runDefaultViewer)
    {
        Invoke-ViewerSmoke `
            $viewerRoot `
            '^Renderer frame rendered: Storm / OpenGL;'
    }
}
catch
{
    if ($ownedProcesses.Count -ne 0)
    {
        foreach ($path in @(
            (Join-Path $diagnosticRoot 'xvfb.stderr.log'),
            (Join-Path $diagnosticRoot 'weston.log'),
            (Join-Path $diagnosticRoot 'weston.stderr.log'),
            (Join-Path $diagnosticRoot 'xwayland.stderr.log')))
        {
            if (Test-Path $path)
            {
                Write-Host "----- $([System.IO.Path]::GetFileName($path)) -----"
                Get-Content $path | Write-Host
            }
        }
    }
    throw
}
finally
{
    for ($index = $ownedProcesses.Count - 1; $index -ge 0; $index--)
    {
        $process = $ownedProcesses[$index]
        if (-not $process.HasExited)
        {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
    }
    if ($ownedRuntimeRoot -and (Test-Path $ownedRuntimeRoot))
    {
        Remove-Item $ownedRuntimeRoot -Recurse -Force
    }
    foreach ($entry in $oldEnvironment.GetEnumerator())
    {
        Set-EnvironmentVariable $entry.Key $entry.Value
    }
}
