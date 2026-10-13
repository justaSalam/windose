# Windose ELF loader

Run static x86-64 ELF programs (written in C) inside your Cosmos kernel, with windows,
buttons, labels, events and files exposed through a function table.

```
kernel/    -> add all of these to your kernel project
programs/  -> C programs + build scripts (hello.elf and window.elf are prebuilt)
```

## Setup (do in order, test after each step)

1. Copy everything in `kernel/` into your kernel project. Turn on `AllowUnsafeBlocks` in the .csproj.
2. In `WindoseBridge.Install()` replace the `Output` TODO with your terminal write, and call
   `WindoseBridge.Install();` once at boot.
3. Add a terminal command `run <path>` (on its own Thread, like your other shell commands):
   ```csharp
   byte[] data = /* read the file from your VFS */;
   string err;
   if (!ElfRunner.Run(data, out err)) Terminal.WriteLine("exec failed: " + err);
   ```
4. Put `programs/hello.elf` on your disk and run it.
   Expected output:  `Hello from an ELF program!`  then  `c=8`
5. Fill in the TODOs in `WindoseBridge.cs` (CreateWindow, CreateButton, CreateLabel, Bind, SetText,
   FillRect, Close, ReadFile, WriteFile). Then run `programs/window.elf`:
   a window with a button; each click updates the label; closing the window ends the program.

## Writing programs

Edit/copy `programs/hello.c` or `window.c`, then build:

* Windows native: `winget install LLVM.LLVM`, then `build.bat hello` (untested; prefer WSL).
  Do NOT use MinGW/MSVC - they produce .exe, not ELF.

Rules for programs: freestanding C, no libc (no printf/malloc/memcpy), entry point is
`KABI void _start(KernelApi *k)`. Avoid static tables of pointers (they need relocations,
which the loader doesn't apply). If linking complains about `memcpy`/`memset`, you used
a big struct copy or array init - write the loop by hand.

## Adding a kernel function

Three places, same order every time:
1. `programs/kapi.h`      - add a field at the END of `KernelApi`
2. `kernel/ElfRunner.cs`  - add the matching field at the END of `KernelApi`
3. `kernel/ElfRunner.cs`  - write an `[UnmanagedCallersOnly]` handler (try/catch, validate everything)
   and assign it in `Run()`: `api.MyThing = &ApiMyThing;`

If it needs OS access, add a method to `IElfHost` and implement it in `WindoseBridge.cs`.

## If it breaks

| Symptom | Fix |
|---|---|
| Page fault on the first instruction | Heap isn't executable. Set `ElfRunner.MakeExecutable` to mark those pages executable. |
| Crash on the first kernel call from the program | ABI mismatch. In `kapi.h` change `sysv_abi` to `ms_abi`, rebuild the programs. |
| `exec failed: need ET_EXEC` | Built as PIE/shared. Use the provided build scripts. |
| `exec failed: not an ELF file` | Built with MinGW/MSVC, or the VFS read returned wrong bytes. |
| Window functions return 0 | `WindoseBridge` TODOs not filled in yet. |

## Limits

Ring 0, no isolation: a buggy program can crash or corrupt the kernel. One program at a time
(static state). No relocations, no dynamic linking, no TLS.

## Handles

Programs only see `int` handles (`handle_t`). The kernel keeps the real objects in `HandleTable`.
`0` always means failure/none. Don't do arithmetic on handles. A handle becomes invalid when its
object is closed (stale handles return an error, they never hit a reused slot).
