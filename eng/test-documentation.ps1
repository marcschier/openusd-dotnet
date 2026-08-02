#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$samplesRoot = Join-Path $repoRoot 'samples'
$failures = [System.Collections.Generic.List[object]]::new()
$pathComparer = if ($IsWindows)
{
    [System.StringComparer]::OrdinalIgnoreCase
}
else
{
    [System.StringComparer]::Ordinal
}
$markdownAnchorCache = [System.Collections.Generic.Dictionary[string, object]]::new(
    $pathComparer)
$pathComparison = if ($IsWindows)
{
    [System.StringComparison]::OrdinalIgnoreCase
}
else
{
    [System.StringComparison]::Ordinal
}

function Get-RepositoryRelativePath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetRelativePath($repoRoot, $Path).Replace('\', '/')
}

function Add-DocumentationFailure
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Line,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $relativePath = if ([System.IO.Path]::IsPathRooted($Path))
    {
        Get-RepositoryRelativePath $Path
    }
    else
    {
        $Path.Replace('\', '/')
    }

    [void]$failures.Add([pscustomobject]@{
        Path = $relativePath
        Line = $Line
        Message = $Message
    })
}

function Get-MarkdownFiles
{
    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $git)
    {
        try
        {
            $relativePaths = & $git.Source -C $repoRoot ls-files `
                --cached --others --exclude-standard -- '*.md' 2>$null
            if ($LASTEXITCODE -eq 0)
            {
                foreach ($relativePath in $relativePaths)
                {
                    $fullPath = Join-Path $repoRoot $relativePath
                    if (Test-Path -LiteralPath $fullPath -PathType Leaf)
                    {
                        [void]$paths.Add([System.IO.Path]::GetFullPath($fullPath))
                    }
                }
            }
        }
        catch
        {
            # Fall back to a filesystem scan when Git is unavailable to this process.
        }
    }

    if ($paths.Count -eq 0)
    {
        foreach ($file in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.md')
        {
            $relativePath = Get-RepositoryRelativePath $file.FullName
            if ($relativePath -notmatch (
                '(?i)(^|/)(?:\.git|\.dotnet|\.cache|artifacts|bin|obj|out|' +
                'packages|node_modules|TestResults)(?:/|$)'))
            {
                [void]$paths.Add($file.FullName)
            }
        }
    }

    $orderedPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $paths)
    {
        [void]$orderedPaths.Add($path)
    }
    $orderedPaths.Sort([System.StringComparer]::Ordinal)
    return $orderedPaths.ToArray()
}

function Test-IsDocumentationScope
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $relativePath = Get-RepositoryRelativePath $Path
    return -not $relativePath.Contains('/') -or
        $relativePath.StartsWith('docs/', [System.StringComparison]::Ordinal) -or
        $relativePath.StartsWith('samples/', [System.StringComparison]::Ordinal)
}

function Get-MarkdownLinesOutsideFences
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines,
        [switch]$SuppressDiagnostics
    )

    $outsideLines = [System.Collections.Generic.List[object]]::new()
    $fenceCharacter = $null
    $fenceLength = 0
    $fenceLine = 0
    $openingPattern = '^(?: {0,3})(?<fence>`{3,}|~{3,})(?<info>.*)$'

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $line = $Lines[$index]
        $lineNumber = $index + 1
        if ($null -eq $fenceCharacter)
        {
            $openingMatch = [System.Text.RegularExpressions.Regex]::Match(
                $line,
                $openingPattern)
            if ($openingMatch.Success)
            {
                $fence = $openingMatch.Groups['fence'].Value
                $fenceCharacter = $fence[0]
                $fenceLength = $fence.Length
                $fenceLine = $lineNumber
                continue
            }

            [void]$outsideLines.Add([pscustomobject]@{
                Number = $lineNumber
                Text = $line
            })
            continue
        }

        $closingPattern = '^(?: {0,3})' +
            [System.Text.RegularExpressions.Regex]::Escape(
                [string]$fenceCharacter) +
            '{' + $fenceLength + ',}[ \t]*$'
        if ([System.Text.RegularExpressions.Regex]::IsMatch($line, $closingPattern))
        {
            $fenceCharacter = $null
            $fenceLength = 0
            $fenceLine = 0
        }
    }

    if ($null -ne $fenceCharacter -and -not $SuppressDiagnostics)
    {
        Add-DocumentationFailure `
            -Path $Path `
            -Line $fenceLine `
            -Message "Unclosed Markdown fence beginning with '$fenceCharacter'."
    }

    return $outsideLines.ToArray()
}

function ConvertTo-GitHubHeadingAnchor
{
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $value = [System.Text.RegularExpressions.Regex]::Replace(
        $Text,
        '<[^>]+>',
        '')
    $value = [System.Net.WebUtility]::HtmlDecode($value)
    $value = [System.Text.RegularExpressions.Regex]::Replace(
        $value,
        '!\[(?<text>[^\]]*)\]\([^)]*\)',
        '${text}')
    $value = [System.Text.RegularExpressions.Regex]::Replace(
        $value,
        '\[(?<text>[^\]]*)\]\([^)]*\)',
        '${text}')
    $value = [System.Text.RegularExpressions.Regex]::Replace(
        $value,
        '!?\[(?<text>[^\]]*)\]\[[^\]]*\]',
        '${text}')
    $value = [System.Text.RegularExpressions.Regex]::Replace($value, '`+', '')
    $value = $value.Normalize([System.Text.NormalizationForm]::FormC).ToLowerInvariant()
    $value = [System.Text.RegularExpressions.Regex]::Replace(
        $value,
        '[^\p{L}\p{M}\p{N}\s_-]',
        '')
    $value = [System.Text.RegularExpressions.Regex]::Replace($value, '\s', '-')
    return $value.Normalize([System.Text.NormalizationForm]::FormC)
}

