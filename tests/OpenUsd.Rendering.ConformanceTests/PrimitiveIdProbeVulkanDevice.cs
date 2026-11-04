// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using global::Silk.NET.Vulkan;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;
using VkDevice = global::Silk.NET.Vulkan.Device;
using VkQueue = global::Silk.NET.Vulkan.Queue;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed class PrimitiveIdProbeVulkanDevice : IDisposable
{
    private readonly Vk _api;
    private readonly Instance _instance;
    private readonly VkDevice _nativeDevice;
    private bool _disposed;

    private PrimitiveIdProbeVulkanDevice(
        Vk api,
        Instance instance,
        VkDevice nativeDevice,
        VulkanSilkGraphicsDevice device)
    {
        _api = api;
        _instance = instance;
        _nativeDevice = nativeDevice;
        Device = device;
    }

    internal VulkanSilkGraphicsDevice Device { get; }

    internal static unsafe PrimitiveIdProbeVulkanDevice Create()
    {
        Vk api = new(SilkNativeLibraryContext.Load(VulkanLoaderLibrary.GetCandidateNames()));
        Instance instance = default;
        VkDevice nativeDevice = default;
        VulkanSilkGraphicsDevice? device = null;
        bool success = false;
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
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.CreateInstance(&instanceInfo, null, &instance),
                "vkCreateInstance for the primitive-ID probe");

            uint count = 0;
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.EnumeratePhysicalDevices(instance, &count, null),
                "vkEnumeratePhysicalDevices for the primitive-ID probe");
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "The primitive-ID probe requires a Vulkan physical device.");
            }

            var physicalDevices = new PhysicalDevice[count];
            fixed (PhysicalDevice* pointer = physicalDevices)
            {
                VulkanSilkGraphicsDevice.ThrowIfFailed(
                    api.EnumeratePhysicalDevices(instance, &count, pointer),
                    "vkEnumeratePhysicalDevices for the primitive-ID probe");
            }
            PhysicalDevice physicalDevice = physicalDevices[0];
            api.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
            byte* namePointer = properties.DeviceName;
            string deviceName = Marshal.PtrToStringUTF8((nint)namePointer) ?? "Vulkan Device";
            bool software = properties.DeviceType == PhysicalDeviceType.Cpu;
            if (Environment.GetEnvironmentVariable("OPENUSD_REQUIRE_SWIFTSHADER") == "1" &&
                (!software || !deviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Expected SwiftShader for the primitive-ID probe, but Vulkan selected '{deviceName}'.");
            }

            api.GetPhysicalDeviceFeatures(physicalDevice, out PhysicalDeviceFeatures supported);
            if (!supported.GeometryShader)
            {
                throw new InvalidOperationException(
                    $"Vulkan device '{deviceName}' does not expose geometryShader, required for " +
                    "the fragment PrimitiveId SPIR-V capability; the probe cannot run.");
            }

            uint queueFamily = FindGraphicsQueue(api, physicalDevice);
            float priority = 1;
            var queueInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = queueFamily,
                QueueCount = 1,
                PQueuePriorities = &priority
            };
            // The production-owned Vulkan device does not enable this feature.
            var enabled = new PhysicalDeviceFeatures { GeometryShader = true };
            var deviceInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                PEnabledFeatures = &enabled
            };
            VulkanSilkGraphicsDevice.ThrowIfFailed(
                api.CreateDevice(physicalDevice, &deviceInfo, null, &nativeDevice),
                "vkCreateDevice for the primitive-ID probe");
            api.GetDeviceQueue(nativeDevice, queueFamily, 0, out VkQueue queue);
            api.GetPhysicalDeviceMemoryProperties(
                physicalDevice,
                out PhysicalDeviceMemoryProperties memoryProperties);
            uint major = properties.ApiVersion >> 22;
            uint minor = (properties.ApiVersion >> 12) & 0x3ff;
            uint patch = properties.ApiVersion & 0xfff;
            var capabilities = new SilkGraphicsCapabilities(
                deviceName,
                $"{major}.{minor}.{patch}",
                SupportsCompute: true,
                IsSoftware: software)
            {
                MaxTextureDimension2D = properties.Limits.MaxImageDimension2D
            };
            device = VulkanSilkGraphicsDevice.CreateBorrowed(
                api,
                instance,
                physicalDevice,
                nativeDevice,
                queue,
                queueFamily,
                memoryProperties,
                capabilities);
            var owner = new PrimitiveIdProbeVulkanDevice(api, instance, nativeDevice, device);
            success = true;
            return owner;
        }
        finally
        {
            if (!success)
            {
                try
                {
                    device?.Dispose();
                }
                finally
                {
                    if (nativeDevice.Handle != 0)
                    {
                        api.DestroyDevice(nativeDevice, null);
                    }
                    if (instance.Handle != 0)
                    {
                        api.DestroyInstance(instance, null);
                    }
                    api.Dispose();
                }
            }
        }
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Device.Dispose();
        _api.DestroyDevice(_nativeDevice, null);
        _api.DestroyInstance(_instance, null);
        _api.Dispose();
        _disposed = true;
    }

    private static unsafe uint FindGraphicsQueue(Vk api, PhysicalDevice physicalDevice)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        var properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = properties)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, pointer);
        }
        QueueFlags required = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
        for (uint index = 0; index < count; index++)
        {
            if (properties[index].QueueCount != 0 &&
                (properties[index].QueueFlags & required) == required)
            {
                return index;
            }
        }
        throw new InvalidOperationException(
            "The primitive-ID probe requires a Vulkan graphics-and-compute queue.");
    }
}
