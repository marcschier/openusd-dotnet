#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LinuxElfValidation.ps1')

$valid = @(
    '',
    'Dynamic section at offset 0x123 contains 2 entries:',
    ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN]',
    ' 0x000000000000000e (SONAME) Library soname: [libexample.so]',
    '')
$entries = @(Get-OpenUsdElfDynamicEntries -Lines $valid)
$parts = @(Assert-OpenUsdElfRunpath `
    -DynamicEntries $entries `
    -LibraryPath 'libexample.so' `
    -AllowedEntries @('$ORIGIN'))
if ($parts.Count -ne 1 -or $parts[0] -ne '$ORIGIN')
{
    throw 'The valid DT_RUNPATH parser case did not return the exact allowlist.'
}
$soname = Assert-OpenUsdElfSoname `
    -DynamicEntries $entries `
    -LibraryPath 'libopenusd_storm_child.so' `
    -RequiredSoname 'libexample.so'
if ($soname -cne 'libexample.so')
{
    throw 'The valid DT_SONAME parser case did not return the exact value.'
}

function Assert-Rejected
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    try
    {
        $dynamic = @(Get-OpenUsdElfDynamicEntries -Lines $Lines)
        Assert-OpenUsdElfRunpath `
            -DynamicEntries $dynamic `
            -LibraryPath 'libexample.so' `
            -AllowedEntries @('$ORIGIN') |
            Out-Null
    }

    catch
    {
        if ($_.Exception.Message -notmatch $ExpectedMessage)
        {
            throw "$Name returned an unexpected diagnostic: $($_.Exception.Message)"
        }
        return
    }
    throw "$Name was not rejected."
}

function Assert-SonameRejected
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    try
    {
        $dynamic = @(Get-OpenUsdElfDynamicEntries -Lines $Lines)
        Assert-OpenUsdElfSoname `
            -DynamicEntries $dynamic `
            -LibraryPath 'libopenusd_storm_child.so' `
            -RequiredSoname 'libopenusd_storm_child.so.8' |
            Out-Null
    }
    catch
    {
        if ($_.Exception.Message -notmatch $ExpectedMessage)
        {
            throw "$Name returned an unexpected diagnostic: $($_.Exception.Message)"
        }
        return
    }
    throw "$Name was not rejected."
}

Assert-Rejected `
    -Name 'legacy RPATH' `
    -Lines @(' 0x000000000000000f (RPATH) Library rpath: [$ORIGIN]') `
    -ExpectedMessage 'legacy DT_RPATH'
Assert-Rejected `
    -Name 'absolute path' `
    -Lines @(' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN:/tmp]') `
    -ExpectedMessage 'absolute DT_RUNPATH'
Assert-Rejected `
    -Name 'source install path' `
    -Lines @(
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN:/repo/native/install/lib]') `
    -ExpectedMessage 'absolute DT_RUNPATH'
Assert-Rejected `
    -Name 'relative source install path' `
    -Lines @(
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN:native/install/lib]') `
    -ExpectedMessage 'unexpected DT_RUNPATH'
Assert-Rejected `
    -Name 'missing runpath' `
    -Lines @(' 0x000000000000000e (SONAME) Library soname: [libexample.so]') `
    -ExpectedMessage 'exactly one DT_RUNPATH'
Assert-Rejected `
    -Name 'empty entry' `
    -Lines @(' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN::]') `
    -ExpectedMessage 'empty DT_RUNPATH'
Assert-Rejected `
    -Name 'duplicate entry' `
    -Lines @(
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN:$ORIGIN]') `
    -ExpectedMessage 'duplicate DT_RUNPATH'
Assert-Rejected `
    -Name 'unexpected relative entry' `
    -Lines @(
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN:$ORIGIN/../lib]') `
    -ExpectedMessage 'unexpected DT_RUNPATH'
Assert-Rejected `
    -Name 'multiple runpath tags' `
    -Lines @(
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN]',
        ' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN]') `
    -ExpectedMessage 'exactly one DT_RUNPATH'
Assert-SonameRejected `
    -Name 'missing SONAME' `
    -Lines @(' 0x000000000000001d (RUNPATH) Library runpath: [$ORIGIN]') `
    -ExpectedMessage 'missing DT_SONAME'
Assert-SonameRejected `
    -Name 'unversioned SONAME' `
    -Lines @(
        ' 0x000000000000000e (SONAME) Library soname: [libopenusd_storm_child.so]') `
    -ExpectedMessage 'must use DT_SONAME'
Assert-SonameRejected `
    -Name 'wrong SONAME version' `
    -Lines @(
        ' 0x000000000000000e (SONAME) Library soname: [libopenusd_storm_child.so.6]') `
    -ExpectedMessage 'must use DT_SONAME'
Assert-SonameRejected `
    -Name 'multiple SONAME tags' `
    -Lines @(
        ' 0x000000000000000e (SONAME) Library soname: [libopenusd_storm_child.so.8]',
        ' 0x000000000000000e (SONAME) Library soname: [libopenusd_storm_child.so.8]') `
    -ExpectedMessage 'multiple DT_SONAME'

Write-Output 'Linux ELF DT_RUNPATH and ABI-8 DT_SONAME parser tests passed.'