function Get-MarkdownAnchorSetFromLines
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [object[]]$Lines
    )

    $anchors = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $headingAnchors = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $atxHeadingPattern = '^(?: {0,3})#{1,6}(?:[ \t]+|$)(?<text>.*)$'
    $setextPattern = '^(?: {0,3})(?:=+|-+)[ \t]*$'
    $htmlTagPattern = '<[A-Za-z][^>]*>'
    $htmlAnchorAttributePattern = (
        '(?i)(?:^|\s)(?:id|name)\s*=\s*' +
        '(?:"(?<double>[^"]*)"|''(?<single>[^'']*)''|(?<bare>[^\s>]+))')

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $line = $Lines[$index]
        foreach ($tagMatch in [System.Text.RegularExpressions.Regex]::Matches(
            $line.Text,
            $htmlTagPattern))
        {
            foreach ($attributeMatch in [System.Text.RegularExpressions.Regex]::Matches(
                $tagMatch.Value,
                $htmlAnchorAttributePattern))
            {
                $anchor = if ($attributeMatch.Groups['double'].Success)
                {
                    $attributeMatch.Groups['double'].Value
                }
                elseif ($attributeMatch.Groups['single'].Success)
                {
                    $attributeMatch.Groups['single'].Value
                }
                else
                {
                    $attributeMatch.Groups['bare'].Value
                }
                $anchor = [System.Net.WebUtility]::HtmlDecode($anchor).Normalize(
                    [System.Text.NormalizationForm]::FormC)
                if (-not [string]::IsNullOrEmpty($anchor))
                {
                    [void]$anchors.Add($anchor)
                }
            }
        }

        $headingText = $null
        $atxMatch = [System.Text.RegularExpressions.Regex]::Match(
            $line.Text,
            $atxHeadingPattern)
        if ($atxMatch.Success)
        {
            $headingText = [System.Text.RegularExpressions.Regex]::Replace(
                $atxMatch.Groups['text'].Value,
                '[ \t]+#+[ \t]*$',
                '').Trim()
        }
        elseif (
            $index + 1 -lt $Lines.Count -and
            $Lines[$index + 1].Number -eq $line.Number + 1 -and
            [System.Text.RegularExpressions.Regex]::IsMatch(
                $Lines[$index + 1].Text,
                $setextPattern) -and
            -not [string]::IsNullOrWhiteSpace($line.Text))
        {
            $headingText = $line.Text.Trim()
        }

        if ($null -eq $headingText)
        {
            continue
        }

        $baseAnchor = ConvertTo-GitHubHeadingAnchor $headingText
        $anchor = $baseAnchor
        $suffix = 0
        while (-not $headingAnchors.Add($anchor))
        {
            $suffix++
            $anchor = "$baseAnchor-$suffix"
        }
        [void]$anchors.Add($anchor)
    }

    return ,$anchors
}

