# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CompilerRoot,
    [string]$EvidencePath = (Join-Path $PSScriptRoot '../artifacts/native-probe/osx-arm64/shaderc-cache-verification.json')
)

$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[object]]::new()
$record = [ordered]@{
    schemaVersion = 1
    kind = 'private-cpu-compiler-cache-verification'
    status = 'failed'
    phase = 'receipt-read'
    receiptSha256 = $null
    receiptLength = $null
    actualReceipt = $null
    comparisons = @()
}
$retentionFailure = $null

function Convert-PublicIdentity([object]$Value, [string]$Kind)
{
    if ($null -eq $Value) { return 'missing' }
    switch ($Kind)
    {
        'hash' { if ($Value -is [string] -and $Value -cmatch '^[A-Fa-f0-9]{64}$') { return $Value } }
        'commit' { if ($Value -is [string] -and $Value -cmatch '^[a-f0-9]{40}$') { return $Value } }
        'schema' { if ($Value -is [int] -or $Value -is [long]) { return $Value } }
        'kind' { if ($Value -ceq 'cpu-only-static-shaderc') { return 'cpu-only-static-shaderc' } }
    }
    'invalid'
}

function Add-Comparison([string]$Field, [object]$Expected, [object]$Actual, [bool]$Matches)
{
    $checks.Add([ordered]@{ field = $Field; expected = $Expected; actual = $Actual; matches = $Matches })
}

