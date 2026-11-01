// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk.Vulkan;

public sealed unsafe partial class VulkanSilkGraphicsDevice
{
    // Keep the original declarations unless every use of the removed builtin is accounted for.
    private static void RemoveUnusedDrawParameters(
        ref byte[] code,
        uint baseInstanceVariable,
        uint baseInstanceValue)
    {
        const ushort opName = 5;
        const ushort opEntryPoint = 15;
        const ushort opCapability = 17;
        const ushort opTypePointer = 32;
        const ushort opVariable = 59;
        const ushort opLoad = 61;
        const ushort opDecorate = 71;
        const ushort opMemberDecorate = 72;
        const uint decorationBuiltIn = 11;
        const uint builtInBaseVertex = 4424;
        const uint builtInBaseInstance = 4425;
        const uint builtInDrawIndex = 4426;
        const uint capabilityDrawParameters = 4427;

        var removals = new List<(int Offset, int Length)>();
        var entryPointInterfaces = new List<(int Offset, int Length, int InterfaceWord)>();
        bool foundVariable = false;
        bool foundLoad = false;
        int wordCount = code.Length / sizeof(uint);
        for (int offset = 5; offset < wordCount;)
        {
            uint first = ReadWord(code, offset);
            int length = (int)(first >> 16);
            ushort opcode = (ushort)first;
            if (length <= 0 || offset + length > wordCount)
            {
                return;
            }

            switch (opcode)
            {
                case opName:
                    if (length < 3)
                    {
                        return;
                    }
                    if (isTracked(ReadWord(code, offset + 1)))
                    {
                        removals.Add((offset, length));
                    }
                    break;

                case opEntryPoint:
                    if (length < 4 || isTracked(ReadWord(code, offset + 2)))
                    {
                        return;
                    }
                    int interfaceStart = offset + 3;
                    while (interfaceStart < offset + length &&
                        !containsStringTerminator(ReadWord(code, interfaceStart++)))
                    {
                    }
                    if (interfaceStart > offset + length ||
                        !containsStringTerminator(ReadWord(code, interfaceStart - 1)))
                    {
                        return;
                    }
                    int interfaceWord = -1;
                    for (int index = interfaceStart; index < offset + length; index++)
                    {
                        uint value = ReadWord(code, index);
                        if (value == baseInstanceValue ||
                            (value == baseInstanceVariable && interfaceWord >= 0))
                        {
                            return;
                        }
                        if (value == baseInstanceVariable)
                        {
                            interfaceWord = index;
                        }
                    }
                    if (interfaceWord >= 0)
                    {
                        entryPointInterfaces.Add((offset, length, interfaceWord));
                    }
                    break;

                case opCapability:
                    if (length != 2)
                    {
                        return;
                    }
                    if (ReadWord(code, offset + 1) == capabilityDrawParameters)
                    {
                        removals.Add((offset, length));
                    }
                    break;

                case opTypePointer:
                    if (length != 4 ||
                        isTracked(ReadWord(code, offset + 1)) ||
                        isTracked(ReadWord(code, offset + 3)))
                    {
                        return;
                    }
                    break;

                case opVariable:
                    if (length < 4 || isTracked(ReadWord(code, offset + 1)))
                    {
                        return;
                    }
                    uint variable = ReadWord(code, offset + 2);
                    if (variable == baseInstanceVariable)
                    {
                        if (foundVariable || length != 4)
                        {
                            return;
                        }
                        foundVariable = true;
                        removals.Add((offset, length));
                    }
                    else if (isTracked(variable) ||
                        containsTrackedWords(code, offset + 4, offset + length))
                    {
                        return;
                    }
                    break;

                case opLoad:
                    if (length < 4 || isTracked(ReadWord(code, offset + 1)))
                    {
                        return;
                    }
                    uint result = ReadWord(code, offset + 2);
                    uint pointer = ReadWord(code, offset + 3);
                    if (pointer == baseInstanceVariable && result == baseInstanceValue)
                    {
                        if (foundLoad || length != 4)
                        {
                            return;
                        }
                        foundLoad = true;
                        removals.Add((offset, length));
                    }
                    else if (isTracked(result) || isTracked(pointer) ||
                        containsTrackedWords(code, offset + 4, offset + length))
                    {
                        return;
                    }
                    break;

                case opDecorate:
                    if (length < 3)
                    {
                        return;
                    }
                    uint target = ReadWord(code, offset + 1);
                    uint decoration = ReadWord(code, offset + 2);
                    if (isTracked(target))
                    {
                        if (decoration == decorationBuiltIn &&
                            (length != 4 || target != baseInstanceVariable ||
                                ReadWord(code, offset + 3) != builtInBaseInstance))
                        {
                            return;
                        }
                        removals.Add((offset, length));
                    }
                    else if (usesDrawBuiltin(code, offset, length, 2) ||
                        (!hasLiteralDecorationOperands(decoration) &&
                            containsTrackedWords(code, offset + 2, offset + length)))
                    {
                        return;
                    }
                    break;

                case opMemberDecorate:
                    if (length < 4 || isTracked(ReadWord(code, offset + 1)))
                    {
                        return;
                    }
                    uint memberDecoration = ReadWord(code, offset + 3);
                    if (usesDrawBuiltin(code, offset, length, 3) ||
                        (!hasLiteralDecorationOperands(memberDecoration) &&
                            containsTrackedWords(code, offset + 3, offset + length)))
                    {
                        return;
                    }
                    break;

                // Grouped and extension decorations can introduce indirect builtin uses.
                case 74:
                case 75:
                case 332:
                case 5632:
                case 5633:
                    return;

                default:
                    if (containsTrackedWords(code, offset + 1, offset + length))
                    {
                        return;
                    }
                    break;
            }
            offset += length;
        }

        if (!foundVariable || !foundLoad)
        {
            return;
        }

        // Global SPIR-V sections cannot contain OpNop, so compact removed instructions.
        int removedWords = entryPointInterfaces.Count;
        foreach (var removal in removals)
        {
            removedWords = checked(removedWords + removal.Length);
        }
        var rewritten = new byte[checked(code.Length - (removedWords * sizeof(uint)))];
        code.AsSpan(0, 5 * sizeof(uint)).CopyTo(rewritten);
        int sourceOffset = 5;
        int destinationOffset = 5;
        int removalIndex = 0;
        int entryPointIndex = 0;
        while (sourceOffset < wordCount)
        {
            if (removalIndex < removals.Count && removals[removalIndex].Offset == sourceOffset)
            {
                sourceOffset += removals[removalIndex++].Length;
                continue;
            }

            if (entryPointIndex < entryPointInterfaces.Count &&
                entryPointInterfaces[entryPointIndex].Offset == sourceOffset)
            {
                var entryPoint = entryPointInterfaces[entryPointIndex++];
                int entryLength = entryPoint.Length;
                int interfaceWord = entryPoint.InterfaceWord;
                WriteWord(rewritten, destinationOffset++, ((uint)(entryLength - 1) << 16) | opEntryPoint);
                for (int index = sourceOffset + 1; index < sourceOffset + entryLength; index++)
                {
                    if (index != interfaceWord)
                    {
                        WriteWord(rewritten, destinationOffset++, ReadWord(code, index));
                    }
                }
                sourceOffset += entryLength;
                continue;
            }

            int instructionLength = (int)(ReadWord(code, sourceOffset) >> 16);
            code.AsSpan(sourceOffset * sizeof(uint), instructionLength * sizeof(uint))
                .CopyTo(rewritten.AsSpan(destinationOffset * sizeof(uint)));
            sourceOffset += instructionLength;
            destinationOffset += instructionLength;
        }
        code = rewritten;

        bool isTracked(uint value) => value == baseInstanceVariable || value == baseInstanceValue;

        bool containsTrackedWords(ReadOnlySpan<byte> bytes, int first, int end)
        {
            for (int index = first; index < end; index++)
            {
                if (isTracked(ReadWord(bytes, index)))
                {
                    return true;
                }
            }
            return false;
        }

        static bool usesDrawBuiltin(ReadOnlySpan<byte> bytes, int offset, int length, int decorationOffset) =>
            length > decorationOffset + 1 &&
            ReadWord(bytes, offset + decorationOffset) == decorationBuiltIn &&
            ReadWord(bytes, offset + decorationOffset + 1) is
                builtInBaseVertex or builtInBaseInstance or builtInDrawIndex;

        static bool hasLiteralDecorationOperands(uint decoration) =>
            decoration is 6 or decorationBuiltIn or 33 or 35;

        static bool containsStringTerminator(uint word) =>
            (word & 0x000000ff) == 0 ||
            (word & 0x0000ff00) == 0 ||
            (word & 0x00ff0000) == 0 ||
            (word & 0xff000000) == 0;
    }

    private static uint ReadWord(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset * sizeof(uint), sizeof(uint)));

    private static void WriteWord(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.Slice(offset * sizeof(uint), sizeof(uint)),
            value);
}
