using System;

// secp256r1 (NIST P-256) arithmetic for the TLS 1.2 ECDHE key exchange.
//
// The prime and group order are baked in as byte arrays so there is no
// culture-aware number parsing anywhere. Field elements are stored as eight
// 32-bit limbs, little-endian. Reduction uses a simple, obviously-correct
// bit-by-bit shift-and-subtract loop over the 512-bit product -- O(bits) but
// trivially verifiable, and fast enough for a couple of scalar multiplications
// per handshake.
//
// Point multiplication runs in Jacobian coordinates (one field inversion at the
// very end), which keeps the per-handshake cost to ~20M integer operations.
namespace Windose.Web.Tls
{
    /// <summary>Immutable-ish 256-bit field arithmetic over the P-256 prime.
    /// All operations return fresh arrays; callers never alias inputs.</summary>
    public static class P256
    {
        // p = 2^256 - 2^224 + 2^192 + 2^96 - 1
        public static readonly byte[] PrimeBytes = Hex(
            "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");

        // Group order n.
        public static readonly byte[] OrderBytes = Hex(
            "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");

        // Generator point G (uncompressed, without the 0x04 prefix).
        public static readonly byte[] Gx = Hex(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
        public static readonly byte[] Gy = Hex(
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

        // Curve coefficient a = -3 (P-256 has a = -3, which lets doubling skip
        // a generic multiply by a).

        private static byte[] Hex(string s)
        {
            byte[] r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++)
                r[i] = (byte)((HexVal(s[i * 2]) << 4) | HexVal(s[i * 2 + 1]));
            return r;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }

        // ---- big-endian bytes <-> little-endian 32-bit limbs -------------

        public static uint[] ToLimbs(byte[] be)
        {
            // Accept up to 32 bytes, big-endian; shorter input is right-aligned
            // (zero-padded) so callers can pass small constants like {3}.
            uint[] r = new uint[8];
            if (be == null) return r;
            int n = be.Length;
            if (n > 32) n = 32;
            // map each byte to (limb, shift) with right-alignment
            for (int i = 0; i < n; i++)
            {
                // byte be[i] is the i-th most-significant of n bytes
                int fromLsb = n - 1 - i;      // 0 = least-significant byte
                int limb = fromLsb / 4;
                int shift = (fromLsb % 4) * 8;
                r[limb] |= (uint)be[i] << shift;
            }
            return r;
        }

        public static byte[] ToBytes(uint[] limbs)
        {
            byte[] r = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                int limb = (31 - i) / 4;
                int shift = ((31 - i) % 4) * 8;
                r[i] = (byte)(limbs[limb] >> shift);
            }
            return r;
        }

        private static uint[] PrimeLimbs() { return ToLimbs(PrimeBytes); }

        // ---- 512-bit product and reduction -------------------------------

        // Multiply two 8-limb numbers into a 16-limb product.
        private static void MulRaw(uint[] a, uint[] b, uint[] prod)
        {
            for (int i = 0; i < 16; i++) prod[i] = 0;
            for (int i = 0; i < 8; i++)
            {
                ulong carry = 0;
                for (int j = 0; j < 8; j++)
                {
                    ulong cur = prod[i + j] + (ulong)a[i] * b[j] + carry;
                    prod[i + j] = (uint)cur;
                    carry = cur >> 32;
                }
                prod[i + 8] += (uint)carry;
            }
        }

        // Reduce a 16-limb (512-bit) value modulo m (8 limbs) by walking every
        // bit from the top and keeping an < m remainder. Returns 8 limbs.
        private static uint[] Reduce512(uint[] value, uint[] m)
        {
            // remainder as 9 limbs (needs one extra bit while shifting)
            uint[] r = new uint[9];
            // value has 512 bits; scan from bit 511 down to 0
            for (int bit = 511; bit >= 0; bit--)
            {
                // r <<= 1
                int carry = 0;
                for (int i = 0; i < 9; i++)
                {
                    int v = (int)((r[i] >> 31) & 1);
                    r[i] = (r[i] << 1) | (uint)carry;
                    carry = v;
                }
                // bring in value's bit
                r[0] |= (uint)((value[bit / 32] >> (bit % 32)) & 1);
                // if r >= m, subtract m
                if (Cmp(r, m) >= 0) SubInPlace(r, m);
            }
            uint[] outp = new uint[8];
            Array.Copy(r, outp, 8);
            return outp;
        }

