// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using Google.Protobuf.Reflection;
using OpenUsd.Bridge.Protocol;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// The generated descriptor is the contract a peer in another language consumes, so it is validated
/// as a contract: it must be self-contained, free of the constructs this project bans, complete for
/// every declared message, and loadable by a Python protobuf runtime when one is available.
/// </summary>
public sealed class BridgeDescriptorTests
{
    private const string PythonProbeMarker = "BRIDGE_DESCRIPTOR_PYTHON_PREREQUISITES_ABSENT";

    [Test]
    public async Task TheDescriptorSetParsesAndDescribesTheVersionedPackage()
    {
        byte[] descriptorSet = BridgeProtocol.CreateDescriptorSet();

        FileDescriptorSet parsed = FileDescriptorSet.Parser.ParseFrom(descriptorSet);

        await Assert.That(parsed.File.Count).IsEqualTo(1);
        FileDescriptorProto file = parsed.File[0];
        await Assert.That(file.Package).IsEqualTo(BridgeProtocol.PackageName);
        await Assert.That(file.Name).IsEqualTo("openusd/bridge/v1/wire.proto");
        await Assert.That(file.Syntax).IsEqualTo("proto3");
        await Assert.That(file.Dependency.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheContractUsesNoAnyNoJsonAndNoNativeHandles()
    {
        FileDescriptorProto file = ParseWireFile();

        foreach (DescriptorProto message in file.MessageType)
        {
            foreach (FieldDescriptorProto field in message.Field)
            {
                await Assert.That(field.TypeName).DoesNotContain("google.protobuf.Any");
                await Assert.That(field.Name).DoesNotContain("json");
                await Assert.That(field.Name).DoesNotContain("handle");
                await Assert.That(field.Name).DoesNotContain("pointer");
            }
        }
    }

    [Test]
    public async Task EveryEnumReservesZeroForAnUnspecifiedValue()
    {
        FileDescriptorProto file = ParseWireFile();

        await Assert.That(file.EnumType.Count).IsGreaterThan(0);
        foreach (EnumDescriptorProto enumeration in file.EnumType)
        {
            EnumValueDescriptorProto zero = enumeration.Value.Single(value => value.Number == 0);
            await Assert.That(zero.Name).EndsWith("UNSPECIFIED");
        }
    }

    [Test]
    public async Task EveryUpdateAndValueOneofCaseIsPresentInTheDescriptor()
    {
        FileDescriptorProto file = ParseWireFile();

        DescriptorProto stageUpdate = file.MessageType.Single(m => m.Name == "StageUpdate");
        DescriptorProto attributeValue = file.MessageType.Single(m => m.Name == "AttributeValue");
        DescriptorProto metadataValue = file.MessageType.Single(m => m.Name == "MetadataValue");

        // One oneof case per authoring update kind, minus the overlay replacement that a snapshot
        // expresses instead, and one case per attribute and metadata value kind.
        await Assert.That(stageUpdate.Field.Count).IsEqualTo(13);
        await Assert.That(attributeValue.Field.Count)
            .IsEqualTo(Enum.GetValues<LiveAuthoring.LiveAttributeKind>().Length);
        await Assert.That(metadataValue.Field.Count)
            .IsEqualTo(Enum.GetValues<LiveAuthoring.LiveMetadataKind>().Length);
        await Assert.That(stageUpdate.OneofDecl.Count).IsEqualTo(1);
        await Assert.That(attributeValue.OneofDecl.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheCheckedInProtoMatchesTheCompiledDescriptor()
    {
        string protoPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Bridge.Protocol",
            "protos",
            "openusd",
            "bridge",
            "v1",
            "wire.proto");
        string proto = await File.ReadAllTextAsync(protoPath);
        FileDescriptorProto file = ParseWireFile();

        foreach (DescriptorProto message in file.MessageType)
        {
            await Assert.That(proto).Contains($"message {message.Name} ");
        }
        foreach (EnumDescriptorProto enumeration in file.EnumType)
        {
            await Assert.That(proto).Contains($"enum {enumeration.Name} ");
        }
        await Assert.That(proto).Contains($"package {BridgeProtocol.PackageName};");
    }

    [Test]
    public async Task TheGrpcDescriptorSetCarriesTheServiceAndItsImportInOrder()
    {
        byte[] descriptorSet = Grpc.BridgeGrpcProtocol.CreateDescriptorSet();

        FileDescriptorSet parsed = FileDescriptorSet.Parser.ParseFrom(descriptorSet);

        await Assert.That(parsed.File.Count).IsEqualTo(2);
        await Assert.That(parsed.File[0].Name).IsEqualTo("openusd/bridge/v1/wire.proto");
        await Assert.That(parsed.File[1].Name).IsEqualTo("openusd/bridge/v1/service.proto");
        ServiceDescriptorProto service = parsed.File[1].Service.Single();
        await Assert.That($"{parsed.File[1].Package}.{service.Name}")
            .IsEqualTo(Grpc.BridgeGrpcProtocol.ServiceName);
        await Assert.That(service.Method.Count).IsEqualTo(5);
        MethodDescriptorProto stream = service.Method.Single(m => m.Name == "StreamChanges");
        await Assert.That(stream.ClientStreaming).IsTrue();
        await Assert.That(stream.ServerStreaming).IsTrue();
    }

    [Test]
    public async Task APythonProtobufRuntimeCanConsumeTheDescriptor()
    {
        string repositoryRoot = FindRepositoryRoot();
        if (!TryFindPython(out string python))
        {
            Console.WriteLine(
                $"{PythonProbeMarker}: no python interpreter is on PATH.");
            return;
        }
        if (!HasPythonProtobuf(python))
        {
            Console.WriteLine(
                $"{PythonProbeMarker}: the interpreter has no google.protobuf module. " +
                "Install it with 'python -m pip install protobuf' to run this gate.");
            return;
        }

        string workRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "bridge-descriptor-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        try
        {
            string descriptorPath = Path.Combine(workRoot, "openusd.bridge.v1.pb");
            await File.WriteAllBytesAsync(descriptorPath, BridgeProtocol.CreateDescriptorSet());
            string serviceDescriptorPath = Path.Combine(workRoot, "openusd.bridge.v1.service.pb");
            await File.WriteAllBytesAsync(
                serviceDescriptorPath,
                Grpc.BridgeGrpcProtocol.CreateDescriptorSet());
            string scriptPath = Path.Combine(workRoot, "probe.py");
            await File.WriteAllTextAsync(scriptPath, PythonProbeScript);

            (int exitCode, string output) = RunProcess(
                python,
                $"\"{scriptPath}\" \"{descriptorPath}\" \"{serviceDescriptorPath}\"",
                workRoot);

            Console.WriteLine(output);
            await Assert.That(exitCode).IsEqualTo(0).Because(output);
            await Assert.That(output).Contains("PYTHON_DESCRIPTOR_OK");
            await Assert.That(output).Contains("PACKAGE=openusd.bridge.v1");
            await Assert.That(output).Contains("STAGE_UPDATE_CASES=13");
            await Assert.That(output).Contains("SERVICE_METHODS=5");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    private const string PythonProbeScript = """
        # Copyright (c) marcschier. Licensed under the MIT License.
        #
        # Loads the generated openusd.bridge.v1 descriptor into a Python descriptor pool and builds
        # one message of every kind, which is what a Kit-side Python peer does before it can speak
        # the contract. No generated Python code is committed: the descriptor is the contract.
        import sys

        from google.protobuf import descriptor_pb2, descriptor_pool, message_factory

        with open(sys.argv[1], "rb") as handle:
            file_set = descriptor_pb2.FileDescriptorSet.FromString(handle.read())

        pool = descriptor_pool.DescriptorPool()
        for file_proto in file_set.file:
            pool.Add(file_proto)

        stage_update = pool.FindMessageTypeByName("openusd.bridge.v1.StageUpdate")
        delta = pool.FindMessageTypeByName("openusd.bridge.v1.StageDelta")
        snapshot = pool.FindMessageTypeByName("openusd.bridge.v1.StageSnapshot")
        handshake = pool.FindMessageTypeByName("openusd.bridge.v1.HandshakeRequest")

        delta_class = message_factory.GetMessageClass(delta)
        message = delta_class()
        message.epoch.remote_origin_id = "kit-bridge"
        message.epoch.session_id = "session-a"
        message.epoch.epoch = 1
        message.sequence = 7
        update = message.updates.add()
        update.set_attribute.prim_path = "/Bridge/Cube"
        update.set_attribute.attribute_name = "custom:pressure"
        update.set_attribute.value.double_value = 1.5
        encoded = message.SerializeToString()
        decoded = delta_class.FromString(encoded)
        assert decoded.sequence == 7
        assert decoded.updates[0].set_attribute.value.double_value == 1.5

        print("PYTHON_DESCRIPTOR_OK")
        print(f"PACKAGE={delta.file.package}")
        print(f"STAGE_UPDATE_CASES={len(stage_update.fields)}")
        print(f"SNAPSHOT_FIELDS={len(snapshot.fields)}")
        print(f"HANDSHAKE_FIELDS={len(handshake.fields)}")

        service_pool = descriptor_pool.DescriptorPool()
        with open(sys.argv[2], "rb") as handle:
            service_set = descriptor_pb2.FileDescriptorSet.FromString(handle.read())
        for file_proto in service_set.file:
            service_pool.Add(file_proto)

        service = service_pool.FindServiceByName("openusd.bridge.v1.LiveBridge")
        print(f"SERVICE_METHODS={len(service.methods)}")

        try:
            import grpc  # noqa: F401

            print("PYTHON_GRPC_AVAILABLE=True")
        except ImportError:
            # grpcio is a Kit-side deployment choice, not a requirement of the contract. The
            # descriptor above is what a Python peer needs; the runtime is installed separately.
            print("PYTHON_GRPC_AVAILABLE=False")
        """;

    private static FileDescriptorProto ParseWireFile() =>
        FileDescriptorSet.Parser.ParseFrom(BridgeProtocol.CreateDescriptorSet()).File[0];

    private static bool TryFindPython(out string python)
    {
        foreach (string candidate in new[] { "python", "python3" })
        {
            try
            {
                (int exitCode, _) = RunProcess(candidate, "--version", Environment.CurrentDirectory);
                if (exitCode == 0)
                {
                    python = candidate;
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The candidate is not on PATH; try the next one.
            }
        }

        python = string.Empty;
        return false;
    }

    private static bool HasPythonProtobuf(string python)
    {
        (int exitCode, _) = RunProcess(
            python,
            "-c \"import google.protobuf\"",
            Environment.CurrentDirectory);
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) RunProcess(
        string fileName,
        string arguments,
        string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            }
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }
}
