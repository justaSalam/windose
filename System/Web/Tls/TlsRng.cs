using System;

// Random source for the TLS handshake: the 32-byte ClientHello.random, the
// ephemeral ECDHE private scalar, and any padding. A hobby OS has no OS CSPRNG,
// so this is a SHA-256-based CTR_DRBG seeded from the wall clock plus a process
// unique counter. It is deterministic-strength only -- adequate for a toy OS
// that already documents certificate validation as disabled, but NOT a real
// cryptographic random number generator. Callers should treat it as such.
namespace Windose.Web.Tls
{
    public static class TlsRng
    {
        private static byte[] seed;
        private static ulong counter;
        private static readonly object gate = new object();

        private static void EnsureSeeded()
        {
            if (seed != null) return;
            long ticks = DateTime.UtcNow.Ticks;
            byte[] t = new byte[8];
            for (int i = 0; i < 8; i++) t[i] = (byte)(ticks >> (8 * i));
            byte[] pid = new byte[8];
            // vary by a static counter so repeated seeding still differs
            ulong c = (ulong)Environment.TickCount;
            for (int i = 0; i < 8; i++) pid[i] = (byte)(c >> (8 * i));
            byte[] buf = new byte[16];
            Array.Copy(t, buf, 8);
            Array.Copy(pid, 0, buf, 8, 8);
            seed = Sha256.Hash(buf);
        }

        /// <summary>Fill buf with pseudo-random bytes.</summary>
        public static void Fill(byte[] buf)
        {
            Fill(buf, 0, buf.Length);
        }

        public static void Fill(byte[] buf, int off, int len)
        {
            lock (gate)
            {
                EnsureSeeded();
                int produced = 0;
                while (produced < len)
                {
                    counter++;
                    byte[] blockInput = new byte[seed.Length + 8];
                    Array.Copy(seed, blockInput, seed.Length);
                    for (int i = 0; i < 8; i++)
                        blockInput[seed.Length + i] = (byte)(counter >> (8 * i));
                    byte[] block = Sha256.Hash(blockInput);
                    int take = Math.Min(block.Length, len - produced);
                    Array.Copy(block, 0, buf, off + produced, take);
                    produced += take;
                }
            }
        }

        /// <summary>A 32-byte value suitable for ClientHello.random.</summary>
        public static byte[] NextBytes(int n)
        {
            byte[] b = new byte[n];
            Fill(b);
            return b;
        }
    }
}
