using System;

namespace PokemonTcgPocket.Metadata
{
    internal static class Arm64
    {
        internal static bool TryResolveAdrpAdd(
            ulong instructionAddress,
            uint adrp,
            uint add,
            out ulong target)
        {
            target = 0;
            if ((adrp & 0x9F000000U) != 0x90000000U ||
                (add & 0xFF000000U) != 0x91000000U)
            {
                return false;
            }
            uint register = adrp & 0x1FU;
            if (((add >> 5) & 0x1FU) != register)
            {
                return false;
            }

            long immediate = (long)((((adrp >> 5) & 0x7FFFFU) << 2) |
                ((adrp >> 29) & 3U));
            immediate = SignExtend(immediate, 21) << 12;
            long page = (long)(instructionAddress & ~0xFFFUL) + immediate;
            ulong addend = (add >> 10) & 0xFFFU;
            if (((add >> 22) & 1U) != 0)
            {
                addend <<= 12;
            }
            target = checked((ulong)page + addend);
            return true;
        }

        internal static ulong DecodeBranchTarget(ulong address, uint instruction)
        {
            if ((instruction & 0xFC000000U) != 0x94000000U)
            {
                throw new InvalidOperationException("Expected an ARM64 BL instruction.");
            }
            long immediate = SignExtend(instruction & 0x03FFFFFFU, 26) << 2;
            return checked((ulong)((long)address + immediate));
        }

        internal static ulong DecodeWideConstant(uint[] instructions, uint register)
        {
            ulong value = 0;
            bool initialized = false;
            foreach (uint instruction in instructions)
            {
                if ((instruction & 0x1FU) != register)
                {
                    throw new InvalidOperationException("Unexpected wide-move register.");
                }
                uint operation = instruction & 0xFF800000U;
                int shift = (int)((instruction >> 21) & 3U) * 16;
                ulong immediate = (ulong)((instruction >> 5) & 0xFFFFU) << shift;
                if (operation == 0xD2800000U)
                {
                    value = immediate;
                    initialized = true;
                }
                else if (operation == 0xF2800000U && initialized)
                {
                    value = (value & ~(0xFFFFUL << shift)) | immediate;
                }
                else
                {
                    throw new InvalidOperationException("Unexpected ARM64 wide-move sequence.");
                }
            }
            return value;
        }

        private static long SignExtend(long value, int bits)
        {
            long sign = 1L << (bits - 1);
            return (value ^ sign) - sign;
        }
    }
}
