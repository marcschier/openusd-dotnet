// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace OpenUsd.Editing;

/// <summary>An immutable exact Sdf path list operation, not a composed or flattened path list.</summary>
public sealed class UsdPathListEdit : IUsdDetachedResult
{
    /// <summary>Copies all six buckets, preserving their order and explicit/non-explicit mode.</summary>
    /// <remarks>Paths used by mutations must additionally be absolute and non-variant.</remarks>
    public UsdPathListEdit(
        bool isExplicit,
        IReadOnlyList<string>? explicitItems = null,
        IReadOnlyList<string>? addedItems = null,
        IReadOnlyList<string>? prependedItems = null,
        IReadOnlyList<string>? appendedItems = null,
        IReadOnlyList<string>? deletedItems = null,
        IReadOnlyList<string>? orderedItems = null)
    {
        IsExplicit = isExplicit;
        ExplicitItems = Copy(explicitItems);
        AddedItems = Copy(addedItems);
        PrependedItems = Copy(prependedItems);
        AppendedItems = Copy(appendedItems);
        DeletedItems = Copy(deletedItems);
        OrderedItems = Copy(orderedItems);
        if (isExplicit
            ? AddedItems.Count + PrependedItems.Count + AppendedItems.Count +
                DeletedItems.Count + OrderedItems.Count != 0
            : ExplicitItems.Count != 0)
        {
            throw new ArgumentException("Explicit and non-explicit path-list buckets cannot be mixed.");
        }
    }

    /// <summary>Gets whether this is an explicit list, including an explicitly empty list.</summary>
    public bool IsExplicit { get; }
    /// <summary>Gets the exact explicit bucket.</summary>
    public IReadOnlyList<string> ExplicitItems { get; }
    /// <summary>Gets the exact added bucket.</summary>
    public IReadOnlyList<string> AddedItems { get; }
    /// <summary>Gets the exact prepended bucket.</summary>
    public IReadOnlyList<string> PrependedItems { get; }
    /// <summary>Gets the exact appended bucket.</summary>
    public IReadOnlyList<string> AppendedItems { get; }
    /// <summary>Gets the exact deleted bucket.</summary>
    public IReadOnlyList<string> DeletedItems { get; }
    /// <summary>Gets the exact ordered bucket.</summary>
    public IReadOnlyList<string> OrderedItems { get; }

    internal void ValidateMutationPaths(bool connection)
    {
        Validate(ExplicitItems, connection);
        Validate(AddedItems, connection);
        Validate(PrependedItems, connection);
        Validate(AppendedItems, connection);
        Validate(DeletedItems, connection);
        Validate(OrderedItems, connection);
    }

    private static void Validate(IReadOnlyList<string> items, bool connection)
    {
        foreach (string path in items)
        {
            UsdEditingValidation.MutationPath(path, connection);
        }
    }

    private static ReadOnlyCollection<string> Copy(IReadOnlyList<string>? items)
    {
        int count = items?.Count ?? 0;
        UsdEditingValidation.ItemCount(count);
        var result = new string[count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string item = items![index];
            _ = UsdEditingValidation.Text(item, nameof(items));
            if (!unique.Add(item))
            {
                throw new ArgumentException("Duplicate items in a path-list bucket are not supported.", nameof(items));
            }
            result[index] = item;
        }
        return Array.AsReadOnly(result);
    }
}
