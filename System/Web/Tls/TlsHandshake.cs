using System;
using System.Collections.Generic;

namespace Windose.Web.Tls
{
    /// <summary>Result of a completed TLS handshake, exposed for callers.</summary>
    public sealed class TlsSession
    {
        public string Host;
        public int CipherSuite;
        public int HashAlgo;           // 0 = SHA-256, 1 = SHA-384
        public byte[] ServerRandom;
        public byte[] ClientRandom;
        public ITlsTransport Transport;
        public TlsRecordLayer Record;
        public bool IsTls13;

        // application-data receive buffer (decrypted bytes not yet consumed)
        private byte[] appBuf = new byte[0];
        private int appLen;

        public void SendApplicationData(byte[] data)
        {
            // TLS allows up to 16384 bytes of plaintext per record.
            int off = 0;
            while (off < data.Length)
            {
                int take = Math.Min(16384, data.Length - off);
                byte[] chunk = new byte[take];
                Array.Copy(data, off, chunk, 0, take);
                Record.Send(TlsContent.ApplicationData, chunk, true);
                off += take;
            }
        }

        /// <summary>Read decrypted application data into buf. Returns bytes read,
        /// 0 on orderly close. Buffers across record boundaries.</summary>
        public int ReceiveApplicationData(byte[] buf, int off, int len)
        {
            if (appLen == 0)
            {
                if (!FillAppBuffer()) return 0;
            }
            int take = Math.Min(len, appLen);
            Array.Copy(appBuf, 0, buf, off, take);
            // shift remaining
            if (take < appLen)
                Array.Copy(appBuf, take, appBuf, 0, appLen - take);
            appLen -= take;
            return take;
        }

        private bool FillAppBuffer()
        {
            while (true)
            {
                byte ct;
                byte[] frag = Record.ReadRecord(out ct);
                if (frag == null) return false;
                if (ct == TlsContent.Alert)
                {
                    if (frag.Length >= 2 && frag[1] != 0)
                        throw new Exception("TLS alert: " + frag[1]);
                    return false; // close_notify
                }
                if (ct != TlsContent.ApplicationData)
                    continue; // ignore non-application-data records (e.g. NewSessionTicket, renegotiation) and read next record

                // append to appBuf
                byte[] nb = new byte[appLen + frag.Length];
                Array.Copy(appBuf, 0, nb, 0, appLen);
                Array.Copy(frag, 0, nb, appLen, frag.Length);
                appBuf = nb;
                appLen += frag.Length;
                return true;
            }
        }

        public void Close()
        {
            try { Transport.Close(); } catch { }
        }
    }

    /// <summary>A hand-rolled TLS 1.2 client handshake over an arbitrary byte
    /// transport. Supports ECDHE (secp256r1) with AES-128/256-GCM-SHA256/384.
    ///
    /// Matches the OS's existing "NoVerification" stance: it performs the key
    /// exchange and derives keys, but does not validate the certificate chain
    /// (a hobby OS ships no root-CA store). It DOES verify the server's Finished
    /// message, which proves the server derived the same keys -- confirming the
    /// ECDHE, PRF and transcript hash are all correct.</summary>
    public static class TlsHandshake
    {
        // Optional diagnostic sink (set by harness/tests; null in the OS).
        // Cosmos-safe: a plain delegate field, no reflection.
        public delegate void DiagHandler(string message);
        public static DiagHandler OnDiag;
        private static void Diag(string m) { if (OnDiag != null) OnDiag(m); }

        /// <summary>4 uppercase hex digits, formatted by hand — a hobby OS cannot
        /// assume a primitive ToString("X4") plug exists, and this path also runs
        /// inside error messages.</summary>
        private static string Hex4(int v)
        {
            char[] c = new char[4];
            for (int i = 0; i < 4; i++)
            {
                int nib = (v >> ((3 - i) * 4)) & 0xf;
                c[i] = (char)(nib < 10 ? '0' + nib : 'A' + (nib - 10));
            }
            return new string(c);
        }

