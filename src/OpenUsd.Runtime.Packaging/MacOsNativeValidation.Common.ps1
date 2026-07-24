# Copyright (c) marcschier. Licensed under the MIT License.

function Assert-OpenUsdMacOsRPaths
{
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RPaths,
        [Parameter(Mandatory = $true)][string]$Context
    )

    foreach ($rpath in $RPaths)
    {
        $normalized = $rpath.Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($normalized) -or
            [System.IO.Path]::IsPathRooted($normalized) -or
            $normalized -match '^[A-Za-z]:' -or
            $normalized.Contains('..', [StringComparison]::Ordinal) -or
            $normalized.Contains('/src/', [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.Contains('/source/', [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.Contains('/build/', [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.Contains('/install/', [StringComparison]::OrdinalIgnoreCase))
        {
            throw "macOS runtime package input '$Context' has an unsafe LC_RPATH: '$rpath'."
        }
    }

    if ($RPaths.Count -ne 1 -or $RPaths[0] -ne '@loader_path')
    {
        throw (
            "macOS runtime package input '$Context' must have exactly one " +
            "LC_RPATH entry, '@loader_path'. Found: [$($RPaths -join ', ')].")
    }
}

function Assert-OpenUsdMacOsDependency
{
    param(
        [Parameter(Mandatory = $true)][string]$Dependency,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $normalized = $Dependency.Replace('\', '/')
    if ($normalized.StartsWith('/System/Library/', [StringComparison]::Ordinal) -or
        $normalized.StartsWith('/usr/lib/', [StringComparison]::Ordinal))
    {
        return
    }
    if (($normalized.StartsWith('@rpath/', [StringComparison]::Ordinal) -or
         $normalized.StartsWith('@loader_path/', [StringComparison]::Ordinal)) -and
        -not $normalized.Contains('..', [StringComparison]::Ordinal) -and
        -not $normalized.Contains('/src/', [StringComparison]::OrdinalIgnoreCase) -and
        -not $normalized.Contains('/source/', [StringComparison]::OrdinalIgnoreCase) -and
        -not $normalized.Contains('/build/', [StringComparison]::OrdinalIgnoreCase) -and
        -not $normalized.Contains('/install/', [StringComparison]::OrdinalIgnoreCase))
    {
        return
    }

    throw "macOS runtime package input '$Context' has a non-relocatable dependency: $Dependency"
}
