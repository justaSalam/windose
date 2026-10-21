using System;

// Hash primitives for the native TLS stack (stage 6): SHA-256, SHA-512 and
// SHA-384 (a truncated SHA-512 with its own IV), plus HMAC in both strengths.
// TLS 1.2's PRF is P_SHA256 or P_SHA384 depending on the cipher suite, and the
// handshake transcript is hashed with the suite's digest. Everything is
// Cosmos-safe: no LINQ, no regex, no culture-aware parsing.
namespace Windose.Web.Tls
{
    /// <summary>Incremental SHA-256. Clone() lets the TLS engine fork the
    /// transcript hash without disturbing the running state.</summary>
    public sealed class Sha256
    {
        private static readonly uint[] K = new uint[]
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        private uint[] h = new uint[8];
        private byte[] buf = new byte[64];
        private int bufLen;
        private ulong totalLen;

        public Sha256()
        {
            h[0] = 0x6a09e667; h[1] = 0xbb67ae85; h[2] = 0x3c6ef372; h[3] = 0xa54ff53a;
            h[4] = 0x510e527f; h[5] = 0x9b05688c; h[6] = 0x1f83d9ab; h[7] = 0x5be0cd19;
        }

        private Sha256(Sha256 o)
        {
            h = (uint[])o.h.Clone();
            buf = (byte[])o.buf.Clone();
            bufLen = o.bufLen;
            totalLen = o.totalLen;
        }

        public Sha256 Clone() { return new Sha256(this); }

        public void Update(byte[] data) { if (data != null) Update(data, 0, data.Length); }

        public void Update(byte[] data, int off, int len)
        {
            totalLen += (ulong)len;
            while (len > 0)
            {
                int take = 64 - bufLen;
                if (take > len) take = len;
                Array.Copy(data, off, buf, bufLen, take);
                bufLen += take; off += take; len -= take;
                if (bufLen == 64) { Compress(buf, 0); bufLen = 0; }
            }
        }

        public byte[] Digest()
        {
            ulong bits = totalLen * 8;
            byte[] pad = new byte[64 - ((totalLen + 9) % 64) + 9];
            if (pad.Length == 0) pad = new byte[72];
            pad[0] = 0x80;
            for (int i = 0; i < 8; i++) pad[pad.Length - 1 - i] = (byte)(bits >> (8 * i));
            Update(pad, 0, pad.Length);

            byte[] outp = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                outp[i * 4] = (byte)(h[i] >> 24);
                outp[i * 4 + 1] = (byte)(h[i] >> 16);
                outp[i * 4 + 2] = (byte)(h[i] >> 8);
                outp[i * 4 + 3] = (byte)h[i];
            }
            return outp;
        }

        public static byte[] Hash(byte[] data) { Sha256 s = new Sha256(); s.Update(data); return s.Digest(); }

