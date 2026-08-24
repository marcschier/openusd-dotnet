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

    /// <summary>Authors a Boolean attribute.</summary>
    SetBool,

    /// <summary>Authors a signed 64-bit integer attribute.</summary>
    SetInt64,

    /// <summary>Authors a string attribute.</summary>
    SetString,

    /// <summary>Authors a token attribute.</summary>
    SetToken,

    /// <summary>Authors a float3 vector attribute.</summary>
    SetFloat3,

    /// <summary>Authors a color3f attribute.</summary>
    SetColor3f,

    /// <summary>Clears only the attribute value opinion authored in the session overlay.</summary>
    ClearOverlayAttribute,

    /// <summary>Legacy proposal operation interpreted as deactivation, never removal.</summary>
    RemovePrim,

    /// <summary>Legacy proposal operation that clears only the overlay-authored opinion.</summary>
    ClearAttribute
}

/// <summary>Authors a Boolean attribute in the session overlay.</summary>
public sealed record SetBoolWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a Boolean-attribute edit.</summary>
    public SetBoolWorkspaceEdit(
        string primPath,
        string attributeName,
        bool value,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetBool, primPath)
    {
        AttributeName = attributeName;
        Value = value;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored value.</summary>
    public bool Value { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate() =>
        ValidateAttribute(AttributeName, TimeCode);
}

/// <summary>Authors a signed 64-bit integer attribute in the session overlay.</summary>
public sealed record SetInt64WorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes an integer-attribute edit.</summary>
    public SetInt64WorkspaceEdit(
        string primPath,
        string attributeName,
        long value,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetInt64, primPath)
    {
        AttributeName = attributeName;
        Value = value;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored value.</summary>
    public long Value { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate() =>
        ValidateAttribute(AttributeName, TimeCode);
}

/// <summary>Authors a string attribute in the session overlay.</summary>
public sealed record SetStringWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a string-attribute edit.</summary>
    public SetStringWorkspaceEdit(
        string primPath,
        string attributeName,
        string value,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetString, primPath)
    {
        AttributeName = attributeName;
        Value = value;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored value.</summary>
    public string Value { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate()
    {
        ValidateAttribute(AttributeName, TimeCode);
        WorkspaceEditValidation.ValidateTextValue(Value, nameof(Value));
    }
}

/// <summary>Authors a token attribute in the session overlay.</summary>
public sealed record SetTokenWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a token-attribute edit.</summary>
    public SetTokenWorkspaceEdit(
        string primPath,
        string attributeName,
        string value,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetToken, primPath)
    {
        AttributeName = attributeName;
        Value = value;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored value.</summary>
    public string Value { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate()
    {
        ValidateAttribute(AttributeName, TimeCode);
        WorkspaceEditValidation.ValidateTextValue(Value, nameof(Value));
    }
}

/// <summary>Authors a float3 vector attribute in the session overlay.</summary>
public sealed record SetFloat3WorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a float3-attribute edit.</summary>
    public SetFloat3WorkspaceEdit(
        string primPath,
        string attributeName,
        float x,
        float y,
        float z,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetFloat3, primPath)
    {
        AttributeName = attributeName;
        X = x;
        Y = y;
        Z = z;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored X component.</summary>
    public float X { get; }

    /// <summary>Gets the authored Y component.</summary>
    public float Y { get; }

    /// <summary>Gets the authored Z component.</summary>
    public float Z { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate()
    {
        ValidateAttribute(AttributeName, TimeCode);
        WorkspaceEditValidation.ValidateFloat3(X, Y, Z);
    }
}

/// <summary>Authors a color3f attribute in the session overlay.</summary>
public sealed record SetColor3fWorkspaceEdit : WorkspaceEditOperation
{
    /// <summary>Initializes a color3f-attribute edit.</summary>
    public SetColor3fWorkspaceEdit(
        string primPath,
        string attributeName,
        float red,
        float green,
        float blue,
        double? timeCode = null)
        : base(WorkspaceEditKind.SetColor3f, primPath)
    {
        AttributeName = attributeName;
        Red = red;
        Green = green;
        Blue = blue;
        TimeCode = timeCode;
    }

    /// <summary>Gets the attribute name.</summary>
    public string AttributeName { get; }

    /// <summary>Gets the authored red component.</summary>
    public float Red { get; }

    /// <summary>Gets the authored green component.</summary>
    public float Green { get; }

    /// <summary>Gets the authored blue component.</summary>
    public float Blue { get; }

    /// <summary>Gets the optional numeric time code.</summary>
    public double? TimeCode { get; }

    internal override void Validate()
    {
        ValidateAttribute(AttributeName, TimeCode);
        WorkspaceEditValidation.ValidateFloat3(Red, Green, Blue);
    }
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

    private protected void ValidateAttribute(string attributeName, double? timeCode)
    {
        WorkspaceEditValidation.ValidatePrimPath(PrimPath, nameof(PrimPath));
        WorkspaceEditValidation.ValidatePropertyName(attributeName, nameof(attributeName));
        if (timeCode is double value && !double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }
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

    internal static void ValidateTextValue(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > OpenUsdMcpLimits.MaximumTextLength ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Attribute text must contain at most {OpenUsdMcpLimits.MaximumTextLength} non-control characters.",
                parameterName);
        }
    }

    internal static void ValidateFloat3(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Float3 components must be finite.");
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
