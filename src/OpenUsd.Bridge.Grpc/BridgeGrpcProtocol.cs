// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Grpc;

/// <summary>
/// The identity of the optional gRPC surface: the service name, and the serialized descriptor set a
/// peer in another language needs to generate its own server or client.
/// </summary>
/// <remarks>
/// The descriptor set contains the service file and the wire file it imports, in dependency order,
/// which is exactly what a Python <c>descriptor_pool</c> or a <c>protoc</c> descriptor consumer
/// requires. It is built from the compiled contract, so it cannot drift from the stub this package
/// actually calls through.
/// </remarks>
public static class BridgeGrpcProtocol
{
    /// <summary>Gets the fully qualified gRPC service name.</summary>
    public static string ServiceName => BridgeProtocol.ServiceName;

    /// <summary>Gets the protocol version this adapter speaks.</summary>
    public static BridgeProtocolVersion Version => BridgeProtocol.Version;

    /// <summary>
    /// Returns the serialized <c>FileDescriptorSet</c> for the service and every file it imports.
    /// </summary>
    public static byte[] CreateDescriptorSet() =>
        BridgeDescriptorSet.Create(ServiceReflection.Descriptor);
}
