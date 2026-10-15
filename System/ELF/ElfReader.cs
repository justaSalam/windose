// ElfReader.cs - ELF32/ELF64 parser for Cosmos / NativeAOT.
// No LINQ, no lambdas, no reflection, no System.IO. Pure byte[] in, objects out.
// Handles LE and BE, ET_EXEC/ET_DYN/ET_REL, program headers, sections, symbols.
// Loading: GetImageExtent() + CopyImage() lay out PT_LOAD segments, ApplyRelocations() fixes up a PIE
// (little-endian x86-64 only) via RELA/JMPREL and RELR. No dynamic linking against shared libraries.

namespace Windose.System.Elf
{
    public static class ElfConst
    {
        // e_type
        public const ushort ET_NONE = 0, ET_REL = 1, ET_EXEC = 2, ET_DYN = 3, ET_CORE = 4;
        // e_machine (common)
        public const ushort EM_386 = 3, EM_ARM = 40, EM_X86_64 = 62, EM_AARCH64 = 183, EM_RISCV = 243;
        // p_type
        public const uint PT_NULL = 0, PT_LOAD = 1, PT_DYNAMIC = 2, PT_INTERP = 3, PT_NOTE = 4, PT_PHDR = 6;
        // p_flags
        public const uint PF_X = 1, PF_W = 2, PF_R = 4;
        // sh_type
        public const uint SHT_NULL = 0, SHT_PROGBITS = 1, SHT_SYMTAB = 2, SHT_STRTAB = 3,
                          SHT_RELA = 4, SHT_NOBITS = 8, SHT_REL = 9, SHT_DYNSYM = 11;
        // sh_flags
        public const ulong SHF_WRITE = 1, SHF_ALLOC = 2, SHF_EXECINSTR = 4;
        // dynamic tags
        public const long DT_NULL = 0, DT_PLTRELSZ = 2, DT_STRTAB = 5, DT_SYMTAB = 6, DT_RELA = 7, DT_RELASZ = 8,
                          DT_RELAENT = 9, DT_SYMENT = 11, DT_JMPREL = 23, DT_RELRSZ = 35, DT_RELR = 36, DT_RELRENT = 37;
        // x86-64 relocation types
        public const uint R_X86_64_NONE = 0, R_X86_64_64 = 1, R_X86_64_GLOB_DAT = 6, R_X86_64_JUMP_SLOT = 7, R_X86_64_RELATIVE = 8;
    }

    public sealed class ElfSegment
    {
        public uint Type;
        public uint Flags;
        public ulong Offset;
        public ulong VAddr;
        public ulong PAddr;
        public ulong FileSize;
        public ulong MemSize;
        public ulong Align;

        public bool IsLoad { get { return Type == ElfConst.PT_LOAD; } }
        public bool Readable { get { return (Flags & ElfConst.PF_R) != 0; } }
        public bool Writable { get { return (Flags & ElfConst.PF_W) != 0; } }
        public bool Executable { get { return (Flags & ElfConst.PF_X) != 0; } }
    }

    public sealed class ElfSection
    {
        public string Name;
        public uint NameIndex;
        public uint Type;
        public ulong Flags;
        public ulong Addr;
        public ulong Offset;
        public ulong Size;
        public uint Link;
        public uint Info;
        public ulong AddrAlign;
        public ulong EntSize;
    }

    public sealed class ElfSymbol
    {
        public string Name;
        public ulong Value;
        public ulong Size;
        public byte Info;
        public byte Other;
        public ushort SectionIndex;

        public int Bind { get { return Info >> 4; } }   // 0 local, 1 global, 2 weak
        public int Kind { get { return Info & 0xF; } }  // 0 notype, 1 object, 2 func, 3 section, 4 file
    }

    public sealed class ElfFile
    {
        private byte[] _d;
        private bool _le;

        public string Error;
        public bool Is64;
        public bool LittleEndian { get { return _le; } }
        public byte OsAbi;
        public ushort Type;
        public ushort Machine;
        public ulong Entry;
        public uint Flags;
        public ElfSegment[] Segments;
        public ElfSection[] Sections;

        private ElfFile() { }

        // ---------------------------------------------------------------- parse

        /// <summary>Returns null on failure; the reason goes to 'error'.</summary>
        public static ElfFile Parse(byte[] data, out string error)
        {
            ElfFile f = new ElfFile();
            f._d = data;
            if (!f.ParseInternal())
            {
                error = f.Error;
                return null;
            }
            error = null;
            return f;
        }

