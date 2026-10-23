using System;

// TLS 1.2 pseudorandom function (RFC 5246 §5) and the handshake key schedule.
// P_hash(secret, seed) chains HMAC over A(i) = HMAC(secret, A(i-1)).
// PRF(secret, label, seed) = P_hash(secret, label || seed).
// The suite's bulk/hash algorithm selects SHA-256 or SHA-384.
namespace Windose.Web.Tls
{
    public static class TlsPrf
    {
        /// <summary>hashAlgo: 0 = SHA-256 (P_SHA256), 1 = SHA-384 (P_SHA384).</summary>
        public static byte[] Prf(byte[] secret, string label, byte[] seed, int length, int hashAlgo)
        {
            byte[] labelBytes = Ascii(label);
            byte[] concat = new byte[labelBytes.Length + seed.Length];
            Array.Copy(labelBytes, concat, labelBytes.Length);
            Array.Copy(seed, 0, concat, labelBytes.Length, seed.Length);
            return PHash(secret, concat, length, hashAlgo);
        }

        private static byte[] PHash(byte[] secret, byte[] seed, int length, int hashAlgo)
        {
            byte[] result = new byte[length];
            int done = 0;
            byte[] a = seed;                 // A(0) = seed
            while (done < length)
            {
                // A(i) = HMAC(secret, A(i-1))
                a = Hmac(secret, a, hashAlgo);
                // HMAC(secret, A(i) || seed)
                byte[] blockInput = new byte[a.Length + seed.Length];
                Array.Copy(a, blockInput, a.Length);
                Array.Copy(seed, 0, blockInput, a.Length, seed.Length);
                byte[] block = Hmac(secret, blockInput, hashAlgo);
                int take = Math.Min(block.Length, length - done);
                Array.Copy(block, 0, result, done, take);
                done += take;
            }
            return result;
        }

        public static byte[] Hmac(byte[] secret, byte[] data, int hashAlgo)
        {
            if (hashAlgo == 1) return new HmacSha384(secret).Compute(data);
            return new HmacSha256(secret).Compute(data);
        }

        public static int HashLenFor(int hashAlgo)
        {
            return hashAlgo == 1 ? 48 : 32;
        }

        public static byte[] HkdfExtract(int hashAlgo, byte[] salt, byte[] ikm)
        {
            int hashLen = HashLenFor(hashAlgo);
            if (salt == null || salt.Length == 0) salt = new byte[hashLen];
            if (ikm == null) ikm = new byte[0];
            return Hmac(salt, ikm, hashAlgo);
        }

        public static byte[] HkdfExpand(int hashAlgo, byte[] prk, byte[] info, int length)
        {
            int hashLen = HashLenFor(hashAlgo);
            int n = (length + hashLen - 1) / hashLen;
            byte[] okm = new byte[length];
            byte[] t = new byte[0];
            int outPos = 0;
            for (byte i = 1; i <= n; i++)
            {
                byte[] input = new byte[t.Length + (info != null ? info.Length : 0) + 1];
                Array.Copy(t, 0, input, 0, t.Length);
                if (info != null && info.Length > 0) Array.Copy(info, 0, input, t.Length, info.Length);
                input[input.Length - 1] = i;

                t = Hmac(prk, input, hashAlgo);

                int copyLen = Math.Min(hashLen, length - outPos);
                Array.Copy(t, 0, okm, outPos, copyLen);
                outPos += copyLen;
            }
            return okm;
        }

        public static byte[] HkdfExpandLabel(int hashAlgo, byte[] secret, string label, byte[] context, int length)
        {
            byte[] prefix = Ascii("tls13 " + label);
            if (context == null) context = new byte[0];
            byte[] hkdfLabel = new byte[2 + 1 + prefix.Length + 1 + context.Length];
            hkdfLabel[0] = (byte)(length >> 8);
            hkdfLabel[1] = (byte)length;
            hkdfLabel[2] = (byte)prefix.Length;
            Array.Copy(prefix, 0, hkdfLabel, 3, prefix.Length);
            int pos = 3 + prefix.Length;
            hkdfLabel[pos++] = (byte)context.Length;
            if (context.Length > 0) Array.Copy(context, 0, hkdfLabel, pos, context.Length);

            return HkdfExpand(hashAlgo, secret, hkdfLabel, length);
        }

        public static byte[] BuildTls13Nonce(byte[] iv, ulong seq)
        {
            byte[] nonce = new byte[12];
            Array.Copy(iv, 0, nonce, 0, 12);
            for (int i = 0; i < 8; i++)
            {
                nonce[4 + i] ^= (byte)(seq >> (8 * (7 - i)));
            }
            return nonce;
        }

        public static byte[] Ascii(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        /// <summary>master_secret = PRF(pms, "master secret",
        /// client_random + server_random)[0..47]. (Plain, non-extended,
        /// since we do not offer extended_master_secret.)</summary>
        public static byte[] MasterSecret(byte[] pms, byte[] clientRandom, byte[] serverRandom, int hashAlgo)
        {
            byte[] seed = new byte[64];
            Array.Copy(clientRandom, 0, seed, 0, 32);
            Array.Copy(serverRandom, 0, seed, 32, 32);
            return Prf(pms, "master secret", seed, 48, hashAlgo);
        }

        /// <summary>key_block = PRF(master, "key expansion",
        /// server_random + client_random) (note the reversed order).</summary>
        public static byte[] KeyBlock(byte[] master, byte[] clientRandom, byte[] serverRandom, int hashAlgo, int needed)
        {
            byte[] seed = new byte[64];
            Array.Copy(serverRandom, 0, seed, 0, 32);
            Array.Copy(clientRandom, 0, seed, 32, 32);
            return Prf(master, "key expansion", seed, needed, hashAlgo);
        }

        /// <summary>verify_data = PRF(master, finished_label,
        /// handshake_hash)[0..11]. The label is direction-specific:
        /// "client finished" for our Finished, "server finished" for theirs.</summary>
        public static byte[] FinishedVerifyData(byte[] master, byte[] handshakeHash, int hashAlgo, string label)
        {
            return Prf(master, label, handshakeHash, 12, hashAlgo);
        }
    }
}