function Get-MarkdownAnchorSet
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $cached = $null
    if ($markdownAnchorCache.TryGetValue($fullPath, [ref]$cached))
    {
        return ,$cached
    }

    $text = [System.IO.File]::ReadAllText($fullPath)
    $lines = [System.Text.RegularExpressions.Regex]::Split(
        $text,
        "\r\n|\n|\r")
    $outsideFenceLines = @(
        Get-MarkdownLinesOutsideFences `
            -Path $fullPath `
            -Lines $lines `
            -SuppressDiagnostics)
    $anchors = Get-MarkdownAnchorSetFromLines $outsideFenceLines
    $markdownAnchorCache.Add($fullPath, $anchors)
    return ,$anchors
}

function Test-MarkdownAnchorSelfTest
{
    $text = @'
# Mixed CASE, punctuation!
## Use `UsdStage`
## Café 東京
## Duplicate
## Duplicate
## Duplicate
Setext heading
--------------
<a id="Explicit-ID"></a>
<a name='legacy.anchor'></a>
'@
    $lines = [System.Text.RegularExpressions.Regex]::Split(
        $text,
        "\r\n|\n|\r")
    $outsideFenceLines = @(
        Get-MarkdownLinesOutsideFences `
            -Path 'eng/test-documentation.ps1' `
            -Lines $lines `
            -SuppressDiagnostics)
    $anchors = Get-MarkdownAnchorSetFromLines $outsideFenceLines
    $expected = @(
        'mixed-case-punctuation',
        'use-usdstage',
        'café-東京',
        'duplicate',
        'duplicate-1',
        'duplicate-2',
        'setext-heading',
        'Explicit-ID',
        'legacy.anchor'
    )
    $missing = @($expected | Where-Object { -not $anchors.Contains($_) })
    if ($missing.Count -gt 0 -or $anchors.Count -ne $expected.Count)
    {
        $actual = [System.Collections.Generic.List[string]]::new()
        foreach ($anchor in $anchors)
        {
            [void]$actual.Add($anchor)
        }
        $actual.Sort([System.StringComparer]::Ordinal)
        Add-DocumentationFailure `
            -Path 'eng/test-documentation.ps1' `
            -Message (
                'Markdown anchor self-test failed. Missing: ' +
                "$($missing -join ', '); actual: $($actual -join ', ').")
    }
}

