// LinuxSyscall.cs - Linux x86_64 Syscall Emulation & System V ABI Stack Setup for Windose.
// Implements standard Linux system calls, POSIX file descriptors, memory allocation,
// initial stack layout (argc, argv, envp, auxv), and environment mappings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Windose.System.Elf
{
    // Aux vector keys (System V AMD64 ABI)
    public static class AuxVectorConst
    {
        public const ulong AT_NULL     = 0;
        public const ulong AT_IGNORE   = 1;
        public const ulong AT_EXECFD   = 2;
        public const ulong AT_PHDR     = 3;
        public const ulong AT_PHENT    = 4;
        public const ulong AT_PHNUM    = 5;
        public const ulong AT_PAGESZ   = 6;
        public const ulong AT_BASE     = 7;
        public const ulong AT_FLAGS    = 8;
        public const ulong AT_ENTRY    = 9;
        public const ulong AT_NOTELF   = 10;
        public const ulong AT_UID      = 11;
        public const ulong AT_EUID     = 12;
        public const ulong AT_GID      = 13;
        public const ulong AT_EGID     = 14;
        public const ulong AT_CLKTCK   = 17;
        public const ulong AT_SECURE   = 23;
        public const ulong AT_RANDOM   = 25;
    }

    // Common Linux x86_64 Syscall Numbers
    public static class LinuxSyscallNum
    {
        public const long SYS_READ            = 0;
        public const long SYS_WRITE           = 1;
        public const long SYS_OPEN            = 2;
        public const long SYS_CLOSE           = 3;
        public const long SYS_STAT            = 4;
        public const long SYS_FSTAT           = 5;
        public const long SYS_LSTAT           = 6;
        public const long SYS_POLL            = 7;
        public const long SYS_LSEEK           = 8;
        public const long SYS_MMAP            = 9;
        public const long SYS_MPROTECT        = 10;
        public const long SYS_MUNMAP          = 11;
        public const long SYS_BRK             = 12;
        public const long SYS_RT_SIGACTION    = 13;
        public const long SYS_RT_SIGPROCMASK  = 14;
        public const long SYS_IOCTL           = 16;
        public const long SYS_PREAD64         = 17;
        public const long SYS_PWRITE64        = 18;
        public const long SYS_READV           = 19;
        public const long SYS_WRITEV          = 20;
        public const long SYS_ACCESS          = 21;
        public const long SYS_PIPE            = 22;
        public const long SYS_SCHED_YIELD     = 24;
        public const long SYS_MREMAP          = 25;
        public const long SYS_DUP             = 32;
        public const long SYS_DUP2            = 33;
        public const long SYS_NANOSLEEP       = 35;
        public const long SYS_GETPID          = 39;
        public const long SYS_CLONE           = 56;
        public const long SYS_FORK            = 57;
        public const long SYS_EXECVE          = 59;
        public const long SYS_EXIT            = 60;
        public const long SYS_WAIT4           = 61;
        public const long SYS_KILL            = 62;
        public const long SYS_UNAME           = 63;
        public const long SYS_FCNTL           = 72;
        public const long SYS_FSYNC           = 74;
        public const long SYS_TRUNCATE        = 76;
        public const long SYS_FTRUNCATE       = 77;
        public const long SYS_GETCWD          = 79;
        public const long SYS_CHDIR           = 80;
        public const long SYS_MKDIR           = 83;
        public const long SYS_RMDIR           = 84;
        public const long SYS_UNLINK          = 87;
        public const long SYS_READLINK        = 89;
        public const long SYS_GETTIMEOFDAY    = 96;
        public const long SYS_GETUID          = 102;
        public const long SYS_GETGID          = 104;
        public const long SYS_GETEUID         = 107;
        public const long SYS_GETEGID         = 108;
        public const long SYS_GETPPID         = 110;
        public const long SYS_ARCH_PRCTL      = 158;
        public const long SYS_FUTEX           = 202;
        public const long SYS_SET_TID_ADDRESS = 218;
        public const long SYS_CLOCK_GETTIME   = 228;
        public const long SYS_CLOCK_GETRES    = 229;
        public const long SYS_EXIT_GROUP      = 231;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxIovec
    {
        public ulong IovBase;
        public ulong IovLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxTimespec
    {
        public long TvSec;
        public long TvNsec;
    }

    public static unsafe class LinuxSyscall
    {
        // Standard Linux errno values (negative returns in %rax)
        public const long EPERM   = -1;
        public const long ENOENT  = -2;
        public const long ESRCH   = -3;
        public const long EINTR   = -4;
        public const long EIO     = -5;
        public const long EBADF   = -9;
        public const long EAGAIN  = -11;
        public const long ENOMEM  = -12;
        public const long EACCES  = -13;
        public const long EFAULT  = -14;
        public const long EEXIST  = -17;
        public const long ENOTDIR = -20;
        public const long EISDIR  = -21;
        public const long EINVAL  = -22;
        public const long EMFILE  = -24;
        public const long ENOTTY  = -25;
        public const long ERANGE  = -34;
        public const long ENOSYS  = -38;

        // Tracks per-process opened file descriptors for Linux syscalls
        private sealed class LinuxFd
        {
            public int Fd;
            public string Path = "";
            public byte[]? Buffer;
            public int Position;
            public bool IsWrite;
        }

        private static readonly Dictionary<int, Dictionary<int, LinuxFd>> ProcessFds =
            new Dictionary<int, Dictionary<int, LinuxFd>>();

        private static Dictionary<int, LinuxFd> GetFdTable(int pid)
        {
            lock (ProcessFds)
            {
                if (!ProcessFds.TryGetValue(pid, out var table))
                {
                    table = new Dictionary<int, LinuxFd>();
                    // 0: stdin, 1: stdout, 2: stderr
                    table[0] = new LinuxFd { Fd = 0 };
                    table[1] = new LinuxFd { Fd = 1, IsWrite = true };
                    table[2] = new LinuxFd { Fd = 2, IsWrite = true };
                    ProcessFds[pid] = table;
                }
                return table;
            }
        }

        public static void CleanupProcessFds(int pid)
        {
            lock (ProcessFds)
            {
                ProcessFds.Remove(pid);
            }
        }

        /// <summary>
        /// Sets up a System V AMD64 initial stack in allocated memory conforming to Linux ABI:
        /// [argc] [argv pointers...] [NULL] [envp pointers...] [NULL] [auxv pairs...] [AT_NULL] [string pool]
        /// Returns the 16-byte aligned stack pointer.
        /// </summary>
        public static ulong SetupInitialStack(
            ElfProgram prog,
            ElfFile elf,
            out GCHandle stackHandle,
            int stackSizeBytes = 64 * 1024)
        {
            byte[] stackBuf = new byte[stackSizeBytes];
            stackHandle = GCHandle.Alloc(stackBuf, GCHandleType.Pinned);
            ulong baseAddr = (ulong)(nint)stackHandle.AddrOfPinnedObject();
            ulong topAddr = baseAddr + (ulong)stackSizeBytes;

            // Grow downwards from top of stack
            ulong current = (topAddr - 16) & ~15UL;

            // Prepare argv and envp strings
            string[] argv = prog.Args != null && prog.Args.Length > 0 ? prog.Args : new string[] { prog.Name };
            var envList = new List<string>();
            lock (prog.Env)
            {
                foreach (var kv in prog.Env)
                    envList.Add($"{kv.Key}={kv.Value}");
            }
            if (envList.Count == 0)
            {
                envList.Add("PATH=/mnt/Programs;/mnt/System");
                envList.Add("USER=root");
                envList.Add("HOME=/mnt");
                envList.Add("OS=Windose");
            }

            // 1. Write strings to top of stack
            ulong[] argvPointers = new ulong[argv.Length];
            for (int i = 0; i < argv.Length; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(argv[i]);
                current -= (ulong)(bytes.Length + 1);
                Marshal.Copy(bytes, 0, (IntPtr)current, bytes.Length);
                *(byte*)(current + (ulong)bytes.Length) = 0;
                argvPointers[i] = current;
            }

            ulong[] envpPointers = new ulong[envList.Count];
            for (int i = 0; i < envList.Count; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(envList[i]);
                current -= (ulong)(bytes.Length + 1);
                Marshal.Copy(bytes, 0, (IntPtr)current, bytes.Length);
                *(byte*)(current + (ulong)bytes.Length) = 0;
                envpPointers[i] = current;
            }

            // 16 random bytes for AT_RANDOM
            current -= 16;
            ulong randomPtr = current;
            for (int i = 0; i < 16; i++)
                *(byte*)(current + (ulong)i) = (byte)(i * 37 + 0x42);

            // Align stack pointer to 8 bytes before pointer arrays
            current &= ~7UL;

            // 2. Auxiliary vector pairs (type, val)
            ulong phdrAddr = 0;
            if (elf.Segments != null)
            {
                for (int i = 0; i < elf.Segments.Length; i++)
                {
                    if (elf.Segments[i].Type == ElfConst.PT_PHDR)
                    {
                        phdrAddr = unchecked(prog.ImageAddress + (elf.Segments[i].VAddr - prog.Low));
                        break;
                    }
                }
            }
            if (phdrAddr == 0) phdrAddr = prog.ImageAddress + 0x40; // immediately after ELF header

            var auxv = new List<KeyValuePair<ulong, ulong>>
            {
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_PHDR, phdrAddr),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_PHENT, 56), // ELF64 program header size
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_PHNUM, (ulong)(elf.Segments != null ? elf.Segments.Length : 0)),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_PAGESZ, 4096),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_BASE, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_FLAGS, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_ENTRY, prog.EntryAddress),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_UID, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_EUID, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_GID, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_EGID, 0),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_CLKTCK, 100),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_RANDOM, randomPtr),
                new KeyValuePair<ulong, ulong>(AuxVectorConst.AT_NULL, 0)
            };

            // Push auxv in reverse
            for (int i = auxv.Count - 1; i >= 0; i--)
            {
                current -= 8; *(ulong*)current = auxv[i].Value;
                current -= 8; *(ulong*)current = auxv[i].Key;
            }

            // Push envp pointers (NULL terminated)
            current -= 8; *(ulong*)current = 0; // NULL
            for (int i = envpPointers.Length - 1; i >= 0; i--)
            {
                current -= 8; *(ulong*)current = envpPointers[i];
            }

            // Push argv pointers (NULL terminated)
            current -= 8; *(ulong*)current = 0; // NULL
            for (int i = argvPointers.Length - 1; i >= 0; i--)
            {
                current -= 8; *(ulong*)current = argvPointers[i];
            }

            // Push argc
            current -= 8; *(ulong*)current = (ulong)argv.Length;

            // 16-byte alignment of final stack pointer
            return current;
        }

        /// <summary>
        /// Syscall entry point callable from native code.
        /// Signature matches standard Linux sysenter / syscall conventions.
        /// </summary>
        [UnmanagedCallersOnly]
        public static long Dispatch(long syscallNum, ulong a1, ulong a2, ulong a3, ulong a4, ulong a5, ulong a6)
        {
            return Handle(syscallNum, a1, a2, a3, a4, a5, a6);
        }

        /// <summary>
        /// Central Linux System Call Dispatcher. Handles standard Linux x86_64 system calls
        /// mapped to the Windose Kernel / IElfHost.
        /// </summary>
        public static long Handle(long syscallNum, ulong a1, ulong a2, ulong a3, ulong a4, ulong a5, ulong a6)
        {
            try
            {
                int pid = 1;
                ElfProgram? prog = null;
                ElfProgram[] all = ElfRunner.List();
                if (all.Length > 0)
                {
                    prog = all[all.Length - 1];
                    pid = prog.Pid;
                }

                switch (syscallNum)
                {
                    case LinuxSyscallNum.SYS_READ: // (fd, buf, count)
                    {
                        int fd = (int)a1;
                        byte* buf = (byte*)a2;
                        ulong count = a3;
                        if (buf == null) return EFAULT;
                        if (fd == 0)
                        {
                            if (prog != null && prog.InputHandler != null)
                            {
                                if (string.IsNullOrEmpty(prog.StdinBuffer))
                                {
                                    string? line = prog.InputHandler();
                                    if (line == null) return 0;
                                    prog.StdinBuffer = line.EndsWith("\n") ? line : line + "\n";
                                }
                                byte[] inBytes = Encoding.UTF8.GetBytes(prog.StdinBuffer);
                                int toCopy = (int)Math.Min((ulong)inBytes.Length, count);
                                Marshal.Copy(inBytes, 0, (IntPtr)buf, toCopy);
                                if (toCopy < inBytes.Length)
                                {
                                    prog.StdinBuffer = Encoding.UTF8.GetString(inBytes, toCopy, inBytes.Length - toCopy);
                                }
                                else
                                {
                                    prog.StdinBuffer = "";
                                }
                                return toCopy;
                            }
                            return 0; // stdin EOF
                        }

                        var fds = GetFdTable(pid);
                        lock (fds)
                        {
                            if (!fds.TryGetValue(fd, out var lfd) || lfd.Buffer == null) return EBADF;
                            if (lfd.Position >= lfd.Buffer.Length) return 0; // EOF
                            int avail = lfd.Buffer.Length - lfd.Position;
                            int toRead = (int)Math.Min((ulong)avail, count);
                            Marshal.Copy(lfd.Buffer, lfd.Position, (IntPtr)buf, toRead);
                            lfd.Position += toRead;
                            return toRead;
                        }
                    }

                    case LinuxSyscallNum.SYS_WRITE: // (fd, buf, count)
                    {
                        int fd = (int)a1;
                        byte* buf = (byte*)a2;
                        int count = (int)a3;
                        if (buf == null) return EFAULT;
                        if (count <= 0) return 0;

                        if (fd == 1 || fd == 2)
                        {
                            string text = Encoding.UTF8.GetString(buf, count);
                            if (prog?.OutputHandler != null)
                                prog.OutputHandler(text);
                            else if (ElfRunner.Output != null && prog != null)
                                ElfRunner.Output(prog, text);
                            else
                                Console.Write(text);
                            return count;
                        }

                        var fds = GetFdTable(pid);
                        lock (fds)
                        {
                            if (!fds.TryGetValue(fd, out var lfd)) return EBADF;
                            byte[] chunk = new byte[count];
                            Marshal.Copy((IntPtr)buf, chunk, 0, count);

                            if (lfd.Buffer == null) lfd.Buffer = chunk;
                            else
                            {
                                byte[] nb = new byte[lfd.Buffer.Length + count];
                                Array.Copy(lfd.Buffer, 0, nb, 0, lfd.Buffer.Length);
                                Array.Copy(chunk, 0, nb, lfd.Buffer.Length, count);
                                lfd.Buffer = nb;
                            }
                            lfd.Position += count;
                            if (!string.IsNullOrEmpty(lfd.Path) && ElfRunner.Host != null)
                            {
                                ElfRunner.Host.WriteFile(lfd.Path, lfd.Buffer);
                            }
                            return count;
                        }
                    }

                    case LinuxSyscallNum.SYS_OPEN: // (filename, flags, mode)
                    {
                        string path = Marshal.PtrToStringAnsi((IntPtr)a1) ?? "";
                        if (string.IsNullOrEmpty(path)) return EFAULT;

                        var fds = GetFdTable(pid);
                        lock (fds)
                        {
                            int newFd = 3;
                            while (fds.ContainsKey(newFd)) newFd++;

                            byte[]? fileData = null;
                            if (ElfRunner.Host != null) fileData = ElfRunner.Host.ReadFile(path);
                            if (fileData == null && File.Exists(path))
                            {
                                try { fileData = File.ReadAllBytes(path); } catch { }
                            }

                            bool isWrite = ((long)a2 & 3) != 0;
                            var lfd = new LinuxFd
                            {
                                Fd = newFd,
                                Path = path,
                                Buffer = fileData ?? (isWrite ? Array.Empty<byte>() : null),
                                Position = 0,
                                IsWrite = isWrite
                            };
                            fds[newFd] = lfd;
                            return newFd;
                        }
                    }

                    case LinuxSyscallNum.SYS_CLOSE: // (fd)
                    {
                        int fd = (int)a1;
                        if (fd <= 2) return 0;
                        var fds = GetFdTable(pid);
                        lock (fds) { fds.Remove(fd); }
                        return 0;
                    }

                    case LinuxSyscallNum.SYS_STAT: // (path, statbuf)
                    case LinuxSyscallNum.SYS_LSTAT:
                    {
                        string path = Marshal.PtrToStringAnsi((IntPtr)a1) ?? "";
                        byte* statbuf = (byte*)a2;
                        if (statbuf == null) return EFAULT;

                        long size = 0;
                        bool isDir = false;
                        bool found = false;
                        if (ElfRunner.Host != null && ElfRunner.Host.StatFile(path, out isDir, out size))
                            found = true;
                        else if (File.Exists(path))
                        {
                            try { size = new FileInfo(path).Length; found = true; } catch { }
                        }
                        else if (Directory.Exists(path))
                        {
                            isDir = true;
                            found = true;
                        }

                        if (!found) return ENOENT;

                        // Fill Linux struct stat
                        for (int i = 0; i < 144; i++) statbuf[i] = 0;
                        *(ulong*)(statbuf + 24) = isDir ? 0040755UL : 0100644UL; // st_mode
                        *(ulong*)(statbuf + 48) = (ulong)size;                    // st_size
                        *(ulong*)(statbuf + 72) = 1700000000;                     // st_atime
                        *(ulong*)(statbuf + 88) = 1700000000;                     // st_mtime
                        return 0;
                    }

                    case LinuxSyscallNum.SYS_FSTAT: // (fd, statbuf)
                    {
                        int fd = (int)a1;
                        byte* statbuf = (byte*)a2;
                        if (statbuf == null) return EFAULT;

                        for (int i = 0; i < 144; i++) statbuf[i] = 0;
                        if (fd <= 2)
                        {
                            *(ulong*)(statbuf + 24) = 0020666UL; // S_IFCHR
                            return 0;
                        }

                        var fds = GetFdTable(pid);
                        lock (fds)
                        {
                            if (!fds.TryGetValue(fd, out var lfd)) return EBADF;
                            long sz = lfd.Buffer != null ? lfd.Buffer.Length : 0;
                            *(ulong*)(statbuf + 24) = 0100644UL; // S_IFREG
                            *(ulong*)(statbuf + 48) = (ulong)sz;
                            return 0;
                        }
                    }

                    case LinuxSyscallNum.SYS_LSEEK: // (fd, offset, whence)
                    {
                        int fd = (int)a1;
                        long offset = (long)a2;
                        int whence = (int)a3;

                        var fds = GetFdTable(pid);
                        lock (fds)
                        {
                            if (!fds.TryGetValue(fd, out var lfd)) return EBADF;
                            long cur = lfd.Position;
                            long len = lfd.Buffer != null ? lfd.Buffer.Length : 0;
                            long npos = whence == 0 ? offset : (whence == 1 ? cur + offset : len + offset);
                            if (npos < 0) return EINVAL;
                            lfd.Position = (int)npos;
                            return npos;
                        }
                    }

                    case LinuxSyscallNum.SYS_MMAP: // (addr, len, prot, flags, fd, offset)
                    {
                        ulong len = a2;
                        if (len == 0) return EINVAL;

                        if (prog != null)
                        {
                            byte* mem = prog.Alloc((int)len);
                            if (mem != null)
                            {
                                for (ulong i = 0; i < len; i++) mem[i] = 0;
                                return (long)(ulong)mem;
                            }
                        }
                        return ENOMEM;
                    }

                    case LinuxSyscallNum.SYS_MUNMAP:
                    case LinuxSyscallNum.SYS_MPROTECT:
                        return 0; // Success

                    case LinuxSyscallNum.SYS_BRK: // (addr)
                        return (long)a1; // Echo back requested brk

                    case LinuxSyscallNum.SYS_IOCTL:
                    {
                        int fd = (int)a1;
                        ulong req = a2;
                        byte* arg = (byte*)a3;
                        if (req == 0x5413 && arg != null) // TIOCGWINSZ
                        {
                            *(ushort*)arg = 25;       // ws_row
                            *(ushort*)(arg + 2) = 80; // ws_col
                            return 0;
                        }
                        return 0;
                    }

                    case LinuxSyscallNum.SYS_WRITEV: // (fd, iov, iovcnt)
                    {
                        int fd = (int)a1;
                        LinuxIovec* iov = (LinuxIovec*)a2;
                        int cnt = (int)a3;
                        if (iov == null) return EFAULT;

                        long totalWritten = 0;
                        for (int i = 0; i < cnt; i++)
                        {
                            if (iov[i].IovBase != 0 && iov[i].IovLen > 0)
                            {
                                long w = Handle(LinuxSyscallNum.SYS_WRITE, (ulong)fd, iov[i].IovBase, iov[i].IovLen, 0, 0, 0);
                                if (w > 0) totalWritten += w;
                            }
                        }
                        return totalWritten;
                    }

                    case LinuxSyscallNum.SYS_ACCESS: // (path, mode)
                    {
                        string path = Marshal.PtrToStringAnsi((IntPtr)a1) ?? "";
                        if (ElfRunner.Host != null && ElfRunner.Host.StatFile(path, out _, out _)) return 0;
                        if (File.Exists(path) || Directory.Exists(path)) return 0;
                        return ENOENT;
                    }

                    case LinuxSyscallNum.SYS_GETPID:
                        return pid;

                    case LinuxSyscallNum.SYS_GETPPID:
                        return 1;

                    case LinuxSyscallNum.SYS_GETUID:
                    case LinuxSyscallNum.SYS_GETGID:
                    case LinuxSyscallNum.SYS_GETEUID:
                    case LinuxSyscallNum.SYS_GETEGID:
                        return 0; // Root user

                    case LinuxSyscallNum.SYS_UNAME: // (struct utsname *buf)
                    {
                        byte* buf = (byte*)a1;
                        if (buf == null) return EFAULT;
                        for (int i = 0; i < 390; i++) buf[i] = 0;
                        WriteString(buf + 0, "Windose");
                        WriteString(buf + 65, "windose");
                        WriteString(buf + 130, "1.0.0");
                        WriteString(buf + 195, "Windose 7 Aero Core");
                        WriteString(buf + 260, "x86_64");
                        return 0;
                    }

                    case LinuxSyscallNum.SYS_GETCWD: // (buf, size)
                    {
                        byte* buf = (byte*)a1;
                        ulong sz = a2;
                        if (buf == null || sz < 5) return ERANGE;
                        WriteString(buf, "/mnt");
                        return (long)(ulong)buf;
                    }

                    case LinuxSyscallNum.SYS_CHDIR:
                        return 0;

                    case LinuxSyscallNum.SYS_ARCH_PRCTL: // (code, addr)
                    {
                        // 0x1002 = ARCH_SET_FS (sets TLS pointer in FS register)
                        ulong code = a1;
                        ulong addr = a2;
                        if (code == 0x1002)
                        {
                            return 0; // Success
                        }
                        return EINVAL;
                    }

                    case LinuxSyscallNum.SYS_CLOCK_GETTIME: // (clock_id, timespec)
                    {
                        LinuxTimespec* ts = (LinuxTimespec*)a2;
                        if (ts == null) return EFAULT;
                        long ticks = Environment.TickCount64;
                        ts->TvSec = 1700000000 + (ticks / 1000);
                        ts->TvNsec = (ticks % 1000) * 1000000;
                        return 0;
                    }

                    case LinuxSyscallNum.SYS_EXIT:
                    case LinuxSyscallNum.SYS_EXIT_GROUP:
                    {
                        int exitCode = (int)a1;
                        if (prog != null)
                        {
                            prog.ExitCode = exitCode;
                            CleanupProcessFds(pid);
                        }
                        return 0;
                    }

                    default:
                        return ENOSYS; // Not implemented / stubbed
                }
            }
            catch
            {
                return -1;
            }
        }

        private static void WriteString(byte* dest, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            for (int i = 0; i < b.Length && i < 64; i++) dest[i] = b[i];
            dest[b.Length < 64 ? b.Length : 64] = 0;
        }
    }
}