        public static bool IsElf(byte[] data)
        {
            return data != null && data.Length >= 4 &&
                   data[0] == 0x7F && data[1] == (byte)'E' && data[2] == (byte)'L' && data[3] == (byte)'F';
        }

        private bool Fail(string msg) { Error = msg; return false; }

        private bool ParseInternal()
        {
            if (!IsElf(_d)) return Fail("not an ELF file");
            if (_d.Length < 52) return Fail("truncated header");

            byte cls = _d[4];
            if (cls == 1) Is64 = false;
            else if (cls == 2) Is64 = true;
            else return Fail("bad EI_CLASS");

            byte enc = _d[5];
            if (enc == 1) _le = true;
            else if (enc == 2) _le = false;
            else return Fail("bad EI_DATA");

            if (_d[6] != 1) return Fail("bad EI_VERSION");
            OsAbi = _d[7];

            ulong phoff, shoff;
            int phentsize, phnum, shentsize, shnum, shstrndx;

            if (Is64)
            {
                if (_d.Length < 64) return Fail("truncated ELF64 header");
                Type = U16(16); Machine = U16(18);
                Entry = U64(24); phoff = U64(32); shoff = U64(40);
                Flags = U32(48);
                phentsize = U16(54); phnum = U16(56);
                shentsize = U16(58); shnum = U16(60); shstrndx = U16(62);
                if (phnum > 0 && phentsize < 56) return Fail("bad phentsize");
                if (shnum > 0 && shentsize < 64) return Fail("bad shentsize");
            }
            else
            {
                Type = U16(16); Machine = U16(18);
                Entry = U32(24); phoff = U32(28); shoff = U32(32);
                Flags = U32(36);
                phentsize = U16(42); phnum = U16(44);
                shentsize = U16(46); shnum = U16(48); shstrndx = U16(50);
                if (phnum > 0 && phentsize < 32) return Fail("bad phentsize");
                if (shnum > 0 && shentsize < 40) return Fail("bad shentsize");
            }

            // ---- program headers
            Segments = new ElfSegment[phnum];
            for (int i = 0; i < phnum; i++)
            {
                ulong o = phoff + (ulong)i * (ulong)phentsize;
                if (!Fits(o, (ulong)phentsize)) return Fail("program header out of range");
                int p = (int)o;
                ElfSegment s = new ElfSegment();
                if (Is64)
                {
                    s.Type = U32(p); s.Flags = U32(p + 4);
                    s.Offset = U64(p + 8); s.VAddr = U64(p + 16); s.PAddr = U64(p + 24);
                    s.FileSize = U64(p + 32); s.MemSize = U64(p + 40); s.Align = U64(p + 48);
                }
                else
                {
                    s.Type = U32(p); s.Offset = U32(p + 4); s.VAddr = U32(p + 8); s.PAddr = U32(p + 12);
                    s.FileSize = U32(p + 16); s.MemSize = U32(p + 20); s.Flags = U32(p + 24); s.Align = U32(p + 28);
                }
                if (s.Type == ElfConst.PT_LOAD)
                {
                    if (s.FileSize > s.MemSize) return Fail("segment filesz > memsz");
                    if (!Fits(s.Offset, s.FileSize)) return Fail("segment data out of range");
                }
                Segments[i] = s;
            }

            // ---- section headers (optional; stripped/minimal files may have none)
            if (shoff == 0 || shnum == 0)
            {
                Sections = new ElfSection[0];
                return true;
            }

            // Extended numbering: PN_XNUM / section 0 holds the real counts (large objects from real toolchains).
            const ushort PN_XNUM = 0xFFFF;
            if ((phnum == PN_XNUM || shnum == 0) && shoff != 0)
            {
                int need = Is64 ? 64 : 40;
                if (!Fits(shoff, (ulong)need)) return Fail("section 0 out of range");
                int p0 = (int)shoff;
                uint extInfo, extSize, extLink;
                if (Is64) { extSize = (uint)U64(p0 + 32); extLink = U32(p0 + 40); extInfo = U32(p0 + 44); }
                else { extSize = U32(p0 + 20); extLink = U32(p0 + 24); extInfo = U32(p0 + 28); }
                if (phnum == PN_XNUM) phnum = (int)extInfo;
                if (shnum == 0) shnum = (int)extSize;
                if (shstrndx == 0xFFFF) shstrndx = (int)extLink;   // SHN_XINDEX
                if (phnum < 0 || shnum < 0 || shstrndx < 0) return Fail("bad extended numbering");
            }
            if (phnum > 64) return Fail("too many program headers");
            if (shnum > 4096) return Fail("too many section headers");
            Sections = new ElfSection[shnum];
            for (int i = 0; i < shnum; i++)
            {
                ulong o = shoff + (ulong)i * (ulong)shentsize;
                if (!Fits(o, (ulong)shentsize)) return Fail("section header out of range");
                int p = (int)o;
                ElfSection s = new ElfSection();
                if (Is64)
                {
                    s.NameIndex = U32(p); s.Type = U32(p + 4); s.Flags = U64(p + 8);
                    s.Addr = U64(p + 16); s.Offset = U64(p + 24); s.Size = U64(p + 32);
                    s.Link = U32(p + 40); s.Info = U32(p + 44); s.AddrAlign = U64(p + 48); s.EntSize = U64(p + 56);
                }
                else
                {
                    s.NameIndex = U32(p); s.Type = U32(p + 4); s.Flags = U32(p + 8);
                    s.Addr = U32(p + 12); s.Offset = U32(p + 16); s.Size = U32(p + 20);
                    s.Link = U32(p + 24); s.Info = U32(p + 28); s.AddrAlign = U32(p + 32); s.EntSize = U32(p + 36);
                }
                if (s.Type != ElfConst.SHT_NOBITS && s.Type != ElfConst.SHT_NULL && !Fits(s.Offset, s.Size))
                    return Fail("section data out of range");
                Sections[i] = s;
            }

            // resolve names
            if (shstrndx > 0 && shstrndx < shnum)
            {
                ElfSection str = Sections[shstrndx];
                for (int i = 0; i < shnum; i++)
                    Sections[i].Name = ReadCStr(str.Offset + Sections[i].NameIndex, str.Offset + str.Size);
            }
            return true;
        }

