// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
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

    /// <summary>
    /// Requires the capability mask the shim actually declares to equal the one
    /// recorded in <c>eng/openusd.lock.json</c>.
    /// </summary>
    /// <remarks>
    /// Both sides are read from the real artifacts -- the C header for the bit
    /// assignments, <c>common.h</c> for the combined expression, and the lock
    /// for the recorded value -- so this cannot agree with a stale restatement
    /// of the expectation.
    ///
    /// It exists because the mismatch is otherwise invisible until the native
    /// artifact pipeline runs, and that costs about ninety minutes and fails on
    /// all three RIDs at once. Bit 14 shipped as
    /// <c>OPENUSD_DOTNET_CAPABILITY_USD_SHADE_SKEL</c> while every other bit
    /// used the <c>OPENUSD_CAPABILITY_</c> prefix, so the metadata script's
    /// name pattern skipped it, the mask resolved to <c>0x3FFF</c> instead of
    /// <c>0x7FFF</c>, and nothing local noticed.
    /// </remarks>
    [Test]
    public async Task ShimCapabilityMaskMatchesTheLock()
    {
        string root = FindRepositoryRoot();
        string common = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_dotnet", "src", "internal", "common.h"));
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_dotnet", "include", "openusd_dotnet.h"));
        string lockText = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "openusd.lock.json"));

        Match expression = Regex.Match(
            common,
            @"DataCapabilities\s*=\s*(?<expression>.*?);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        await Assert.That(expression.Success)
            .IsTrue()
            .Because("common.h must declare the combined DataCapabilities expression");

        string[] operands = [.. expression.Groups["expression"].Value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        ulong mask = 0;
        List<string> unresolved = [];
        foreach (string operand in operands)
        {
            Match bit = Regex.Match(
                header,
                @"#define\s+" + Regex.Escape(operand) + @"\s+\(UINT64_C\(1\)\s*<<\s*(?<bit>\d+)\)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

            if (!bit.Success)
            {
                unresolved.Add(operand);
                continue;
            }

            mask |= 1UL << int.Parse(bit.Groups["bit"].Value, CultureInfo.InvariantCulture);
        }

        // Non-vacuity: an empty operand list would make the mask trivially zero.
        await Assert.That(operands.Length).IsGreaterThan(10);
        await Assert.That(unresolved)
            .IsEmpty()
            .Because(
                "every operand must resolve to a bit in openusd_dotnet.h, or " +
                "the computed mask silently omits it: " + string.Join(", ", unresolved));

        ulong recorded = ulong.Parse(
            Regex.Match(
                lockText,
                @"""dataCapabilities""\s*:\s*(?<value>\d+)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5)).Groups["value"].Value,
            CultureInfo.InvariantCulture);

        await Assert.That(mask)
            .IsEqualTo(recorded)
            .Because(
                $"the shim declares 0x{mask:X} but eng/openusd.lock.json records " +
                $"0x{recorded:X}, which fails the native pipeline on all three RIDs");
    }

    /// <summary>
    /// Requires the shim's data ABI version to equal the one in the lock.
    /// </summary>
    [Test]
    public async Task ShimDataAbiVersionMatchesTheLock()
    {
        string root = FindRepositoryRoot();
        string common = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_dotnet", "src", "internal", "common.h"));
        string lockText = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "openusd.lock.json"));

        Match declared = Regex.Match(
            common,
            @"DataAbiVersion\s*=\s*(?<value>\d+)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        Match recorded = Regex.Match(
            lockText,
            @"""data""\s*:\s*(?<value>\d+)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        await Assert.That(declared.Success).IsTrue();
        await Assert.That(recorded.Success).IsTrue();
        await Assert.That(declared.Groups["value"].Value)
            .IsEqualTo(recorded.Groups["value"].Value)
            .Because("a data ABI mismatch fails the native pipeline on all three RIDs");
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
