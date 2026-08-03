// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenUsd.Tests;

/// <summary>
/// Requires every detached snapshot record with a collection member to
/// implement value equality itself, rather than inheriting the compiler's.
/// </summary>
/// <remarks>
/// The inspection API returns detached records so a UI can poll a stage and
/// diff the results without holding native handles, which only pays for itself
/// if two snapshots of unchanged state compare equal.
///
/// A C# record does not give that. The synthesized <c>Equals</c> compares each
/// member with <c>EqualityComparer&lt;T&gt;.Default</c>, which for an array or
/// an <c>IReadOnlyList</c> is **reference** equality. Every poll allocates
/// fresh collections, so every snapshot compared unequal to the previous one
/// and the detachment design silently bought nothing.
///
/// Six records were fixed by hand. What makes this a test rather than a note is
/// that the seventh arrived immediately: `UsdSkelBlendShapeInbetween` was added
/// by the UsdShade/UsdSkel merge *while* the equality fix was in flight, so the
/// survey that found the other six could not have seen it. It reached a merge
/// conflict carrying exactly the defect that had just been fixed elsewhere.
///
/// A convention nobody can forget is better than one everybody must remember,
/// so this enumerates the assembly instead of naming types. A hand-maintained
/// list would have the same blind spot as the original survey.
/// </remarks>
public sealed class DetachedRecordEqualityContractTests
{
    [Test]
    public async Task EveryDetachedRecordWithACollectionMemberDeclaresItsOwnEquality()
    {
        List<string> offenders = [];
        int checkedCount = 0;

        foreach (Type type in DetachedRecordsWithCollectionMembers())
        {
            checkedCount++;

            MethodInfo? equals = type.GetMethod(
                "Equals",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                [type],
                modifiers: null);

            if (equals is null || equals.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                offenders.Add(type.Name);
            }
        }

        // Non-vacuity: if the discovery predicate stops matching, an empty
        // offender list would pass while checking nothing at all.
        await Assert.That(checkedCount)
            .IsGreaterThanOrEqualTo(6)
            .Because("the records carrying collection members should still be discoverable");

        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "a record's synthesized Equals compares collections by " +
                "reference, so these snapshots would never compare equal " +
                "across two polls of an unchanged stage: " +
                string.Join(", ", offenders));
    }

    [Test]
    public async Task EveryDetachedRecordWithACollectionMemberDeclaresItsOwnHashCode()
    {
        List<string> offenders = [];

        foreach (Type type in DetachedRecordsWithCollectionMembers())
        {
            MethodInfo? hash = type.GetMethod(
                nameof(GetHashCode),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            if (hash is null ||
                hash.DeclaringType != type ||
                hash.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                offenders.Add(type.Name);
            }
        }

        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "GetHashCode must stay consistent with a hand-written Equals, " +
                "or these records break the moment one lands in a dictionary " +
                "or a set: " + string.Join(", ", offenders));
    }

    [Test]
    public async Task EveryDetachedRecordWithACollectionMemberPrintsItsContents()
    {
        List<string> offenders = [];

        foreach (Type type in DetachedRecordsWithCollectionMembers())
        {
            MethodInfo? toString = type.GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            if (toString is null || toString.DeclaringType != type)
            {
                offenders.Add(type.Name);
            }
        }

        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "a record's synthesized ToString prints a collection member as " +
                "its type name, which makes assertion failures and logs far " +
                "harder to read: " + string.Join(", ", offenders));
    }

    private static IEnumerable<Type> DetachedRecordsWithCollectionMembers()
    {
        foreach (Type type in typeof(IUsdDetachedResult).Assembly.GetExportedTypes())
        {
            if (!type.IsClass || !IsRecord(type))
            {
                continue;
            }

            if (type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(IsCollection))
            {
                yield return type;
            }
        }
    }

    /// <summary>
    /// Identifies a record by its synthesized clone method, which the compiler
    /// emits for every record and for nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately not keyed on <see cref="IUsdDetachedResult"/>: only two of
    /// the six affected records declare it, so an interface-keyed predicate
    /// found two, and the non-vacuity assertion below caught that before this
    /// test could pass while checking almost nothing.
    /// </remarks>
    private static bool IsRecord(Type type) =>
        type.GetMethod(
            "<Clone>$",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static bool IsCollection(PropertyInfo property) =>
        property.PropertyType != typeof(string) &&
        typeof(IEnumerable).IsAssignableFrom(property.PropertyType);
}
