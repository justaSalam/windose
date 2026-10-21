using System;

// Minimal DER (ASN.1) reader. Just enough to walk an X.509 certificate and a
// SubjectPublicKeyInfo, plus the ServerKeyExchange signature block. No writing,
// no tags we don't need, no reflection, no culture-aware parsing.
namespace Windose.Web.Tls
{
    public sealed class DerReader
    {
        public byte[] Data;
        public int Pos;
        public int End;

        public DerReader(byte[] data) : this(data, 0, data.Length) { }

        public DerReader(byte[] data, int start, int len)
        {
            Data = data;
            Pos = start;
            End = start + len;
        }

        public bool Eof { get { return Pos >= End; } }

        // Read the tag and length of the next element; returns the tag.
        // Leaves Pos at the first content byte and records ContentEnd.
        public byte Tag;
        public int ContentStart;
        public int ContentEnd;

        public void ReadTagAndLength()
        {
            if (Pos >= End) throw new Exception("DER: unexpected end");
            Tag = Data[Pos++];
            int lenByte = Data[Pos++];
            int len;
            if ((lenByte & 0x80) == 0)
            {
                len = lenByte;
            }
            else
            {
                int n = lenByte & 0x7f;
                if (n == 0 || n > 4) throw new Exception("DER: bad length");
                len = 0;
                for (int i = 0; i < n; i++)
                {
                    len = (len << 8) | Data[Pos++];
                }
            }
            ContentStart = Pos;
            ContentEnd = Pos + len;
            if (ContentEnd > End) throw new Exception("DER: length overruns buffer");
            Pos = ContentEnd;
        }

        /// <summary>Enter the element just read (its content) as a sub-reader.</summary>
        public DerReader Enter()
        {
            return new DerReader(Data, ContentStart, ContentEnd - ContentStart);
        }

        /// <summary>Skip the element just read (Pos already advanced past it).</summary>
        public void Skip() { /* ReadTagAndLength already advanced Pos */ }

        // ---- content helpers (operate on the region [ContentStart, ContentEnd))

        public byte[] ContentBytes()
        {
            int n = ContentEnd - ContentStart;
            byte[] r = new byte[n];
            Array.Copy(Data, ContentStart, r, 0, n);
            return r;
        }

        /// <summary>Read a non-negative INTEGER as big-endian bytes, trimming a
        /// leading 0x00 sign octet. Used for RSA modulus/exponent and serials.</summary>
        public byte[] ReadIntegerBytes()
        {
            byte[] raw = ContentBytes();
            // strip leading zeros but keep at least one byte
            int i = 0;
            while (i < raw.Length - 1 && raw[i] == 0) i++;
            byte[] r = new byte[raw.Length - i];
            Array.Copy(raw, i, r, 0, r.Length);
            return r;
        }

        public ulong ReadIntegerAsU64()
        {
            byte[] b = ReadIntegerBytes();
            ulong v = 0;
            for (int i = 0; i < b.Length && i < 8; i++)
                v = (v << 8) | b[i];
            return v;
        }

        // ---- static convenience: walk a sequence of children -------------

        /// <summary>Collect the content regions of each child of a constructed
        /// element currently under the cursor as fresh DerReaders.</summary>
        public DerReader[] Children()
        {
            DerReader sub = Enter();
            global::System.Collections.Generic.List<DerReader> list =
                new global::System.Collections.Generic.List<DerReader>();
            while (!sub.Eof)
            {
                int before = sub.Pos;
                sub.ReadTagAndLength();
                int len = sub.ContentEnd - sub.ContentStart;
                list.Add(new DerReader(sub.Data, sub.ContentStart, len));
                sub.Pos = sub.ContentEnd;
                if (sub.Pos <= before) break; // guard against infinite loop
            }
            return list.ToArray();
        }

        /// <summary>Find the first child whose tag matches.</summary>
        public static DerReader Find(DerReader[] kids, byte tag)
        {
            for (int i = 0; i < kids.Length; i++)
                if (kids[i].Tag == tag) return kids[i];
            return null;
        }

        /// <summary>Find the first descendant matching a tag within one level of
        /// constructed children (helper for EXPLICIT [n] wrappers).</summary>
        public static DerReader FindInConstructed(DerReader r, byte tag)
        {
            if (r == null) return null;
            DerReader[] kids = r.Children();
            for (int i = 0; i < kids.Length; i++)
                if (kids[i].Tag == tag) return kids[i];
            return null;
        }
    }
}
