#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows)
{
    throw 'The managed workflow path regression requires Windows.'
}
. (Join-Path $PSScriptRoot 'managed-workflow-reports.ps1')

function Assert-RejectedPath
{
    param([string]$Name, [string]$Path, [string]$Expected)

    $message = $null
    try { Resolve-ManagedWorkflowPhysicalDirectory -Path $Path | Out-Null }
    catch { $message = $_.Exception.Message }
    if ($null -eq $message -or -not $message.Contains($Expected, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "${Name}: expected rejection containing '$Expected', got '$message'."
    }
    Write-Output "PASS $Name"
}

$parent = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName
$root = Join-Path $parent ("openusd-workflow-paths-" + [Guid]::NewGuid().ToString('N'))
$nested = Join-Path $root 'MixedCaseParent\MixedCaseChild\leaf'
$junction = Join-Path $root 'alias'
try
{
    New-Item -ItemType Directory -Path $nested -ErrorAction Stop | Out-Null
    $canonical = Resolve-ManagedWorkflowPhysicalDirectory -Path $nested
    if ($canonical -cne $nested)
    {
        throw "CanonicalDirectory: expected '$nested', got '$canonical'."
    }
    Write-Output 'PASS CanonicalDirectory'

    $misspelled = Join-Path $root 'MIXEDCASEPARENT\mixedcasechild\leaf'
    $resolved = Resolve-ManagedWorkflowPhysicalDirectory -Path $misspelled
    if ($resolved -cne $nested)
    {
        throw "AncestorCaseIsCanonical: expected '$nested', got '$resolved'."
    }
    Write-Output 'PASS AncestorCaseIsCanonical'

    $driveRoot = (Get-Item -LiteralPath ([IO.Path]::GetPathRoot($root))).FullName
    if ((Resolve-ManagedWorkflowPhysicalDirectory -Path $driveRoot) -cne $driveRoot)
    {
        throw 'DriveRootIsPreserved: the drive root changed.'
    }
    Write-Output 'PASS DriveRootIsPreserved'

    Assert-RejectedPath 'RelativeDirectoryIsRejected' '.' 'absolute'
    Assert-RejectedPath 'MissingDirectoryIsRejected' (Join-Path $root 'absent') 'does not exist'
    $file = Join-Path $root 'file.txt'
    [IO.File]::WriteAllText($file, 'unchanged')
    Assert-RejectedPath 'FileIsRejected' $file 'directory'
    if ([IO.File]::ReadAllText($file) -cne 'unchanged')
    {
        throw 'FileIsRejected: resolving a path changed file contents.'
    }

    New-Item -ItemType Junction -Path $junction -Target (Join-Path $root 'MixedCaseParent') |
        Out-Null
    Assert-RejectedPath 'JunctionAncestorIsRejected' (Join-Path $junction 'MixedCaseChild\leaf') 'reparse'
}
finally
{
    if ([IO.Directory]::Exists($junction))
    {
        [IO.Directory]::Delete($junction)
    }
    if ([IO.Directory]::Exists($root))
    {
        [IO.Directory]::Delete($root, $true)
    }
}
