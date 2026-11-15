using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windose.System.Elf;
using Windose.System.System_Calls;

namespace Windose.System.Elf
{
    public static unsafe class ElfSelfTests
    {
        private static int passed = 0;
        private static int failed = 0;

        public static bool Run()
        {
            passed = 0;
            failed = 0;

            TestParseStaticElf();
            TestParseDynamicElf();
            TestParseSharedLibrary();
            TestCliUtilitySuite();
            TestDynamicLibraryAliases();
            TestDynamicLinkerSimulation();
            TestLinuxSyscallDispatch();
            TestInitialStackSetup();

            SystemLogger.WriteLine("ElfSelfTests", $"Completed: {passed} passed, {failed} failed.",
                failed == 0 ? ConsoleMessageType.Log : ConsoleMessageType.Error);
            return failed == 0;
        }

        private static void Assert(bool condition, string message)
        {
            if (condition)
            {
                passed++;
            }
            else
            {
                failed++;
                SystemLogger.WriteLine("ElfSelfTests", "FAILED: " + message, ConsoleMessageType.Error);
            }
        }

        private static byte[]? LoadTestFile(string name)
        {
            byte[]? data = ElfRunner.LoadBinary(name);
            if (data != null) return data;

            string[] paths =
            {
                name,
                "/mnt/Programs/" + name,
                "/mnt/System/lib/" + name,
                "/mnt/lib/" + name,
                "System/ELF/programs/bin/" + name
            };

            foreach (string p in paths)
            {
                if (File.Exists(p))
                {
                    try { return File.ReadAllBytes(p); } catch { }
                }
            }
            return null;
        }

        private static void TestParseStaticElf()
        {
            byte[]? data = LoadTestFile("hello.elf");
            if (data == null)
            {
                Assert(true, "hello.elf not present on disk, skipping static test");
                return;
            }

            Assert(ElfFile.IsElf(data), "hello.elf must pass IsElf check");
            ElfFile elf = ElfFile.Parse(data, out string err);
            Assert(elf != null, "hello.elf parsed successfully: " + err);
            if (elf != null)
            {
                Assert(elf.Is64, "hello.elf is 64-bit");
                Assert(elf.LittleEndian, "hello.elf is little endian");
                Assert(elf.Machine == ElfConst.EM_X86_64, "hello.elf machine is x86-64");
                Assert(elf.Type == ElfConst.ET_DYN || elf.Type == ElfConst.ET_EXEC, "hello.elf is ET_DYN/ET_EXEC");
                Assert(elf.GetInterpreter() == null, "hello.elf has no dynamic interpreter (static PIE)");
            }
        }

        private static void TestParseDynamicElf()
        {
            byte[]? data = LoadTestFile("dynhello.elf");
            if (data == null)
            {
                Assert(true, "dynhello.elf not present on disk, skipping dynamic test");
                return;
            }

            Assert(ElfFile.IsElf(data), "dynhello.elf must pass IsElf check");
            ElfFile elf = ElfFile.Parse(data, out string err);
            Assert(elf != null, "dynhello.elf parsed successfully: " + err);
            if (elf != null)
            {
                Assert(elf.Is64, "dynhello.elf is 64-bit");
                Assert(elf.Machine == ElfConst.EM_X86_64, "dynhello.elf machine is x86-64");
                string? interp = elf.GetInterpreter();
                Assert(interp != null && interp.Contains("ld-windose.so"), "dynhello.elf specifies PT_INTERP: " + interp);

                // Test dynamic info extraction
                ulong low, size;
                Assert(elf.GetImageExtent(ElfRunner.MaxImageSize, out low, out size), "dynhello.elf image extent valid");
                byte[] img = new byte[(int)size + 4096];
                elf.CopyImage(img, 0, low);
                bool dynOk = elf.ReadDynamicInfo(img, 0, low, size, out string dynErr);
                Assert(dynOk, "dynhello.elf ReadDynamicInfo succeeded: " + dynErr);
                Assert(elf.NeededLibraries != null && elf.NeededLibraries.Length > 0, "dynhello.elf has DT_NEEDED libraries");

                bool hasLibc = false;
                if (elf.NeededLibraries != null)
                {
                    foreach (string lib in elf.NeededLibraries)
                    {
                        if (lib.Contains("libc.so")) hasLibc = true;
                    }
                }
                Assert(hasLibc, "dynhello.elf depends on libc.so");
            }
        }