        // cipher suite ids
        public const ushort TLS_AES_128_GCM_SHA256 = 0x1301;
        public const ushort TLS_AES_256_GCM_SHA384 = 0x1302;
        public const ushort ECDHE_ECDSA_AES_128_GCM_SHA256 = 0xC02B;
        public const ushort ECDHE_RSA_AES_128_GCM_SHA256 = 0xC02F;
        public const ushort ECDHE_ECDSA_AES_256_GCM_SHA384 = 0xC02C;
        public const ushort ECDHE_RSA_AES_256_GCM_SHA384 = 0xC030;

        public static TlsSession Connect(string host, int port, ITlsTransport transport)
        {
            TlsRecordLayer record = new TlsRecordLayer(transport);
            SessionReader reader = new SessionReader(record);

            byte[] clientRandom = TlsRng.NextBytes(32);
            List<byte> transcript = new List<byte>();

            // Generate ephemeral client keypair for TLS 1.3
            byte[] clientPriv = TlsRng.NextBytes(32);
            clientPriv[0] &= 0x7F; // ensure scalar < curve order
            byte[] clientPub = P256.PublicKey(clientPriv);

            // ---- ClientHello ----
            byte[] ch = BuildClientHello(host, clientRandom, clientPub);
            AppendTranscript(transcript, ch);
            record.SendHandshake(ch, false);

            // ---- ServerHello ----
            byte[] shMsg = reader.ReadHandshakeMessage();
            if (shMsg == null || shMsg[0] != 2) throw new Exception("TLS: expected ServerHello");
            AppendTranscript(transcript, shMsg);

            byte[] shBody = SubBody(shMsg);
            byte[] serverRandom;
            ushort suite;
            bool isTls13;
            byte[] serverKeyShare;
            ParseServerHello(shBody, out serverRandom, out suite, out isTls13, out serverKeyShare);

            Diag("ServerHello suite=0x" + Hex4(suite) +
                 " tls13=" + isTls13 +
                 " hash=" + (HashAlgoFor(suite) == 1 ? "sha384" : "sha256"));

            int hashAlgo = HashAlgoFor(suite);
            int keyLen = KeyLenFor(suite);
            int hashLen = HashLenFor(hashAlgo);

            if (isTls13)
            {
                if (serverKeyShare == null || serverKeyShare.Length != 65)
                    throw new Exception("TLS 1.3: missing or invalid server key_share");

                record.IsTls13 = true;

                // ECDH shared secret
                byte[] sharedSecret = P256.SharedSecret(clientPriv, serverKeyShare);

                // TLS 1.3 Key Schedule (RFC 8446 §7.1)
                byte[] zeroKey = new byte[hashLen];
                byte[] zeroHash = hashAlgo == 1 ? Sha384.Hash(new byte[0]) : Sha256.Hash(new byte[0]);

                byte[] earlySecret = TlsPrf.HkdfExtract(hashAlgo, null, zeroKey);
                byte[] derivedSecret1 = TlsPrf.HkdfExpandLabel(hashAlgo, earlySecret, "derived", zeroHash, hashLen);
                byte[] handshakeSecret = TlsPrf.HkdfExtract(hashAlgo, derivedSecret1, sharedSecret);

                byte[] helloHash = HashTranscript(transcript, hashAlgo);

                byte[] clientHsTraffic = TlsPrf.HkdfExpandLabel(hashAlgo, handshakeSecret, "c hs traffic", helloHash, hashLen);
                byte[] serverHsTraffic = TlsPrf.HkdfExpandLabel(hashAlgo, handshakeSecret, "s hs traffic", helloHash, hashLen);

                byte[] clientHsKey = TlsPrf.HkdfExpandLabel(hashAlgo, clientHsTraffic, "key", null, keyLen);
                byte[] clientHsIv  = TlsPrf.HkdfExpandLabel(hashAlgo, clientHsTraffic, "iv",  null, 12);

                byte[] serverHsKey = TlsPrf.HkdfExpandLabel(hashAlgo, serverHsTraffic, "key", null, keyLen);
                byte[] serverHsIv  = TlsPrf.HkdfExpandLabel(hashAlgo, serverHsTraffic, "iv",  null, 12);

                // Enable read protection with server handshake keys
                record.EnableReadProtection(serverHsKey, serverHsIv);

                // Read server flight: EncryptedExtensions (8), Certificate (11), CertificateVerify (15), Finished (20)
                byte[] serverFinishedVerifyData = null;
                while (serverFinishedVerifyData == null)
                {
                    byte[] msg = reader.ReadHandshakeMessage();
                    if (msg == null) throw new Exception("TLS 1.3: connection closed during handshake");
                    byte type = msg[0];

                    if (type == 20) // Finished
                    {
                        byte[] serverFinishedKey = TlsPrf.HkdfExpandLabel(hashAlgo, serverHsTraffic, "finished", null, hashLen);
                        byte[] transcriptHashBeforeFinished = HashTranscript(transcript, hashAlgo);
                        byte[] expectedVerifyData = TlsPrf.Hmac(serverFinishedKey, transcriptHashBeforeFinished, hashAlgo);

                        byte[] actualVerifyData = SubBody(msg);
                        if (!EqualBytes(expectedVerifyData, actualVerifyData))
                            throw new Exception("TLS 1.3: server Finished verification failed");

                        serverFinishedVerifyData = actualVerifyData;
                    }

                    AppendTranscript(transcript, msg);
                }

                // Transcript hash up to server Finished (RFC 8446 §7.1 & §7.3)
                byte[] transcriptHashUpToServerFinished = HashTranscript(transcript, hashAlgo);

                // Send Client Finished
                byte[] clientFinishedKey = TlsPrf.HkdfExpandLabel(hashAlgo, clientHsTraffic, "finished", null, hashLen);
                byte[] clientVerifyData = TlsPrf.Hmac(clientFinishedKey, transcriptHashUpToServerFinished, hashAlgo);
                byte[] clientFinishedMsg = WrapHandshake(20, clientVerifyData);

                // Middlebox compatibility: send ChangeCipherSpec before encrypted flight
                record.SendChangeCipherSpec();

                // Enable write protection with client handshake keys
                record.EnableWriteProtection(clientHsKey, clientHsIv);
                record.SendHandshake(clientFinishedMsg, true);

                // Derive Application Traffic Keys
                byte[] derivedSecret2 = TlsPrf.HkdfExpandLabel(hashAlgo, handshakeSecret, "derived", zeroHash, hashLen);
                byte[] masterSecret = TlsPrf.HkdfExtract(hashAlgo, derivedSecret2, zeroKey);

                byte[] clientAppTraffic = TlsPrf.HkdfExpandLabel(hashAlgo, masterSecret, "c ap traffic", transcriptHashUpToServerFinished, hashLen);
                byte[] serverAppTraffic = TlsPrf.HkdfExpandLabel(hashAlgo, masterSecret, "s ap traffic", transcriptHashUpToServerFinished, hashLen);

                byte[] clientAppKey = TlsPrf.HkdfExpandLabel(hashAlgo, clientAppTraffic, "key", null, keyLen);
                byte[] clientAppIv  = TlsPrf.HkdfExpandLabel(hashAlgo, clientAppTraffic, "iv",  null, 12);

                byte[] serverAppKey = TlsPrf.HkdfExpandLabel(hashAlgo, serverAppTraffic, "key", null, keyLen);
                byte[] serverAppIv  = TlsPrf.HkdfExpandLabel(hashAlgo, serverAppTraffic, "iv",  null, 12);

                // Switch record layer to Application keys
                record.EnableWriteProtection(clientAppKey, clientAppIv);
                record.EnableReadProtection(serverAppKey, serverAppIv);

                TlsSession session13 = new TlsSession();
                session13.IsTls13 = true;
                session13.Host = host;
                session13.CipherSuite = suite;
                session13.HashAlgo = hashAlgo;
                session13.ServerRandom = serverRandom;
                session13.ClientRandom = clientRandom;
                session13.Transport = transport;
                session13.Record = record;
                return session13;
            }
            else
            {
                // ---- TLS 1.2 Path ----
                byte[] serverEcdhePoint = null;

                while (true)
                {
                    byte[] msg = reader.ReadHandshakeMessage();
                    if (msg == null) throw new Exception("TLS: connection closed during handshake");
                    byte type = msg[0];
                    byte[] body = SubBody(msg);

                    if (type == 11) // Certificate — skip contents (no chain validation)
                    {
                        AppendTranscript(transcript, msg);
                    }
                    else if (type == 12) // ServerKeyExchange
                    {
                        AppendTranscript(transcript, msg);
                        serverEcdhePoint = ParseServerKeyExchange(body);
                    }
                    else if (type == 14) // ServerHelloDone
                    {
                        AppendTranscript(transcript, msg);
                        break;
                    }
                    else if (type == 0) // HelloRequest — ignore
                    { }
                    else if (type == 4) // NewSessionTicket — RFC 5077: MUST NOT be included in transcript hash
                    { }
                    else
                    {
                        // Unknown handshake message — record in transcript, continue.
                        AppendTranscript(transcript, msg);
                    }
                }

                if (serverRandom == null) throw new Exception("TLS: no ServerHello");
                if (serverEcdhePoint == null) throw new Exception("TLS: no ServerKeyExchange (not an ECDHE suite?)");

                // ---- ECDHE key exchange ----
                byte[] privScalar = TlsRng.NextBytes(32);
                byte[] sharedX = P256.SharedSecret(privScalar, serverEcdhePoint);
                byte[] myPub = P256.PublicKey(privScalar);

                // ClientKeyExchange: point<1..255> = length(1) || point
                byte[] ckeBody = new byte[1 + myPub.Length];
                ckeBody[0] = (byte)myPub.Length;
                Array.Copy(myPub, 0, ckeBody, 1, myPub.Length);
                byte[] cke = WrapHandshake(16, ckeBody);
                AppendTranscript(transcript, cke);
                record.SendHandshake(cke, false);

                // ---- key schedule ----
                byte[] master = TlsPrf.MasterSecret(sharedX, clientRandom, serverRandom, hashAlgo);
                if (diagTranscript)
                    Diag("  client_random=" + FullHex(clientRandom) + "\n  pms=" + FullHex(sharedX) +
                         "\n  master=" + FullHex(master));
                int ivLen = 4;
                int needed = keyLen * 2 + ivLen * 2;   // ckey, skey, civ, siv (GCM: no MAC keys)
                byte[] keyBlock = TlsPrf.KeyBlock(master, clientRandom, serverRandom, hashAlgo, needed);

                byte[] clientKey = new byte[keyLen];
                byte[] serverKey = new byte[keyLen];
                byte[] clientIv = new byte[ivLen];
                byte[] serverIv = new byte[ivLen];
                Array.Copy(keyBlock, 0, clientKey, 0, keyLen);
                Array.Copy(keyBlock, keyLen, serverKey, 0, keyLen);
                Array.Copy(keyBlock, keyLen * 2, clientIv, 0, ivLen);
                Array.Copy(keyBlock, keyLen * 2 + ivLen, serverIv, 0, ivLen);

                // ---- Finished (client) ----
                byte[] transcriptHash = HashTranscript(transcript, hashAlgo);
                byte[] clientVerify = TlsPrf.FinishedVerifyData(master, transcriptHash, hashAlgo, "client finished");
                byte[] finishedBody = new byte[12];
                Array.Copy(clientVerify, finishedBody, 12);
                byte[] finishedMsg = WrapHandshake(20, finishedBody);

                // send CCS (plaintext), then enable write protection, then Finished
                record.SendChangeCipherSpec();
                AppendTranscript(transcript, finishedMsg);
                record.EnableWriteProtection(clientKey, clientIv);
                record.SendHandshake(finishedMsg, true);

                // ---- read server CCS + Finished ----
                reader.ExpectChangeCipherSpec();
                record.EnableReadProtection(serverKey, serverIv);

                byte[] serverFinishedMsg = reader.ReadHandshakeMessage();
                while (serverFinishedMsg != null && serverFinishedMsg[0] == 4) // Skip NewSessionTicket if sent between CCS and Finished
                {
                    serverFinishedMsg = reader.ReadHandshakeMessage();
                }
                if (serverFinishedMsg == null || serverFinishedMsg[0] != 20)
                    throw new Exception("TLS: expected server Finished");
                byte[] serverVerifyBody = SubBody(serverFinishedMsg);
                if (serverVerifyBody.Length < 12) throw new Exception("TLS: short server Finished");

                // expected = PRF(master, "server finished", Hash(transcript incl. client Finished))
                byte[] expectedServer = TlsPrf.FinishedVerifyData(master, HashTranscript(transcript, hashAlgo), hashAlgo, "server finished");
                for (int i = 0; i < 12; i++)
                {
                    if (serverVerifyBody[i] != expectedServer[i])
                        throw new Exception("TLS: server Finished mismatch at byte " + i);
                }

                TlsSession session = new TlsSession();
                session.IsTls13 = false;
                session.Host = host;
                session.CipherSuite = suite;
                session.HashAlgo = hashAlgo;
                session.ServerRandom = serverRandom;
                session.ClientRandom = clientRandom;
                session.Transport = transport;
                session.Record = record;
                return session;
            }
        }

