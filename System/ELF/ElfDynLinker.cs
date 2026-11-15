// ElfDynLinker.cs - Runtime dynamic linker for Windose OS.
// Loads ET_DYN shared libraries (.so) listed in DT_NEEDED, handles dynamic symbol resolution,
// applies relocations across shared libraries, and wires KernelApi and runtime lifecycle.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Windose.System.Elf
{
    /// <summary>One loaded shared library image (pinned in memory for the life of its owning program).</summary>
    public sealed class LoadedLib
    {
        public string Name = "";
        public string ResolvedPath = "";
        public byte[]? Image;        // backing byte array (GC-pinned)
        public GCHandle ImageHandle;
        public ulong Address;       // 4 KB-aligned start of image inside Image[]
        public ulong Size;          // total image byte count
        public ulong Low;           // lowest VAddr across all PT_LOAD segments
        public ulong Bias;          // runtime address offset: Address - Low
        public ElfFile? Elf;

        /// <summary>Runtime address of every globally-visible exported symbol.</summary>
        public readonly Dictionary<string, ulong> Exports =
            new Dictionary<string, ulong>(StringComparer.Ordinal);

        public void Free()
        {
            if (ImageHandle.IsAllocated)
            {
                try { ImageHandle.Free(); } catch { }
            }
            Image = null;
        }
    }

    public static class ElfDynLinker
    {
        // Standard virtual-filesystem paths searched in order for shared libraries.
        public static readonly string[] DefaultSearchPaths =
        {
            "/mnt/Programs",
            "/mnt/Programs/lib",
            "/mnt/System/lib",
            "/mnt/lib",
            "/System/lib",
            "/lib"
        };

        // Standard Linux ABI aliases to Windose runtime libraries
        private static readonly Dictionary<string, string> LibAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "libc.so.6", "libc.so" },
            { "ld-linux-x86-64.so.2", "libc.so" },
            { "ld-musl-x86_64.so.1", "libc.so" },
            { "libm.so.6", "libm.so" },
            { "libpthread.so.0", "libc.so" },
            { "libdl.so.2", "libc.so" },
            { "librt.so.1", "libc.so" }
        };

        /// <summary>
        /// Links the main ELF program against all required shared libraries:
        /// 1. Reads dynamic info from main image.
        /// 2. Recursively loads all DT_NEEDED libraries into memory.
        /// 3. Collects all exported symbols into a global symbol table.
        /// 4. Applies relocations to all loaded libraries.
        /// 5. Applies relocations to the main executable.
        /// 6. Binds __k if exported by any library.
        /// </summary>
        public static bool Link(
            ElfProgram prog,
            ElfFile mainElf,
            byte[] mainBacking,
            int mainOffset,
            ulong mainLow,
            ulong mainSize,
            ulong mainBias,
            out string? error)
        {
            error = null;

            // Step 1: Ensure dynamic info is read from the main executable
            if (!mainElf.ReadDynamicInfo(mainBacking, mainOffset, mainLow, mainSize, out error))
            {
                return false;
            }

            var loaded = new List<LoadedLib>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var globs = new Dictionary<string, ulong>(StringComparer.Ordinal);

            // Step 2: Recursively load all library dependencies (Pass 1 - Image mapping & symbol collection)
            if (mainElf.NeededLibraries != null)
            {
                foreach (string needed in mainElf.NeededLibraries)
                {
                    if (!LoadLibraryRecursive(prog, needed, seen, loaded, globs, out error))
                    {
                        // Clean up any partially loaded libraries
                        foreach (var l in loaded) l.Free();
                        return false;
                    }
                }
            }

            prog.LoadedLibs = loaded;

            // Build dynamic resolver delegate from the global symbol table
            Func<string, ulong> resolver = name =>
            {
                if (globs.TryGetValue(name, out ulong addr))
                    return addr;
                return 0; // unresolved external symbol
            };

            // Step 3: Apply relocations to all loaded shared libraries (Pass 2)
            foreach (LoadedLib lib in loaded)
            {
                if (lib.Elf != null && lib.Image != null)
                {
                    int offset = (int)(lib.Address - (ulong)(nint)lib.ImageHandle.AddrOfPinnedObject());
                    if (!lib.Elf.ApplyRelocations(lib.Image, offset, lib.Low, lib.Size, lib.Bias, out string? relErr, resolver))
                    {
                        error = $"{lib.Name}: dynamic relocation failed: {relErr}";
                        return false;
                    }

                    if (ElfRunner.MakeExecutable != null)
                    {
                        ElfRunner.MakeExecutable(lib.Address, lib.Size);
                    }
                }
            }

            // Step 4: Apply relocations to the main program (Pass 3)
            if (!mainElf.ApplyRelocations(mainBacking, mainOffset, mainLow, mainSize, mainBias, out error, resolver))
            {
                return false;
            }

            // Step 5: Check for __k symbol in loaded libraries to inject KernelApi*
            if (globs.TryGetValue("__k", out ulong kAddr) && kAddr != 0)
            {
                prog.ExternalKernelApiAddress = kAddr;
            }

            return true;
        }

        private static bool LoadLibraryRecursive(
            ElfProgram prog,
            string libName,
            HashSet<string> seen,
            List<LoadedLib> loaded,
            Dictionary<string, ulong> globs,
            out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(libName)) return true;

            string normalizedName = Path.GetFileName(libName);
            if (seen.Contains(normalizedName)) return true;
            seen.Add(normalizedName);

            // Also mark any alias as seen
            if (LibAliases.TryGetValue(normalizedName, out string? alias) && !seen.Contains(alias))
            {
                seen.Add(alias);
            }

            // Find library bytes
            string? foundPath;
            byte[]? data = ResolveLibrary(prog, normalizedName, out foundPath);
            if (data == null)
            {
                error = $"shared library not found: '{normalizedName}'";
                return false;
            }

            if (!ElfFile.IsElf(data))
            {
                error = $"'{normalizedName}': invalid ELF header";
                return false;
            }

            ElfFile elf = ElfFile.Parse(data, out string parseErr);
            if (elf == null)
            {
                error = $"'{normalizedName}': parse error: {parseErr}";
                return false;
            }

            if (!elf.Is64 || !elf.LittleEndian || elf.Machine != ElfConst.EM_X86_64)
            {
                error = $"'{normalizedName}': architecture mismatch (requires x86-64 LE ELF64)";
                return false;
            }

            if (elf.Type != ElfConst.ET_DYN)
            {
                error = $"'{normalizedName}': not a shared object (ET_DYN required)";
                return false;
            }

            ulong low, size;
            if (!elf.GetImageExtent(ElfRunner.MaxImageSize, out low, out size))
            {
                error = $"'{normalizedName}': {elf.Error}";
                return false;
            }

            // Allocate backing array with 4 KB alignment slack
            byte[] backing = new byte[(int)size + 4096];
            GCHandle handle;
            try
            {
                handle = GCHandle.Alloc(backing, GCHandleType.Pinned);
            }
            catch (Exception ex)
            {
                error = $"'{normalizedName}': failed to pin memory: {ex.Message}";
                return false;
            }

            ulong rawAddr = (ulong)(nint)handle.AddrOfPinnedObject();
            ulong aligned = (rawAddr + 4095) & ~4095UL;
            int offset = (int)(aligned - rawAddr);

            elf.CopyImage(backing, offset, low);
            ulong bias = aligned - low;

            if (!elf.ReadDynamicInfo(backing, offset, low, size, out error))
            {
                handle.Free();
                return false;
            }

            var lib = new LoadedLib
            {
                Name = normalizedName,
                ResolvedPath = foundPath ?? normalizedName,
                Image = backing,
                ImageHandle = handle,
                Address = aligned,
                Size = size,
                Low = low,
                Bias = bias,
                Elf = elf
            };

            // Register exported symbols into the global map
            ElfSymbol[] syms = elf.GetSymbols();
            foreach (ElfSymbol sym in syms)
            {
                if (string.IsNullOrEmpty(sym.Name)) continue;
                if (sym.SectionIndex == 0) continue; // SHN_UNDEF (imported)
                int bind = sym.Info >> 4;
                if (bind == 0) continue; // STB_LOCAL

                ulong runtimeAddr = sym.SectionIndex == 0xFFF1 // SHN_ABS
                    ? sym.Value
                    : unchecked(bias + sym.Value);

                if (!globs.ContainsKey(sym.Name))
                {
                    globs[sym.Name] = runtimeAddr;
                }

                lib.Exports[sym.Name] = runtimeAddr;
            }

            loaded.Add(lib);

            // Recursively load this library's own DT_NEEDED dependencies
            if (elf.NeededLibraries != null)
            {
                foreach (string dep in elf.NeededLibraries)
                {
                    if (!LoadLibraryRecursive(prog, dep, seen, loaded, globs, out error))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Searches virtual filesystem, binary cache, program directory, and aliases for a shared library.
        /// </summary>
        public static byte[]? ResolveLibrary(ElfProgram? prog, string libName, out string? resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrEmpty(libName)) return null;

            // 1. Direct path / filename check in binary cache
            byte[]? cached = ElfRunner.LoadBinary(libName);
            if (cached != null)
            {
                resolvedPath = libName;
                return cached;
            }

            // 2. Program's own directory (if program was launched from a file)
            if (prog?.FilePath != null)
            {
                string? progDir = Path.GetDirectoryName(prog.FilePath);
                if (!string.IsNullOrEmpty(progDir))
                {
                    string cand = Path.Combine(progDir, libName);
                    byte[]? d = ReadVfsOrDisk(cand);
                    if (d != null)
                    {
                        resolvedPath = cand;
                        ElfRunner.Preload(cand, d);
                        return d;
                    }
                }
            }

            // 3. Search standard library paths
            foreach (string dir in DefaultSearchPaths)
            {
                string cand = dir + "/" + libName;
                byte[]? d = ReadVfsOrDisk(cand);
                if (d != null)
                {
                    resolvedPath = cand;
                    ElfRunner.Preload(cand, d);
                    ElfRunner.Preload(libName, d);
                    return d;
                }
            }

            // 4. Try aliases (e.g. libc.so.6 -> libc.so or libwindose.so)
            if (LibAliases.TryGetValue(libName, out string? alias) && !string.IsNullOrEmpty(alias))
            {
                byte[]? d = ResolveLibrary(prog, alias, out resolvedPath);
                if (d != null)
                {
                    ElfRunner.Preload(libName, d);
                    return d;
                }
            }

            // 5. Try stripped version suffix (libfoo.so.1 -> libfoo.so)
            int soIndex = libName.IndexOf(".so.", StringComparison.OrdinalIgnoreCase);
            if (soIndex > 0)
            {
                string baseName = libName.Substring(0, soIndex + 3);
                byte[]? d = ResolveLibrary(prog, baseName, out resolvedPath);
                if (d != null)
                {
                    ElfRunner.Preload(libName, d);
                    return d;
                }
            }

            return null;
        }

        private static byte[]? ReadVfsOrDisk(string path)
        {
            try
            {
                if (ElfRunner.Host != null)
                {
                    byte[]? data = ElfRunner.Host.ReadFile(path);
                    if (data != null) return data;
                }

                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
            catch
            {
                // ignore I/O exceptions during probing
            }
            return null;
        }
    }
}
