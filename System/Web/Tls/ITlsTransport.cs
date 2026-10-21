using System;

// Transport abstraction so the TLS engine is byte-stream agnostic: the Windows
// validation harness plugs in a System.Net.Sockets stream, the OS plugs in a
// Cosmos socket. The handshake code only ever sees Read/Write/Close.
namespace Windose.Web.Tls
{
    public interface ITlsTransport
    {
        /// <summary>Read up to len bytes into buf[off..]. Returns 0 on
        /// orderly close, throws on error. Blocks until at least 1 byte.</summary>
        int Read(byte[] buf, int off, int len);

        /// <summary>Write exactly len bytes (loops on partial writes).</summary>
        void Write(byte[] buf, int off, int len);

        void Close();
    }

    /// <summary>Adapts any System.IO.Stream to ITlsTransport. Both the harness
    /// (NetworkStream) and Cosmos's socket stream are System.IO.Stream.</summary>
    public sealed class StreamTransport : ITlsTransport
    {
        private readonly global::System.IO.Stream stream;

        public StreamTransport(global::System.IO.Stream s) { stream = s; }

        public int Read(byte[] buf, int off, int len)
        {
            return stream.Read(buf, off, len);
        }

        public void Write(byte[] buf, int off, int len)
        {
            int written = 0;
            while (written < len)
            {
                stream.Write(buf, off + written, len - written);
                written = len; // Stream.Write writes all for our sockets
            }
        }

        public void Close()
        {
            try { stream.Close(); } catch { }
        }
    }
}
