using System;
using System.IO;
using System.Text;

namespace PokemonTcgPocket.Metadata
{
    internal sealed class MetadataLayout
    {
        internal byte[] Key;
        internal byte[] Prefix;

        internal static MetadataLayout Recover(ElfImage elf)
        {
            byte[] name = Encoding.ASCII.GetBytes("global-metadata.dat\0");
            ulong nameAddress = elf.FindVirtualAddress(name);
            foreach (ElfImage.Segment segment in elf.ExecutableSegments)
            {
                ulong count = segment.FileSize - (segment.FileSize % 4);
                for (ulong offset = 0; offset + 16 <= count; offset += 4)
                {
                    ulong address = segment.VirtualAddress + offset;
                    int fileOffset = checked((int)(segment.Offset + offset));
                    uint adrp = BitConverter.ToUInt32(elf.Data, fileOffset);
                    uint add = BitConverter.ToUInt32(elf.Data, fileOffset + 4);
                    ulong candidate;
                    if (!Arm64.TryResolveAdrpAdd(address, adrp, add, out candidate) ||
                        candidate != nameAddress)
                    {
                        continue;
                    }
                    try
                    {
                        return RecoverFromReference(elf, address);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            throw new InvalidDataException(
                "Could not recover the embedded metadata prefix and key from libil2cpp.so.");
        }

        private static MetadataLayout RecoverFromReference(ElfImage elf, ulong reference)
        {
            ulong loaderCall = reference + 0xC;
            ulong loader = Arm64.DecodeBranchTarget(loaderCall, elf.ReadInstruction(loaderCall));
            ulong decryptCall = loader + 0x15C;
            ulong decrypt = Arm64.DecodeBranchTarget(
                decryptCall,
                elf.ReadInstruction(decryptCall));
            ulong constant = Arm64.DecodeWideConstant(
                new uint[]
                {
                    elf.ReadInstruction(loader + 0x148),
                    elf.ReadInstruction(loader + 0x150),
                    elf.ReadInstruction(loader + 0x154),
                    elf.ReadInstruction(loader + 0x158)
                },
                1);

            ulong sizeAddress = ResolveAddress(elf, decrypt + 0x38);
            uint prefixSize = elf.ReadUInt32(sizeAddress);
            if (prefixSize == 0 || prefixSize > 0x100000 || prefixSize % 16 != 0)
            {
                throw new InvalidDataException("Embedded metadata prefix size is invalid.");
            }
            ulong keyMaskAddress = ResolveAddress(elf, decrypt + 0x84);
            ulong prefixBaseAddress = ResolveAddress(elf, decrypt + 0x184);
            byte[] keyMask = elf.ReadVirtual(keyMaskAddress, 16);
            byte[] constantBytes = BitConverter.GetBytes(constant);
            byte[] key = new byte[16];
            for (int index = 0; index < key.Length; index++)
            {
                key[index] = (byte)(keyMask[index] ^ constantBytes[index % 8]);
            }
            return new MetadataLayout
            {
                Key = key,
                Prefix = elf.ReadVirtual(prefixBaseAddress + 16, checked((int)prefixSize))
            };
        }

        private static ulong ResolveAddress(ElfImage elf, ulong address)
        {
            ulong result;
            if (!Arm64.TryResolveAdrpAdd(
                address,
                elf.ReadInstruction(address),
                elf.ReadInstruction(address + 4),
                out result))
            {
                throw new InvalidDataException("Expected an ARM64 ADRP/ADD address pair.");
            }
            return result;
        }
    }
}
