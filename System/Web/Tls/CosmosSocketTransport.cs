using System.Net.Sockets;
using System.Threading;

namespace Windose.Web.Tls
{
    /// <summary>Adapts a Cosmos <c>Socket</c> to <see cref="ITlsTransport"/> so the
    /// hand-rolled TLS engine can drive a raw kernel TCP connection.
    ///
    /// Reads block until at least one byte is available, following the exact
    /// Poll/Available/Receive idiom the OS's FTP server already uses against the
    /// Cosmos socket plugs (a bare Read can return early, so we poll first and
    /// treat "readable but nothing available" as an orderly close).</summary>
    public sealed class CosmosSocketTransport : ITlsTransport
    {
        private readonly Socket socket;

        public CosmosSocketTransport(Socket s) { socket = s; }

        public int Read(byte[] buf, int off, int len)
        {
            if (len <= 0) return 0;

            // Block until the peer has something for us (with 15s timeout).
            int waitedMs = 0;
            while (socket.Available == 0)
            {
                if (socket.Poll(0, SelectMode.SelectRead))
                {
                    if (socket.Available == 0)
                        return 0;   // readable with nothing buffered => the peer closed
                    break;
                }
                Thread.Sleep(1);
                waitedMs++;
                if (waitedMs > 15000)
                    throw new global::System.TimeoutException("TLS transport: socket read timed out");
            }

            int want = len;
            int avail = socket.Available;
            if (avail < len) want = avail;
            return socket.Receive(buf, off, want, SocketFlags.None);
        }

        public void Write(byte[] buf, int off, int len)
        {
            int sent = 0;
            while (sent < len)
            {
                int n = socket.Send(buf, off + sent, len - sent, SocketFlags.None);
                if (n <= 0) throw new global::System.Exception("TLS transport: socket send failed");
                sent += n;
            }
        }

        public void Close()
        {
            try { socket.Close(); } catch { }
        }
    }
}
