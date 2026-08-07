// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Guards structural properties of the workflow files that nothing else can
/// see until a release run reaches the job in question.
/// </summary>
/// <remarks>
/// Shipping 0.5.0-alpha took six release runs. Four of the five failures were
/// the first time the failing gate had ever executed, because
/// <c>package.yml</c> and <c>release.yml</c> only run from a release and a
/// release is the most expensive way to discover anything: each attempt costs
/// hours, and the fix cannot be verified except by another release.
///
/// Two of those failures are structural rather than behavioural, so they can be
/// caught here in milliseconds:
///
/// The publish job gained a step that reads <c>eng/pack-packages.ps1</c>, and
/// that job had never needed a checkout because publishing consumes only
/// artifacts and credentials. It failed with "The term './eng/pack-packages.ps1'
/// is not recognized" after every artifact in the release had already been
/// built. actionlint does not model this, and no amount of local testing would,
/// because locally the file is always present.
///
/// And the package execution gates ran nowhere but a release, which is what
/// made the other three defects expensive. That is now a
/// <c>workflow_run</c> trigger, and this pins it so it cannot quietly revert to
/// release-only.
///
/// Both properties are read out of the workflow files rather than restated, so
/// this cannot pass against a stale copy of the expectation.
/// </remarks>
public sealed class WorkflowStructureContractTests
{
    /// <summary>Matches a job header: two spaces, a name, a colon, nothing else.</summary>
    private static readonly Regex JobHeader = new(
        @"^  (?<name>[A-Za-z0-9_-]+):\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Matches an invocation of a repository script from a run block.</summary>
    private static readonly Regex RepositoryScript = new(
        @"(\./)?eng/[A-Za-z0-9._/-]+\.(ps1|py|sh)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public async Task EveryJobRunningARepositoryScriptChecksOutTheRepository()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];
        int scriptJobs = 0;

        foreach (string workflow in Directory.EnumerateFiles(
            Path.Combine(root, ".github", "workflows"),
            "*.yml"))
        {
            string name = Path.GetFileName(workflow);
            foreach ((string job, string body) in ReadJobs(
                await File.ReadAllTextAsync(workflow)))
            {
                // Jobs that delegate to a reusable workflow have no steps of
                // their own, so the checkout belongs to the callee.
                if (!body.Contains("steps:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!RepositoryScript.IsMatch(body))
                {
                    continue;
                }

                scriptJobs++;
                if (!body.Contains("actions/checkout@", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}:{job}");
                }
            }
        }

        // Non-vacuity: a parser that stops recognising jobs would report no
        // offenders while checking nothing at all.
        await Assert.That(scriptJobs)
            .IsGreaterThan(4)
            .Because("the workflows must still be parseable into jobs that run eng/ scripts");
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "these jobs invoke a repository script without checking the " +
                "repository out, which fails only when the job actually runs: " +
                string.Join(", ", offenders));
    }

    [Test]
    public async Task PackageGatesRunOutsideARelease()
    {
        string root = FindRepositoryRoot();
        string package = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "package.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        string triggers = ReadTriggerBlock(package);
        await Assert.That(triggers).IsNotEmpty();

        // workflow_call and workflow_dispatch both require someone to ask. Only
        // a self-firing trigger makes these gates run without a release.
        bool selfFiring =
            triggers.Contains("workflow_run:", StringComparison.Ordinal) ||
            Regex.IsMatch(triggers, @"^\s{2}push:", RegexOptions.Multiline) ||
            Regex.IsMatch(triggers, @"^\s{2}pull_request:", RegexOptions.Multiline);

        await Assert.That(selfFiring)
            .IsTrue()
            .Because(
                "package.yml carries the package-only execution gates; with only " +
                "workflow_call and workflow_dispatch they run once per release, " +
                "which is how three of them shipped having never executed");

        // The workflow_run form names its upstream workflow by title, and a
        // renamed upstream silently never fires.
        Match named = Regex.Match(
            triggers,
            @"workflows:\s*\[\s*'(?<title>[^']+)'\s*\]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        if (named.Success)
        {
            Match upstream = Regex.Match(
                native,
                @"^name:\s*(?<title>.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));
            await Assert.That(upstream.Success).IsTrue();
            await Assert.That(named.Groups["title"].Value)
                .IsEqualTo(upstream.Groups["title"].Value)
                .Because(
                    "package.yml triggers on the native pipeline by title, and a " +
                    "title that no longer matches never fires and never reports why");
        }
    }

    [Test]
    public async Task PackageWorkflowRestoresTheCacheTheNativePipelineSaves()
    {
        string root = FindRepositoryRoot();
        string package = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "package.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        const string prefix = "native-${{ matrix.rid }}-";
        IReadOnlyList<string> saved = HashFileInputs(native, prefix);
        IReadOnlyList<string> restored = HashFileInputs(package, prefix);

        // Non-vacuity: two empty lists compare equal and would prove nothing.
        await Assert.That(saved.Count)
            .IsGreaterThan(20)
            .Because("native.yml must still key its install cache on hashFiles");
        await Assert.That(restored.Count)
            .IsGreaterThan(20)
            .Because("package.yml must still restore that cache rather than rebuilding");
        await Assert.That(restored)
            .IsEquivalentTo(saved)
            .Because(
                "hashFiles over a different file list yields a different digest, " +
                "so the restore would silently never hit and every packaging push " +
                "would rebuild OpenUSD from source on all three RIDs");

        // The producer saves; the consumer must not, or it can write a smaller
        // archive under the producer's key. actions/cache never overwrites, so
        // native.yml would restore a partial tree from then on.
        await Assert.That(package)
            .Contains("actions/cache/restore@", StringComparison.Ordinal)
            .Because("package.yml consumes the native install and must not save it");
    }

    /// <summary>
    /// Returns the quoted arguments of the <c>hashFiles</c> call anchored on a
    /// cache key prefix, in declaration order.
    /// </summary>
    private static IReadOnlyList<string> HashFileInputs(string workflow, string prefix)
    {
        Match anchor = Regex.Match(
            workflow,
            Regex.Escape(prefix) + @"\$\{\{\s*hashFiles\((?<args>[^)]*)\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        return anchor.Success
            ? [.. Regex.Matches(
                anchor.Groups["args"].Value,
                @"'(?<file>[^']+)'",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5))
                .Select(match => match.Groups["file"].Value)]
            : [];
    }

    /// <summary>Splits a workflow into its jobs, keyed by job id.</summary>
    private static IEnumerable<(string Name, string Body)> ReadJobs(string workflow)
    {
        string[] lines = workflow.Split('\n');
        int jobsAt = Array.FindIndex(
            lines,
            line => line.StartsWith("jobs:", StringComparison.Ordinal));
        if (jobsAt < 0)
        {
            yield break;
        }

        string? current = null;
        List<string> body = [];
        for (int index = jobsAt + 1; index < lines.Length; index++)
        {
            Match header = JobHeader.Match(lines[index].TrimEnd('\r'));
            if (header.Success)
            {
                if (current is not null)
                {
                    yield return (current, string.Join("\n", body));
                }

                current = header.Groups["name"].Value;
                body = [];
                continue;
            }

            body.Add(lines[index]);
        }

        if (current is not null)
        {
            yield return (current, string.Join("\n", body));
        }
    }

    /// <summary>Returns the <c>on:</c> block, up to the next top-level key.</summary>
    private static string ReadTriggerBlock(string workflow)
    {
        string[] lines = workflow.Split('\n');
        int start = Array.FindIndex(
            lines,
            line => line.StartsWith("on:", StringComparison.Ordinal));
        if (start < 0)
        {
            return string.Empty;
        }

        List<string> block = [];
        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            block.Add(line);
        }

        return string.Join("\n", block);
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
