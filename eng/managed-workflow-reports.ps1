# Copyright (c) marcschier. Licensed under the MIT License.

function Resolve-ManagedWorkflowPhysicalDirectory
{
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not $IsWindows)
    {
        throw 'Physical workflow scratch paths require Windows.'
    }
    if (-not [IO.Path]::IsPathFullyQualified($Path))
    {
        throw 'The workflow scratch directory must be absolute.'
    }
    $current = $Path
    while ($true)
    {
        $item = Get-Item -LiteralPath $current -ErrorAction Stop
        if ($item -isnot [IO.DirectoryInfo])
        {
            throw "The workflow scratch path must be a directory: $current"
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "The workflow scratch path contains a reparse point: $current"
        }
        if ($null -eq $item.Parent)
        {
            break
        }
        $current = $item.Parent.FullName
    }
    if (-not ('OpenUsd.Workflows.WindowsDirectoryPath' -as [type]))
    {
        # A handle resolves stored casing without listing protected temp-directory parents.
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Workflows
{
    public static class WindowsDirectoryPath
    {
        public static string Resolve(string path)
        {
            using (SafeFileHandle handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length >= buffer.Capacity)
                    throw new System.IO.PathTooLongException("The physical workflow path exceeds the Windows limit.");
                string result = buffer.ToString();
                if (result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                    return @"\\" + result.Substring(8);
                if (result.StartsWith(@"\\?\", StringComparison.Ordinal))
                    return result.Substring(4);
                throw new System.IO.IOException("The physical workflow directory has no DOS or UNC path.");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    }
}
'@
    }
    return [OpenUsd.Workflows.WindowsDirectoryPath]::Resolve($Path)
}

function Invoke-ManagedWorkflowScenario
{
    param(
        [Parameter(Mandatory = $true)][string]$Runner,
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$AssemblyDirectory,
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][object]$Entry,
        [string]$Configuration = 'Release',
        [string]$ReportAssembly = 'OpenUsd.Viewer.Tests',
        [string[]]$TestArguments = @(),
        [bool]$RequireNoSkips = $true,
        [switch]$MesaWgl
    )

    $logPath = Join-Path $EvidenceRoot "$($Entry.Id).log"
    $reportDirectory = Join-Path $AssemblyDirectory 'TestResults'
    $reports = @(
        @{ source = "$ReportAssembly-*-net10.0.tunit-report.json"
            name = "$($Entry.Id).tunit-report.json" },
        @{ source = "$ReportAssembly-*-net10.0-report.html"
            name = "$($Entry.Id).report.html" }
    )
    foreach ($report in $reports)
    {
        $report.copy = Join-Path $EvidenceRoot $report.name
        if (Test-Path -LiteralPath $reportDirectory)
        {
            foreach ($old in @(Get-ChildItem -LiteralPath $reportDirectory -Filter $report.source -File))
            {
                $prior = Join-Path $EvidenceRoot "$($Entry.Id).prior-$($old.Name)"
                if (Test-Path -LiteralPath $prior)
                {
                    throw "The prior report evidence already exists: $prior"
                }
                $priorHash = (Get-FileHash -LiteralPath $old.FullName -Algorithm SHA256).Hash
                Move-Item -LiteralPath $old.FullName -Destination $prior -ErrorAction Stop
                if ((Get-FileHash -LiteralPath $prior -Algorithm SHA256).Hash -cne $priorHash)
                {
                    throw "The pre-existing report changed while separating stale evidence: $prior"
                }
            }
        }
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $primaryFailure = $null
    $primaryExit = 0
    $retentionErrors = [Collections.Generic.List[string]]::new()
    try
    {
        $selectedArguments = if ($TestArguments.Count -ne 0) { $TestArguments }
        else { @('--treenode-filter', "/*/*/$($Entry.Class)/*") }
        & $Runner -Project $Project -Framework net10.0 -Configuration $Configuration `
            -UseWindowsAppHost:$MesaWgl -MinimumExpectedTests 1 -TestArguments $selectedArguments `
            *>&1 | Tee-Object -FilePath $logPath | ForEach-Object { Write-Host $_ }
        $primaryExit = $LASTEXITCODE
        if ($primaryExit -ne 0)
        {
            throw "Managed workflow '$($Entry.Id)' failed; see $logPath"
        }
        $log = [IO.File]::ReadAllText($logPath)
        $passed = [regex]::Matches($log, '(?m)^\s*succeeded:\s*(\d+)\s*$')
        $failed = [regex]::Matches($log, '(?m)^\s*failed:\s*(\d+)\s*$')
        $skipped = [regex]::Matches($log, '(?m)^\s*skipped:\s*(\d+)\s*$')
        if ($passed.Count -ne 1 -or $failed.Count -ne 1 -or $skipped.Count -ne 1 -or
            [int]$passed[0].Groups[1].Value -lt 1 -or [int]$failed[0].Groups[1].Value -ne 0 -or
            ($RequireNoSkips -and [int]$skipped[0].Groups[1].Value -ne 0))
        {
            throw "Workflow '$($Entry.Id)' did not provide one complete no-skip execution summary."
        }
    }
    catch
    {
        # Retention failures must not replace the original test or count failure.
        $primaryFailure = $_
        $primaryExit = $LASTEXITCODE
    }
    finally
    {
        foreach ($report in $reports)
        {
            try
            {
                $current = if (Test-Path -LiteralPath $reportDirectory)
                {
                    @(Get-ChildItem -LiteralPath $reportDirectory -Filter $report.source -File)
                }
                else { @() }
                if (@($current).Count -ne 1)
                {
                    throw [IO.IOException]::new(
                        "Expected exactly one current report matching $($report.source); found $(@($current).Count).")
                }
                $report.sha256 = (Get-FileHash -LiteralPath $current[0].FullName -Algorithm SHA256 `
                    -ErrorAction Stop).Hash.ToLowerInvariant()
                Copy-Item -LiteralPath $current[0].FullName -Destination $report.copy -ErrorAction Stop
                if ((Get-FileHash -LiteralPath $report.copy -Algorithm SHA256 `
                    -ErrorAction Stop).Hash.ToLowerInvariant() -cne $report.sha256)
                {
                    throw [IO.IOException]::new("The current report changed while retaining $($report.copy).")
                }
            }
            catch [IO.IOException]
            {
                $retentionErrors.Add($_.Exception.Message)
                Write-Warning "[managed-workflows] report-retention-failed=$($Entry.Id): $($_.Exception.Message)" `
                    -WarningAction Continue
            }
            catch [UnauthorizedAccessException]
            {
                $retentionErrors.Add($_.Exception.Message)
                Write-Warning "[managed-workflows] report-retention-failed=$($Entry.Id): $($_.Exception.Message)" `
                    -WarningAction Continue
            }
            catch [Management.Automation.RuntimeException]
            {
                $retentionErrors.Add($_.Exception.Message)
                Write-Warning "[managed-workflows] report-retention-failed=$($Entry.Id): $($_.Exception.Message)" `
                    -WarningAction Continue
            }
        }
        if ($retentionErrors.Count -ne 0)
        {
            Write-Warning "[managed-workflows] original-verdict-preserved=$($Entry.Id); $($retentionErrors -join '; ')" `
                -WarningAction Continue
        }
        $global:LASTEXITCODE = $primaryExit
    }
    if ($null -ne $primaryFailure)
    {
        throw $primaryFailure
    }
    if ($retentionErrors.Count -ne 0)
    {
        throw "Workflow '$($Entry.Id)' report retention failed: $($retentionErrors -join '; ')"
    }
    [ordered]@{
        scenario = $Entry.Id
        testClass = $Entry.Class
        passed = [int]$passed[0].Groups[1].Value
        elapsedMilliseconds = $watch.ElapsedMilliseconds
        logSha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
        rawReport = $reports[0].name
        rawReportSha256 = $reports[0].sha256
        htmlReport = $reports[1].name
        htmlReportSha256 = $reports[1].sha256
    }
}
