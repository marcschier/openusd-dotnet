// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed record ParityExecutionIdentity(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("checkoutCommit")] string? CheckoutCommit,
    [property: JsonPropertyName("checkoutDirty")] bool? CheckoutDirty,
    [property: JsonPropertyName("triggerCommit")] string? TriggerCommit,
    [property: JsonPropertyName("repository")] string? Repository,
    [property: JsonPropertyName("runId")] string? RunId,
    [property: JsonPropertyName("runAttempt")] int? RunAttempt,
    [property: JsonPropertyName("job")] string? Job)
{
    [JsonPropertyName("runtimeIdentifier")]
    public string RuntimeIdentifier { get; } = RuntimeInformation.RuntimeIdentifier;

    [JsonPropertyName("framework")]
    public string Framework { get; } = RuntimeInformation.FrameworkDescription;

    [JsonPropertyName("checkoutStatus")]
    public string CheckoutStatus => CheckoutCommit is null || CheckoutDirty is null
        ? "unavailable"
        : CheckoutDirty.Value ? "modified" : "identified";

    internal static ParityExecutionIdentity Read(Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        string? checkout = ReadCommit(readEnvironment, "OPENUSD_PARITY_SOURCE_COMMIT");
        string? dirtyText = readEnvironment("OPENUSD_PARITY_SOURCE_DIRTY");
        bool? dirty = null;
        if (!string.IsNullOrEmpty(dirtyText))
        {
            if (!bool.TryParse(dirtyText, out bool value))
            {
                throw Invalid("OPENUSD_PARITY_SOURCE_DIRTY");
            }
            dirty = value;
        }
        if ((checkout is null) != (dirty is null))
        {
            throw new InvalidOperationException(
                "Parity checkout evidence requires both OPENUSD_PARITY_SOURCE_COMMIT and OPENUSD_PARITY_SOURCE_DIRTY.");
        }

        bool hosted = string.Equals(
            readEnvironment("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
        if (!hosted)
        {
            return new ParityExecutionIdentity("local", checkout, dirty, null, null, null, null, null);
        }

        string repository = Required(readEnvironment, "GITHUB_REPOSITORY");
        string runId = Required(readEnvironment, "GITHUB_RUN_ID");
        if (!long.TryParse(runId, NumberStyles.None, CultureInfo.InvariantCulture, out long run) || run <= 0)
        {
            throw Invalid("GITHUB_RUN_ID");
        }
        string attemptText = Required(readEnvironment, "GITHUB_RUN_ATTEMPT");
        if (!int.TryParse(attemptText, NumberStyles.None, CultureInfo.InvariantCulture, out int attempt) ||
            attempt <= 0)
        {
            throw Invalid("GITHUB_RUN_ATTEMPT");
        }

        return new ParityExecutionIdentity(
            "github-actions",
            checkout,
            dirty,
            ReadCommit(readEnvironment, "GITHUB_SHA"),
            repository,
            runId,
            attempt,
            Required(readEnvironment, "GITHUB_JOB"));
    }

    private static string Required(Func<string, string?> readEnvironment, string name)
    {
        string? value = readEnvironment(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(name);
        }
        return value;
    }

    private static string? ReadCommit(Func<string, string?> readEnvironment, string name)
    {
        string? value = readEnvironment(name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (value.Length is not (40 or 64) || !value.All(char.IsAsciiHexDigit))
        {
            throw Invalid(name);
        }
        return value.ToLowerInvariant();
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"Parity execution evidence has a missing or invalid {name}.");
}
