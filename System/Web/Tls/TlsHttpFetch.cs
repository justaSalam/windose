using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Windose.Web.Tls
{
    /// <summary>A hand-rolled HTTP/1.1 response carried out of the native TLS
    /// client: the pieces the browser actually needs (status, a raw header
    /// block, and the body bytes). Deliberately not a Cosmos HttpResponseMessage
    /// so the TLS stack stays independent of the networking library.</summary>
    public sealed class TlsHttpResponse
    {
        public int Status;
        public string Reason = "";
        /// <summary>Raw "Name: value\r\n" lines, excluding the trailing blank line.</summary>
        public string HeaderBlock = "";
        public byte[] Body = new byte[0];
        public string FinalUrl = "";
        /// <summary>Every Set-Cookie value the response carried (a response may
        /// hold several). The caller feeds these to the cookie jar.</summary>
        public global::System.Collections.Generic.List<string> SetCookies =
            new global::System.Collections.Generic.List<string>();
    }

    public interface IHttpDataStream
    {
        void Send(byte[] data);
        int Receive(byte[] buf, int off, int len);
        void Close();
    }

    /// <summary>Minimal HTTP/1.1 client that speaks over plain TCP or the hand-rolled TLS 1.2
    /// engine, so the browser can load http:// and https:// pages without any external HTTP
    /// library. One call performs a single request (no redirect following — the
    /// caller owns that, alongside cookie handling).
    ///
    /// Cosmos-safety: no LINQ, no StringComparison overloads, no char.Is* helpers,
    /// no reflection. Header names are compared via ToLowerInvariant().</summary>
    public static class TlsHttpFetch
    {
        private sealed class TlsDataStream : IHttpDataStream
        {
            private readonly TlsSession session;
            public TlsDataStream(TlsSession s) { session = s; }
            public void Send(byte[] data) { session.SendApplicationData(data); }
            public int Receive(byte[] buf, int off, int len) { return session.ReceiveApplicationData(buf, off, len); }
            public void Close() { session.Close(); }
        }

        private sealed class PlainDataStream : IHttpDataStream
        {
            private readonly ITlsTransport transport;
            public PlainDataStream(ITlsTransport t) { transport = t; }
            public void Send(byte[] data) { transport.Write(data, 0, data.Length); }
            public int Receive(byte[] buf, int off, int len) { return transport.Read(buf, off, len); }
            public void Close() { transport.Close(); }
        }

        /// <summary>Connect to <paramref name="url"/>'s host, complete TLS handshake if https,
        /// send the request and read one response. Throws on any transport failure; never returns null.</summary>
        /// <param name="method">HTTP verb ("GET", "POST", ...). Null/empty means GET.</param>
        /// <param name="requestHeaders">Extra "Name: value\r\n" lines to merge in (may be null).</param>
        /// <param name="body">Request body bytes, or null for none.</param>
        /// <param name="contentType">Content-Type for a body, or null.</param>
        /// <param name="cookieHeader">Pre-built "Cookie: ..." value (without the name), or null.</param>
        public static TlsHttpResponse Fetch(string method, string url,
            string requestHeaders, byte[] body, string contentType, string cookieHeader)
        {
            string scheme = SchemeOf(url);
            bool isHttps = scheme == "https";
            string host = HostOf(url);
            int port = PortOf(url, isHttps);
            string target = TargetOf(url);

            IHttpDataStream stream = Connect(host, port, isHttps);
            try
            {
                string verb = (method == null || method.Length == 0) ? "GET" : method.ToUpperInvariant();
                byte[] request = BuildRequest(verb, host, port, target,
                    requestHeaders, body, contentType, cookieHeader, isHttps);
                stream.Send(request);
                return ReadResponse(stream, url);
            }
            finally
            {
                stream.Close();
            }
        }

        /// <summary>Open a TCP connection to host:port (and run TLS handshake if https).</summary>
        private static IHttpDataStream Connect(string host, int port, bool isHttps)
        {
            global::System.Net.IPAddress address = ResolveIPv4(host);

            TcpClient tcp = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                tcp.Connect(address, port);
            }
            catch
            {
                try { tcp.Close(); } catch { }
                throw;
            }

            Socket socket = tcp.Client;
            ITlsTransport transport = new CosmosSocketTransport(socket);
            if (isHttps)
            {
                TlsSession session = TlsHandshake.Connect(host, port, transport);
                return new TlsDataStream(session);
            }
            else
            {
                return new PlainDataStream(transport);
            }
        }

        /// <summary>Resolve host to its first IPv4 address, the same way the Cosmos
        /// HTTP client does. Throws when DNS yields nothing usable.</summary>
        private static global::System.Net.IPAddress ResolveIPv4(string host)
        {
            // A literal IP needs no lookup; Dns would reject or mis-resolve it.
            global::System.Net.IPAddress literal;
            if (global::System.Net.IPAddress.TryParse(host, out literal))
                return literal;

            IPHostEntry entry = Dns.GetHostEntry(host);
            if (entry == null || entry.AddressList == null)
                throw new global::System.Exception("Cannot resolve " + host);

            global::System.Net.IPAddress[] list = entry.AddressList;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null && list[i].AddressFamily == AddressFamily.InterNetwork)
                    return list[i];
            }
            throw new global::System.Exception("No IPv4 address for " + host);
        }

        private static byte[] BuildRequest(string verb, string host, int port, string target,
            string requestHeaders, byte[] body, string contentType, string cookieHeader, bool isHttps)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append(verb).Append(' ').Append(target).Append(" HTTP/1.1\r\n");
            if ((isHttps && port == 443) || (!isHttps && port == 80))
                sb.Append("Host: ").Append(host).Append("\r\n");
            else
                sb.Append("Host: ").Append(host).Append(':').Append(port).Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36\r\n");
            sb.Append("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,text/css,*/*;q=0.8\r\n");
            sb.Append("Accept-Encoding: identity\r\n");
            sb.Append("Accept-Language: en-US,en;q=0.9\r\n");

            if (cookieHeader != null && cookieHeader.Length > 0)
                sb.Append("Cookie: ").Append(cookieHeader).Append("\r\n");

            if (requestHeaders != null && requestHeaders.Length > 0)
                AppendExtraHeaders(sb, requestHeaders);

            if (body != null)
            {
                sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                if (contentType != null && contentType.Length > 0)
                    sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            }

            sb.Append("\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            if (body == null || body.Length == 0) return head;

            byte[] all = new byte[head.Length + body.Length];
            global::System.Array.Copy(head, 0, all, 0, head.Length);
            global::System.Array.Copy(body, 0, all, head.Length, body.Length);
            return all;
        }

        /// <summary>Merge caller-supplied header lines, skipping the ones the client
        /// sets itself (Host, Content-Length, any Connection) so a script cannot
        /// corrupt framing. User-Agent is dropped as a forbidden header name.</summary>
        private static void AppendExtraHeaders(StringBuilder sb, string headerLines)
        {
            int start = 0;
            while (start < headerLines.Length)
            {
                int nl = headerLines.IndexOf('\n', start);
                if (nl < 0) nl = headerLines.Length;
                string line = headerLines.Substring(start, nl - start);
                if (line.Length > 0 && line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                start = nl + 1;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (name.Length == 0 || value.Length == 0) continue;

                string lower = name.ToLowerInvariant();
                if (lower == "host" || lower == "content-length" || lower == "connection"
                    || lower == "user-agent" || lower == "cookie")
                    continue;

                sb.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }

        // ---- response ---------------------------------------------------

        private static TlsHttpResponse ReadResponse(IHttpDataStream stream, string url)
        {
            ResponseReader reader = new ResponseReader(stream);

            string statusLine = reader.ReadLine();
            if (statusLine == null || !statusLine.StartsWith("HTTP/"))
                throw new global::System.Exception("Bad HTTP status line");

            TlsHttpResponse response = new TlsHttpResponse();
            response.FinalUrl = url;
            ParseStatusLine(statusLine, response);

            string contentLength = null;
            bool chunked = false;
            StringBuilder headers = new StringBuilder(256);
            while (true)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                if (line.Length == 0) break;   // blank line ends the headers
                headers.Append(line).Append("\r\n");

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();

                if (name == "content-length") contentLength = value;
                else if (name == "transfer-encoding" && value.ToLowerInvariant().IndexOf("chunked") >= 0) chunked = true;
                else if (name == "set-cookie") response.SetCookies.Add(value);
            }
            response.HeaderBlock = headers.ToString();

            if (chunked) response.Body = ReadChunkedBody(reader);
            else if (contentLength != null) response.Body = ReadFixedBody(reader, contentLength);
            else response.Body = ReadUntilClose(reader);

            return response;
        }

        private static void ParseStatusLine(string line, TlsHttpResponse response)
        {
            // "HTTP/1.1 200 OK"
            int sp1 = line.IndexOf(' ');
            if (sp1 < 0) return;
            int sp2 = line.IndexOf(' ', sp1 + 1);
            string codeText = sp2 < 0 ? line.Substring(sp1 + 1) : line.Substring(sp1 + 1, sp2 - sp1 - 1);
            response.Status = ParseInt(codeText, 0);
            response.Reason = sp2 < 0 ? "" : line.Substring(sp2 + 1);
        }

        private static byte[] ReadFixedBody(ResponseReader reader, string contentLength)
        {
            int len = ParseInt(contentLength, 0);
            if (len <= 0) return new byte[0];
            return reader.ReadExactly(len);
        }

        private static byte[] ReadUntilClose(ResponseReader reader)
        {
            global::System.Collections.Generic.List<byte> acc =
                new global::System.Collections.Generic.List<byte>();
            byte[] buf = new byte[4096];
            int n;
            while ((n = reader.ReadSome(buf, 0, buf.Length)) > 0)
            {
                byte[] chunk = new byte[n];
                global::System.Array.Copy(buf, 0, chunk, 0, n);
                acc.AddRange(chunk);
            }
            return acc.ToArray();
        }

        private static byte[] ReadChunkedBody(ResponseReader reader)
        {
            global::System.Collections.Generic.List<byte> acc =
                new global::System.Collections.Generic.List<byte>();
            while (true)
            {
                string sizeLine = reader.ReadLine();
                if (sizeLine == null) break;
                sizeLine = sizeLine.Trim();
                if (sizeLine.Length == 0) continue; // skip blank lines between chunks
                int semi = sizeLine.IndexOf(';');
                string hex = (semi < 0 ? sizeLine : sizeLine.Substring(0, semi)).Trim();
                int size = ParseHex(hex);
                if (size == 0) break;   // terminal chunk
                byte[] chunk = reader.ReadExactly(size);
                acc.AddRange(chunk);
                reader.ReadLine();      // trailing CRLF after each chunk
            }
            return acc.ToArray();
        }

        // ---- parsing helpers --------------------------------------------

        private static int ParseInt(string s, int fallback)
        {
            if (s == null || s.Length == 0) return fallback;
            int value = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                value = value * 10 + (c - '0');
            }
            return value;
        }

        private static int ParseHex(string s)
        {
            int value = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else break;
                value = value * 16 + digit;
            }
            return value;
        }

        // ---- URL pieces (local copies so the TLS layer needs no WebUrl) ----

        private static string HostOf(string url)
        {
            int idx = url.IndexOf("://");
            int start = idx >= 0 ? idx + 3 : 0;
            int slash = url.IndexOf('/', start);
            string authority = slash < 0 ? url.Substring(start) : url.Substring(start, slash - start);
            int colon = authority.IndexOf(':');
            return colon > 0 ? authority.Substring(0, colon) : authority;
        }

        private static string SchemeOf(string url)
        {
            int idx = url.IndexOf("://");
            if (idx <= 0) return "http";
            return url.Substring(0, idx).ToLowerInvariant();
        }

        private static int PortOf(string url, bool isHttps)
        {
            int idx = url.IndexOf("://");
            int start = idx >= 0 ? idx + 3 : 0;
            int slash = url.IndexOf('/', start);
            string authority = slash < 0 ? url.Substring(start) : url.Substring(start, slash - start);
            int colon = authority.IndexOf(':');
            int defaultPort = isHttps ? 443 : 80;
            if (colon > 0) return ParseInt(authority.Substring(colon + 1), defaultPort);
            return defaultPort;
        }

        /// <summary>Path + query (always starts with '/'), for the request line.</summary>
        private static string TargetOf(string url)
        {
            int idx = url.IndexOf("://");
            int start = idx >= 0 ? idx + 3 : 0;
            int slash = url.IndexOf('/', start);
            if (slash < 0) return "/";
            string rest = url.Substring(slash);
            int hash = rest.IndexOf('#');
            if (hash >= 0) rest = rest.Substring(0, hash);
            return rest.Length == 0 ? "/" : rest;
        }

        /// <summary>Byte reader over the transport stream that can back up to
        /// a line boundary without losing bytes.</summary>
        private sealed class ResponseReader
        {
            private readonly IHttpDataStream stream;
            private byte[] buf = new byte[0];
            private int pos;

            public ResponseReader(IHttpDataStream s) { stream = s; }

            private bool Ensure()
            {
                if (pos < buf.Length) return true;
                byte[] tmp = new byte[4096];
                int n = stream.Receive(tmp, 0, tmp.Length);
                if (n <= 0) return false;
                byte[] nb = new byte[n];
                global::System.Array.Copy(tmp, 0, nb, 0, n);
                buf = nb;
                pos = 0;
                return n > 0;
            }

            public int ReadSome(byte[] dst, int off, int len)
            {
                if (!Ensure()) return 0;
                int take = len;
                if (take > buf.Length - pos) take = buf.Length - pos;
                global::System.Array.Copy(buf, pos, dst, off, take);
                pos += take;
                return take;
            }

            public byte[] ReadExactly(int n)
            {
                byte[] outp = new byte[n];
                int got = 0;
                while (got < n)
                {
                    if (!Ensure()) break;
                    int take = n - got;
                    if (take > buf.Length - pos) take = buf.Length - pos;
                    global::System.Array.Copy(buf, pos, outp, got, take);
                    pos += take;
                    got += take;
                }
                if (got < n)
                {
                    byte[] shrunk = new byte[got];
                    global::System.Array.Copy(outp, 0, shrunk, 0, got);
                    return shrunk;
                }
                return outp;
            }

            /// <summary>Read one CRLF-terminated line (CRLF stripped), or null at end
            /// of stream. Returns "" for a blank line.</summary>
            public string ReadLine()
            {
                global::System.Collections.Generic.List<byte> line =
                    new global::System.Collections.Generic.List<byte>();
                bool sawCr = false;
                while (true)
                {
                    if (!Ensure())
                    {
                        if (line.Count == 0) return null;
                        break;
                    }
                    byte b = buf[pos++];
                    if (b == (byte)'\n') break;
                    if (sawCr) { line.Add((byte)'\r'); sawCr = false; }
                    if (b == (byte)'\r') { sawCr = true; continue; }
                    line.Add(b);
                }
                return Encoding.ASCII.GetString(line.ToArray());
            }
        }
    }
}