        // Compare 9-limb a against 8-limb m.
        private static int Cmp(uint[] a, uint[] m)
        {
            if (a[8] != 0) return 1;
            for (int i = 7; i >= 0; i--)
            {
                if (a[i] != m[i]) return a[i] > m[i] ? 1 : -1;
            }
            return 0;
        }

        private static void SubInPlace(uint[] a, uint[] m)
        {
            long borrow = 0;
            for (int i = 0; i < 8; i++)
            {
                long d = (long)a[i] - m[i] - borrow;
                if (d < 0) { d += 1L << 32; borrow = 1; }
                else borrow = 0;
                a[i] = (uint)d;
            }
            // a[8] should already be 0 when a >= m < 2^256
            a[8] -= (uint)borrow;
        }

        // ---- modular primitives -----------------------------------------

        public static uint[] MulMod(uint[] a, uint[] b, uint[] m)
        {
            uint[] prod = new uint[16];
            MulRaw(a, b, prod);
            return Reduce512(prod, m);
        }

        public static uint[] AddMod(uint[] a, uint[] b, uint[] m)
        {
            uint[] r = new uint[9];
            ulong carry = 0;
            for (int i = 0; i < 8; i++)
            {
                ulong s = (ulong)a[i] + b[i] + carry;
                r[i] = (uint)s;
                carry = s >> 32;
            }
            r[8] = (uint)carry;
            while (Cmp(r, m) >= 0) SubInPlace(r, m);
            uint[] outp = new uint[8];
            Array.Copy(r, outp, 8);
            return outp;
        }

        public static uint[] SubMod(uint[] a, uint[] b, uint[] m)
        {
            uint[] r = new uint[9];
            long borrow = 0;
            for (int i = 0; i < 8; i++)
            {
                long d = (long)a[i] - b[i] - borrow;
                if (d < 0) { d += 1L << 32; borrow = 1; }
                else borrow = 0;
                r[i] = (uint)d;
            }
            if (borrow != 0)
            {
                // add m (once is enough since a,b < m)
                ulong c = 0;
                for (int i = 0; i < 8; i++)
                {
                    ulong s = (ulong)r[i] + m[i] + c;
                    r[i] = (uint)s;
                    c = s >> 32;
                }
            }
            uint[] outp = new uint[8];
            Array.Copy(r, outp, 8);
            return outp;
        }

        public static bool IsZero(uint[] a)
        {
            for (int i = 0; i < 8; i++) if (a[i] != 0) return false;
            return true;
        }

        // a^(e) mod m via square-and-multiply. e is an 8-limb number.
        public static uint[] ModExp(uint[] a, uint[] e, uint[] m)
        {
            uint[] result = new uint[8];
            result[0] = 1;                       // 1 (safe for odd/any m > 1)
            uint[] baseCur = (uint[])a.Clone();
            for (int bit = 0; bit < 256; bit++)
            {
                int limb = bit / 32;
                int off = bit % 32;
                if (((e[limb] >> off) & 1) != 0)
                    result = MulMod(result, baseCur, m);
                baseCur = MulMod(baseCur, baseCur, m);
            }
            return result;
        }

        // Modular inverse via Fermat: a^(m-2) mod m (m prime).
        public static uint[] InvMod(uint[] a, uint[] m)
        {
            uint[] e = (uint[])m.Clone();
            // e = m - 2
            long borrow = 2;
            for (int i = 0; i < 8 && borrow != 0; i++)
            {
                long d = (long)e[i] - borrow;
                if (d < 0) { d += 1L << 32; borrow = 1; }
                else { borrow = 0; }
                e[i] = (uint)d;
            }
            return ModExp(a, e, m);
        }

        // ---- Jacobian point arithmetic ----------------------------------
        // A point is (X, Y, Z); affine = (X/Z^2, Y/Z^3). Z = 0 is infinity.

        public sealed class Point
        {
            public uint[] X, Y, Z;
            public Point(uint[] x, uint[] y, uint[] z) { X = x; Y = y; Z = z; }
        }

        public static Point Infinity()
        {
            return new Point(new uint[8], new uint[8], new uint[8]);
        }

