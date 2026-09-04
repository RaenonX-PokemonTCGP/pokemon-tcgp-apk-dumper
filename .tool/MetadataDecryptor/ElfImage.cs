using System;
using System.Collections.Generic;
using System.IO;

namespace PokemonTcgPocket.Metadata
{
    internal sealed class ElfImage
    {
        internal sealed class Segment
        {
            internal ulong Offset;
            internal ulong VirtualAddress;
            internal ulong FileSize;
            internal uint Flags;
        }

        private readonly byte[] data;
        private readonly List<Segment> segments = new List<Segment>();

        internal ElfImage(string path)
        {
            data = File.ReadAllBytes(path);
            if (data.Length < 64 || data[0] != 0x7F || data[1] != (byte)'E' ||
                data[2] != (byte)'L' || data[3] != (byte)'F' || data[4] != 2 ||
                data[5] != 1)
            {
                throw new InvalidDataException("Expected a little-endian ELF64 binary.");
            }

            ulong tableOffset = BitConverter.ToUInt64(data, 0x20);
            ushort entrySize = BitConverter.ToUInt16(data, 0x36);
            ushort entryCount = BitConverter.ToUInt16(data, 0x38);
            for (int index = 0; index < entryCount; index++)
            {
                ulong offset = tableOffset + (ulong)(index * entrySize);
                int position = checked((int)offset);
                if (BitConverter.ToUInt32(data, position) != 1)
                {
                    continue;
                }
                segments.Add(new Segment
                {
                    Flags = BitConverter.ToUInt32(data, position + 4),
                    Offset = BitConverter.ToUInt64(data, position + 8),
                    VirtualAddress = BitConverter.ToUInt64(data, position + 16),
                    FileSize = BitConverter.ToUInt64(data, position + 32)
                });
            }
        }

        internal IEnumerable<Segment> ExecutableSegments
        {
            get
            {
                foreach (Segment segment in segments)
                {
                    if ((segment.Flags & 1) != 0)
                    {
                        yield return segment;
                    }
                }
            }
        }

        internal ulong FindVirtualAddress(byte[] pattern)
        {
            int position = 0;
            while ((position = Array.IndexOf(data, pattern[0], position)) >= 0)
            {
                bool matches = position + pattern.Length <= data.Length;
                for (int index = 1; matches && index < pattern.Length; index++)
                {
                    matches = data[position + index] == pattern[index];
                }
                if (matches)
                {
                    return FileOffsetToVirtual((ulong)position);
                }
                position++;
            }
            throw new InvalidDataException("Required ELF byte pattern was not found.");
        }

        internal byte[] ReadVirtual(ulong address, int count)
        {
            Segment segment = FindSegment(address, (ulong)count);
            int position = checked((int)(segment.Offset + address - segment.VirtualAddress));
            byte[] result = new byte[count];
            Buffer.BlockCopy(data, position, result, 0, count);
            return result;
        }

        internal uint ReadUInt32(ulong address)
        {
            return BitConverter.ToUInt32(ReadVirtual(address, 4), 0);
        }

        internal uint ReadInstruction(ulong address)
        {
            return ReadUInt32(address);
        }

        internal byte[] Data
        {
            get { return data; }
        }

        private ulong FileOffsetToVirtual(ulong offset)
        {
            foreach (Segment segment in segments)
            {
                if (offset >= segment.Offset && offset < segment.Offset + segment.FileSize)
                {
                    return segment.VirtualAddress + offset - segment.Offset;
                }
            }
            throw new InvalidDataException("ELF file offset is not mapped by a load segment.");
        }

        private Segment FindSegment(ulong address, ulong count)
        {
            foreach (Segment segment in segments)
            {
                if (address >= segment.VirtualAddress &&
                    address + count <= segment.VirtualAddress + segment.FileSize)
                {
                    return segment;
                }
            }
            throw new InvalidDataException("ELF virtual address is not file-backed.");
        }
    }
}