        // ---- helpers ----------------------------------------------------

        private sealed class SessionReader
        {
            private readonly TlsRecordLayer record;
            private byte[] hsBuf = new byte[0];
            private int hsLen;
            public bool CcsSeen;

            public SessionReader(TlsRecordLayer r) { record = r; }

            private bool Extract(out byte[] msg)
            {
                msg = null;
                if (hsLen >= 4)
                {
                    int bodyLen = (hsBuf[1] << 16) | (hsBuf[2] << 8) | hsBuf[3];
                    if (hsLen >= 4 + bodyLen)
                    {
                        msg = new byte[4 + bodyLen];
                        Array.Copy(hsBuf, msg, 4 + bodyLen);
                        int rem = hsLen - (4 + bodyLen);
                        if (rem > 0) Array.Copy(hsBuf, 4 + bodyLen, hsBuf, 0, rem);
                        hsLen = rem;
                        return true;
                    }
                }
                return false;
            }

            private void Append(byte[] frag)
            {
                if (hsLen + frag.Length > hsBuf.Length)
                {
                    int cap = Math.Max(hsBuf.Length * 2, hsLen + frag.Length);
                    byte[] nb = new byte[cap];
                    Array.Copy(hsBuf, nb, hsLen);
                    hsBuf = nb;
                }
                Array.Copy(frag, 0, hsBuf, hsLen, frag.Length);
                hsLen += frag.Length;
            }

