# Windose ELF loader (v2)

Runs x86-64 ELF programs written in C inside your Cosmos kernel: several at once, each with its own
handles, events, memory quota and process-list entry, with a small libc and normal `main(argc, argv)`.

```
kernel/            add all of these to your kernel project (AllowUnsafeBlocks on)
programs/          C library, headers, example programs, build scripts; prebuilt ELFs in bin/
tests/             desktop test suite (Linux/WSL + dotnet 8): format_test.c and ElfTests/
```

```csharp
byte[] data = File.ReadAllBytes(path);
string err; ElfProgram p = ElfRunner.Spawn(data, "hello", new string[] { "one" }, out err);
if (p == null) Terminal.WriteLine("exec failed: " + err);
```

## What it does now

* **PIE + relocations**: normal compiler output works, including tables of pointers (R_X86_64_RELATIVE/64/GLOB_DAT/JUMP_SLOT
  against symbols in the program itself). Images are page-aligned. Unresolved symbols, TLS and RELR are rejected with a message.
* **libc** (`programs/libc`, headers in `programs/include`): string.h, stdlib.h (malloc/free/calloc/realloc, strtol, qsort,
  bsearch, rand), stdio.h (printf family without floats, puts, `file_read`), ctype.h, `exit()` from any depth, `abort()`.
  `crt0` builds argc/argv and turns the return value / `exit()` into the program's exit code.
* **Processes**: `ElfRunner.Spawn` gives every program its own thread, pid, handle table, event queue and memory quota
  (`ElfRunner.MemoryQuota`, default 64 MB). Windows and memory are released when it exits. `ElfRunner.Kill(pid)` closes its windows
  at once and the program stops at its next kernel call (sleep/poll are checked every 10 ms). `ElfRunner.List()` for `ps`.
* **API** (`kernel.h` -> `k_*`): write, create_window, close, sleep, create_button, create_label, set_text, fill_rect,
  poll_event, read_file, write_file, ticks, pid. Memory comes from malloc (kernel `alloc` underneath).

## Writing programs

`programs/src/*.c`: write a normal `int main(int argc, char **argv)`; `#include <stdio.h>` etc. and `"kernel.h"` for k_* calls.
Build: Linux/WSL `sh build.sh [name]` (gcc + binutils), Windows `build.bat [name]` (LLVM, untested). Output: `bin/<name>.elf`.

Limits: no floating point in printf, no global constructors (`.init_array`), no TLS, no `fopen` (use `file_read` / `k_write_file`),
no threads inside a program. Kill cannot stop a program stuck in a pure compute loop that never calls the kernel.

## Adding a kernel function

1. `programs/include/kernel.h`: new field at the END of `KernelApi` (first parameter is always `int pid`) + a `k_*` wrapper.
2. `kernel/ElfRunner.cs`: same field at the END of `KernelApi`, an `[UnmanagedCallersOnly]` handler (start with
   `Live(pid)`, try/catch, validate everything), and assign it in `FillApi`. Bump `KAPI_VERSION`/`ApiVersion` if you change existing ones.
3. Need OS access? Add a method to `IElfHost`, implement it in `WindoseBridge.cs`.

## If it breaks

| Symptom | Fix |
|---|---|
| Page fault on the first instruction | Heap isn't executable: set `ElfRunner.MakeExecutable` to mark the pages. |
| Crash on the first kernel call | ABI mismatch: in `kernel.h` change `sysv_abi` to `ms_abi`, rebuild libc + programs. |
| `kernel API version mismatch` | Program built against an older `kernel.h`: rebuild. |
| `exec failed: ... unresolved symbol 'x'` | The program calls a function that isn't in libc: add it to `programs/libc`. |
| `exec failed: ET_EXEC linked at a fixed address` | Use the provided build scripts (they link PIE). |

## Testing on a desktop first

```
cd tests && gcc -O1 -o /tmp/format_test format_test.c ../programs/libc/format.c && /tmp/format_test    # printf engine vs glibc
cd ElfTests && dotnet run ../../programs/bin                                                           # loader, processes, kill, quota
```

## Isolation

Programs run in ring 0 on kernel threads: a buggy program can still crash or corrupt the kernel, and `Kill` is cooperative.
Programs only use the `k_*` wrappers, so a future ring-3 transport (syscall instruction, per-process page tables) only
changes `kernel.h` and the kernel entry path, not program sources.
