using System.Text;

namespace Windose.System.Kernel.Cryptography
{
    public static class SHA256
    {
        private static readonly uint[] K =
        {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
        0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
        0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
        0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
        0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
        0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
        0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
        0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
        0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    };

        private static uint RotateRight(uint x, int n)
        {
            return (x >> n) | (x << (32 - n));
        }

        private static uint Ch(uint x, uint y, uint z)
        {
            return (x & y) ^ (~x & z);
        }

        private static uint Maj(uint x, uint y, uint z)
        {
            return (x & y) ^ (x & z) ^ (y & z);
        }

        private static uint Sigma0(uint x)
        {
            return RotateRight(x, 2) ^
                   RotateRight(x, 13) ^
                   RotateRight(x, 22);
        }

        private static uint Sigma1(uint x)
        {
            return RotateRight(x, 6) ^
                   RotateRight(x, 11) ^
                   RotateRight(x, 25);
        }

        private static uint Gamma0(uint x)
        {
            return RotateRight(x, 7) ^
                   RotateRight(x, 18) ^
                   (x >> 3);
        }

        private static uint Gamma1(uint x)
        {
            return RotateRight(x, 17) ^
                   RotateRight(x, 19) ^
                   (x >> 10);
        }

        public static byte[] Compute(byte[] data)
        {
            // SHA-256 padding
            ulong bitLength = (ulong)data.Length * 8;

            int paddingLength = 64 - ((data.Length + 9) % 64);
            if (paddingLength == 64)
                paddingLength = 0;

            byte[] padded = new byte[data.Length + 1 + paddingLength + 8];

            // Original data
            Array.Copy(data, padded, data.Length);

            // Append 1 bit
            padded[data.Length] = 0x80;

            // Append original length in bits, big-endian
            for (int i = 0; i < 8; i++)
            {
                padded[padded.Length - 1 - i] =
                    (byte)(bitLength >> (i * 8));
            }

            // Initial hash values
            uint h0 = 0x6a09e667;
            uint h1 = 0xbb67ae85;
            uint h2 = 0x3c6ef372;
            uint h3 = 0xa54ff53a;
            uint h4 = 0x510e527f;
            uint h5 = 0x9b05688c;
            uint h6 = 0x1f83d9ab;
            uint h7 = 0x5be0cd19;

            uint[] w = new uint[64];

            // Process 512-bit blocks
            for (int offset = 0; offset < padded.Length; offset += 64)
            {
                // First 16 words
                for (int i = 0; i < 16; i++)
                {
                    int index = offset + i * 4;

                    w[i] =
                        ((uint)padded[index] << 24) |
                        ((uint)padded[index + 1] << 16) |
                        ((uint)padded[index + 2] << 8) |
                        padded[index + 3];
                }

                // Extend to 64 words
                for (int i = 16; i < 64; i++)
                {
                    w[i] = Gamma1(w[i - 2])
                         + w[i - 7]
                         + Gamma0(w[i - 15])
                         + w[i - 16];
                }

                uint a = h0;
                uint b = h1;
                uint c = h2;
                uint d = h3;
                uint e = h4;
                uint f = h5;
                uint g = h6;
                uint h = h7;

                // Main compression loop
                for (int i = 0; i < 64; i++)
                {
                    uint temp1 =
                        h +
                        Sigma1(e) +
                        Ch(e, f, g) +
                        K[i] +
                        w[i];

                    uint temp2 =
                        Sigma0(a) +
                        Maj(a, b, c);

                    h = g;
                    g = f;
                    f = e;
                    e = d + temp1;
                    d = c;
                    c = b;
                    b = a;
                    a = temp1 + temp2;
                }

                // Add compressed chunk to current hash
                h0 += a;
                h1 += b;
                h2 += c;
                h3 += d;
                h4 += e;
                h5 += f;
                h6 += g;
                h7 += h;
            }

            // Produce 32-byte hash
            byte[] result = new byte[32];

            WriteUInt32BE(result, 0, h0);
            WriteUInt32BE(result, 4, h1);
            WriteUInt32BE(result, 8, h2);
            WriteUInt32BE(result, 12, h3);
            WriteUInt32BE(result, 16, h4);
            WriteUInt32BE(result, 20, h5);
            WriteUInt32BE(result, 24, h6);
            WriteUInt32BE(result, 28, h7);

            return result;
        }

        private static void WriteUInt32BE(byte[] output, int offset, uint value)
        {
            output[offset] = (byte)(value >> 24);
            output[offset + 1] = (byte)(value >> 16);
            output[offset + 2] = (byte)(value >> 8);
            output[offset + 3] = (byte)value;
        }

        public static string ComputeString(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] hash = Compute(data);

            char[] chars = new char[hash.Length * 2];

            const string hex = "0123456789abcdef";

            for (int i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = hex[hash[i] >> 4];
                chars[i * 2 + 1] = hex[hash[i] & 0x0F];
            }

            return new string(chars);
        }
    }
}

