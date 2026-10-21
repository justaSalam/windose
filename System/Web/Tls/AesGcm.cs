using System;

// AES-128/256 block cipher and GCM (Galois/Counter Mode) AEAD, hand-rolled for
// the native TLS record layer. TLS 1.2's AES-GCM suites (RFC 5288) protect each
// record with a per-record nonce built from a 4-byte implicit IV and an 8-byte
// explicit sequence number, over a 13-byte AAD header. Cosmos-safe.
namespace Windose.Web.Tls
{
    /// <summary>AES block cipher with 128- or 256-bit keys (10 or 14 rounds).</summary>
    public sealed class AesCore
    {
        private byte[] rk;       // expanded round keys, 16 bytes each
        private int rounds;

        private static readonly byte[] Sbox = new byte[]
        {
            0x63,0x7c,0x77,0x7b,0xf2,0x6b,0x6f,0xc5,0x30,0x01,0x67,0x2b,0xfe,0xd7,0xab,0x76,
            0xca,0x82,0xc9,0x7d,0xfa,0x59,0x47,0xf0,0xad,0xd4,0xa2,0xaf,0x9c,0xa4,0x72,0xc0,
            0xb7,0xfd,0x93,0x26,0x36,0x3f,0xf7,0xcc,0x34,0xa5,0xe5,0xf1,0x71,0xd8,0x31,0x15,
            0x04,0xc7,0x23,0xc3,0x18,0x96,0x05,0x9a,0x07,0x12,0x80,0xe2,0xeb,0x27,0xb2,0x75,
            0x09,0x83,0x2c,0x1a,0x1b,0x6e,0x5a,0xa0,0x52,0x3b,0xd6,0xb3,0x29,0xe3,0x2f,0x84,
            0x53,0xd1,0x00,0xed,0x20,0xfc,0xb1,0x5b,0x6a,0xcb,0xbe,0x39,0x4a,0x4c,0x58,0xcf,
            0xd0,0xef,0xaa,0xfb,0x43,0x4d,0x33,0x85,0x45,0xf9,0x02,0x7f,0x50,0x3c,0x9f,0xa8,
            0x51,0xa3,0x40,0x8f,0x92,0x9d,0x38,0xf5,0xbc,0xb6,0xda,0x21,0x10,0xff,0xf3,0xd2,
            0xcd,0x0c,0x13,0xec,0x5f,0x97,0x44,0x17,0xc4,0xa7,0x7e,0x3d,0x64,0x5d,0x19,0x73,
            0x60,0x81,0x4f,0xdc,0x22,0x2a,0x90,0x88,0x46,0xee,0xb8,0x14,0xde,0x5e,0x0b,0xdb,
            0xe0,0x32,0x3a,0x0a,0x49,0x06,0x24,0x5c,0xc2,0xd3,0xac,0x62,0x91,0x95,0xe4,0x79,
            0xe7,0xc8,0x37,0x6d,0x8d,0xd5,0x4e,0xa9,0x6c,0x56,0xf4,0xea,0x65,0x7a,0xae,0x08,
            0xba,0x78,0x25,0x2e,0x1c,0xa6,0xb4,0xc6,0xe8,0xdd,0x74,0x1f,0x4b,0xbd,0x8b,0x8a,
            0x70,0x3e,0xb5,0x66,0x48,0x03,0xf6,0x0e,0x61,0x35,0x57,0xb9,0x86,0xc1,0x1d,0x9e,
            0xe1,0xf8,0x98,0x11,0x69,0xd9,0x8e,0x94,0x9b,0x1e,0x87,0xe9,0xce,0x55,0x28,0xdf,
            0x8c,0xa1,0x89,0x0d,0xbf,0xe6,0x42,0x68,0x41,0x99,0x2d,0x0f,0xb0,0x54,0xbb,0x16
        };

        private static readonly byte[] Rcon = new byte[] { 0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80,0x1b,0x36 };

        public AesCore(byte[] key)
        {
            if (key.Length == 16) { rounds = 10; Expand(key, 4, 10); }
            else if (key.Length == 32) { rounds = 14; Expand(key, 8, 14); }
            else throw new Exception("AES key must be 16 or 32 bytes");
        }

