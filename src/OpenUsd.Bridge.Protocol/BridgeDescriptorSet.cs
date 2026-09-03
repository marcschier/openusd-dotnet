// Copyright (c) marcschier. Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.Reflection;
using OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Builds a serialized <c>FileDescriptorSet</c> from the compiled contract.
/// </summary>
/// <remarks>
/// The bytes come from the descriptor the protobuf compiler embedded in the generated code, so the
/// descriptor a peer consumes and the code this package encodes with are the same artifact. There is
/// no second, hand-maintained copy that could drift, and no file is read from disk at runtime.
/// </remarks>
internal static class BridgeDescriptorSet
{
    internal static byte[] CreateWireDescriptorSet() => Create(WireReflection.Descriptor);

    /// <summary>
    /// Builds a descriptor set containing <paramref name="descriptors"/> and, transitively, every
    /// file they import, in dependency order and without duplicates. Python's
    /// <c>descriptor_pool</c> and protobuf's own <c>FileDescriptorSet</c> readers both require a
    /// file's dependencies to appear before the file itself.
    /// </summary>
    internal static byte[] Create(params FileDescriptor[] descriptors)
    {
        var set = new FileDescriptorSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FileDescriptor descriptor in descriptors)
        {
            AddWithDependencies(descriptor, set, seen);
        }

        return set.ToByteArray();
    }

    private static void AddWithDependencies(
        FileDescriptor descriptor,
        FileDescriptorSet set,
        HashSet<string> seen)
    {
        if (!seen.Add(descriptor.Name))
        {
            return;
        }

        foreach (FileDescriptor dependency in descriptor.Dependencies)
        {
            AddWithDependencies(dependency, set, seen);
        }

        set.File.Add(descriptor.ToProto());
    }
}
