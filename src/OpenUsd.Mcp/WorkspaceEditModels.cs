// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Identifies an edit supported by the transactional workspace.</summary>
public enum WorkspaceEditKind
{
    /// <summary>Defines a prim.</summary>
    DefinePrim,

    /// <summary>Authors a prim active-state opinion in the session overlay.</summary>
    SetActive,

    /// <summary>Authors a double attribute.</summary>
    SetDouble,

    /// <summary>Clears only the attribute value opinion authored in the session overlay.</summary>
    ClearOverlayAttribute,

    /// <summary>Legacy proposal operation interpreted as deactivation, never removal.</summary>
    RemovePrim,

    /// <summary>Legacy proposal operation that clears only the overlay-authored opinion.</summary>
    ClearAttribute
}

/// <summary>Base class for closed, typed workspace edit operations.</summary>
public abstract record WorkspaceEditOperation
{
    private protected WorkspaceEditOperation(WorkspaceEditKind kind, string primPath)
    {
        Kind = kind;
        PrimPath = primPath;
    }

    /// <summary>Gets the operation kind.</summary>
    public WorkspaceEditKind Kind { get; }

    /// <summary>Gets the target prim path.</summary>
    public string PrimPath { get; }

    internal virtual void Validate()
    {
        WorkspaceEditValidation.ValidatePrimPath(PrimPath, nameof(PrimPath));
    }
}

/// <summary>Defines a prim in the session overlay.</summary>
public sealed record DefinePrimWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a define-prim edit.</summary>
    public DefinePrimWorkspaceEdit(string primPath, string? typeName = null)
        : base(WorkspaceEditKind.DefinePrim, primPath)
    {
        if (typeName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        }

        TypeName = typeName;
    }

    /// <summary>Gets the optional authored type name.</summary>
    public string? TypeName { get; }

    internal override void Validate()
    {
        base.Validate();
        if (TypeName is not null)
        {
            WorkspaceEditValidation.ValidateIdentifier(TypeName, nameof(TypeName));
        }
    }
}

/// <summary>Authors a prim active-state opinion in the session overlay.</summary>
public sealed record SetActiveWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes an active-state edit.</summary>
    public SetActiveWorkspaceEdit(string primPath, bool active)
        : base(WorkspaceEditKind.SetActive, primPath)
    {
        Active = active;
    }

    /// <summary>Gets the active state to author.</summary>
    public bool Active { get; }
}

/// <summary>Authors a double attribute in the session overlay.</summary>
public sealed record SetDoubleWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a double-attribute edit.</summary>
    public SetDoubleWorkspaceEdit(
        string primPath,
        string attributeName,
        double value,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetDouble, primPath)
    {
        AttributeName = attributeName;
        Value = value;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored value.</summary>
    public double Value { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate()
    {
        base.Validate();
        WorkspaceEditValidation.ValidatePropertyName(AttributeName, nameof(AttributeName));
        if (!double.IsFinite(Value))
        {
            throw new ArgumentOutOfRangeException(nameof(Value), "The value must be finite.");
        }

        if (TimeCode is double timeCode && !double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(TimeCode), "The time code must be finite.");
        }
    }
}

/// <summary>
/// Clears only the attribute value opinion authored in the session overlay.
/// </summary>
/// <remarks>A value authored by a weaker source layer becomes visible again.</remarks>
public sealed record ClearOverlayAttributeWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes an overlay-attribute-opinion clear.</summary>
    public ClearOverlayAttributeWorkspaceEdit(string primPath, string attributeName)
        : base(WorkspaceEditKind.ClearOverlayAttribute, primPath)
    {
        AttributeName = attributeName;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    internal override void Validate()
    {
        base.Validate();
        WorkspaceEditValidation.ValidatePropertyName(AttributeName, nameof(AttributeName));
    }
}

// Kept internal until proposal payloads migrate to the explicit public operation names.
internal sealed record RemovePrimWorkspaceEdit : WorkspaceEditOperation
{
    internal RemovePrimWorkspaceEdit(string primPath)
        : base(WorkspaceEditKind.RemovePrim, primPath)
    {
    }
}

internal sealed record ClearAttributeWorkspaceEdit : WorkspaceEditOperation
{
    internal ClearAttributeWorkspaceEdit(string primPath, string attributeName)
        : base(WorkspaceEditKind.ClearAttribute, primPath)
    {
        AttributeName = attributeName;
    }

    internal string AttributeName { get; }

    internal override void Validate()
    {
        base.Validate();
        WorkspaceEditValidation.ValidatePropertyName(AttributeName, nameof(AttributeName));
    }
}

/// <summary>Contains one bounded atomic edit request.</summary>
public sealed class WorkspaceEditBatch
{
    private readonly WorkspaceEditOperation[] _operations;

    /// <summary>Initializes an edit batch.</summary>
    public WorkspaceEditBatch(IEnumerable<WorkspaceEditOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations.ToArray();
    }

    /// <summary>Gets the requested operations.</summary>
    public IReadOnlyList<WorkspaceEditOperation> Operations => _operations;

    internal void Validate(int maximumOperationCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOperationCount);
        if (_operations.Length == 0 || _operations.Length > maximumOperationCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOperationCount),
                $"A batch must contain between 1 and {maximumOperationCount} operations.");
        }

        foreach (WorkspaceEditOperation? operation in _operations)
        {
            if (operation is null)
            {
                throw new ArgumentException(
                    "Edit batches cannot contain null operations.",
                    nameof(maximumOperationCount));
            }

            operation.Validate();
        }
    }
}

internal static class WorkspaceEditValidation
{
    internal static void ValidatePrimPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path[0] != '/' ||
            path.Length == 1 ||
            path.EndsWith('/') ||
            path.Contains("//", StringComparison.Ordinal) ||
            path.Contains('\\'))
        {
            throw new ArgumentException("A target must be an absolute USD prim path.", parameterName);
        }

        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ValidateIdentifier(segment, parameterName);
        }
    }

    internal static void ValidatePropertyName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
        foreach (string component in name.Split(':'))
        {
            ValidateIdentifier(component, parameterName);
        }
    }

    internal static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!(char.IsAsciiLetter(value[0]) || value[0] == '_') ||
            value.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("The value is not a valid USD identifier.", parameterName);
        }
    }
}

/// <summary>Reports an edit operation that the backend cannot implement.</summary>
public sealed class WorkspaceEditUnsupportedException : NotSupportedException
{
    /// <summary>Initializes an unsupported-edit error.</summary>
    public WorkspaceEditUnsupportedException(WorkspaceEditKind kind)
        : base($"Workspace edit kind '{kind}' is not supported.")
    {
        Kind = kind;
    }

    /// <summary>Gets the unsupported operation kind.</summary>
    public WorkspaceEditKind Kind { get; }
}