        private void Expand(byte[] key, int nk, int nr)
        {
            int totalWords = 4 * (nr + 1);
            rk = new byte[16 * totalWords];
            Array.Copy(key, rk, key.Length);   // 16 (AES-128) or 32 (AES-256)
            byte[] temp = new byte[4];
            int wordIndex = nk;            // words already filled (first Nk from key)
            int rconIter = 0;
            while (wordIndex < totalWords)
            {
                // temp = w[wordIndex - 1]
                int prev = (wordIndex - 1) * 4;
                for (int i = 0; i < 4; i++) temp[i] = rk[prev + i];

                if (wordIndex % nk == 0)
                {
                    // RotWord + SubWord + Rcon[wordIndex / Nk]
                    byte t = temp[0];
                    temp[0] = (byte)(Sbox[temp[1]] ^ Rcon[rconIter]);
                    temp[1] = Sbox[temp[2]];
                    temp[2] = Sbox[temp[3]];
                    temp[3] = Sbox[t];
                    rconIter++;
                }
                else if (nk > 6 && (wordIndex % nk) == 4)
                {
                    // SubWord only (AES-256 extra transform)
                    temp[0] = Sbox[temp[0]];
                    temp[1] = Sbox[temp[1]];
                    temp[2] = Sbox[temp[2]];
                    temp[3] = Sbox[temp[3]];
                }

                // w[wordIndex] = w[wordIndex - Nk] xor temp
                int dst = wordIndex * 4;
                int src = (wordIndex - nk) * 4;
                for (int i = 0; i < 4; i++)
                    rk[dst + i] = (byte)(rk[src + i] ^ temp[i]);
                wordIndex++;
            }
        }

        /// <summary>Encrypt one 16-byte block in place-safe fashion.</summary>
        public void EncryptBlock(byte[] input, int inOff, byte[] output, int outOff)
        {
            byte[] s = new byte[16];
            Array.Copy(input, inOff, s, 0, 16);
            AddRoundKey(s, 0);
            for (int round = 1; round < rounds; round++)
            {
                SubBytes(s);
                ShiftRows(s);
                MixColumns(s);
                AddRoundKey(s, round);
            }
            SubBytes(s);
            ShiftRows(s);
            AddRoundKey(s, rounds);
            Array.Copy(s, 0, output, outOff, 16);
        }

        private void AddRoundKey(byte[] s, int round)
        {
            int off = round * 16;
            for (int i = 0; i < 16; i++) s[i] ^= rk[off + i];
        }

        private static void SubBytes(byte[] s) { for (int i = 0; i < 16; i++) s[i] = Sbox[s[i]]; }

        // State is column-major: s[r + 4c]. ShiftRows rotates row r left by r.
        private static void ShiftRows(byte[] s)
        {
            byte t;
            t = s[1]; s[1] = s[5]; s[5] = s[9]; s[9] = s[13]; s[13] = t;
            t = s[2]; s[2] = s[10]; s[10] = t; t = s[6]; s[6] = s[14]; s[14] = t;
            t = s[3]; s[3] = s[15]; s[15] = s[11]; s[11] = s[7]; s[7] = t;
        }

        private static void MixColumns(byte[] s)
        {
            for (int c = 0; c < 4; c++)
            {
                int i = c * 4;
                byte a0 = s[i], a1 = s[i + 1], a2 = s[i + 2], a3 = s[i + 3];
                s[i] = (byte)(Xt(a0) ^ Xt(a1) ^ a1 ^ a2 ^ a3);
                s[i + 1] = (byte)(a0 ^ Xt(a1) ^ Xt(a2) ^ a2 ^ a3);
                s[i + 2] = (byte)(a0 ^ a1 ^ Xt(a2) ^ Xt(a3) ^ a3);
                s[i + 3] = (byte)(Xt(a0) ^ a0 ^ a1 ^ a2 ^ Xt(a3));
            }
        }

        // xtime: multiply by 2 in GF(2^8).
        private static byte Xt(byte a)
        {
            return (byte)((a << 1) ^ ((a & 0x80) != 0 ? 0x1b : 0));
        }
    }

    /// <summary>AES-GCM AEAD (RFC 5116 / 5288). Seal/Open use the TLS nonce
    /// convention: implicit 4-byte salt + 8-byte explicit sequence number.</summary>
    public static class AesGcm
    {
        private static void MultGF(byte[] x, byte[] y, byte[] output)
        {
            // Carryless multiply in GF(2^128) with the GCM reduction polynomial.
            byte[] z = new byte[16];
            byte[] v = (byte[])y.Clone();
            for (int i = 0; i < 128; i++)
            {
                int bit = (x[i / 8] >> (7 - (i % 8))) & 1;
                if (bit == 1)
                {
                    for (int j = 0; j < 16; j++) z[j] ^= v[j];
                }
                bool lsb = (v[15] & 1) != 0;
                for (int j = 15; j > 0; j--) v[j] = (byte)((v[j] >> 1) | ((v[j - 1] & 1) << 7));
                v[0] >>= 1;
                if (lsb) v[0] ^= 0xe1;
            }
            Array.Copy(z, output, 16);
        }