        public static bool IsInfinity(Point p)
        {
            return IsZero(p.Z);
        }

        public static Point FromAffine(byte[] x32, byte[] y32)
        {
            return new Point(ToLimbs(x32), ToLimbs(y32), One());
        }

        private static uint[] One()
        {
            uint[] r = new uint[8];
            r[0] = 1;
            return r;
        }

        private static uint[] Zero() { return new uint[8]; }

        public static Point Double(Point p, uint[] mod)
        {
            if (IsInfinity(p) || IsZero(p.Y)) return Infinity();
            uint[] X = p.X, Y = p.Y, Z = p.Z;

            uint[] XX = MulMod(X, X, mod);
            uint[] YY = MulMod(Y, Y, mod);
            uint[] YYYY = MulMod(YY, YY, mod);
            uint[] ZZ = MulMod(Z, Z, mod);

            // S = 2*((X + YY)^2 - XX - YYYY)  == 4*X*YY
            uint[] t1 = AddMod(X, YY, mod);
            uint[] t2 = MulMod(t1, t1, mod);
            t2 = SubMod(t2, XX, mod);
            t2 = SubMod(t2, YYYY, mod);
            uint[] S = AddMod(t2, t2, mod);

            // M = 3*XX - 3*ZZ^2  (a = -3)
            uint[] ZZ2 = MulMod(ZZ, ZZ, mod);
            uint[] M = AddMod(AddMod(XX, XX, mod), XX, mod);   // 3*XX
            uint[] threeZZ2 = AddMod(AddMod(ZZ2, ZZ2, mod), ZZ2, mod);
            M = SubMod(M, threeZZ2, mod);

            // T = M^2 - 2*S
            uint[] MM = MulMod(M, M, mod);
            uint[] twoS = AddMod(S, S, mod);
            uint[] T = SubMod(MM, twoS, mod);

            uint[] X3 = T;
            // Y3 = M*(S - T) - 8*YYYY
            uint[] Y3 = MulMod(M, SubMod(S, T, mod), mod);
            uint[] eightYYYY = ShiftLeft3(YYYY, mod);          // 8*YYYY
            Y3 = SubMod(Y3, eightYYYY, mod);
            // Z3 = (Y + Z)^2 - YY - ZZ
            uint[] yz = AddMod(Y, Z, mod);
            uint[] Z3 = MulMod(yz, yz, mod);
            Z3 = SubMod(Z3, YY, mod);
            Z3 = SubMod(Z3, ZZ, mod);

            return new Point(X3, Y3, Z3);
        }

        private static uint[] ShiftLeft3(uint[] a, uint[] mod)
        {
            // 8*a = ((a*2)*2)*2
            uint[] r = AddMod(a, a, mod);
            r = AddMod(r, r, mod);
            r = AddMod(r, r, mod);
            return r;
        }

        public static Point Add(Point p, Point q, uint[] mod)
        {
            if (IsInfinity(p)) return ClonePoint(q);
            if (IsInfinity(q)) return ClonePoint(p);

            uint[] Z1Z1 = MulMod(p.Z, p.Z, mod);
            uint[] Z2Z2 = MulMod(q.Z, q.Z, mod);
            uint[] U1 = MulMod(p.X, Z2Z2, mod);
            uint[] U2 = MulMod(q.X, Z1Z1, mod);
            // S1 = Y1*Z2^3, S2 = Y2*Z1^3  (add-2007-bl)
            uint[] s1Mul = MulMod(q.Z, Z2Z2, mod);   // Z2 * Z2^2
            uint[] s2Mul = MulMod(p.Z, Z1Z1, mod);   // Z1 * Z1^2
            uint[] S1 = MulMod(p.Y, s1Mul, mod);
            uint[] S2 = MulMod(q.Y, s2Mul, mod);

            uint[] H = SubMod(U2, U1, mod);
            uint[] r = SubMod(S2, S1, mod);

            if (IsZero(H))
            {
                if (IsZero(r)) return Double(p, mod);
                return Infinity();
            }

            uint[] twoH = AddMod(H, H, mod);
            uint[] I = MulMod(twoH, twoH, mod);
            uint[] J = MulMod(H, I, mod);
            uint[] twoR = AddMod(r, r, mod);
            uint[] V = MulMod(U1, I, mod);

            uint[] X3 = MulMod(twoR, twoR, mod);
            X3 = SubMod(X3, J, mod);
            uint[] twoV = AddMod(V, V, mod);
            X3 = SubMod(X3, twoV, mod);

            uint[] Y3 = MulMod(twoR, SubMod(V, X3, mod), mod);
            uint[] S1J = MulMod(S1, J, mod);
            uint[] twoS1J = AddMod(S1J, S1J, mod);
            Y3 = SubMod(Y3, twoS1J, mod);

            uint[] z1z2 = AddMod(p.Z, q.Z, mod);
            uint[] Z3 = MulMod(z1z2, z1z2, mod);
            Z3 = SubMod(Z3, Z1Z1, mod);
            Z3 = SubMod(Z3, Z2Z2, mod);
            Z3 = MulMod(Z3, H, mod);

            return new Point(X3, Y3, Z3);
        }