        // ---------------------------------------------------------------- queries

        public ElfSection FindSection(string name)
        {
            for (int i = 0; i < Sections.Length; i++)
                if (Sections[i].Name == name) return Sections[i];
            return null;
        }

        /// <summary>Raw bytes of a section (copy). Empty array for NOBITS.</summary>
        public byte[] GetSectionData(ElfSection s)
        {
            if (s == null || s.Type == ElfConst.SHT_NOBITS || s.Size == 0) return new byte[0];
            byte[] r = new byte[(int)s.Size];
            global::System.Array.Copy(_d, (int)s.Offset, r, 0, r.Length);
            return r;
        }

        /// <summary>Reads symbols from .symtab (or .dynsym if no .symtab).</summary>
        public ElfSymbol[] GetSymbols()
        {
            ElfSection tab = null;
            for (int i = 0; i < Sections.Length; i++)
                if (Sections[i].Type == ElfConst.SHT_SYMTAB) { tab = Sections[i]; break; }
            if (tab == null)
                for (int i = 0; i < Sections.Length; i++)
                    if (Sections[i].Type == ElfConst.SHT_DYNSYM) { tab = Sections[i]; break; }
            if (tab == null || tab.Link >= (uint)Sections.Length) return new ElfSymbol[0];

            ElfSection str = Sections[tab.Link];
            int entSize = Is64 ? 24 : 16;
            if (tab.EntSize != 0 && (int)tab.EntSize != entSize) return new ElfSymbol[0];

            int count = (int)(tab.Size / (ulong)entSize);
            ElfSymbol[] syms = new ElfSymbol[count];
            for (int i = 0; i < count; i++)
            {
                int p = (int)tab.Offset + i * entSize;
                ElfSymbol s = new ElfSymbol();
                uint nameOff = U32(p);
                if (Is64)
                {
                    s.Info = _d[p + 4]; s.Other = _d[p + 5]; s.SectionIndex = U16(p + 6);
                    s.Value = U64(p + 8); s.Size = U64(p + 16);
                }
                else
                {
                    s.Value = U32(p + 4); s.Size = U32(p + 8);
                    s.Info = _d[p + 12]; s.Other = _d[p + 13]; s.SectionIndex = U16(p + 14);
                }
                s.Name = ReadCStr(str.Offset + nameOff, str.Offset + str.Size);
                syms[i] = s;
            }
            return syms;
        }

