// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OpenUsd;

/// <summary>
/// Marks an object or value that remains bound to the lifetime and owner thread of a USD stage.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "The scheduler uses this marker to prevent stage-owned values from escaping.")]
public interface IUsdStageBound
{
}

/// <summary>
/// Opts a concrete custom DTO into scheduler result delivery as a detached value.
/// </summary>
/// <remarks>
/// Implementers promise that instances do not retain stages, stage-bound wrappers, native handles,
/// lazy sequences, or other thread-affine state. The scheduler treats implementing concrete types
/// as opaque and does not reflect their fields.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "This opt-in marker declares a trusted detached scheduler result contract.")]
public interface IUsdDetachedResult
{
}

/// <summary>
/// Reports a stage scheduler result that does not satisfy the detached-result contract.
/// </summary>
public sealed class UsdStageBoundResultException : InvalidOperationException
{
    /// <summary>Identifies this scheduler contract violation.</summary>
    public const string ErrorCode = "OPENUSD_SCHEDULER_STAGE_BOUND_RESULT";

    /// <summary>Provides the stable scheduler contract violation message.</summary>
    public const string ErrorMessage =
        "UsdStageScheduler callbacks cannot return stage-bound OpenUSD values.";

    internal UsdStageBoundResultException()
        : base(ErrorMessage)
    {
    }

    /// <summary>Gets the stable scheduler error code.</summary>
    public string Code { get; } = ErrorCode;
}

internal static class UsdStageBoundResultGuard
{
    internal static void ThrowIfForbiddenType(Type resultType)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        if (!IsAllowedType(resultType))
        {
            throw new UsdStageBoundResultException();
        }
    }

    internal static void ThrowIfForbiddenResult<T>(T result)
    {
        if (result is null)
        {
            return;
        }

        InspectTrustedResult(result, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static bool IsAllowedType(Type type)
    {
        if (IsAsyncResultType(type) ||
            typeof(IUsdStageBound).IsAssignableFrom(type) ||
            type == typeof(object) ||
            type.IsInterface ||
            type.IsAbstract)
        {
            return false;
        }

        if (type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(ValueTuple) ||
            typeof(IUsdDetachedResult).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            Type? elementType = type.GetElementType();
            return elementType is not null && IsAllowedType(elementType);
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        if (definition != typeof(Nullable<>) &&
            definition != typeof(List<>) &&
            definition != typeof(Dictionary<,>) &&
            !IsTupleDefinition(definition))
        {
            return false;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            if (!IsAllowedType(argument))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsyncResultType(Type type)
    {
        if (typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask))
        {
            return true;
        }

        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(ValueTask<>);
    }

    private static bool IsTupleDefinition(Type definition) =>
        definition == typeof(Tuple<>) ||
        definition == typeof(Tuple<,>) ||
        definition == typeof(Tuple<,,>) ||
        definition == typeof(Tuple<,,,>) ||
        definition == typeof(Tuple<,,,,>) ||
        definition == typeof(Tuple<,,,,,>) ||
        definition == typeof(Tuple<,,,,,,>) ||
        definition == typeof(Tuple<,,,,,,,>) ||
        definition == typeof(ValueTuple<>) ||
        definition == typeof(ValueTuple<,>) ||
        definition == typeof(ValueTuple<,,>) ||
        definition == typeof(ValueTuple<,,,>) ||
        definition == typeof(ValueTuple<,,,,>) ||
        definition == typeof(ValueTuple<,,,,,>) ||
        definition == typeof(ValueTuple<,,,,,,>) ||
        definition == typeof(ValueTuple<,,,,,,,>);

    private static void InspectTrustedResult(object result, HashSet<object> visited)
    {
        ThrowIfForbiddenType(result.GetType());

        if (!result.GetType().IsValueType && !visited.Add(result))
        {
            return;
        }

        if (result is Array array)
        {
            foreach (object? item in array)
            {
                if (item is not null)
                {
                    InspectTrustedResult(item, visited);
                }
            }
            return;
        }

        Type type = result.GetType();
        Type? definition = type.IsGenericType
            ? type.GetGenericTypeDefinition()
            : null;

        if (definition is not null && IsTupleDefinition(definition))
        {
            var tuple = (ITuple)result;
            for (int i = 0; i < tuple.Length; i++)
            {
                object? item = tuple[i];
                if (item is not null)
                {
                    InspectTrustedResult(item, visited);
                }
            }
            return;
        }

        if (definition == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)result;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not null)
                {
                    InspectTrustedResult(entry.Key, visited);
                }
                if (entry.Value is not null)
                {
                    InspectTrustedResult(entry.Value, visited);
                }
            }
            return;
        }

        if (definition == typeof(List<>))
        {
            var list = (IList)result;
            for (int i = 0; i < list.Count; i++)
            {
                object? item = list[i];
                if (item is not null)
                {
                    InspectTrustedResult(item, visited);
                }
            }
        }
    }
}
