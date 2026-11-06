// Copyright (c) marcschier. Licensed under the MIT License.

import Darwin
import Foundation
import Metal

let samples: [Float] = [1, 20, 1e10, 1e18, 1e19, 1e20, 1e30, 1e37]

func require<T>(_ value: T?, _ description: String) -> T {
    guard let value else {
        fatalError("Metal volume math diagnostic: \(description) is unavailable.")
    }
    return value
}

func describe(_ value: Float) -> [String: Any] {
    return [
        "value": String(value),
        "bits": String(format: "0x%08x", value.bitPattern),
        "finite": value.isFinite,
    ]
}

func run(
    device: MTLDevice,
    libraryPath: String,
    variant: String
) throws -> [String: Any] {
    let library = try device.makeLibrary(URL: URL(fileURLWithPath: libraryPath))
    let descriptor = MTLRenderPipelineDescriptor()
    descriptor.vertexFunction = require(library.makeFunction(name: "volumeMathVertex"), "vertex function")
    descriptor.fragmentFunction = require(library.makeFunction(name: "volumeMathFragment"), "fragment function")
    descriptor.colorAttachments[0].pixelFormat = .rgba32Float
    let pipeline = try device.makeRenderPipelineState(descriptor: descriptor)

    let values: [SIMD4<Float>] = samples.flatMap { value in
        [SIMD4(value, value, value, 0.125), SIMD4(0.5, 2e38, value * 0.25, 0)]
    } + [
        SIMD4(0.3663415014743805, 1, 1, 0.25),
        SIMD4(1, Float.greatestFiniteMagnitude, 31.201824188232422 * 0.25, 0),
    ]
    let input = require(values.withUnsafeBytes { bytes in
        device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared)
    }, "input buffer")

    let textureDescriptor = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: .rgba32Float,
        width: values.count,
        height: 1,
        mipmapped: false)
    textureDescriptor.storageMode = .private
    textureDescriptor.usage = .renderTarget
    let texture = require(device.makeTexture(descriptor: textureDescriptor), "render target")
    let bytesPerRow = ((values.count * 4 * MemoryLayout<Float>.size) + 255) & ~255
    let output = require(device.makeBuffer(length: bytesPerRow, options: .storageModeShared), "readback buffer")
    let queue = require(device.makeCommandQueue(), "command queue")
    let commands = require(queue.makeCommandBuffer(), "command buffer")
    let pass = MTLRenderPassDescriptor()
    pass.colorAttachments[0].texture = texture
    pass.colorAttachments[0].loadAction = .clear
    pass.colorAttachments[0].storeAction = .store
    pass.colorAttachments[0].clearColor = MTLClearColorMake(0, 0, 0, 0)
    let render = require(commands.makeRenderCommandEncoder(descriptor: pass), "render encoder")
    render.setRenderPipelineState(pipeline)
    render.setViewport(MTLViewport(
        originX: 0,
        originY: 0,
        width: Double(values.count),
        height: 1,
        znear: 0,
        zfar: 1))
    render.setFragmentBuffer(input, offset: 0, index: 0)
    render.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
    render.endEncoding()

    let blit = require(commands.makeBlitCommandEncoder(), "blit encoder")
    blit.copy(
        from: texture,
        sourceSlice: 0,
        sourceLevel: 0,
        sourceOrigin: MTLOrigin(x: 0, y: 0, z: 0),
        sourceSize: MTLSize(width: values.count, height: 1, depth: 1),
        to: output,
        destinationOffset: 0,
        destinationBytesPerRow: bytesPerRow,
        destinationBytesPerImage: bytesPerRow)
    blit.endEncoding()
    commands.commit()
    commands.waitUntilCompleted()
    guard commands.status == .completed else {
        fatalError("Metal volume math diagnostic failed: \(String(describing: commands.error)).")
    }

    let pixels = output.contents().bindMemory(to: Float.self, capacity: values.count * 4)
    let control: [Float] = [1, 0.25, 1, 0.7788008, 0.5, 1e38, 2e38, 1.5576016e38]
    let baselineValid = control.indices.allSatisfy { index in
        pixels[index].isFinite && abs((pixels[index] / control[index]) - 1) < 0.0001
    }
    let results: [[String: Any]] = samples.enumerated().map { index, value in
        [
            "input": describe(value),
            "densityFraction": describe(pixels[(index * 8)]),
            "fieldDepth": describe(pixels[(index * 8) + 1]),
            "depthRatio": describe(pixels[(index * 8) + 2]),
            "transmittance": describe(pixels[(index * 8) + 3]),
            "tintWeight": describe(pixels[(index * 8) + 4]),
            "tintContribution": describe(pixels[(index * 8) + 5]),
            "tintSource": describe(pixels[(index * 8) + 6]),
            "tintAfterTransmittance": describe(pixels[(index * 8) + 7]),
        ]
    }
    let twoFieldOffset = samples.count * 8
    let twoFieldMaximumTint: [String: Any] = [
        "fieldDepth": describe(pixels[twoFieldOffset + 1]),
        "tintWeight": describe(pixels[twoFieldOffset + 4]),
        "tintContribution": describe(pixels[twoFieldOffset + 5]),
        "tintSource": describe(pixels[twoFieldOffset + 6]),
        "tintAfterTransmittance": describe(pixels[twoFieldOffset + 7]),
    ]
    return [
        "variant": variant,
        "baselineValid": baselineValid,
        "samples": results,
        "twoFieldMaximumTint": twoFieldMaximumTint,
    ]
}

guard CommandLine.arguments.count == 3 else {
    fatalError("Expected default and precise Metal library paths.")
}
let device = require(MTLCreateSystemDefaultDevice(), "Metal device")
let variants = [
    try run(device: device, libraryPath: CommandLine.arguments[1], variant: "default"),
    try run(device: device, libraryPath: CommandLine.arguments[2], variant: "no-fast-math"),
]
let report: [String: Any] = [
    "scope": "standalone-fragment-diagnostic",
    "device": device.name,
    "variants": variants,
]
let json = try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
FileHandle.standardOutput.write(json)
FileHandle.standardOutput.write(Data("\n".utf8))
guard variants.allSatisfy({ $0["baselineValid"] as? Bool == true }) else {
    FileHandle.standardError.write(Data("Metal volume math low-density control failed.\n".utf8))
    exit(1)
}