            public byte[] ReadHandshakeMessage()
            {
                byte[] msg;
                if (Extract(out msg)) return msg;
                while (true)
                {
                    byte ct;
                    byte[] frag = record.ReadRecord(out ct);
                    if (frag == null) return null;
                    if (ct == TlsContent.Handshake) { Append(frag); if (Extract(out msg)) return msg; }
                    else if (ct == TlsContent.Alert)
                    {
                        if (frag.Length >= 2 && frag[1] != 0)
                            throw new Exception("TLS alert level=" + frag[0] + " desc=" + frag[1]);
                        return null;
                    }
                    else if (ct == TlsContent.ChangeCipherSpec) { CcsSeen = true; }
                    else if (ct == TlsContent.ApplicationData) { /* unexpected pre-key */ }
                }
            }

            public void ExpectChangeCipherSpec()
            {
                if (CcsSeen) { CcsSeen = false; return; }
                while (true)
                {
                    byte ct;
                    byte[] frag = record.ReadRecord(out ct);
                    if (frag == null) throw new Exception("TLS: closed waiting for CCS");
                    if (ct == TlsContent.ChangeCipherSpec) return;
                    if (ct == TlsContent.Alert)
                        throw new Exception("TLS alert before CCS: " + (frag.Length >= 2 ? frag[1].ToString() : "?"));
                    if (ct == TlsContent.Handshake) Append(frag); // tolerate interleaved
                }
            }
        }