        private static Point ClonePoint(Point p)
        {
            return new Point((uint[])p.X.Clone(), (uint[])p.Y.Clone(), (uint[])p.Z.Clone());
        }

        // Scalar multiplication: k * P. k is a big-endian byte array (<= 32 bytes).
        public static Point Mul(Point p, byte[] k, uint[] mod)
        {
            Point result = Infinity();
            Point addend = ClonePoint(p);
            for (int i = k.Length * 8 - 1; i >= 0; i--)
            {
                int bit = (k[k.Length - 1 - i / 8] >> (i % 8)) & 1;
                // process bits from most-significant downward
                result = Double(result, mod);
                if (bit == 1) result = Add(result, addend, mod);
            }
            return result;
        }

        // Convert Jacobian to affine; returns null if infinity.
        public static byte[][] ToAffine(Point p, uint[] mod)
        {
            if (IsInfinity(p)) return null;
            uint[] zInv = InvMod(p.Z, mod);
            uint[] zInv2 = MulMod(zInv, zInv, mod);
            uint[] zInv3 = MulMod(zInv2, zInv, mod);
            uint[] ax = MulMod(p.X, zInv2, mod);
            uint[] ay = MulMod(p.Y, zInv3, mod);
            return new byte[][] { ToBytes(ax), ToBytes(ay) };
        }

        // ---- ECDH -------------------------------------------------------

        /// <summary>Compute the shared secret (affine X, 32 bytes) from our
        /// private scalar and the peer's uncompressed point (65 bytes, 0x04||X||Y
        /// or raw 64-byte X||Y).</summary>
        public static byte[] SharedSecret(byte[] privScalar, byte[] peerPoint)
        {
            uint[] mod = PrimeLimbs();
            byte[] px, py;
            if (peerPoint.Length == 65 && peerPoint[0] == 0x04)
            {
                px = new byte[32];
                py = new byte[32];
                Array.Copy(peerPoint, 1, px, 0, 32);
                Array.Copy(peerPoint, 33, py, 0, 32);
            }
            else if (peerPoint.Length == 64)
            {
                px = new byte[32];
                py = new byte[32];
                Array.Copy(peerPoint, 0, px, 0, 32);
                Array.Copy(peerPoint, 32, py, 0, 32);
            }
            else throw new Exception("Bad peer point length " + peerPoint.Length);

            Point peer = FromAffine(px, py);
            Point secret = Mul(peer, privScalar, mod);
            byte[][] aff = ToAffine(secret, mod);
            if (aff == null) throw new Exception("ECDH produced point at infinity");
            return aff[0];
        }

        /// <summary>Derive our public point (uncompressed 65 bytes) from a
        /// private scalar.</summary>
        public static byte[] PublicKey(byte[] privScalar)
        {
            uint[] mod = PrimeLimbs();
            Point g = FromAffine(Gx, Gy);
            Point pub = Mul(g, privScalar, mod);
            byte[][] aff = ToAffine(pub, mod);
            if (aff == null) throw new Exception("Generated point at infinity");
            byte[] outp = new byte[65];
            outp[0] = 0x04;
            Array.Copy(aff[0], 0, outp, 1, 32);
            Array.Copy(aff[1], 0, outp, 33, 32);
            return outp;
        }
    }
}
