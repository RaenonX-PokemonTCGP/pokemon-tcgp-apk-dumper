using System;
using System.IO;
using System.Security.Cryptography;

namespace PokemonTcgPocket.Metadata
{
    public static class MetadataDecryptor
    {
        private const uint MetadataMagic = 0xFAB11BAF;
        private const int ChunkSize = 1024 * 1024;

        public static int Decrypt(string binaryPath, string metadataPath, string outputPath)
        {
            ElfImage elf = new ElfImage(binaryPath);
            MetadataLayout layout = MetadataLayout.Recover(elf);
            byte[] protectedMetadata = File.ReadAllBytes(metadataPath);
            if (protectedMetadata.Length < 4)
            {
                throw new InvalidDataException("Protected metadata has no length header.");
            }
            uint payloadSize = BitConverter.ToUInt32(protectedMetadata, 0);
            if (payloadSize != protectedMetadata.Length - 4)
            {
                throw new InvalidDataException("Protected metadata payload length is invalid.");
            }

            int outputSize = checked((int)payloadSize + layout.Prefix.Length);
            byte[] firstBytes = new byte[8];
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = layout.Key;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                using (FileStream output = File.Create(outputPath))
                {
                    DecryptToStream(
                        encryptor,
                        layout.Prefix,
                        protectedMetadata,
                        outputSize,
                        firstBytes,
                        output);
                }
            }
            if (BitConverter.ToUInt32(firstBytes, 0) != MetadataMagic)
            {
                File.Delete(outputPath);
                throw new InvalidDataException("Decrypted metadata magic is invalid.");
            }
            return outputSize;
        }

        private static void DecryptToStream(
            ICryptoTransform encryptor,
            byte[] prefix,
            byte[] protectedMetadata,
            int outputSize,
            byte[] firstBytes,
            Stream output)
        {
            byte[] counter = new byte[16];
            for (int position = 0; position < outputSize; position += ChunkSize)
            {
                int count = Math.Min(ChunkSize, outputSize - position);
                int paddedCount = (count + 15) & ~15;
                byte[] counters = new byte[paddedCount];
                byte[] keyStream = new byte[paddedCount];
                byte[] ciphertext = new byte[count];
                FillCiphertext(ciphertext, position, prefix, protectedMetadata);
                for (int block = 0; block < paddedCount; block += 16)
                {
                    IncrementCounter(counter);
                    Buffer.BlockCopy(counter, 0, counters, block, 16);
                }
                if (encryptor.TransformBlock(counters, 0, paddedCount, keyStream, 0) !=
                    paddedCount)
                {
                    throw new CryptographicException("AES generated an incomplete key stream.");
                }
                for (int index = 0; index < count; index++)
                {
                    ciphertext[index] ^= keyStream[index];
                }
                if (position == 0)
                {
                    Buffer.BlockCopy(ciphertext, 0, firstBytes, 0, firstBytes.Length);
                }
                output.Write(ciphertext, 0, count);
            }
        }

        private static void FillCiphertext(
            byte[] target,
            int position,
            byte[] prefix,
            byte[] protectedMetadata)
        {
            int written = 0;
            if (position < prefix.Length)
            {
                int count = Math.Min(target.Length, prefix.Length - position);
                Buffer.BlockCopy(prefix, position, target, 0, count);
                written = count;
            }
            if (written < target.Length)
            {
                int metadataPosition = 4 + position + written - prefix.Length;
                Buffer.BlockCopy(
                    protectedMetadata,
                    metadataPosition,
                    target,
                    written,
                    target.Length - written);
            }
        }

        private static void IncrementCounter(byte[] counter)
        {
            for (int index = counter.Length - 1; index >= 8; index--)
            {
                counter[index]++;
                if (counter[index] != 0)
                {
                    return;
                }
            }
        }
    }
}