        private static void AppendTranscript(List<byte> transcript, byte[] msg)
        {
            for (int i = 0; i < msg.Length; i++) transcript.Add(msg[i]);
            if (diagTranscript)
                Diag("  ts+ msg type=" + msg[0] + " len=" + (msg.Length - 4) +
                     " total=" + transcript.Count);
        }

        // When true, log every transcript append (set by harness).
        public static bool diagTranscript;

        private static string HexShort(byte[] b)
        {
            if (b == null) return "(null)";
            int n = b.Length < 8 ? b.Length : 8;
            char[] c = new char[n * 2];
            for (int i = 0; i < n; i++)
            {
                c[i * 2] = "0123456789abcdef"[b[i] >> 4];
                c[i * 2 + 1] = "0123456789abcdef"[b[i] & 0xf];
            }
            return new string(c) + "..(" + b.Length + ")";
        }

        private static string FullHex(byte[] b)
        {
            if (b == null) return "(null)";
            char[] c = new char[b.Length * 2];
            for (int i = 0; i < b.Length; i++)
            {
                c[i * 2] = "0123456789abcdef"[b[i] >> 4];
                c[i * 2 + 1] = "0123456789abcdef"[b[i] & 0xf];
            }
            return new string(c);
        }

        private static byte[] HashTranscript(List<byte> transcript, int hashAlgo)
        {
            byte[] all = transcript.ToArray();
            if (diagTranscript)
            {
                byte[] h = hashAlgo == 1 ? Sha384.Hash(all) : Sha256.Hash(all);
                Diag("  ts hash(" + all.Length + ")=" + HexShort(h));
            }
            if (hashAlgo == 1) return Sha384.Hash(all);
            return Sha256.Hash(all);
        }

