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

        private static byte[] Hmac(byte[] secret, byte[] data, int hashAlgo)
        {
            if (hashAlgo == 1) return new HmacSha384(secret).Compute(data);
            return new HmacSha256(secret).Compute(data);
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
