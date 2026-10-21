using System;

namespace Windose.Web.Tls
{
    public static class TlsContent
    {
        public const byte ChangeCipherSpec = 20;
        public const byte Alert = 21;
        public const byte Handshake = 22;
        public const byte ApplicationData = 23;
    }

    /// <summary>The TLS 1.2 record layer: framing (type + version + length) and,
    /// once keys are active, AES-128/256-GCM protection.
    ///
    /// Nonce = implicit_iv(4, from key block) || sequence_number(8). The same
    /// tracked sequence number feeds both the 12-byte nonce and the 13-byte AAD
    /// header (seq[8] || type || version || plaintext_len[2]), which keeps them
    /// consistent. RFC 5288 puts the sequence number in the record's 8-byte
    /// explicit-nonce field too; we derive it from our counter rather than
    /// trusting the wire copy, since they are equal in practice.</summary>
    public sealed class TlsRecordLayer
    {
        private readonly ITlsTransport transport;

        private byte[] writeKey, writeIv;
        private byte[] readKey, readIv;
        private ulong writeSeq, readSeq;
        private bool writeProtected, readProtected;

        public TlsRecordLayer(ITlsTransport t) { transport = t; }

        public void EnableWriteProtection(byte[] key, byte[] iv)
        {
            writeKey = key; writeIv = iv; writeSeq = 0; writeProtected = true;
        }

        public void EnableReadProtection(byte[] key, byte[] iv)
        {
            readKey = key; readIv = iv; readSeq = 0; readProtected = true;
        }

        // ---- write ------------------------------------------------------

        public void Send(byte contentType, byte[] fragment, bool encrypt)
        {
            byte[] payload;
            if (encrypt && writeProtected)
            {
                byte[] aad = BuildAad(writeSeq, contentType, fragment.Length);
                byte[] nonce = BuildNonce(writeIv, writeSeq);
                byte[] sealedData = AesGcm.Seal(writeKey, nonce, aad, fragment);
                payload = new byte[8 + sealedData.Length];
                for (int i = 0; i < 8; i++) payload[i] = (byte)(writeSeq >> (8 * (7 - i)));
                Array.Copy(sealedData, 0, payload, 8, sealedData.Length);
                writeSeq++;
            }
            else
            {
                payload = fragment;
            }

            byte[] header = new byte[5];
            header[0] = contentType;
            header[1] = 3; header[2] = 3;
            header[3] = (byte)(payload.Length >> 8);
            header[4] = (byte)payload.Length;

            transport.Write(header, 0, 5);
            transport.Write(payload, 0, payload.Length);
        }

        public void SendHandshake(byte[] handshakeMsg, bool encrypt)
        {
            Send(TlsContent.Handshake, handshakeMsg, encrypt);
        }

        public void SendChangeCipherSpec()
        {
            Send(TlsContent.ChangeCipherSpec, new byte[] { 1 }, false);
        }

        // ---- read -------------------------------------------------------

        /// <summary>Read one record and return its (decrypted) fragment.
        /// contentType tells the caller what it got. Returns null on orderly
        /// transport close.</summary>
        public byte[] ReadRecord(out byte contentType)
        {
            byte[] header = ReadExact(5);
            if (header == null) { contentType = 0; return null; }
            contentType = header[0];
            int recLen = (header[3] << 8) | header[4];
            if (recLen > (16384 + 2048)) throw new Exception("TLS: oversized record " + recLen);
            byte[] payload = ReadExact(recLen);
            if (payload == null) { contentType = 0; return null; }

            if (readProtected)
            {
                if (payload.Length < 8 + 16) throw new Exception("TLS: short protected record");
                byte[] wireNonce8 = new byte[8];
                Array.Copy(payload, 0, wireNonce8, 0, 8);
                int ctLen = payload.Length - 8 - 16;
                byte[] ctAndTag = new byte[ctLen + 16];
                Array.Copy(payload, 8, ctAndTag, 0, ctLen + 16);

                // RFC 5288: GCM nonce = implicit_iv(4) || explicit_nonce(8). The
                // explicit nonce is the record's nonce_explicit field taken from the
                // WIRE — a peer may legitimately choose a random 8-byte value
                // (OpenSSL does) rather than the sequence number, so we must read it
                // here instead of assuming it equals our counter. The AAD, by
                // contrast, always uses our true sequence number (RFC 5116), which is
                // independent of the nonce choice.
                byte[] nonce = new byte[12];
                Array.Copy(readIv, 0, nonce, 0, 4);
                Array.Copy(wireNonce8, 0, nonce, 4, 8);
                byte[] aad = BuildAad(readSeq, contentType, ctLen);
                byte[] plain = AesGcm.Open(readKey, nonce, aad, ctAndTag);
                if (plain == null)
                    throw new Exception("TLS: AEAD authentication failed (seq " + readSeq + ", ct " + contentType + ")");
                readSeq++;
                return plain;
            }
            return payload;
        }

        private byte[] ReadExact(int n)
        {
            byte[] buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = transport.Read(buf, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return buf;
        }

        private static byte[] BuildAad(ulong seq, byte contentType, int plainLen)
        {
            byte[] aad = new byte[13];
            for (int i = 0; i < 8; i++) aad[i] = (byte)(seq >> (8 * (7 - i)));
            aad[8] = contentType;
            aad[9] = 3; aad[10] = 3;
            aad[11] = (byte)(plainLen >> 8);
            aad[12] = (byte)plainLen;
            return aad;
        }

        private static byte[] BuildNonce(byte[] implicitIv, ulong seq)
        {
            byte[] nonce = new byte[12];
            Array.Copy(implicitIv, 0, nonce, 0, 4);
            for (int i = 0; i < 8; i++) nonce[4 + i] = (byte)(seq >> (8 * (7 - i)));
            return nonce;
        }
    }
}
