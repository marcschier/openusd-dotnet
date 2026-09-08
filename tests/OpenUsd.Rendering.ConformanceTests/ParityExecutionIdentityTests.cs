// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class ParityExecutionIdentityTests
{
    [Test]
    public async Task HostedEvidenceKeepsAttemptAndCheckoutSeparateFromTheTriggerCommit()
    {
        Dictionary<string, string> environment = HostedEnvironment();

        ParityExecutionIdentity identity = ParityExecutionIdentity.Read(
            name => environment.GetValueOrDefault(name));

        await Assert.That(identity.Kind).IsEqualTo("github-actions");
        await Assert.That(identity.Repository).IsEqualTo("example/rendering");
        await Assert.That(identity.RunId).IsEqualTo("123456");
        await Assert.That(identity.RunAttempt).IsEqualTo(2);
        await Assert.That(identity.Job).IsEqualTo("render");
        await Assert.That(identity.CheckoutCommit).IsEqualTo(new string('a', 40));
        await Assert.That(identity.TriggerCommit).IsEqualTo(new string('b', 40));
        await Assert.That(identity.CheckoutDirty).IsFalse();
        await Assert.That(identity.CheckoutStatus).IsEqualTo("identified");
    }

    [Test]
    public async Task ALocalRunCannotAcquireAnInventedCiRunOrCheckout()
    {
        ParityExecutionIdentity identity = ParityExecutionIdentity.Read(static _ => null);

        await Assert.That(identity.Kind).IsEqualTo("local");
        await Assert.That(identity.RunId).IsNull();
        await Assert.That(identity.RunAttempt).IsNull();
        await Assert.That(identity.Repository).IsNull();
        await Assert.That(identity.CheckoutCommit).IsNull();
        await Assert.That(identity.CheckoutDirty).IsNull();
        await Assert.That(identity.CheckoutStatus).IsEqualTo("unavailable");
    }

    [Test]
    public async Task ATriggerCommitNeverSubstitutesForAnUnknownCheckout()
    {
        Dictionary<string, string> environment = HostedEnvironment();
        environment.Remove("OPENUSD_PARITY_SOURCE_COMMIT");
        environment.Remove("OPENUSD_PARITY_SOURCE_DIRTY");

        ParityExecutionIdentity identity = ParityExecutionIdentity.Read(
            name => environment.GetValueOrDefault(name));

        await Assert.That(identity.TriggerCommit).IsEqualTo(new string('b', 40));
        await Assert.That(identity.CheckoutCommit).IsNull();
        await Assert.That(identity.CheckoutStatus).IsEqualTo("unavailable");
        await Assert.That(identity.RunAttempt).IsEqualTo(2);
    }

    [Test]
    public async Task WorkingTreeChangesRemainVisibleInTheProvenance()
    {
        Dictionary<string, string> environment = HostedEnvironment();
        environment["OPENUSD_PARITY_SOURCE_DIRTY"] = "true";

        ParityExecutionIdentity identity = ParityExecutionIdentity.Read(
            name => environment.GetValueOrDefault(name));

        await Assert.That(identity.CheckoutCommit).IsEqualTo(new string('a', 40));
        await Assert.That(identity.CheckoutDirty).IsTrue();
        await Assert.That(identity.CheckoutStatus).IsEqualTo("modified");
    }

    [Test]
    [Arguments("GITHUB_RUN_ATTEMPT", "")]
    [Arguments("GITHUB_RUN_ATTEMPT", "0")]
    [Arguments("GITHUB_RUN_ATTEMPT", "-1")]
    [Arguments("GITHUB_RUN_ATTEMPT", "2147483648")]
    [Arguments("GITHUB_RUN_ID", "0")]
    [Arguments("GITHUB_RUN_ID", "not-a-run")]
    [Arguments("GITHUB_REPOSITORY", "")]
    [Arguments("GITHUB_JOB", "")]
    [Arguments("GITHUB_SHA", "not-a-commit")]
    [Arguments("OPENUSD_PARITY_SOURCE_COMMIT", "")]
    [Arguments("OPENUSD_PARITY_SOURCE_DIRTY", "")]
    [Arguments("OPENUSD_PARITY_SOURCE_DIRTY", "maybe")]
    public async Task IncompleteOrInvalidHostedCoordinatesAreRejected(string name, string value)
    {
        Dictionary<string, string> environment = HostedEnvironment();
        environment[name] = value;

        await Assert.That(() => ParityExecutionIdentity.Read(
            key => environment.GetValueOrDefault(key))).Throws<InvalidOperationException>();
    }

    private static Dictionary<string, string> HostedEnvironment() => new()
    {
        ["GITHUB_ACTIONS"] = "true",
        ["GITHUB_REPOSITORY"] = "example/rendering",
        ["GITHUB_RUN_ID"] = "123456",
        ["GITHUB_RUN_ATTEMPT"] = "2",
        ["GITHUB_JOB"] = "render",
        ["GITHUB_SHA"] = new string('b', 40),
        ["OPENUSD_PARITY_SOURCE_COMMIT"] = new string('a', 40),
        ["OPENUSD_PARITY_SOURCE_DIRTY"] = "false"
    };
}
