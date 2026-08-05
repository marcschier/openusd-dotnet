// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;
using Silk.NET.Core.Native;

namespace OpenUsd.Rendering.Silk.Vulkan;

/// <summary>
/// Headless Vulkan device, queue, memory, and buffer implementation.
/// </summary>
public sealed unsafe partial class VulkanSilkGraphicsDevice
    : SilkGraphicsDeviceLifetimeBase,
      ISilkGraphicsDevice,
      ISilkVolumeTextureGraphicsDevice,
      ISilkPickingGraphicsDevice,
      ISilkSelectionOutlineGraphicsDevice
{
    private readonly Vk _api;
    private readonly Instance _instance;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly uint _queueFamily;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;
    private readonly VulkanDescriptorIndexingFeatures _descriptorIndexingFeatures;
    private readonly VulkanDescriptorIndexedTextureTables? _materialDescriptorTables;
    private readonly bool _ownsNativeObjects;
    private bool _disposed;

    private VulkanSilkGraphicsDevice(
        Vk api,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue queue,
        uint queueFamily,
        PhysicalDeviceMemoryProperties memoryProperties,
        VulkanDescriptorIndexingFeatures descriptorIndexingFeatures,
        VulkanDescriptorIndexedTextureTables? materialDescriptorTables,
        SilkGraphicsCapabilities capabilities,
        bool ownsNativeObjects)
    {
        _api = api;
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _queueFamily = queueFamily;
        _memoryProperties = memoryProperties;
        _descriptorIndexingFeatures = descriptorIndexingFeatures;
        _materialDescriptorTables = materialDescriptorTables;
        _ownsNativeObjects = ownsNativeObjects;
        Capabilities = capabilities;
        InitializePicking();
    }

    /// <inheritdoc/>
    public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

    /// <inheritdoc/>
    public SilkGraphicsCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public bool ClipSpaceYPointsDown => true;

    internal VulkanDescriptorIndexingFeatures DescriptorIndexingFeaturesForTesting =>
        _descriptorIndexingFeatures;

    /// <summary>Creates a headless Vulkan device and graphics queue.</summary>
    public static VulkanSilkGraphicsDevice Create()
    {
        Vk api = new(SilkNativeLibraryContext.Load(GetVulkanLibraryNames()));
        Instance instance = default;
        Device device = default;
        try
        {
            var application = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                ApiVersion = Vk.Version12
            };
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &application
            };
            ThrowIfFailed(api.CreateInstance(&instanceInfo, null, &instance), "vkCreateInstance");

            uint deviceCount = 0;
            ThrowIfFailed(
                api.EnumeratePhysicalDevices(instance, &deviceCount, null),
                "vkEnumeratePhysicalDevices");
            if (deviceCount == 0)
            {
                throw new PlatformNotSupportedException("No Vulkan physical device is available.");
            }

            var physicalDevices = new PhysicalDevice[deviceCount];
            fixed (PhysicalDevice* physicalDevicePointer = physicalDevices)
            {
                ThrowIfFailed(
                    api.EnumeratePhysicalDevices(instance, &deviceCount, physicalDevicePointer),
                    "vkEnumeratePhysicalDevices");
            }

            PhysicalDevice physicalDevice = physicalDevices[0];
            uint queueFamily = FindGraphicsQueue(api, physicalDevice);
            api.GetPhysicalDeviceProperties(
                physicalDevice,
                out PhysicalDeviceProperties properties);
            bool descriptorIndexingExtension =
                SupportsDeviceExtension(api, physicalDevice, "VK_EXT_descriptor_indexing");
            bool descriptorIndexingIsCore = properties.ApiVersion >= Vk.Version12;
            VulkanDescriptorIndexingFeatures descriptorIndexingFeatures =
                descriptorIndexingIsCore || descriptorIndexingExtension
                    ? QueryDescriptorIndexingFeatures(api, physicalDevice)
                    : default;
            bool enableDescriptorIndexing =
                descriptorIndexingFeatures.SupportsDescriptorIndexedTextureTables;
            float queuePriority = 1;
            var queueInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = queueFamily,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
            string[] enabledExtensions =
                enableDescriptorIndexing && !descriptorIndexingIsCore
                    ? ["VK_EXT_descriptor_indexing"]
                    : [];
            using GlobalMemory extensionNames = SilkMarshal.StringArrayToMemory(
                enabledExtensions,
                NativeStringEncoding.UTF8);
            var enabledDescriptorIndexingFeatures =
                new PhysicalDeviceDescriptorIndexingFeatures
                {
                    SType = StructureType.PhysicalDeviceDescriptorIndexingFeatures,
                    RuntimeDescriptorArray =
                        descriptorIndexingFeatures.RuntimeDescriptorArray,
                    DescriptorBindingPartiallyBound =
                        descriptorIndexingFeatures.DescriptorBindingPartiallyBound,
                    ShaderSampledImageArrayNonUniformIndexing =
                        descriptorIndexingFeatures.ShaderSampledImageArrayNonUniformIndexing,
                    DescriptorBindingVariableDescriptorCount =
                        descriptorIndexingFeatures.DescriptorBindingVariableDescriptorCount
                };
            var deviceInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = checked((uint)enabledExtensions.Length),
                PpEnabledExtensionNames = (byte**)extensionNames.Handle,
                PNext = enableDescriptorIndexing
                    ? &enabledDescriptorIndexingFeatures
                    : null
            };
            ThrowIfFailed(
                api.CreateDevice(physicalDevice, &deviceInfo, null, &device),
                "vkCreateDevice");
            api.GetDeviceQueue(device, queueFamily, 0, out Queue queue);

            api.GetPhysicalDeviceMemoryProperties(
                physicalDevice,
                out PhysicalDeviceMemoryProperties memoryProperties);
            byte* namePointer = properties.DeviceName;
            string deviceName = Marshal.PtrToStringUTF8((nint)namePointer) ?? "Vulkan Device";
            uint major = properties.ApiVersion >> 22;
            uint minor = (properties.ApiVersion >> 12) & 0x3ff;
            uint patch = properties.ApiVersion & 0xfff;
            VulkanDescriptorIndexedTextureTables? materialDescriptorTables =
                enableDescriptorIndexing
                    ? VulkanDescriptorIndexedTextureTables.TryCreate(api, device)
                    : null;
            var capabilities = new SilkGraphicsCapabilities(
                deviceName,
                $"{major}.{minor}.{patch}",
                SupportsCompute: true,
                IsSoftware: properties.DeviceType == PhysicalDeviceType.Cpu)
            {
                SupportsDescriptorIndexedTextureTables =
                    materialDescriptorTables is not null
            };
            return new VulkanSilkGraphicsDevice(
                api,
                instance,
                physicalDevice,
                device,
                queue,
                queueFamily,
                memoryProperties,
                descriptorIndexingFeatures,
                materialDescriptorTables,
                capabilities,
                ownsNativeObjects: true);
        }

        catch
        {
            if (device.Handle != 0)
            {
                api.DestroyDevice(device, null);
            }
            if (instance.Handle != 0)
            {
                api.DestroyInstance(instance, null);
            }
            api.Dispose();
            throw;
        }
    }

    internal static VulkanSilkGraphicsDevice CreateBorrowed(
        Vk api,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue queue,
        uint queueFamily,
        PhysicalDeviceMemoryProperties memoryProperties,
        SilkGraphicsCapabilities capabilities) =>
        new(
            api,
            instance,
            physicalDevice,
            device,
            queue,
            queueFamily,
            memoryProperties,
            default,
            null,
            capabilities,
            ownsNativeObjects: false);

    internal ISilkGraphicsTexture WrapBorrowedColorTarget(
        Image image,
        ImageView imageView,
        uint width,
        uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RegisterDependentObject();
        return new VulkanSilkGraphicsTexture(
            this,
            image,
            default,
            imageView,
            new SilkTextureDescriptor(
                width,
                height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource),
            ownsNativeObjects: false);
    }

    /// <inheritdoc/>
    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(size);
        RegisterDependentObject();

        global::Silk.NET.Vulkan.Buffer buffer = default;
        DeviceMemory memory = default;
        bool success = false;
        try
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = checked((ulong)size),
                Usage = GetBufferUsage(usage),
                SharingMode = SharingMode.Exclusive
            };
            ThrowIfFailed(
                _api.CreateBuffer(_device, &bufferInfo, null, &buffer),
                "vkCreateBuffer");
            _api.GetBufferMemoryRequirements(
                _device,
                buffer,
                out MemoryRequirements requirements);
            MemoryPropertyFlags desired = usage.HasFlag(SilkBufferUsage.Upload)
                ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
                : MemoryPropertyFlags.DeviceLocalBit;
            var allocationInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, desired)
            };
            ThrowIfFailed(
                _api.AllocateMemory(_device, &allocationInfo, null, &memory),
                "vkAllocateMemory");
            ThrowIfFailed(
                _api.BindBufferMemory(_device, buffer, memory, 0),
                "vkBindBufferMemory");
            success = true;
            return new VulkanSilkGraphicsBuffer(
                this,
                _api,
                _device,
                buffer,
                memory,
                size,
                usage);
        }
        finally
        {
            if (!success && memory.Handle != 0)
            {
                _api.FreeMemory(_device, memory, null);
            }
            if (!success && buffer.Handle != 0)
            {
                _api.DestroyBuffer(_device, buffer, null);
            }
            if (!success)
            {
                ReleaseDependentObject();
            }
        }
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(_api.DeviceWaitIdle(_device), "vkDeviceWaitIdle");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!TryBeginDispose())
        {
            return;
        }
        bool idle = false;
        try
        {
            WaitIdle();
            idle = true;
            _materialDescriptorTables?.Dispose();
            if (_ownsNativeObjects)
            {
                _api.DestroyDevice(_device, null);
                _api.DestroyInstance(_instance, null);
                _api.Dispose();
            }
            _disposed = true;
        }
        finally
        {
            if (idle)
            {
                CompleteLifetimeDispose();
            }
            else
            {
                CancelLifetimeDispose();
            }
        }
    }

    internal void RegisterDependentObject() => RegisterDependentLifetime();

    internal void ReleaseDependentObject() => ReleaseDependentLifetime();

    private static VulkanDescriptorIndexingFeatures QueryDescriptorIndexingFeatures(
        Vk api,
        PhysicalDevice physicalDevice)
    {
        var descriptorIndexing = new PhysicalDeviceDescriptorIndexingFeatures
        {
            SType = StructureType.PhysicalDeviceDescriptorIndexingFeatures
        };
        var features = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &descriptorIndexing
        };
        api.GetPhysicalDeviceFeatures2(physicalDevice, &features);
        return new VulkanDescriptorIndexingFeatures(
            descriptorIndexing.RuntimeDescriptorArray,
            descriptorIndexing.DescriptorBindingPartiallyBound,
            descriptorIndexing.ShaderSampledImageArrayNonUniformIndexing,
            descriptorIndexing.DescriptorBindingVariableDescriptorCount);
    }

    private static bool SupportsDeviceExtension(
        Vk api,
        PhysicalDevice physicalDevice,
        string extensionName)
    {
        uint extensionCount = 0;
        ThrowIfFailed(
            api.EnumerateDeviceExtensionProperties(
                physicalDevice,
                (byte*)null,
                &extensionCount,
                null),
            "vkEnumerateDeviceExtensionProperties");
        if (extensionCount == 0)
        {
            return false;
        }
        ExtensionProperties[] extensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* extensionPointer = extensions)
        {
            ThrowIfFailed(
                api.EnumerateDeviceExtensionProperties(
                    physicalDevice,
                    (byte*)null,
                    &extensionCount,
                    extensionPointer),
                "vkEnumerateDeviceExtensionProperties");
            for (uint index = 0; index < extensionCount; index++)
            {
                string? available = Marshal.PtrToStringUTF8(
                    (nint)extensionPointer[index].ExtensionName);
                if (string.Equals(
                    available,
                    extensionName,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryBeginDispose() => TryBeginLifetimeDispose(
        "Cannot dispose the Vulkan device while buffers, textures, or submissions are alive; " +
        "samplers must also be disposed.");

    private static uint FindGraphicsQueue(Vk api, PhysicalDevice physicalDevice)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        var properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = properties)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, pointer);
        }

        for (uint i = 0; i < count; i++)
        {
            QueueFlags required = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
            if ((properties[i].QueueFlags & required) == required)
            {
                return i;
            }
        }
        throw new PlatformNotSupportedException(
            "No Vulkan queue supports both graphics and compute.");
    }

    private static string[] GetVulkanLibraryNames()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["vulkan-1.dll"];
        }
        if (OperatingSystem.IsMacOS())
        {
            return ["libvulkan.1.dylib", "libvulkan.dylib"];
        }
        return ["libvulkan.so.1", "libvulkan.so"];
    }

    private uint FindMemoryType(uint typeBits, MemoryPropertyFlags desired)
    {
        for (uint i = 0; i < _memoryProperties.MemoryTypeCount; i++)
        {
            bool supported = (typeBits & (1u << (int)i)) != 0;
            bool matches =
                (_memoryProperties.MemoryTypes[(int)i].PropertyFlags & desired) == desired;
            if (supported && matches)
            {
                return i;
            }
        }
        throw new PlatformNotSupportedException(
            $"No Vulkan memory type supports {desired}.");
    }

    private static BufferUsageFlags GetBufferUsage(SilkBufferUsage usage)
    {
        BufferUsageFlags flags = 0;
        if (usage.HasFlag(SilkBufferUsage.Vertex))
        {
            flags |= BufferUsageFlags.VertexBufferBit;
        }
        if (usage.HasFlag(SilkBufferUsage.Index))
        {
            flags |= BufferUsageFlags.IndexBufferBit;
        }
        if (usage.HasFlag(SilkBufferUsage.Uniform))
        {
            flags |= BufferUsageFlags.UniformBufferBit;
        }
        if (usage.HasFlag(SilkBufferUsage.Storage))
        {
            flags |= BufferUsageFlags.StorageBufferBit |
                BufferUsageFlags.TransferSrcBit;
        }
        return flags == 0 ? BufferUsageFlags.TransferSrcBit : flags;
    }

    internal static void ThrowIfFailed(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{operation} failed: {result}.");
        }
    }
}

