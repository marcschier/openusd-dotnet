# Copyright (c) marcschier. Licensed under the MIT License.

function Get-OpenUsdElfDynamicEntries
{
    # readelf separates its output with blank lines and begins the dynamic
    # section with one. A Mandatory [string[]] rejects the whole array as "an
    # empty string" if any single element is empty, so without AllowEmptyString
    # this never accepted real readelf output at all. An entirely empty array is
    # still rejected, because that means readelf produced nothing.
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines)

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($line in $Lines)
    {
        $tagMatch = [regex]::Match(
            $line,
            '^\s*0x[0-9A-Fa-f]+\s+\((?<tag>[A-Z0-9_]+)\)\s+(?<text>.*)$')
        if (-not $tagMatch.Success)
        {
            continue
        }

        $tag = $tagMatch.Groups['tag'].Value
        if ($tag -notin @('RUNPATH', 'RPATH', 'SONAME'))
        {
            continue
        }

        $valueMatch = [regex]::Match(
            $tagMatch.Groups['text'].Value,
            '\[(?<value>[^\]]*)\]')
        if (-not $valueMatch.Success)
        {
            throw "Could not parse ELF DT_$tag from readelf output: $line"
        }

        $entries.Add([pscustomobject]@{
            Tag = $tag
            Value = $valueMatch.Groups['value'].Value
        })
    }
    return $entries.ToArray()
}

function Assert-OpenUsdElfRunpath
{
    param(
        [Parameter(Mandatory = $true)][object[]]$DynamicEntries,
        [Parameter(Mandatory = $true)][string]$LibraryPath,
        [Parameter(Mandatory = $true)][string[]]$AllowedEntries
    )

    $legacy = @($DynamicEntries | Where-Object Tag -eq 'RPATH')
    if ($legacy.Count -ne 0)
    {
        throw "Linux runtime package input '$LibraryPath' must not contain legacy DT_RPATH."
    }

    $runpaths = @($DynamicEntries | Where-Object Tag -eq 'RUNPATH')
    if ($runpaths.Count -ne 1)
    {
        throw (
            "Linux runtime package input '$LibraryPath' must contain exactly one " +
            'DT_RUNPATH entry.')
    }

    $parts = @($runpaths[0].Value -split ':')
    $emptyParts = @($parts | Where-Object { [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -eq 0 -or $emptyParts.Count -ne 0)
    {
        throw "Linux runtime package input '$LibraryPath' has an empty DT_RUNPATH entry."
    }

    $unique = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($part in $parts)
    {
        if (-not $unique.Add($part))
        {
            throw (
                "Linux runtime package input '$LibraryPath' has duplicate " +
                "DT_RUNPATH entry '$part'.")
        }
        if ([System.IO.Path]::IsPathRooted($part))
        {
            throw (
                "Linux runtime package input '$LibraryPath' has absolute " +
                "DT_RUNPATH entry '$part'.")
        }
        if ($part -cnotin $AllowedEntries)
        {
            throw (
                "Linux runtime package input '$LibraryPath' has unexpected " +
                "DT_RUNPATH entry '$part'. Allowed entries: " +
                ($AllowedEntries -join ', ') + '.')
        }
    }

    if ($parts.Count -ne $AllowedEntries.Count)
    {
        throw (
            "Linux runtime package input '$LibraryPath' must use the exact " +
            'DT_RUNPATH allowlist.')
    }
    foreach ($allowed in $AllowedEntries)
    {
        if ($allowed -cnotin $parts)
        {
            throw (
                "Linux runtime package input '$LibraryPath' is missing required " +
                "DT_RUNPATH entry '$allowed'.")
        }
    }

    return $parts
}

function Get-OpenUsdElfDynamicValue
{
    param(
        [Parameter(Mandatory = $true)][object[]]$DynamicEntries,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    $values = @($DynamicEntries | Where-Object Tag -eq $Tag)
    if ($values.Count -gt 1)
    {
        throw "ELF dynamic section contains multiple DT_$Tag entries."
    }
    if ($values.Count -eq 1)
    {
        return $values[0].Value
    }
    return $null
}

function Assert-OpenUsdElfSoname
{
    param(
        [Parameter(Mandatory = $true)][object[]]$DynamicEntries,
        [Parameter(Mandatory = $true)][string]$LibraryPath,
        [Parameter(Mandatory = $true)][string]$RequiredSoname
    )

    $soname = Get-OpenUsdElfDynamicValue `
        -DynamicEntries $DynamicEntries `
        -Tag 'SONAME'
    if ([string]::IsNullOrWhiteSpace($soname))
    {
        throw "Linux runtime package input '$LibraryPath' is missing DT_SONAME."
    }
    if ($soname -cne $RequiredSoname)
    {
        throw (
            "Linux runtime package input '$LibraryPath' must use DT_SONAME " +
            "'$RequiredSoname', got '$soname'.")
    }
    return $soname
}
