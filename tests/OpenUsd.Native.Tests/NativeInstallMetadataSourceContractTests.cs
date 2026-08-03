// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Guards the source paths that <c>eng/native-install-metadata.ps1</c> reads
/// against being moved or renamed.
/// </summary>
/// <remarks>
/// That script resolves several ABI version constants by reading specific
/// source files by path. Nothing in an ordinary build touches it: it runs only
/// in the "Validate archive producer and consumer" step of the native artifact
/// pipeline. So when the data ABI implementation was split from a single
/// 11,858-line <c>openusd_dotnet.cpp</c> into per-area translation units, the
/// script kept pointing at a file that no longer existed and every RID of the
/// native pipeline failed with "Cannot find path ... openusd_dotnet.cpp".
///
/// The local <c>cmake</c> build was green, the managed suites were green, and
/// the break surfaced only after a full hosted native run. This test moves that
/// discovery from a ninety-minute pipeline to a few milliseconds.
///
/// It checks two things, because either alone is insufficient: that each
/// referenced path exists, and that the pattern the script searches for
/// actually matches something in it. A file that exists but no longer declares
/// the constant would still fail the pipeline while passing a mere existence
/// check.
/// </remarks>
public sealed class NativeInstallMetadataSourceContractTests
{
    [Test]
    public async Task EveryPathTheMetadataScriptReadsExists()
    {
        string root = FindRepositoryRoot();
        string script = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "native-install-metadata.ps1"));

        List<string> missing = [];
        int referenceCount = 0;

        foreach (Match match in Regex.Matches(
            script,
            @"Join-Path\s+\$repoRoot\s+'(?<path>native/[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)))
        {
            referenceCount++;
            string relative = match.Groups["path"].Value;
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                missing.Add(relative);
            }
        }

        // Non-vacuity: if the regex stops matching, this test would pass while
        // checking nothing at all.
        await Assert.That(referenceCount).IsGreaterThan(5);
        await Assert.That(missing)
            .IsEmpty()
            .Because(
                "eng/native-install-metadata.ps1 reads these paths, and the " +
                "native artifact pipeline fails on every RID when one is " +
                "missing: " + string.Join(", ", missing));
    }

    [Test]
    public async Task EveryAbiVersionPatternStillMatchesItsSource()
    {
        string root = FindRepositoryRoot();
        string script = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "native-install-metadata.ps1"));

        // Each Get-SourceAbiVersion call pairs a -Path variable with a -Pattern.
        // Resolve the variable to its Join-Path literal, then confirm the
        // pattern matches inside that file.
        Dictionary<string, string> pathVariables = [];
        foreach (Match assignment in Regex.Matches(
            script,
            @"\$(?<name>\w+)\s*=\s*Join-Path\s+\$repoRoot\s+'(?<path>native/[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)))
        {
            pathVariables[assignment.Groups["name"].Value] = assignment.Groups["path"].Value;
        }

        List<string> unmatched = [];
        int checkedCount = 0;

        foreach (Match call in Regex.Matches(
            script,
            @"-Path\s+\$(?<name>\w+)\s*`?\s*[\r\n]+\s*-Pattern\s+'(?<pattern>[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)))
        {
            string name = call.Groups["name"].Value;
            if (!pathVariables.TryGetValue(name, out string? relative))
            {
                continue;
            }

            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                continue;
            }

            checkedCount++;
            string content = await File.ReadAllTextAsync(full);
            if (!Regex.IsMatch(
                content,
                call.Groups["pattern"].Value,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5)))
            {
                string pattern = call.Groups["pattern"].Value;
                unmatched.Add($"{relative} does not match '{pattern}'");
            }
        }

        await Assert.That(checkedCount).IsGreaterThan(2);
        await Assert.That(unmatched)
            .IsEmpty()
            .Because(
                "eng/native-install-metadata.ps1 extracts ABI versions with " +
                "these patterns, and a pattern that no longer matches makes " +
                "the native pipeline fail: " + string.Join("; ", unmatched));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }
}