        public ElfSymbol FindSymbol(string name)
        {
            ElfSymbol[] all = GetSymbols();
            for (int i = 0; i < all.Length; i++)
                if (all[i].Name == name) return all[i];
            return null;
        }

        /// <summary>Path from PT_INTERP, or null (static binary).</summary>
        public string GetInterpreter()
        {
            for (int i = 0; i < Segments.Length; i++)
                if (Segments[i].Type == ElfConst.PT_INTERP)
                    return ReadCStr(Segments[i].Offset, Segments[i].Offset + Segments[i].FileSize);
            return null;
        }

        // ---------------------------------------------------------------- loading

        /// <summary>Address range covered by all PT_LOAD segments. size includes bss. False if none / too big / wraps.</summary>
        public bool GetImageExtent(ulong maxSize, out ulong low, out ulong size)
        {
            low = 0; size = 0;
            ulong lo = ulong.MaxValue, hi = 0;
            for (int i = 0; i < Segments.Length; i++)
            {
                ElfSegment s = Segments[i];
                if (!s.IsLoad || s.MemSize == 0) continue;
                if (s.VAddr + s.MemSize < s.VAddr) { Error = "segment wraps address space"; return false; }
                if (s.VAddr < lo) lo = s.VAddr;
                if (s.VAddr + s.MemSize > hi) hi = s.VAddr + s.MemSize;
            }
            if (lo == ulong.MaxValue) { Error = "no PT_LOAD segments"; return false; }
            if (hi - lo > maxSize) { Error = "image exceeds maxSize"; return false; }
            low = lo; size = hi - lo;
            return true;
        }