function Test-IsExternalLink
{
    param([Parameter(Mandatory = $true)][string]$Target)

    return $Target.StartsWith('//', [System.StringComparison]::Ordinal) -or
        $Target.StartsWith('www.', [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.Text.RegularExpressions.Regex]::IsMatch(
            $Target,
            '^[A-Za-z][A-Za-z0-9+.-]*:')
}

function Test-IsInsideRepository
{
    param([Parameter(Mandatory = $true)][string]$Path)

    if ($Path.Equals($repoRoot, $pathComparison))
    {
        return $true
    }

    $rootPrefix = $repoRoot.TrimEnd(
        [char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)) +
        [System.IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($rootPrefix, $pathComparison)
}

function Test-MarkdownLink
{
    param(
        [Parameter(Mandatory = $true)][string]$MarkdownPath,
        [Parameter(Mandatory = $true)][int]$Line,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $target = $Destination.Trim()
    if ($target.StartsWith('<', [System.StringComparison]::Ordinal) -and
        $target.EndsWith('>', [System.StringComparison]::Ordinal))
    {
        $target = $target.Substring(1, $target.Length - 2)
    }

    if ([string]::IsNullOrWhiteSpace($target) -or (Test-IsExternalLink $target))
    {
        return
    }

    $fragment = $null
    $fragmentIndex = $target.IndexOf('#')
    if ($fragmentIndex -ge 0)
    {
        $fragment = $target.Substring($fragmentIndex + 1)
        $target = $target.Substring(0, $fragmentIndex)
    }
    $queryIndex = $target.IndexOf('?')
    if ($queryIndex -ge 0)
    {
        $target = $target.Substring(0, $queryIndex)
    }
    try
    {
        if ([string]::IsNullOrWhiteSpace($target))
        {
            $candidate = [System.IO.Path]::GetFullPath($MarkdownPath)
        }
        else
        {
            $target = [System.Uri]::UnescapeDataString($target)
            $target = $target.Replace('\ ', ' ').Replace('\(', '(').Replace('\)', ')')
            $separator = [string][System.IO.Path]::DirectorySeparatorChar
            $target = $target.Replace('/', $separator).Replace('\', $separator)
            $candidate = if ($target.StartsWith(
                $separator,
                [System.StringComparison]::Ordinal))
            {
                Join-Path $repoRoot $target.TrimStart(
                    [char[]]@(
                        [System.IO.Path]::DirectorySeparatorChar,
                        [System.IO.Path]::AltDirectorySeparatorChar))
            }
            else
            {
                Join-Path (Split-Path -Parent $MarkdownPath) $target
            }
            $candidate = [System.IO.Path]::GetFullPath($candidate)
        }
    }
    catch
    {
        Add-DocumentationFailure `
            -Path $MarkdownPath `
            -Line $Line `
            -Message "Local link '$Destination' is not a valid path: $($_.Exception.Message)"
        return
    }

    if (-not (Test-IsInsideRepository $candidate))
    {
        Add-DocumentationFailure `
            -Path $MarkdownPath `
            -Line $Line `
            -Message "Local link '$Destination' resolves outside the repository."
        return
    }

    if (-not (Test-Path -LiteralPath $candidate))
    {
        Add-DocumentationFailure `
            -Path $MarkdownPath `
            -Line $Line `
            -Message "Local link '$Destination' does not resolve."
        return
    }

    if ([string]::IsNullOrEmpty($fragment) -or
        [System.IO.Path]::GetExtension($candidate) -ine '.md')
    {
        return
    }

    try
    {
        $decodedFragment = [System.Uri]::UnescapeDataString($fragment).Normalize(
            [System.Text.NormalizationForm]::FormC)
        $anchors = Get-MarkdownAnchorSet $candidate
    }
    catch
    {
        Add-DocumentationFailure `
            -Path $MarkdownPath `
            -Line $Line `
            -Message (
                "Local link '$Destination' could not validate Markdown anchors in " +
                "'$(Get-RepositoryRelativePath $candidate)': $($_.Exception.Message)")
        return
    }

    if (-not $anchors.Contains($decodedFragment))
    {
        Add-DocumentationFailure `
            -Path $MarkdownPath `
            -Line $Line `
            -Message (
                "Local link '$Destination' references missing Markdown anchor " +
                "'#$decodedFragment' in '$(Get-RepositoryRelativePath $candidate)'.")
    }
}

function Test-ReferencedRepositoryPaths
{
    param(
        [Parameter(Mandatory = $true)][string]$MarkdownPath,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $referencePattern = (
        '(?<![A-Za-z0-9_.-])(?<path>' +
        '(?:(?:\.\.?)[\\/])?' +
        '(?:[A-Za-z0-9_.-]+[\\/])*' +
        '[A-Za-z0-9_.-]+\.(?:csproj|sln|slnx|ps1|py|sh|cmd|bat))' +
        '(?![A-Za-z0-9_.-])')
    $projectDirectoryPattern = (
        '(?i)(?:--project\s+|dotnet\s+(?:run|build|publish|pack)\s+)' +
        '["'']?(?<path>(?:\.[\\/])?' +
        '(?:samples|src|tests|benchmarks)[\\/][A-Za-z0-9_.-]+)["'']?' +
        '(?=\s|$)')
    $seen = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $line = $Lines[$index]
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches(
            $line,
            $referencePattern))
        {
            if ($match.Index -gt 0 -and $line[$match.Index - 1] -eq '$')
            {
                continue
            }
            $referencePrefix = $line.Substring(0, $match.Index)
            if ($referencePrefix -match '(?i)(?:[a-z][a-z0-9+.-]*:)?//\S*$')
            {
                continue
            }

            $reference = $match.Groups['path'].Value
            $normalized = $reference.Replace('\', '/')
            while ($normalized.StartsWith('./', [System.StringComparison]::Ordinal))
            {
                $normalized = $normalized.Substring(2)
            }

            $key = "$($index + 1)`0$normalized"
            if (-not $seen.Add($key))
            {
                continue
            }

            try
            {
                $repositoryRelative = $normalized -eq 'OpenUsd.slnx' -or
                    $normalized -match (
                        '^(?:eng|samples|src|tests|benchmarks|native|' +
                        'test-assets|\.github)/')
                if ($repositoryRelative)
                {
                    $candidate = [System.IO.Path]::GetFullPath(
                        (Join-Path $repoRoot $normalized))
                }
                elseif (-not $normalized.Contains('/'))
                {
                    $existingCandidates = [System.Collections.Generic.List[string]]::new()
                    foreach ($basePath in @(
                        (Split-Path -Parent $MarkdownPath),
                        $repoRoot,
                        (Join-Path $repoRoot 'eng'),
                        (Join-Path $repoRoot 'eng/shaders'),
                        (Join-Path $repoRoot 'eng/shaders/scripts')))
                    {
                        $possiblePath = [System.IO.Path]::GetFullPath(
                            (Join-Path $basePath $normalized))
                        if ((Test-Path -LiteralPath $possiblePath -PathType Leaf) -and
                            -not $existingCandidates.Contains($possiblePath))
                        {
                            [void]$existingCandidates.Add($possiblePath)
                        }
                    }

                    if ($existingCandidates.Count -gt 1)
                    {
                        Add-DocumentationFailure `
                            -Path $MarkdownPath `
                            -Line ($index + 1) `
                            -Message (
                                "Referenced project or script '$reference' is ambiguous; " +
                                'use a repository-relative path.')
                        continue
                    }
                    $candidate = if ($existingCandidates.Count -eq 1)
                    {
                        $existingCandidates[0]
                    }
                    else
                    {
                        [System.IO.Path]::GetFullPath(
                            (Join-Path (Split-Path -Parent $MarkdownPath) $normalized))
                    }
                }
                else
                {
                    $candidate = [System.IO.Path]::GetFullPath(
                        (Join-Path (Split-Path -Parent $MarkdownPath) $normalized))
                }
            }
            catch
            {
                Add-DocumentationFailure `
                    -Path $MarkdownPath `
                    -Line ($index + 1) `
                    -Message "Referenced path '$reference' is invalid: $($_.Exception.Message)"
                continue
            }

            if (-not (Test-IsInsideRepository $candidate))
            {
                Add-DocumentationFailure `
                    -Path $MarkdownPath `
                    -Line ($index + 1) `
                    -Message "Referenced path '$reference' resolves outside the repository."
                continue
            }

            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf))
            {
                Add-DocumentationFailure `
                    -Path $MarkdownPath `
                    -Line ($index + 1) `
                    -Message "Referenced project or script '$reference' does not exist."
            }
        }

        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches(
            $line,
            $projectDirectoryPattern))
        {
            $reference = $match.Groups['path'].Value
            if ($reference.EndsWith('.csproj', [System.StringComparison]::OrdinalIgnoreCase))
            {
                continue
            }

            $normalized = $reference.Replace('\', '/')
            while ($normalized.StartsWith('./', [System.StringComparison]::Ordinal))
            {
                $normalized = $normalized.Substring(2)
            }
            $key = "$($index + 1)`0$normalized"
            if (-not $seen.Add($key))
            {
                continue
            }

            $candidate = [System.IO.Path]::GetFullPath(
                (Join-Path $repoRoot $normalized))
            if (-not (Test-Path -LiteralPath $candidate -PathType Container))
            {
                Add-DocumentationFailure `
                    -Path $MarkdownPath `
                    -Line ($index + 1) `
                    -Message "Referenced project directory '$reference' does not exist."
            }
        }
    }
}

function Test-SampleDocumentation
{
    if (-not (Test-Path -LiteralPath $samplesRoot -PathType Container))
    {
        Add-DocumentationFailure `
            -Path 'samples' `
            -Message 'The samples directory is missing.'
        return 0
    }

    $projectPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($directory in Get-ChildItem -LiteralPath $samplesRoot -Directory)
    {
        foreach ($project in Get-ChildItem `
            -LiteralPath $directory.FullName `
            -File `
            -Filter '*.csproj')
        {
            [void]$projectPaths.Add($project.FullName)
        }
    }
    $projectPaths.Sort([System.StringComparer]::Ordinal)
    $projects = @(
        $projectPaths |
            ForEach-Object { [System.IO.FileInfo]::new($_) })
    $indexPath = Join-Path $samplesRoot 'README.md'
    $indexText = $null
    if (Test-Path -LiteralPath $indexPath -PathType Leaf)
    {
        try
        {
            $indexText = [System.IO.File]::ReadAllText($indexPath)
        }
        catch
        {
            Add-DocumentationFailure `
                -Path $indexPath `
                -Message "Sample index could not be read: $($_.Exception.Message)"
        }
    }
    else
    {
        Add-DocumentationFailure `
            -Path $indexPath `
            -Message 'Missing sample index; list every samples/*/*.csproj project here.'
    }

    foreach ($project in $projects)
    {
        $readmePath = Join-Path $project.DirectoryName 'README.md'
        if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf))
        {
            Add-DocumentationFailure `
                -Path $readmePath `
                -Message "Missing README for $($project.Name)."
        }

        if ($null -ne $indexText)
        {
            $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
            $relativeProject = [System.IO.Path]::GetRelativePath(
                $samplesRoot,
                $project.FullName).Replace('\', '/')
            $relativeReadme = [System.IO.Path]::GetRelativePath(
                $samplesRoot,
                $readmePath).Replace('\', '/')
            $listed = $indexText.IndexOf(
                $projectName,
                [System.StringComparison]::Ordinal) -ge 0 -or
                $indexText.IndexOf(
                    $relativeProject,
                    [System.StringComparison]::Ordinal) -ge 0 -or
                $indexText.IndexOf(
                    $relativeReadme,
                    [System.StringComparison]::Ordinal) -ge 0
            if (-not $listed)
            {
                Add-DocumentationFailure `
                    -Path $indexPath `
                    -Message "Sample project '$relativeProject' is not listed."
            }
        }
    }

    return $projects.Count
}

Test-MarkdownAnchorSelfTest
$markdownFiles = @(Get-MarkdownFiles)
$sampleProjectCount = Test-SampleDocumentation
$maximumMarkdownLineLength = 120
$staleRepositoryUrlPattern = (
    '(?i)(?:https?://)?(?:www\.)?github\.com/marcschier/openusd(?:\.git)?' +
    '(?=$|[/#?\s)"''>])')
$solutionTestPattern = (
    '(?i)\bdotnet\s+test\b[^\r\n`]*' +
    '\b[A-Za-z0-9_.\\/-]+\.slnx?\b')
$implicitSolutionTestPattern = (
    '(?i)^\s*(?:PS>\s*|\$\s*)?dotnet\s+test\b' +
    '(?![^\r\n]*\.csproj\b)')
$inlineLinkPattern = (
    '!?\[[^\]]*\]\(\s*' +
    '(?<destination><[^>]+>|(?:\\.|[^()\s]|\([^()\r\n]*\))+)')
$referenceDefinitionPattern = (
    '^\s{0,3}\[[^\]]+\]:\s*' +
    '(?<destination><[^>]+>|(?:\\.|[^\s])+)')
$repositoryPackageVersion = $null
try
{
    $versionJsonPath = Join-Path $PSScriptRoot '..' 'version.json'
    $repositoryPackageVersion =
        (Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json).version
}
catch
{
    throw "version.json could not be read, so documented package versions cannot be checked: $_"
}
if ([string]::IsNullOrWhiteSpace($repositoryPackageVersion))
{
    throw 'version.json declares no version, so documented package versions cannot be checked.'
}

# Packages are published to NuGet.org, so documentation may tell readers to install them. What it
# must not do is advertise a version that this repository does not build: a reader following a stale
# instruction gets a version whose docs, ABI and runtime assets do not match. Both an explicit
# --version argument and the OpenUsdPackageVersion property used by the samples are checked.
$documentedPackageVersionPatterns = @(
    '(?i)\bdotnet\s+add\s+package\s+OpenUsd[.A-Za-z0-9_-]*\s+--version\s+(?<version>\S+)',
    '(?i)<OpenUsdPackageVersion>(?<version>[^<]+)</OpenUsdPackageVersion>'
)

foreach ($markdownPath in $markdownFiles)
{
    try
    {
        $bytes = [System.IO.File]::ReadAllBytes($markdownPath)
        $text = [System.IO.File]::ReadAllText($markdownPath)
    }
    catch
    {
        Add-DocumentationFailure `
            -Path $markdownPath `
            -Message "Markdown could not be read: $($_.Exception.Message)"
        continue
    }

    if ([System.Array]::IndexOf[byte]($bytes, 13) -ge 0)
    {
        Add-DocumentationFailure `
            -Path $markdownPath `
            -Message 'Markdown must use LF line endings; a CR byte was found.'
    }

    $lines = [System.Text.RegularExpressions.Regex]::Split(
        $text,
        "\r\n|\n|\r")
    $outsideFenceLines = @(
        Get-MarkdownLinesOutsideFences -Path $markdownPath -Lines $lines)
    $fullMarkdownPath = [System.IO.Path]::GetFullPath($markdownPath)
    if (-not $markdownAnchorCache.ContainsKey($fullMarkdownPath))
    {
        $markdownAnchorCache.Add(
            $fullMarkdownPath,
            (Get-MarkdownAnchorSetFromLines $outsideFenceLines))
    }

    for ($index = 0; $index -lt $lines.Count; $index++)
    {
        $line = $lines[$index]
        if ($line.Length -gt $maximumMarkdownLineLength)
        {
            Add-DocumentationFailure `
                -Path $markdownPath `
                -Line ($index + 1) `
                -Message (
                    "Markdown line is $($line.Length) characters; " +
                    "maximum is $maximumMarkdownLineLength.")
        }

        if ([System.Text.RegularExpressions.Regex]::IsMatch(
            $line,
            $staleRepositoryUrlPattern))
        {
            Add-DocumentationFailure `
                -Path $markdownPath `
                -Line ($index + 1) `
                -Message (
                    'Stale marcschier/openusd URL; use the current ' +
                    'marcschier/openusd-dotnet repository URL.')
        }
        if (
            [System.Text.RegularExpressions.Regex]::IsMatch(
                $line,
                $solutionTestPattern) -or
            [System.Text.RegularExpressions.Regex]::IsMatch(
                $line,
                $implicitSolutionTestPattern))
        {
            Add-DocumentationFailure `
                -Path $markdownPath `
                -Line ($index + 1) `
                -Message (
                    'Solution-wide dotnet test instructions are unsupported; ' +
                    'build first and use eng/run-managed-tests.ps1.')
        }

        foreach ($pattern in $documentedPackageVersionPatterns)
        {
            $versionMatch = [System.Text.RegularExpressions.Regex]::Match(
                $line,
                $pattern)
            if ($versionMatch.Success)
            {
                $documentedVersion = $versionMatch.Groups['version'].Value.Trim()
                if ($documentedVersion -ne $repositoryPackageVersion)
                {
                    Add-DocumentationFailure `
                        -Path $markdownPath `
                        -Line ($index + 1) `
                        -Message (
                            "Documented OpenUsd package version '$documentedVersion' does not " +
                            "match version.json ('$repositoryPackageVersion').")
                }
                break
            }
        }
    }

    if (-not (Test-IsDocumentationScope $markdownPath))
    {
        continue
    }

    foreach ($line in $outsideFenceLines)
    {
        $linkText = [System.Text.RegularExpressions.Regex]::Replace(
            $line.Text,
            '`+[^`]*`+',
            '')
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches(
            $linkText,
            $inlineLinkPattern))
        {
            Test-MarkdownLink `
                -MarkdownPath $markdownPath `
                -Line $line.Number `
                -Destination $match.Groups['destination'].Value
        }

        $definition = [System.Text.RegularExpressions.Regex]::Match(
            $linkText,
            $referenceDefinitionPattern)
        if ($definition.Success)
        {
            Test-MarkdownLink `
                -MarkdownPath $markdownPath `
                -Line $line.Number `
                -Destination $definition.Groups['destination'].Value
        }
    }

    Test-ReferencedRepositoryPaths -MarkdownPath $markdownPath -Lines $lines
}

if ($failures.Count -gt 0)
{
    $orderedFailures = [System.Collections.Generic.List[object]]::new()
    foreach ($failure in $failures)
    {
        [void]$orderedFailures.Add($failure)
    }
    $failureComparison = [System.Comparison[object]]{
        param($left, $right)

        $result = [System.StringComparer]::Ordinal.Compare(
            [string]$left.Path,
            [string]$right.Path)
        if ($result -ne 0)
        {
            return $result
        }

        $result = [int]$left.Line - [int]$right.Line
        if ($result -ne 0)
        {
            return $result
        }

        return [System.StringComparer]::Ordinal.Compare(
            [string]$left.Message,
            [string]$right.Message)
    }
    $orderedFailures.Sort($failureComparison)
    Write-Host (
        "[documentation] Validation failed with $($orderedFailures.Count) issue(s):") `
        -ForegroundColor Red
    foreach ($failure in $orderedFailures)
    {
        $location = if ($failure.Line -gt 0)
        {
            '{0}:{1}' -f $failure.Path, $failure.Line
        }
        else
        {
            $failure.Path
        }
        Write-Host ('  {0}: {1}' -f $location, $failure.Message) -ForegroundColor Red
    }
    exit 1
}

Write-Host (
    "[documentation] Passed: $($markdownFiles.Count) Markdown file(s), " +
    "$sampleProjectCount sample project(s).") -ForegroundColor Green
