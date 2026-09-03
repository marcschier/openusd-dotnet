// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the production startup path -- <see cref="ViewerStartupOptions"/> itself, not
/// the pure resolver underneath it -- and asserts which pick target a viewport click
/// actually requests in each of the three configurations the Viewer ships: a standalone
/// command-line run with no embedding host, a host that configures nothing, and a host
/// that opts into following the operator.
/// </summary>
/// <remarks>
/// The regression these pin was invisible to the pure-policy tests: the resolver was
/// always correct, but the standalone command-line Viewer initialized the follow-viewer
/// mode to <see langword="false"/>, so <c>Tools &gt; Pick Target</c> was inert in the only
/// configuration that has no other way to state a target. Asserting the resolver alone
/// cannot see that, because the resolver never sees which initializer ran.
/// <para>
/// <see cref="ViewerStartupOptions"/> is process-wide, so these must not overlap with
/// anything else, and each restores the standalone default it found.
/// </para>
/// </remarks>
[NotInParallel]
public sealed class ViewerStartupPickTargetTests
{
    private static readonly RenderPickTarget[] AllTargets =
    [
        RenderPickTarget.Primitive,
        RenderPickTarget.Face,
        RenderPickTarget.Edge,
        RenderPickTarget.Point
    ];

    [Test]
    public async Task AStandaloneCommandLineViewerFollowsTheToolsPickTargetMenu()
    {
        try
        {
            ViewerStartupOptions.Initialize([]);

            await Assert.That(ViewerStartupOptions.HostFollowsViewerPickTarget).IsTrue();
            foreach (RenderPickTarget menuTarget in AllTargets)
            {
                await Assert.That(
                        ViewerStartupOptions.ResolveRequestedPickTarget(menuTarget))
                    .IsEqualTo(menuTarget)
                    .Because($"a standalone Viewer must honour Tools > Pick Target = {menuTarget}");
            }
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task AnEmbeddingHostThatConfiguresNothingKeepsTheFixedPrimitiveTarget()
    {
        try
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions());

            await Assert.That(ViewerStartupOptions.HostFollowsViewerPickTarget).IsFalse();
            await Assert.That(ViewerStartupOptions.HostPickTarget)
                .IsEqualTo(RenderPickTarget.Primitive);
            foreach (RenderPickTarget menuTarget in AllTargets)
            {
                await Assert.That(
                        ViewerStartupOptions.ResolveRequestedPickTarget(menuTarget))
                    .IsEqualTo(RenderPickTarget.Primitive)
                    .Because("the documented fixed default must survive every menu change");
            }
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task AnEmbeddingHostThatStatesPrimitiveExplicitlyIsStillFixed()
    {
        // The whole point of the separate mode: an explicit primitive request is a
        // concrete request, not the absence of one, and must not be confused with the
        // standalone no-host case that now follows the operator.
        try
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions
            {
                PickTarget = RenderPickTarget.Primitive,
            });

            await Assert.That(ViewerStartupOptions.HostFollowsViewerPickTarget).IsFalse();
            await Assert.That(
                    ViewerStartupOptions.ResolveRequestedPickTarget(RenderPickTarget.Face))
                .IsEqualTo(RenderPickTarget.Primitive);
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task AnEmbeddingHostThatFixesASubprimTargetKeepsItThroughTheMenu()
    {
        try
        {
            foreach (RenderPickTarget fixedTarget in new[]
            {
                RenderPickTarget.Face,
                RenderPickTarget.Edge,
                RenderPickTarget.Point
            })
            {
                ViewerStartupOptions.Initialize(new ViewerHostOptions
                {
                    PickTarget = fixedTarget,
                });

                foreach (RenderPickTarget menuTarget in AllTargets)
                {
                    await Assert.That(
                            ViewerStartupOptions.ResolveRequestedPickTarget(menuTarget))
                        .IsEqualTo(fixedTarget);
                }
            }
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task AnEmbeddingHostThatOptsIntoFollowViewerFollowsTheMenu()
    {
        try
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions
            {
                PickTarget = RenderPickTarget.Face,
                FollowViewerPickTarget = true,
            });

            await Assert.That(ViewerStartupOptions.HostFollowsViewerPickTarget).IsTrue();
            foreach (RenderPickTarget menuTarget in AllTargets)
            {
                await Assert.That(
                        ViewerStartupOptions.ResolveRequestedPickTarget(menuTarget))
                    .IsEqualTo(menuTarget)
                    .Because("FollowViewerPickTarget ignores the fixed target outright");
            }
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task AHostRunFollowedByACommandLineRunDoesNotLeakTheFixedTarget()
    {
        // Both initializers run in the same process in the Viewer's own tests and smoke
        // runs, so the standalone path must state the follow mode rather than rely on the
        // field's initial value.
        try
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions
            {
                PickTarget = RenderPickTarget.Edge,
            });
            await Assert.That(
                    ViewerStartupOptions.ResolveRequestedPickTarget(RenderPickTarget.Point))
                .IsEqualTo(RenderPickTarget.Edge);

            ViewerStartupOptions.Initialize([]);
            await Assert.That(
                    ViewerStartupOptions.ResolveRequestedPickTarget(RenderPickTarget.Point))
                .IsEqualTo(RenderPickTarget.Point);
            await Assert.That(ViewerStartupOptions.HostPickTarget)
                .IsEqualTo(ViewerPickTargetPolicy.DefaultTarget);
        }
        finally
        {
            ViewerStartupOptions.ResetHostPickTarget();
        }
    }

    [Test]
    public async Task TheViewportPickPathGoesThroughTheStartupSeam()
    {
        // A second call site that resolved the mode itself is exactly how the standalone
        // and embedded paths drifted apart, so the production pick path must consult the
        // one seam rather than reassemble the decision.
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));

        await Assert.That(window)
            .Contains("ViewerStartupOptions.ResolveRequestedPickTarget(PickTarget)");
        await Assert.That(window)
            .DoesNotContain("ViewerPickTargetPolicy.ResolveHostRequestedTarget(");
    }

    [Test]
    public async Task TheCommandLineInitializerStatesTheFollowViewerMode()
    {
        string root = FindRepositoryRoot();
        string startup = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerStartupOptions.cs"));

        int commandLine = startup.IndexOf(
            "internal static void Initialize(string[] args)",
            StringComparison.Ordinal);
        await Assert.That(commandLine).IsGreaterThan(0);
        int hostOverload = startup.IndexOf(
            "internal static void Initialize(ViewerHostOptions options)",
            StringComparison.Ordinal);
        await Assert.That(hostOverload).IsGreaterThan(commandLine);

        string commandLineBody = startup[commandLine..hostOverload];
        await Assert.That(commandLineBody).Contains("ResetHostPickTarget();");
        await Assert.That(commandLineBody)
            .DoesNotContain("HostFollowsViewerPickTarget = false;");

        // The host overload states both values after the environment defaults ran, so a
        // host that configures nothing keeps the documented fixed primitive request.
        string hostBody = startup[hostOverload..];
        int reset = hostBody.IndexOf("Initialize([]);", StringComparison.Ordinal);
        int follow = hostBody.IndexOf(
            "HostFollowsViewerPickTarget = options.FollowViewerPickTarget;",
            StringComparison.Ordinal);
        await Assert.That(reset).IsGreaterThan(0);
        await Assert.That(follow).IsGreaterThan(reset);
        await Assert.That(hostBody).Contains("HostPickTarget = options.PickTarget;");
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
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