        private static byte[] SubBody(byte[] msg)
        {
            int len = msg.Length - 4;
            byte[] b = new byte[len];
            Array.Copy(msg, 4, b, 0, len);
            return b;
        }

        private static byte[] WrapHandshake(byte type, byte[] body)
        {
            byte[] m = new byte[4 + body.Length];
            m[0] = type;
            m[1] = (byte)(body.Length >> 16);
            m[2] = (byte)(body.Length >> 8);
            m[3] = (byte)body.Length;
            Array.Copy(body, 0, m, 4, body.Length);
            return m;
        }

        private static int HashAlgoFor(ushort suite)
        {
            if (suite == TLS_AES_256_GCM_SHA384 ||
                suite == ECDHE_ECDSA_AES_256_GCM_SHA384 ||
                suite == ECDHE_RSA_AES_256_GCM_SHA384)
                return 1;
            return 0;
        }

        private static int KeyLenFor(ushort suite)
        {
            if (suite == TLS_AES_256_GCM_SHA384 ||
                suite == ECDHE_ECDSA_AES_256_GCM_SHA384 ||
                suite == ECDHE_RSA_AES_256_GCM_SHA384)
                return 32;
            return 16;
        }

        private static int HashLenFor(int hashAlgo)
        {
            return hashAlgo == 1 ? 48 : 32;
        }

        private static bool EqualBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        // ---- message builders/parsers -----------------------------------

