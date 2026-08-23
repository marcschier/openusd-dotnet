// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the severity of a <see cref="UsdPhysicsDiagnostic"/>.
/// </summary>
public enum UsdPhysicsDiagnosticSeverity
{
    /// <summary>Informational diagnostic with no impact on behavior.</summary>
    Information,

    /// <summary>A recoverable condition; the affected feature degrades or is skipped.</summary>
    Warning,

    /// <summary>An operation failed or could not produce a result.</summary>
    Error
}

/// <summary>
/// Identifies the <see cref="UsdPhysicsSession"/> operation that produced a diagnostic.
/// </summary>
public enum UsdPhysicsDiagnosticCategory
{
    /// <summary>General session operation not covered by a more specific category.</summary>
    General,

    /// <summary>Capability negotiation or an unavailable optional runtime feature.</summary>
    Capability,

    /// <summary>Stage extraction or world construction during <see cref="UsdPhysicsSession.BuildAsync"/>.</summary>
    Build,

    /// <summary>World reconstruction during <see cref="UsdPhysicsSession.ResetAsync"/>.</summary>
    Reset,

    /// <summary>Time-code seeking during <see cref="UsdPhysicsSession.SeekAsync"/>.</summary>
    Seek,

    /// <summary>Fixed simulation stepping.</summary>
    Step,

    /// <summary>Submitted force, impulse, teleport, or control commands.</summary>
    Command,

    /// <summary>Batched raycast, sweep, or overlap scene queries.</summary>
    Query,

    /// <summary>Contact, trigger, sleep, or joint-break events.</summary>
    Event,

    /// <summary>Baking simulation results into a file-backed animation layer.</summary>
    Bake,

    /// <summary>Authored schema validation, precedence, or translation.</summary>
    Schema
}

/// <summary>
/// Describes one immutable <see cref="UsdPhysicsSession"/> diagnostic.
/// </summary>
public sealed record UsdPhysicsDiagnostic(
    UsdPhysicsDiagnosticSeverity Severity,
    UsdPhysicsDiagnosticCategory Category,
    string Code,
    string Message,
    UsdPhysicsObjectId? ObjectId = null) : IUsdDetachedResult
{
    /// <summary>
    /// Gets the short, stable diagnostic code (for example <c>OPENUSD_PHYSICS_BACKEND_UNAVAILABLE</c>).
    /// </summary>
    public string Code { get; } = ValidateNotBlank(Code, nameof(Code));

    /// <summary>Gets the human-readable diagnostic message.</summary>
    public string Message { get; } = ValidateNotBlank(Message, nameof(Message));

    private static string ValidateNotBlank(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}

/// <summary>
/// Contains an immutable ordered set of <see cref="UsdPhysicsSession"/> diagnostics.
/// </summary>
public sealed class UsdPhysicsDiagnostics : IUsdDetachedResult, IEquatable<UsdPhysicsDiagnostics>
{
    private readonly ImmutableArray<UsdPhysicsDiagnostic> _entries;

    /// <summary>Gets an empty diagnostic set.</summary>
    public static UsdPhysicsDiagnostics Empty { get; } = new([]);

    /// <summary>Initializes diagnostics by defensively copying entries.</summary>
    public UsdPhysicsDiagnostics(IEnumerable<UsdPhysicsDiagnostic> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = ImmutableArray.CreateBuilder<UsdPhysicsDiagnostic>();
        foreach (UsdPhysicsDiagnostic entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            builder.Add(entry);
        }
        _entries = builder.ToImmutable();
    }

    /// <summary>Gets diagnostic entries in stable order.</summary>
    public IReadOnlyList<UsdPhysicsDiagnostic> Entries => _entries;

    /// <summary>
    /// Gets a value indicating whether any entry has <see cref="UsdPhysicsDiagnosticSeverity.Error"/>.
    /// </summary>
    public bool HasErrors
    {
        get
        {
            foreach (UsdPhysicsDiagnostic entry in _entries)
            {
                if (entry.Severity == UsdPhysicsDiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsDiagnostics? other) =>
        other is not null && _entries.AsSpan().SequenceEqual(other._entries.AsSpan());

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsDiagnostics other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (UsdPhysicsDiagnostic entry in _entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two diagnostic sets have equal entries.</summary>
    public static bool operator ==(UsdPhysicsDiagnostics? left, UsdPhysicsDiagnostics? right) =>
        EqualityComparer<UsdPhysicsDiagnostics>.Default.Equals(left, right);

    /// <summary>Determines whether two diagnostic sets have different entries.</summary>
    public static bool operator !=(UsdPhysicsDiagnostics? left, UsdPhysicsDiagnostics? right) =>
        !(left == right);
}