        /// <summary>Copies every PT_LOAD segment's file bytes to dest[destOffset + (vaddr - low)]. dest must be zeroed (bss).</summary>
        public void CopyImage(byte[] dest, int destOffset, ulong low)
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                ElfSegment s = Segments[i];
                if (!s.IsLoad || s.FileSize == 0) continue;
                global::System.Array.Copy(_d, (int)s.Offset, dest, destOffset + (int)(s.VAddr - low), (int)s.FileSize);
            }
        }

        /// <summary>Convenience: extent + copy into a fresh array (no alignment control, no relocation).</summary>
        public byte[] LoadImage(ulong maxSize, out ulong lowAddr)
        {
            ulong size;
            if (!GetImageExtent(maxSize, out lowAddr, out size)) return null;
            byte[] image = new byte[(int)size];
            CopyImage(image, 0, lowAddr);
            return image;
        }

        /// <summary>
        /// Applies the dynamic relocations of a loaded image (RELA + JMPREL tables, plus RELR when present).
        /// 'mem[memOffset]' holds the byte for vaddr 'low'; 'bias' is (runtime address of that byte) - low.
        /// Supports RELATIVE, 64, GLOB_DAT, JUMP_SLOT against symbols defined inside the program itself.
        /// Undefined symbols and TLS are rejected. A file without PT_DYNAMIC has nothing to relocate and succeeds.
        /// </summary>
        public bool ApplyRelocations(byte[] mem, int memOffset, ulong low, ulong size, ulong bias, out string error)
        {
            error = null;
            ElfSegment dyn = null;
            for (int i = 0; i < Segments.Length; i++)
                if (Segments[i].Type == ElfConst.PT_DYNAMIC) { dyn = Segments[i]; break; }
            if (dyn == null) return true;

            ulong rela = 0, relasz = 0, relaent = 24, jmprel = 0, pltrelsz = 0, symtab = 0, strtab = 0;
            ulong syment = 24, relr = 0, relrsz = 0, relent = 8;
            if (dyn.VAddr < low || dyn.VAddr - low + dyn.MemSize > size) { error = "PT_DYNAMIC outside image"; return false; }
            int dp = memOffset + (int)(dyn.VAddr - low);
            for (ulong o = 0; o + 16 <= dyn.MemSize; o += 16)
            {
                long tag = (long)Rd64(mem, dp + (int)o);
                ulong val = Rd64(mem, dp + (int)o + 8);
                if (tag == ElfConst.DT_NULL) break;
                switch (tag)
                {
                    case ElfConst.DT_RELA: rela = val; break;
                    case ElfConst.DT_RELASZ: relasz = val; break;
                    case ElfConst.DT_RELAENT: relaent = val; break;
                    case ElfConst.DT_JMPREL: jmprel = val; break;
                    case ElfConst.DT_PLTRELSZ: pltrelsz = val; break;
                    case ElfConst.DT_SYMTAB: symtab = val; break;
                    case ElfConst.DT_STRTAB: strtab = val; break;
                    case ElfConst.DT_SYMENT: syment = val; break;
                    case ElfConst.DT_RELR: relr = val; break;
                    case ElfConst.DT_RELRSZ: relrsz = val; break;
                    case ElfConst.DT_RELRENT: relent = val; break;
                }
            }
            if (relaent != 24) { error = "bad DT_RELAENT"; return false; }
            if (syment != 24) { error = "bad DT_SYMENT"; return false; }
            if (relent != 8) { error = "bad DT_RELRENT"; return false; }

            if (rela != 0 && relasz != 0 && !ApplyTable(mem, memOffset, low, size, bias, rela, relasz, symtab, strtab, out error)) return false;
            if (jmprel != 0 && pltrelsz != 0 && !ApplyTable(mem, memOffset, low, size, bias, jmprel, pltrelsz, symtab, strtab, out error)) return false;
            if (relr != 0 && relrsz != 0 && !ApplyRelr(mem, memOffset, low, size, bias, relr, relrsz, out error)) return false;
            return true;
        }

        private static bool ApplyTable(byte[] mem, int memOffset, ulong low, ulong size, ulong bias,
                                       ulong tableVaddr, ulong tableSize, ulong symtab, ulong strtab, out string error)
        {
            error = null;
            if (tableVaddr < low || tableVaddr - low + tableSize > size) { error = "relocation table outside image"; return false; }
            int tp = memOffset + (int)(tableVaddr - low);
            ulong count = tableSize / 24;
            for (ulong i = 0; i < count; i++)
            {
                int e = tp + (int)(i * 24);
                ulong rOff = Rd64(mem, e);
                ulong info = Rd64(mem, e + 8);
                long addend = (long)Rd64(mem, e + 16);
                uint type = (uint)(info & 0xFFFFFFFF);
                uint sym = (uint)(info >> 32);
                if (type == ElfConst.R_X86_64_NONE) continue;
                if (rOff < low || rOff - low + 8 > size) { error = "relocation target outside image"; return false; }

                ulong value;
                if (type == ElfConst.R_X86_64_RELATIVE)
                {
                    value = unchecked(bias + (ulong)addend);
                }
                else if (type == ElfConst.R_X86_64_64 || type == ElfConst.R_X86_64_GLOB_DAT || type == ElfConst.R_X86_64_JUMP_SLOT)
                {
                    ulong s = 0;
                    if (sym != 0)
                    {
                        if (symtab == 0 || symtab < low) { error = "relocation needs a symbol table"; return false; }
                        ulong so = symtab - low + (ulong)sym * 24;
                        if (so + 24 > size) { error = "symbol index out of range"; return false; }
                        int sp = memOffset + (int)so;
                        uint nameOff = Rd32(mem, sp);
                        byte stInfo = mem[sp + 4];
                        ushort shndx = (ushort)(mem[sp + 6] | (mem[sp + 7] << 8));
                        ulong stValue = Rd64(mem, sp + 8);
                        if (shndx == 0)
                        {
                            if ((stInfo >> 4) == 2) s = 0;   // weak undefined resolves to 0
                            else { error = "unresolved symbol '" + CStrAt(mem, memOffset, low, size, strtab, nameOff) + "'"; return false; }
                        }
                        else if (shndx == 0xFFF1) s = stValue;                 // SHN_ABS
                        else s = unchecked(bias + stValue);
                    }
                    value = type == ElfConst.R_X86_64_64 ? unchecked(s + (ulong)addend) : s;
                }
                else { error = "unsupported relocation type " + type; return false; }

                Wr64(mem, memOffset + (int)(rOff - low), value);
            }
            return true;
        }

        private static bool ApplyRelr(byte[] mem, int memOffset, ulong low, ulong size, ulong bias,
                                       ulong tableVaddr, ulong tableSize, out string error)
        {
            error = null;
            if (tableVaddr < low || tableVaddr - low + tableSize > size) { error = "DT_RELR table outside image"; return false; }
            if (tableSize % 8 != 0) { error = "bad DT_RELRSZ"; return false; }
            int tp = memOffset + (int)(tableVaddr - low);
            ulong where = 0;
            for (ulong o = 0; o < tableSize; o += 8)
            {
                ulong e = Rd64(mem, tp + (int)o);
                if ((e & 1) == 0)
                {
                    where = e;
                    if (!RelrApplyOne(mem, memOffset, low, size, bias, where, out error)) return false;
                }
                else
                {
                    for (int bit = 1; bit < 64; bit++)
                    {
                        if (((e >> bit) & 1) == 0) continue;
                        ulong addr = where + (ulong)bit * 8;
                        if (addr < where) { error = "DT_RELR address wraps"; return false; }
                        if (!RelrApplyOne(mem, memOffset, low, size, bias, addr, out error)) return false;
                    }
                    if (where + 63UL * 8 < where) { error = "DT_RELR address wraps"; return false; }
                    where += 63UL * 8;
                }
            }
            return true;
        }

        private static bool RelrApplyOne(byte[] mem, int memOffset, ulong low, ulong size, ulong bias,
                                         ulong addr, out string error)
        {
            error = null;
            if (addr < low || addr - low + 8 > size) { error = "DT_RELR target outside image"; return false; }
            int p = memOffset + (int)(addr - low);
            Wr64(mem, p, unchecked(Rd64(mem, p) + bias));
            return true;
        }

        private static string CStrAt(byte[] mem, int memOffset, ulong low, ulong size, ulong strtab, uint nameOff)
        {
            if (strtab < low) return "?";
            ulong o = strtab - low + nameOff;
            if (o >= size) return "?";
            int s = memOffset + (int)o, e = s, lim = memOffset + (int)size;
            while (e < lim && mem[e] != 0 && e - s < 64) e++;
            char[] c = new char[e - s];
            for (int i = 0; i < c.Length; i++) c[i] = (char)mem[s + i];
            return new string(c);
        }

        private static uint Rd32(byte[] m, int o) { return (uint)(m[o] | (m[o + 1] << 8) | (m[o + 2] << 16) | (m[o + 3] << 24)); }
        private static ulong Rd64(byte[] m, int o) { return Rd32(m, o) | ((ulong)Rd32(m, o + 4) << 32); }
        private static void Wr64(byte[] m, int o, ulong v)
        {
            for (int i = 0; i < 8; i++) m[o + i] = (byte)(v >> (8 * i));
        }

        // ---------------------------------------------------------------- helpers

        private bool Fits(ulong off, ulong len)
        {
            ulong end = off + len;
            return end >= off && end <= (ulong)_d.Length;
        }

        private string ReadCStr(ulong start, ulong limit)
        {
            if (limit > (ulong)_d.Length) limit = (ulong)_d.Length;
            if (start >= limit) return "";
            int s = (int)start, e = s, lim = (int)limit;
            while (e < lim && _d[e] != 0) e++;
            char[] c = new char[e - s];
            for (int i = 0; i < c.Length; i++) c[i] = (char)_d[s + i];
            return new string(c);
        }

        private ushort U16(int o)
        {
            return _le ? (ushort)(_d[o] | (_d[o + 1] << 8))
                       : (ushort)((_d[o] << 8) | _d[o + 1]);
        }

        private uint U32(int o)
        {
            return _le ? (uint)(_d[o] | (_d[o + 1] << 8) | (_d[o + 2] << 16) | (_d[o + 3] << 24))
                       : (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3]);
        }

        private ulong U64(int o)
        {
            uint a = U32(o), b = U32(o + 4);
            return _le ? ((ulong)b << 32) | a : ((ulong)a << 32) | b;
        }
    }
}

/* ---------------------------------------------------------------- usage

byte[] file = Kernel.VFS.ReadAllBytes("0:\\hello.elf");   // your VFS call
string err;
ElfFile elf = ElfFile.Parse(file, out err);
if (elf == null) { Terminal.WriteLine("ELF: " + err); return; }

if (elf.Machine != ElfConst.EM_X86_64 || !elf.Is64) { ... reject ... }

ulong low;
byte[] img = elf.LoadImage(16UL * 1024 * 1024, out low);
ulong entryOffset = elf.Entry - low;   // index into img

// symbols / sections:
ElfSymbol main = elf.FindSymbol("main");
ElfSection text = elf.FindSection(".text");

-------------------------------------------------------------------------- */