internal sealed unsafe class VulkanSilkGraphicsBuffer : SilkGraphicsBufferBase
{
    private readonly VulkanSilkGraphicsDevice _owner;
    private readonly Vk _api;
    private readonly Device _device;
    private global::Silk.NET.Vulkan.Buffer _buffer;
    private DeviceMemory _memory;

    internal VulkanSilkGraphicsBuffer(
        VulkanSilkGraphicsDevice owner,
        Vk api,
        Device device,
        global::Silk.NET.Vulkan.Buffer buffer,
        DeviceMemory memory,
        nuint size,
        SilkBufferUsage usage)
        : base(size, usage)
    {
        _owner = owner;
        _api = api;
        _device = device;
        _buffer = buffer;
        _memory = memory;
    }

    public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
    {
        ThrowIfBufferDisposed();
        nuint length = ValidateWrite(data.Length, offset);
        if (length == 0)
        {
            return;
        }

        void* mapped = null;
        VulkanSilkGraphicsDevice.ThrowIfFailed(
            _api.MapMemory(
                _device,
                _memory,
                checked((ulong)offset),
                checked((ulong)length),
                0,
                &mapped),
            "vkMapMemory");
        try
        {
            data.CopyTo(new Span<byte>(mapped, data.Length));
        }
        finally
        {
            _api.UnmapMemory(_device, _memory);
        }
    }

    public override void ReadbackForTesting(Span<byte> destination)
    {
        _ = ValidateReadback(destination.Length);
        _owner.Readback(this, destination);
    }

    protected override void ReleaseNative()
    {
        if (_buffer.Handle != 0)
        {
            _api.DestroyBuffer(_device, _buffer, null);
            _buffer = default;
        }
        if (_memory.Handle != 0)
        {
            _api.FreeMemory(_device, _memory, null);
            _memory = default;
        }
        _owner.ReleaseDependentObject();
    }

    internal global::Silk.NET.Vulkan.Buffer Buffer => _buffer;

    internal VulkanSilkGraphicsDevice Owner => _owner;

    internal IDisposable AcquireLease() => AcquireBufferLease();

    internal void ThrowIfDisposed() => ThrowIfBufferDisposed();
}