        private static byte[] BuildClientHello(string host, byte[] clientRandom, byte[] clientPub)
        {
            List<byte> body = new List<byte>();
            // client_version (0x0303 for TLS 1.2 / TLS 1.3 middlebox compat)
            body.Add(3); body.Add(3);
            // random
            for (int i = 0; i < 32; i++) body.Add(clientRandom[i]);
            // session_id (32 random bytes for middlebox compat RFC 8446 §D.4)
            body.Add(32);
            byte[] sid = TlsRng.NextBytes(32);
            for (int i = 0; i < 32; i++) body.Add(sid[i]);

            // cipher_suites
            ushort[] suites = new ushort[]
            {
                TLS_AES_256_GCM_SHA384,
                TLS_AES_128_GCM_SHA256,
                ECDHE_ECDSA_AES_128_GCM_SHA256,
                ECDHE_RSA_AES_128_GCM_SHA256,
                ECDHE_ECDSA_AES_256_GCM_SHA384,
                ECDHE_RSA_AES_256_GCM_SHA384
            };
            ushort suitesLen = (ushort)(suites.Length * 2);
            body.Add((byte)(suitesLen >> 8)); body.Add((byte)suitesLen);
            for (int i = 0; i < suites.Length; i++)
            {
                body.Add((byte)(suites[i] >> 8));
                body.Add((byte)suites[i]);
            }
            // compression_methods: null
            body.Add(1); body.Add(0);

            // extensions
            List<byte> exts = new List<byte>();

            // server_name (SNI) — required for virtual hosting
            byte[] hostBytes = TlsPrf.Ascii(host);
            // SNI: list_len(2) | type(1)=0 host_name | name_len(2) | name
            int sniInner = 1 + 2 + hostBytes.Length;
            int sniList = 2 + sniInner;
            AddU16(exts, 0);          // ext type server_name
            AddU16(exts, sniList);    // ext data length
            AddU16(exts, sniInner);   // server_name_list length
            exts.Add(0);              // name_type host_name
            AddU16(exts, hostBytes.Length);
            for (int i = 0; i < hostBytes.Length; i++) exts.Add(hostBytes[i]);

            // supported_groups — secp256r1 only (P256.cs only implements curve 0x0017)
            byte[] groups = new byte[] { 0x00, 0x17 };
            AddU16(exts, 10);
            AddU16(exts, 2 + groups.Length);
            AddU16(exts, groups.Length);
            for (int i = 0; i < groups.Length; i++) exts.Add(groups[i]);

            // ec_point_formats — uncompressed
            AddU16(exts, 11);
            AddU16(exts, 2);
            exts.Add(1); exts.Add(0);

            // signature_algorithms — broad set so the server can sign ServerKeyExchange / CertificateVerify
            byte[] sigalgs = new byte[]
            {
                0x04, 0x03,   // ecdsa_secp256r1_sha256
                0x05, 0x03,   // ecdsa_secp384r1_sha384
                0x06, 0x03,   // ecdsa_secp521r1_sha512
                0x04, 0x01,   // rsa_pkcs1_sha256
                0x05, 0x01,   // rsa_pkcs1_sha384
                0x06, 0x01,   // rsa_pkcs1_sha512
                0x08, 0x04,   // rsa_pss_rsae_sha256
                0x08, 0x05,   // rsa_pss_rsae_sha384
                0x08, 0x06,   // rsa_pss_rsae_sha512
                0x08, 0x07    // ed25519
            };
            AddU16(exts, 13);
            AddU16(exts, 2 + sigalgs.Length);
            AddU16(exts, sigalgs.Length);
            for (int i = 0; i < sigalgs.Length; i++) exts.Add(sigalgs[i]);

            // supported_versions (43): TLS 1.3 (0x0304), TLS 1.2 (0x0303)
            AddU16(exts, 43);
            AddU16(exts, 5);
            exts.Add(4);
            exts.Add(3); exts.Add(4);
            exts.Add(3); exts.Add(3);

            // key_share (51): client_shares: group 0x0017, len clientPub.Length, key clientPub
            if (clientPub != null && clientPub.Length > 0)
            {
                AddU16(exts, 51);
                int ksLen = 2 + 2 + 2 + clientPub.Length;
                AddU16(exts, ksLen);
                AddU16(exts, 2 + 2 + clientPub.Length);
                AddU16(exts, 0x0017);
                AddU16(exts, clientPub.Length);
                for (int i = 0; i < clientPub.Length; i++) exts.Add(clientPub[i]);
            }

            // psk_key_exchange_modes (45): psk_dhe_ke (1)
            AddU16(exts, 45);
            AddU16(exts, 2);
            exts.Add(1); exts.Add(1);

            // renegotiation_info (empty, server side)
            AddU16(exts, 0xFF01);
            AddU16(exts, 1);
            exts.Add(0);

            ushort extLen = (ushort)exts.Count;
            AddU16(body, extLen);
            for (int i = 0; i < exts.Count; i++) body.Add(exts[i]);

            return WrapHandshake(1, body.ToArray());
        }