        private void Compress(byte[] block, int off)
        {
            uint[] w = new uint[64];
            for (int i = 0; i < 16; i++)
            {
                int p = off + i * 4;
                w[i] = ((uint)block[p] << 24) | ((uint)block[p + 1] << 16) | ((uint)block[p + 2] << 8) | block[p + 3];
            }
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rot(w[i - 15], 7) ^ Rot(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Rot(w[i - 2], 17) ^ Rot(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 64; i++)
            {
                uint S1 = Rot(e, 6) ^ Rot(e, 11) ^ Rot(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = hh + S1 + ch + K[i] + w[i];
                uint S0 = Rot(a, 2) ^ Rot(a, 13) ^ Rot(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint t2 = S0 + maj;
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }

        private static uint Rot(uint x, int n) { return (x >> n) | (x << (32 - n)); }
    }

    /// <summary>SHA-512 core with pluggable IV so SHA-384 is a truncated
    /// variant with its own starting state (FIPS 180-4).</summary>
    public sealed class Sha512Core
    {
        private static readonly ulong[] K = new ulong[]
        {
            0x428a2f98d728ae22UL, 0x7137449123ef65cdUL, 0xb5c0fbcfec4d3b2fUL, 0xe9b5dba58189dbbcUL,
            0x3956c25bf348b538UL, 0x59f111f1b605d019UL, 0x923f82a4af194f9bUL, 0xab1c5ed5da6d8118UL,
            0xd807aa98a3030242UL, 0x12835b0145706fbeUL, 0x243185be4ee4b28cUL, 0x550c7dc3d5ffb4e2UL,
            0x72be5d74f27b896fUL, 0x80deb1fe3b1696b1UL, 0x9bdc06a725c71235UL, 0xc19bf174cf692694UL,
            0xe49b69c19ef14ad2UL, 0xefbe4786384f25e3UL, 0x0fc19dc68b8cd5b5UL, 0x240ca1cc77ac9c65UL,
            0x2de92c6f592b0275UL, 0x4a7484aa6ea6e483UL, 0x5cb0a9dcbd41fbd4UL, 0x76f988da831153b5UL,
            0x983e5152ee66dfabUL, 0xa831c66d2db43210UL, 0xb00327c898fb213fUL, 0xbf597fc7beef0ee4UL,
            0xc6e00bf33da88fc2UL, 0xd5a79147930aa725UL, 0x06ca6351e003826fUL, 0x142929670a0e6e70UL,
            0x27b70a8546d22ffcUL, 0x2e1b21385c26c926UL, 0x4d2c6dfc5ac42aedUL, 0x53380d139d95b3dfUL,
            0x650a73548baf63deUL, 0x766a0abb3c77b2a8UL, 0x81c2c92e47edaee6UL, 0x92722c851482353bUL,
            0xa2bfe8a14cf10364UL, 0xa81a664bbc423001UL, 0xc24b8b70d0f89791UL, 0xc76c51a30654be30UL,
            0xd192e819d6ef5218UL, 0xd69906245565a910UL, 0xf40e35855771202aUL, 0x106aa07032bbd1b8UL,
            0x19a4c116b8d2d0c8UL, 0x1e376c085141ab53UL, 0x2748774cdf8eeb99UL, 0x34b0bcb5e19b48a8UL,
            0x391c0cb3c5c95a63UL, 0x4ed8aa4ae3418acbUL, 0x5b9cca4f7763e373UL, 0x682e6ff3d6b2b8a3UL,
            0x748f82ee5defb2fcUL, 0x78a5636f43172f60UL, 0x84c87814a1f0ab72UL, 0x8cc702081a6439ecUL,
            0x90befffa23631e28UL, 0xa4506cebde82bde9UL, 0xbef9a3f7b2c67915UL, 0xc67178f2e372532bUL,
            0xca273eceea26619cUL, 0xd186b8c721c0c207UL, 0xeada7dd6cde0eb1eUL, 0xf57d4f7fee6ed178UL,
            0x06f067aa72176fbaUL, 0x0a637dc5a2c898a6UL, 0x113f9804bef90daeUL, 0x1b710b35131c471bUL,
            0x28db77f523047d84UL, 0x32caab7b40c72493UL, 0x3c9ebe0a15c9bebcUL, 0x431d67c49c100d4cUL,
            0x4cc5d4becb3e42b6UL, 0x597f299cfc657e2aUL, 0x5fcb6fab3ad6faecUL, 0x6c44198c4a475817UL
        };

        private ulong[] h = new ulong[8];
        private byte[] buf = new byte[128];
        private int bufLen;
        private ulong totalLenLo;
        private int outBytes;

        public Sha512Core(ulong[] iv, int outBytes)
        {
            h = (ulong[])iv.Clone();
            this.outBytes = outBytes;
        }

        public void Update(byte[] data, int off, int len)
        {
            // 128-bit big-endian length; our messages stay far under 2^61 bytes.
            totalLenLo += (ulong)len;
            while (len > 0)
            {
                int take = 128 - bufLen;
                if (take > len) take = len;
                Array.Copy(data, off, buf, bufLen, take);
                bufLen += take; off += take; len -= take;
                if (bufLen == 128) { Compress(buf, 0); bufLen = 0; }
            }
        }

        public byte[] Digest()
        {
            ulong bitsLo = totalLenLo << 3;
            // Padded block(s): message + 0x80 + zeros + 16-byte big-endian
            // length must total a multiple of 128 bytes.
            int msgLen = (int)(totalLenLo % 128);
            int temp = (msgLen + 16) % 128;
            int padLen = (temp == 0) ? 128 : (128 - temp);   // >= 1, room for 0x80
            byte[] pad = new byte[padLen + 16];
            pad[0] = 0x80;
            // 128-bit length: high 64 bits are zero for our sizes.
            for (int i = 0; i < 8; i++) pad[pad.Length - 1 - i] = (byte)(bitsLo >> (8 * i));
            Update(pad, 0, pad.Length);

            byte[] outp = new byte[64];
            for (int i = 0; i < 8; i++)
            {
                for (int b = 0; b < 8; b++) outp[i * 8 + b] = (byte)(h[i] >> (56 - 8 * b));
            }
            if (outBytes < 64)
            {
                byte[] t = new byte[outBytes];
                Array.Copy(outp, t, outBytes);
                return t;
            }
            return outp;
        }

        private void Compress(byte[] block, int off)
        {
            ulong[] w = new ulong[80];
            for (int i = 0; i < 16; i++)
            {
                int p = off + i * 8;
                ulong v = 0;
                for (int bi = 0; bi < 8; bi++) v = (v << 8) | block[p + bi];
                w[i] = v;
            }
            for (int i = 16; i < 80; i++)
            {
                ulong s0 = Rot(w[i - 15], 1) ^ Rot(w[i - 15], 8) ^ (w[i - 15] >> 7);
                ulong s1 = Rot(w[i - 2], 19) ^ Rot(w[i - 2], 61) ^ (w[i - 2] >> 6);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 80; i++)
            {
                ulong S1 = Rot(e, 14) ^ Rot(e, 18) ^ Rot(e, 41);
                ulong ch = (e & f) ^ (~e & g);
                ulong t1 = hh + S1 + ch + K[i] + w[i];
                ulong S0 = Rot(a, 28) ^ Rot(a, 34) ^ Rot(a, 39);
                ulong maj = (a & b) ^ (a & c) ^ (b & c);
                ulong t2 = S0 + maj;
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }

        private static ulong Rot(ulong x, int n) { return (x >> n) | (x << (64 - n)); }
    }

    public static class Sha512
    {
        private static readonly ulong[] IV = new ulong[]
        {
            0x6a09e667f3bcc908UL, 0xbb67ae8584caa73bUL, 0x3c6ef372fe94f82bUL, 0xa54ff53a5f1d36f1UL,
            0x510e527fade682d1UL, 0x9b05688c2b3e6c1fUL, 0x1f83d9abfb41bd6bUL, 0x5be0cd19137e2179UL
        };
        public static byte[] Hash(byte[] data)
        {
            Sha512Core c = new Sha512Core(IV, 64);
            if (data != null) c.Update(data, 0, data.Length);
            return c.Digest();
        }
    }

    public static class Sha384
    {
        private static readonly ulong[] IV = new ulong[]
        {
            0xcbbb9d5dc1059ed8UL, 0x629a292a367cd507UL, 0x9159015a3070dd17UL, 0x152fecd8f70e5939UL,
            0x67332667ffc00b31UL, 0x8eb44a8768581511UL, 0xdb0c2e0d64f98fa7UL, 0x47b5481dbefa4fa4UL
        };
        public static byte[] Hash(byte[] data)
        {
            Sha512Core c = new Sha512Core(IV, 48);
            if (data != null) c.Update(data, 0, data.Length);
            return c.Digest();
        }
    }

    /// <summary>HMAC over a 64-byte (SHA-256) or 128-byte (SHA-512/384) block.
    /// Precomputes the inner and outer padded keys so P_hash can hammer it.</summary>
    public sealed class HmacSha256
    {
        private byte[] padI;
        private byte[] padO;

        public HmacSha256(byte[] key)
        {
            byte[] k = new byte[64];
            if (key != null && key.Length > 64) key = Sha256.Hash(key);
            if (key != null) Array.Copy(key, k, key.Length);
            padI = new byte[64]; padO = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                padI[i] = (byte)(k[i] ^ 0x36);
                padO[i] = (byte)(k[i] ^ 0x5c);
            }
        }

        public byte[] Compute(byte[] data)
        {
            Sha256 inner = new Sha256();
            inner.Update(padI);
            inner.Update(data);
            byte[] iHash = inner.Digest();
            Sha256 outer = new Sha256();
            outer.Update(padO);
            outer.Update(iHash);
            return outer.Digest();
        }
    }

    public sealed class HmacSha384
    {
        private byte[] padI;
        private byte[] padO;

        public HmacSha384(byte[] key)
        {
            byte[] k = new byte[128];
            if (key != null && key.Length > 128) key = Sha384.Hash(key);
            if (key != null) Array.Copy(key, k, key.Length);
            padI = new byte[128]; padO = new byte[128];
            for (int i = 0; i < 128; i++)
            {
                padI[i] = (byte)(k[i] ^ 0x36);
                padO[i] = (byte)(k[i] ^ 0x5c);
            }
        }

        public byte[] Compute(byte[] data)
        {
            Sha512Core inner = new Sha512Core(Sha384Iv(), 48);
            inner.Update(padI, 0, padI.Length);
            inner.Update(data, 0, data.Length);
            byte[] iHash = inner.Digest();
            Sha512Core outer = new Sha512Core(Sha384Iv(), 48);
            outer.Update(padO, 0, padO.Length);
            outer.Update(iHash, 0, iHash.Length);
            return outer.Digest();
        }

        private static ulong[] Sha384Iv()
        {
            return new ulong[]
            {
                0xcbbb9d5dc1059ed8UL, 0x629a292a367cd507UL, 0x9159015a3070dd17UL, 0x152fecd8f70e5939UL,
                0x67332667ffc00b31UL, 0x8eb44a8768581511UL, 0xdb0c2e0d64f98fa7UL, 0x47b5481dbefa4fa4UL
            };
        }
    }
}