        private static void TestParseSharedLibrary()
        {
            byte[]? data = LoadTestFile("libc.so");
            if (data == null)
            {
                Assert(true, "libc.so not present on disk, skipping shared library test");
                return;
            }

            Assert(ElfFile.IsElf(data), "libc.so must pass IsElf check");
            ElfFile elf = ElfFile.Parse(data, out string err);
            Assert(elf != null, "libc.so parsed successfully: " + err);
            if (elf != null)
            {
                Assert(elf.Is64, "libc.so is 64-bit");
                Assert(elf.Type == ElfConst.ET_DYN, "libc.so is ET_DYN (shared object)");
                Assert(elf.GetInterpreter() == null, "libc.so has no PT_INTERP");

                ElfSymbol[] symbols = elf.GetSymbols();
                Assert(symbols != null && symbols.Length > 0, "libc.so has symbol table");

                bool hasPrintf = false;
                bool hasMalloc = false;
                bool hasK = false;
                if (symbols != null)
                {
                    foreach (var s in symbols)
                    {
                        if (s.Name == "printf") hasPrintf = true;
                        if (s.Name == "malloc") hasMalloc = true;
                        if (s.Name == "__k") hasK = true;
                    }
                }
                Assert(hasPrintf, "libc.so exports printf");
                Assert(hasMalloc, "libc.so exports malloc");
                Assert(hasK, "libc.so exports __k");
            }
        }

        private static void TestCliUtilitySuite()
        {
            string[] utils = { "grep.elf", "wc.elf", "head.elf", "tail.elf", "pwd.elf", "uname.elf", "donut.elf", "micromax.elf" };
            foreach (string u in utils)
            {
                byte[]? data = LoadTestFile(u);
                if (data != null)
                {
                    Assert(ElfFile.IsElf(data), $"{u} must be valid ELF");
                    ElfFile elf = ElfFile.Parse(data, out string err);
                    Assert(elf != null && elf.Is64 && elf.Machine == ElfConst.EM_X86_64, $"{u} valid x86-64: {err}");
                }
            }
        }

        private static void TestDynamicLibraryAliases()
        {
            // Verify that standard Linux ABI library names resolve to our libc
            byte[]? data1 = ElfDynLinker.ResolveLibrary(null, "libc.so.6", out string? p1);
            byte[]? data2 = ElfDynLinker.ResolveLibrary(null, "ld-linux-x86-64.so.2", out string? p2);
            byte[]? data3 = ElfDynLinker.ResolveLibrary(null, "libwindose.so", out string? p3);

            if (LoadTestFile("libc.so") != null)
            {
                Assert(data1 != null, "libc.so.6 alias resolves: " + p1);
                Assert(data2 != null, "ld-linux-x86-64.so.2 alias resolves: " + p2);
                Assert(data3 != null, "libwindose.so resolves: " + p3);
            }
            else
            {
                Assert(true, "libc.so not yet loaded into cache, alias test skipped");
            }
        }