try
{
    $raw = [IO.File]::ReadAllBytes((Join-Path $CompilerRoot 'shaderc-provenance.json'))
    $record.receiptLength = $raw.Length
    $record.receiptSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw))
    $record.phase = 'receipt-parse'
    try { $receipt = [Text.Encoding]::UTF8.GetString($raw) | ConvertFrom-Json }
    catch [ArgumentException] { throw 'Private CPU compiler receipt is not valid JSON; see the bounded receipt-parse record.' }
    $record.actualReceipt = [ordered]@{
        schemaVersion = Convert-PublicIdentity $receipt.schemaVersion 'schema'
        kind = Convert-PublicIdentity $receipt.kind 'kind'
        sourceManifestCommit = Convert-PublicIdentity $receipt.sourceManifestCommit 'commit'
        sourceManifestSha256 = Convert-PublicIdentity $receipt.sourceManifestSha256 'hash'
        buildScriptSha256 = Convert-PublicIdentity $receipt.buildScriptSha256 'hash'
        compilerPolicySha256 = Convert-PublicIdentity $receipt.compilerPolicySha256 'hash'
        sourcePatchLockSha256 = Convert-PublicIdentity $receipt.sourcePatchLockSha256 'hash'
        expectedLibraryKind = $receipt.library -ceq 'lib/libshaderc_combined.a'
        librarySha256 = Convert-PublicIdentity $receipt.librarySha256 'hash'
    }
    $record.phase = 'source-comparison'
    $lock = Get-Content (Join-Path $PSScriptRoot 'openusd.install.lock.json') -Raw | ConvertFrom-Json
    $manifest = @($lock.vulkanSdk.repositories | Where-Object name -CEQ 'shaderc-source-manifest')
    $expectedCommit = if ($manifest.Count -eq 1) { $manifest[0].commit } else { $null }
    $manifestHash = '8FFA915811760A266E2EB5B683EFF227BB0FE077EA722FC5B9AC7D681F60E534'
    $record.canonicalManifestSha256 = $manifestHash
    $buildHash = (Get-FileHash (Join-Path $PSScriptRoot 'build-vulkan-sdk.ps1')).Hash
    $policyHash = (Get-FileHash (Join-Path $PSScriptRoot 'cmake/ShaderCompiler.cmake')).Hash
    Add-Comparison 'manifest-count' 1 $manifest.Count ($manifest.Count -eq 1)
    Add-Comparison 'schema-version' 1 $record.actualReceipt.schemaVersion ($receipt.schemaVersion -eq 1)
    Add-Comparison 'receipt-kind' 'cpu-only-static-shaderc' $record.actualReceipt.kind `
        ($receipt.kind -ceq 'cpu-only-static-shaderc')
    Add-Comparison 'source-manifest-commit' (Convert-PublicIdentity $expectedCommit 'commit') `
        $record.actualReceipt.sourceManifestCommit ($receipt.sourceManifestCommit -ceq $expectedCommit)
    Add-Comparison 'source-manifest-sha256' $manifestHash $record.actualReceipt.sourceManifestSha256 `
        ($receipt.sourceManifestSha256 -ceq $manifestHash)
    Add-Comparison 'build-script-sha256' $buildHash $record.actualReceipt.buildScriptSha256 `
        ($receipt.buildScriptSha256 -ceq $buildHash)
    Add-Comparison 'compiler-policy-sha256' $policyHash $record.actualReceipt.compilerPolicySha256 `
        ($receipt.compilerPolicySha256 -ceq $policyHash)
    Add-Comparison 'source-patch-lock-sha256' (Convert-PublicIdentity $lock.vulkanSdk.sourcePatchLockSha256 'hash') `
        $record.actualReceipt.sourcePatchLockSha256 `
        ($receipt.sourcePatchLockSha256 -ceq $lock.vulkanSdk.sourcePatchLockSha256)
    Add-Comparison 'private-static-library-kind' $true $record.actualReceipt.expectedLibraryKind `
        ($receipt.library -ceq 'lib/libshaderc_combined.a')
    $record.phase = 'library-hash'
    $libraryHash = (Get-FileHash (Join-Path $CompilerRoot 'lib/libshaderc_combined.a')).Hash
    Add-Comparison 'library-sha256' $libraryHash $record.actualReceipt.librarySha256 `
        ($receipt.librarySha256 -ceq $libraryHash)
    $record.phase = 'repair-inventory'
    $profile = Get-Content (Join-Path $PSScriptRoot "..\$($lock.vulkanSdk.sourcePatchLock)") -Raw | ConvertFrom-Json
    $expected = @(
        foreach ($component in $profile.components)
        {
            foreach ($file in $component.files)
            {
                "$($component.component)|$($file.file)|$($file.afterSha256)"
            }
        })
    $actual = @($receipt.repairedSources | ForEach-Object { "$($_.component)|$($_.file)|$($_.sha256)" })
    $expectedDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes((($expected | Sort-Object) -join "`n"))))
    $actualDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes((($actual | Sort-Object) -join "`n"))))
    Add-Comparison 'repaired-source-count' $expected.Count $actual.Count ($expected.Count -eq $actual.Count)
    Add-Comparison 'repaired-source-inventory' $expectedDigest $actualDigest `
        (@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object) -CaseSensitive).Count -eq 0)
    $record.phase = 'comparison-complete'
    if (@($checks | Where-Object matches -EQ $false).Count -eq 0) { $record.status = 'verified' }
}
finally
{
    $record.comparisons = @($checks.ToArray())
    try
    {
        $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($EvidencePath))
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $json = $record | ConvertTo-Json -Depth 7
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        if ($bytes.Length -gt 16384) { throw [IO.IOException]::new('Allowlisted compiler verification record exceeds its bound.') }
        [IO.File]::WriteAllBytes($EvidencePath, $bytes)
    }
    catch [IO.IOException]
    {
        $retentionFailure = $_
        Write-Warning 'Private compiler cache comparison retention failed; original verifier failure is preserved.' -WarningAction Continue
    }
    catch [UnauthorizedAccessException]
    {
        $retentionFailure = $_
        Write-Warning 'Private compiler cache comparison retention was denied; original verifier failure is preserved.' -WarningAction Continue
    }
}
$failed = @($checks | Where-Object matches -EQ $false | ForEach-Object field)
if ($failed.Count -ne 0)
{
    foreach ($check in @($checks | Where-Object matches -EQ $false))
    {
        Write-Host "PRIVATE_COMPILER_CACHE_MISMATCH field=$($check.field) expected=$($check.expected) actual=$($check.actual)"
    }
    throw "Private CPU shader compiler cache provenance differs: $($failed -join ', ')."
}
if ($null -ne $retentionFailure) { throw $retentionFailure }
Write-Host 'PRIVATE_COMPILER_CACHE_VERIFIED: exact public source/receipt/library/inventory identities.'