        private static void ParseServerHello(byte[] body, out byte[] serverRandom, out ushort suite, out bool isTls13, out byte[] serverPub)
        {
            serverRandom = null;
            suite = 0;
            isTls13 = false;
            serverPub = null;

            if (body.Length < 2 + 32 + 1) throw new Exception("TLS: ServerHello too short");
            int p = 0;
            p += 2; // version
            serverRandom = new byte[32];
            Array.Copy(body, p, serverRandom, 0, 32); p += 32;
            int sidLen = body[p]; p += 1 + sidLen;
            if (p + 2 > body.Length) throw new Exception("TLS: ServerHello truncated at suite");
            suite = (ushort)((body[p] << 8) | body[p + 1]); p += 2;
            if (p >= body.Length) return;
            p++; // skip compression method

            if (p + 2 > body.Length) return;
            int extsLen = (body[p] << 8) | body[p + 1]; p += 2;
            int end = p + extsLen;
            if (end > body.Length) end = body.Length;

            while (p + 4 <= end)
            {
                ushort extType = (ushort)((body[p] << 8) | body[p + 1]);
                ushort extLen = (ushort)((body[p + 2] << 8) | body[p + 3]);
                p += 4;
                if (p + extLen > end) break;

                if (extType == 43) // supported_versions
                {
                    if (extLen >= 2)
                    {
                        ushort ver = (ushort)((body[p] << 8) | body[p + 1]);
                        if (ver == 0x0304) isTls13 = true;
                    }
                }
                else if (extType == 51) // key_share
                {
                    if (extLen >= 4)
                    {
                        ushort group = (ushort)((body[p] << 8) | body[p + 1]);
                        ushort klen = (ushort)((body[p + 2] << 8) | body[p + 3]);
                        if (p + 4 + klen <= end)
                        {
                            serverPub = new byte[klen];
                            Array.Copy(body, p + 4, serverPub, 0, klen);
                        }
                    }
                }
                p += extLen;
            }
        }

        private static byte[] ParseServerKeyExchange(byte[] body)
        {
            // ECDHE: curve_type(1)=3, named_curve(2), point_len(1), point
            if (body.Length < 4) throw new Exception("TLS: ServerKeyExchange too short");
            byte curveType = body[0];
            if (curveType != 3) throw new Exception("TLS: unsupported curve_type " + curveType);
            ushort named = (ushort)((body[1] << 8) | body[2]);
            if (named != 0x0017) throw new Exception("TLS: unsupported named curve 0x" + Hex4(named));
            int pointLen = body[3];
            if (body.Length < 4 + pointLen) throw new Exception("TLS: ServerKeyExchange point truncated");
            byte[] point = new byte[pointLen];
            Array.Copy(body, 4, point, 0, pointLen);
            // remainder = signature (ignored: NoVerification stance)
            return point;
        }

        private static void AddU16(List<byte> b, int v)
        {
            b.Add((byte)(v >> 8));
            b.Add((byte)v);
        }
    }
}