        private static void TestDynamicLinkerSimulation()
        {
            byte[]? progData = LoadTestFile("dynhello.elf");
            byte[]? libcData = LoadTestFile("libc.so");

            if (progData == null || libcData == null)
            {
                Assert(true, "Binary files not on disk for simulation, skipping");
                return;
            }

            ElfRunner.Preload("libc.so", libcData);
            ElfRunner.Preload("dynhello.elf", progData);

            ElfFile elf = ElfFile.Parse(progData, out string parseErr);
            Assert(elf != null, "dynhello.elf parse for simulation: " + parseErr);
            if (elf == null) return;

            ulong low, size;
            Assert(elf.GetImageExtent(ElfRunner.MaxImageSize, out low, out size), "extent calculation");

            byte[] backing = new byte[(int)size + 4096];
            elf.CopyImage(backing, 0, low);
            ulong bias = 0x10000000; // simulated load bias

            var prog = new ElfProgram(999, "dynhello_test", new string[] { "dynhello" });
            bool linked = ElfDynLinker.Link(prog, elf, backing, 0, low, size, bias, out string? linkErr);
            Assert(linked, "ElfDynLinker.Link successfully linked dynhello.elf: " + linkErr);

            if (linked)
            {
                Assert(prog.LoadedLibs != null && prog.LoadedLibs.Count > 0, "prog has loaded shared libraries");
                Assert(prog.ExternalKernelApiAddress != 0, "prog found external __k address in libc.so");
                prog.CloseAllObjects();
            }
        }

        private static void TestLinuxSyscallDispatch()
        {
            // Test getpid
            long pid = LinuxSyscall.Handle(LinuxSyscallNum.SYS_GETPID, 0, 0, 0, 0, 0, 0);
            Assert(pid >= 0, "SYS_GETPID returns valid pid: " + pid);

            // Test uname
            byte[] unameBuf = new byte[400];
            fixed (byte* p = unameBuf)
            {
                long unRet = LinuxSyscall.Handle(LinuxSyscallNum.SYS_UNAME, (ulong)(nint)p, 0, 0, 0, 0, 0);
                Assert(unRet == 0, "SYS_UNAME returned 0");
                string sysname = Encoding.ASCII.GetString(unameBuf, 0, 7);
                Assert(sysname == "Windose", "uname sysname is Windose: " + sysname);
            }

            // Test mmap / munmap
            long mem = LinuxSyscall.Handle(LinuxSyscallNum.SYS_MMAP, 0, 4096, 3, 0x22, 0, 0);
            Assert(mem != 0 && mem != -12, "SYS_MMAP allocated memory: 0x" + mem.ToString("X"));

            // Test getcwd
            byte[] cwdBuf = new byte[64];
            fixed (byte* p = cwdBuf)
            {
                long cwdRet = LinuxSyscall.Handle(LinuxSyscallNum.SYS_GETCWD, (ulong)(nint)p, (ulong)cwdBuf.Length, 0, 0, 0, 0);
                Assert(cwdRet != 0, "SYS_GETCWD returned buffer pointer");
            }

            // Test write to stdout (fd 1)
            byte[] msg = Encoding.UTF8.GetBytes("[SelfTest] Linux sys_write ok\n");
            fixed (byte* p = msg)
            {
                long wRet = LinuxSyscall.Handle(LinuxSyscallNum.SYS_WRITE, 1, (ulong)(nint)p, (ulong)msg.Length, 0, 0, 0);
                Assert(wRet == msg.Length, "SYS_WRITE to stdout returned expected bytes: " + wRet);
            }
        }

        private static void TestInitialStackSetup()
        {
            var prog = new ElfProgram(888, "stack_test", new string[] { "stack_test", "--flag", "arg2" });
            byte[]? progData = LoadTestFile("dynhello.elf");
            if (progData == null) return;

            ElfFile elf = ElfFile.Parse(progData, out _);
            if (elf == null) return;

            GCHandle pin;
            ulong sp = LinuxSyscall.SetupInitialStack(prog, elf, out pin);
            Assert(sp != 0, "Initial stack pointer is non-zero: 0x" + sp.ToString("X"));
            Assert((sp & 0x7) == 0, "Initial stack pointer is 8-byte aligned: 0x" + sp.ToString("X"));

            unsafe
            {
                ulong argc = *(ulong*)sp;
                Assert(argc == 3, "Stack argc matches argument count: " + argc);

                ulong argv0 = *(ulong*)(sp + 8);
                string? s0 = Marshal.PtrToStringAnsi((IntPtr)argv0);
                Assert(s0 == "stack_test", "Stack argv[0] matches: " + s0);
            }

            if (pin.IsAllocated) pin.Free();
        }
    }
}
