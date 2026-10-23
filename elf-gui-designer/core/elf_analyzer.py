"""
ELF Binary Inspector and Analyzer.
Parses 32-bit and 64-bit ELF headers, section tables, and symbol tables.
"""

import struct
from typing import Dict, Any, List, Optional


class ElfAnalyzer:
    """Reads and decodes ELF binaries (Linux, BSD, baremetal, OS dev)."""

    OS_ABI = {
        0x00: "System V (Linux default)",
        0x01: "HP-UX",
        0x02: "NetBSD",
        0x03: "Linux",
        0x04: "GNU Hurd",
        0x06: "Solaris",
        0x07: "AIX",
        0x08: "IRIX",
        0x09: "FreeBSD",
        0x0C: "OpenBSD",
        0x40: "Cosmos / ARM Baremetal"
    }

    FILE_TYPES = {
        0: "ET_NONE (No file type)",
        1: "ET_REL (Relocatable object file)",
        2: "ET_EXEC (Executable file)",
        3: "ET_DYN (Shared object / PIE)",
        4: "ET_CORE (Core file)"
    }

    MACHINES = {
        0x02: "SPARC",
        0x03: "x86 (Intel 80386)",
        0x08: "MIPS",
        0x14: "PowerPC",
        0x28: "ARM (32-bit)",
        0x32: "IA-64 (Itanium)",
        0x3E: "AMD x86-64 (AMD64)",
        0xB7: "AArch64 (ARM 64-bit)",
        0xF3: "RISC-V"
    }

    SECTION_TYPES = {
        0: "NULL",
        1: "PROGBITS",
        2: "SYMTAB",
        3: "STRTAB",
        4: "RELA",
        5: "HASH",
        6: "DYNAMIC",
        7: "NOTE",
        8: "NOBITS (BSS)",
        9: "REL",
        10: "SHLIB",
        11: "DYNSYM"
    }

    @staticmethod
    def parse_file(filepath: str) -> Dict[str, Any]:
        with open(filepath, "rb") as f:
            data = f.read()
        return ElfAnalyzer.parse_bytes(data)

    @staticmethod
    def parse_bytes(data: bytes) -> Dict[str, Any]:
        if len(data) < 52 or data[:4] != b"\x7fELF":
            return {"valid": False, "error": "Not a valid ELF binary (Magic bytes mismatch)"}

        ei_class = data[4]    # 1: 32-bit, 2: 64-bit
        ei_data = data[5]     # 1: Little-endian, 2: Big-endian
        ei_version = data[6]
        ei_osabi = data[7]
        ei_abiversion = data[8]

        endian = "<" if ei_data == 1 else ">"
        is_64 = (ei_class == 2)

        result: Dict[str, Any] = {
            "valid": True,
            "class": "ELF64 (64-bit)" if is_64 else "ELF32 (32-bit)",
            "is_64": is_64,
            "endian": "Little Endian" if ei_data == 1 else "Big Endian",
            "os_abi": ElfAnalyzer.OS_ABI.get(ei_osabi, f"Unknown (0x{ei_osabi:02x})"),
            "file_size": len(data)
        }

        try:
            if is_64:
                header = struct.unpack(endian + "HHIQQQIHHHHHH", data[16:64])
                (e_type, e_machine, e_version, e_entry, e_phoff, e_shoff,
                 e_flags, e_ehsize, e_phentsize, e_phnum, e_shentsize, e_shnum, e_shstrndx) = header
            else:
                header = struct.unpack(endian + "HHIIIIIHHHHHH", data[16:52])
                (e_type, e_machine, e_version, e_entry, e_phoff, e_shoff,
                 e_flags, e_ehsize, e_phentsize, e_phnum, e_shentsize, e_shnum, e_shstrndx) = header

            result["type"] = ElfAnalyzer.FILE_TYPES.get(e_type, f"Unknown ({e_type})")
            result["machine"] = ElfAnalyzer.MACHINES.get(e_machine, f"Unknown (0x{e_machine:02x})")
            result["entry_point"] = f"0x{e_entry:016x}" if is_64 else f"0x{e_entry:08x}"
            result["program_headers_offset"] = e_phoff
            result["section_headers_offset"] = e_shoff
            result["flags"] = f"0x{e_flags:08x}"
            result["header_size"] = e_ehsize
            result["program_headers_count"] = e_phnum
            result["section_headers_count"] = e_shnum

            # Parse Section Headers
            sections: List[Dict[str, Any]] = []
            shstrtab_data = b""

            # First pass: find shstrtab offset and size
            if 0 <= e_shstrndx < e_shnum and e_shoff > 0:
                if is_64:
                    sh_entry_off = e_shoff + e_shstrndx * e_shentsize
                    if sh_entry_off + 64 <= len(data):
                        sh = struct.unpack(endian + "IIQQQQIIQQ", data[sh_entry_off:sh_entry_off+64])
                        sh_offset = sh[4]
                        sh_size = sh[5]
                        shstrtab_data = data[sh_offset:sh_offset + sh_size]
                else:
                    sh_entry_off = e_shoff + e_shstrndx * e_shentsize
                    if sh_entry_off + 40 <= len(data):
                        sh = struct.unpack(endian + "IIIIIIIIII", data[sh_entry_off:sh_entry_off+40])
                        sh_offset = sh[4]
                        sh_size = sh[5]
                        shstrtab_data = data[sh_offset:sh_offset + sh_size]

            def get_str(strtab: bytes, offset: int) -> str:
                if offset >= len(strtab):
                    return ""
                end = strtab.find(b"\x00", offset)
                if end == -1:
                    end = len(strtab)
                return strtab[offset:end].decode("ascii", errors="replace")

            # Second pass: read all sections
            symtab_sh = None
            strtab_sh = None

            for i in range(e_shnum):
                entry_off = e_shoff + i * e_shentsize
                if entry_off + e_shentsize > len(data):
                    break
                if is_64:
                    sh = struct.unpack(endian + "IIQQQQIIQQ", data[entry_off:entry_off+64])
                    s_name_idx, s_type, s_flags, s_addr, s_offset, s_size, s_link, s_info, s_align, s_entsize = sh
                else:
                    sh = struct.unpack(endian + "IIIIIIIIII", data[entry_off:entry_off+40])
                    s_name_idx, s_type, s_flags, s_addr, s_offset, s_size, s_link, s_info, s_align, s_entsize = sh

                name = get_str(shstrtab_data, s_name_idx) if shstrtab_data else f"sec_{i}"
                if not name and i == 0:
                    name = "<UNDEF>"

                flag_str = ""
                if s_flags & 0x1: flag_str += "W"  # WRITE
                if s_flags & 0x2: flag_str += "A"  # ALLOC
                if s_flags & 0x4: flag_str += "X"  # EXECINSTR

                sec_info = {
                    "index": i,
                    "name": name,
                    "type": ElfAnalyzer.SECTION_TYPES.get(s_type, f"0x{s_type:x}"),
                    "flags": flag_str or "-",
                    "addr": f"0x{s_addr:x}",
                    "offset": s_offset,
                    "size": s_size,
                    "align": s_align
                }
                sections.append(sec_info)

                if name == ".symtab":
                    symtab_sh = (s_offset, s_size, s_entsize, s_link)
                elif name == ".strtab":
                    strtab_sh = (s_offset, s_size)

            result["sections"] = sections

            # Parse Symbols if present
            symbols: List[Dict[str, Any]] = []
            if symtab_sh and strtab_sh:
                sym_off, sym_size, sym_entsize, _ = symtab_sh
                str_off, str_size = strtab_sh
                sym_strtab = data[str_off:str_off + str_size]
                if sym_entsize > 0:
                    count = sym_size // sym_entsize
                    for si in range(min(count, 100)): # Limit to first 100 symbols
                        s_pos = sym_off + si * sym_entsize
                        if is_64 and s_pos + 24 <= len(data):
                            st_name, st_info, st_other, st_shndx, st_value, st_size = struct.unpack(endian + "IBBHQQ", data[s_pos:s_pos+24])
                            s_name = get_str(sym_strtab, st_name)
                            if s_name:
                                symbols.append({
                                    "name": s_name,
                                    "value": f"0x{st_value:x}",
                                    "size": st_size,
                                    "section_index": st_shndx
                                })
                        elif not is_64 and s_pos + 16 <= len(data):
                            st_name, st_value, st_size, st_info, st_other, st_shndx = struct.unpack(endian + "IIIBBH", data[s_pos:s_pos+16])
                            s_name = get_str(sym_strtab, st_name)
                            if s_name:
                                symbols.append({
                                    "name": s_name,
                                    "value": f"0x{st_value:x}",
                                    "size": st_size,
                                    "section_index": st_shndx
                                })
            result["symbols"] = symbols

        except Exception as ex:
            result["error"] = f"Header parse error: {str(ex)}"

        return result

    @staticmethod
    def format_summary(info: Dict[str, Any]) -> str:
        """Produces a human-readable inspection report."""
        if not info.get("valid"):
            return f"Error: {info.get('error', 'Invalid ELF file')}"

        out = []
        out.append("==================================================")
        out.append("               ELF BINARY ANALYSIS                ")
        out.append("==================================================")
        out.append(f" Format:        {info['class']} ({info['endian']})")
        out.append(f" File Type:     {info['type']}")
        out.append(f" Machine Target:{info['machine']}")
        out.append(f" OS / ABI:      {info['os_abi']}")
        out.append(f" Entry Point:   {info['entry_point']}")
        out.append(f" File Size:     {info['file_size']} bytes")
        out.append(f" Sections:      {len(info.get('sections', []))}")
        out.append("--------------------------------------------------")
        out.append("SECTION HEADERS:")
        out.append(f" {'Idx':<4} {'Name':<18} {'Type':<12} {'Flags':<6} {'Size':<10} {'Address'}")
        out.append("-" * 65)
        for s in info.get("sections", []):
            out.append(f" [{s['index']:2d}] {s['name']:<18} {s['type']:<12} {s['flags']:<6} {s['size']:<10} {s['addr']}")

        symbols = info.get("symbols", [])
        if symbols:
            out.append("--------------------------------------------------")
            out.append(f"SYMBOLS (Sample of {len(symbols)}):")
            out.append(f" {'Symbol Name':<30} {'Value':<18} {'Size'}")
            out.append("-" * 60)
            for sym in symbols[:30]:
                out.append(f" {sym['name']:<30} {sym['value']:<18} {sym['size']}")

        out.append("==================================================")
        return "\n".join(out)