        private static void Ghash(byte[] h, byte[] aad, byte[] ct, byte[] outp)
        {
            byte[] y = new byte[16];

            // GHASH walks AAD then ciphertext, each zero-padded to a whole
            // 16-byte block, then folds in the bit-length block.
            GhashBlocks(aad, h, y);
            GhashBlocks(ct, h, y);

            byte[] lenBlock = new byte[16];
            ulong aadBits = (ulong)aad.Length * 8;
            ulong ctBits = (ulong)ct.Length * 8;
            for (int i = 0; i < 8; i++) lenBlock[7 - i] = (byte)(aadBits >> (8 * i));
            for (int i = 0; i < 8; i++) lenBlock[15 - i] = (byte)(ctBits >> (8 * i));
            for (int j = 0; j < 16; j++) y[j] ^= lenBlock[j];
            MultGF(y, h, y);

            Array.Copy(y, outp, 16);
        }

        private static void GhashBlocks(byte[] data, byte[] h, byte[] y)
        {
            int len = data.Length;
            int blocks = (len + 15) / 16;
            for (int b = 0; b < blocks; b++)
            {
                byte[] block = new byte[16];
                int start = b * 16;
                int take = Math.Min(16, len - start);
                if (take > 0) Array.Copy(data, start, block, 0, take);
                for (int j = 0; j < 16; j++) y[j] ^= block[j];
                MultGF(y, h, y);
            }
        }

        private static void Inc32(byte[] counter)
        {
            for (int i = 15; i >= 12; i--)
            {
                counter[i]++;
                if (counter[i] != 0) break;
            }
        }

        private static byte[] Ctr(byte[] aesKey, byte[] j0, byte[] data)
        {
            AesCore aes = new AesCore(aesKey);
            byte[] outp = new byte[data.Length];
            byte[] counter = (byte[])j0.Clone();
            byte[] ksBlock = new byte[16];
            for (int i = 0; i < data.Length; i += 16)
            {
                Inc32(counter);
                aes.EncryptBlock(counter, 0, ksBlock, 0);
                int take = Math.Min(16, data.Length - i);
                for (int j = 0; j < take; j++) outp[i + j] = (byte)(data[i + j] ^ ksBlock[j]);
            }
            return outp;
        }

        /// <summary>Encrypt and tag. Returns ciphertext || 16-byte tag.</summary>
        public static byte[] Seal(byte[] aesKey, byte[] nonce12, byte[] aad, byte[] plaintext)
        {
            AesCore aes = new AesCore(aesKey);
            byte[] h = new byte[16];
            aes.EncryptBlock(new byte[16], 0, h, 0);

            byte[] j0 = new byte[16];
            Array.Copy(nonce12, j0, 12);
            j0[15] = 1;

            byte[] ct = Ctr(aesKey, j0, plaintext);
            byte[] s = new byte[16];
            Ghash(h, aad, ct, s);

            byte[] ek = new byte[16];
            aes.EncryptBlock(j0, 0, ek, 0);
            byte[] tag = new byte[16];
            for (int i = 0; i < 16; i++) tag[i] = (byte)(s[i] ^ ek[i]);

            byte[] result = new byte[ct.Length + 16];
            Array.Copy(ct, result, ct.Length);
            Array.Copy(tag, 0, result, ct.Length, 16);
            return result;
        }

        /// <summary>Verify tag and decrypt. Returns plaintext, or null on auth failure.</summary>
        public static byte[] Open(byte[] aesKey, byte[] nonce12, byte[] aad, byte[] ctAndTag)
        {
            if (ctAndTag.Length < 16) return null;
            int ctLen = ctAndTag.Length - 16;
            byte[] ct = new byte[ctLen];
            Array.Copy(ctAndTag, ct, ctLen);
            byte[] tag = new byte[16];
            Array.Copy(ctAndTag, ctLen, tag, 0, 16);

            AesCore aes = new AesCore(aesKey);
            byte[] h = new byte[16];
            aes.EncryptBlock(new byte[16], 0, h, 0);

            byte[] j0 = new byte[16];
            Array.Copy(nonce12, j0, 12);
            j0[15] = 1;

            byte[] s = new byte[16];
            Ghash(h, aad, ct, s);
            byte[] ek = new byte[16];
            aes.EncryptBlock(j0, 0, ek, 0);
            byte[] expect = new byte[16];
            for (int i = 0; i < 16; i++) expect[i] = (byte)(s[i] ^ ek[i]);

            int diff = 0;
            for (int i = 0; i < 16; i++) diff |= expect[i] ^ tag[i];
            if (diff != 0) return null;

            return Ctr(aesKey, j0, ct);
        }
    }
}
