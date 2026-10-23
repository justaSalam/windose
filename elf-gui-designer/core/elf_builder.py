"""
ELF Compiler and Linker integration for Windows.
Finds Clang / LLD (e.g. Cosmos Tools / LLVM / MinGW / PATH) and compiles
generated GUI C code into native 64-bit ELF binaries.
"""

import os
import shutil
import subprocess
import tempfile
from typing import Dict, Any, Optional, Tuple


class ElfBuilder:
    """Manages cross-compilation of C code to ELF binaries on Windows."""

    @staticmethod
    def find_llvm_tools() -> Dict[str, Optional[str]]:
        """Discovers Clang and LLD on the current Windows machine."""
        known_paths = [
            r"C:\Users\salam\AppData\Local\Cosmos\Tools\llvm-tools\bin",
            r"C:\Program Files\LLVM\bin",
            r"C:\Program Files (x86)\LLVM\bin",
            r"C:\msys64\mingw64\bin",
            r"C:\msys64\ucrt64\bin",
        ]

        clang_path = None
        lld_path = None

        # Check known paths first
        for p in known_paths:
            c_candidate = os.path.join(p, "clang.exe")
            l_candidate = os.path.join(p, "ld.lld.exe")
            if os.path.isfile(c_candidate) and not clang_path:
                clang_path = c_candidate
            if os.path.isfile(l_candidate) and not lld_path:
                lld_path = l_candidate

        # Fallback to system PATH
        if not clang_path:
            clang_path = shutil.which("clang")
        if not lld_path:
            lld_path = shutil.which("ld.lld")

        return {
            "clang": clang_path,
            "lld": lld_path,
            "available": (clang_path is not None)
        }

    @classmethod
    def compile_standalone_elf(cls, c_code: str, output_elf_path: str) -> Dict[str, Any]:
        """
        Compiles self-contained freestanding C code to an ELF x86_64 binary.
        """
        tools = cls.find_llvm_tools()
        if not tools["clang"]:
            return {
                "success": False,
                "error": "Clang was not found on this system. Please install LLVM or add Clang to PATH."
            }

        temp_dir = tempfile.mkdtemp(prefix="elf_build_")
        c_file = os.path.join(temp_dir, "gui_screen.c")
        obj_file = os.path.join(temp_dir, "gui_screen.o")

        try:
            with open(c_file, "w", encoding="utf-8") as f:
                f.write(c_code)

            # 1. Compile C to ELF object with Clang
            clang_cmd = [
                tools["clang"],
                "-target", "x86_64-unknown-linux-gnu",
                "-ffreestanding",
                "-nostdlib",
                "-O2",
                "-c", c_file,
                "-o", obj_file
            ]

            proc1 = subprocess.run(clang_cmd, capture_output=True, text=True, timeout=15)
            if proc1.returncode != 0:
                return {
                    "success": False,
                    "stage": "clang compilation",
                    "error": proc1.stderr or proc1.stdout,
                    "command": " ".join(clang_cmd)
                }

            # 2. Link with ld.lld if available, or if no linker, output object as ELF
            if tools["lld"]:
                lld_cmd = [
                    tools["lld"],
                    "-m", "elf_x86_64",
                    "-o", output_elf_path,
                    obj_file
                ]
                proc2 = subprocess.run(lld_cmd, capture_output=True, text=True, timeout=15)
                if proc2.returncode != 0:
                    # Fallback to copying the relocatable ELF object
                    shutil.copy2(obj_file, output_elf_path)
                    return {
                        "success": True,
                        "type": "ELF Relocatable Object (.o)",
                        "path": output_elf_path,
                        "warning": f"Linker warning: {proc2.stderr}. Produced relocatable ELF object.",
                        "size": os.path.getsize(output_elf_path)
                    }

                return {
                    "success": True,
                    "type": "ELF x86_64 Executable",
                    "path": output_elf_path,
                    "size": os.path.getsize(output_elf_path),
                    "log": "ELF x86_64 executable compiled and linked successfully!"
                }
            else:
                # Output relocatable ELF
                shutil.copy2(obj_file, output_elf_path)
                return {
                    "success": True,
                    "type": "ELF Relocatable Object (.o)",
                    "path": output_elf_path,
                    "size": os.path.getsize(output_elf_path),
                    "log": "ELF object compiled with Clang (ld.lld not present for standalone link)."
                }

        except Exception as ex:
            return {"success": False, "error": str(ex)}
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)

    @staticmethod
    def find_windose_build_bat() -> Optional[str]:
        """
        Locates the Windose OS ELF build.bat script.
        Searches relative to the current directory and in standard Windose project paths.
        """
        candidates = [
            # If elf-gui-designer is inside windose/
            os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "System", "ELF", "programs", "build.bat")),
            os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "System", "ELF", "programs", "build.bat")),
            r"C:\Users\salam\Desktop\windose\System\ELF\programs\build.bat",
            r"C:\Users\salam\Windose\System\ELF\programs\build.bat",
            r"C:\Users\salam\source\Windose\System\ELF\programs\build.bat",
        ]
        for path in candidates:
            if os.path.isfile(path):
                return path
        return None

    @classmethod
    def compile_windose_elf(cls, project_name: str, c_code: str, custom_build_bat: Optional[str] = None) -> Dict[str, Any]:
        """
        Compiles a native Windose ELF program using System/ELF/programs/build.bat.
        Places source in System/ELF/programs/src/<clean_name>.c and builds into bin/<clean_name>.elf.
        """
        build_bat = custom_build_bat or cls.find_windose_build_bat()
        if not build_bat or not os.path.isfile(build_bat):
            return {
                "success": False,
                "error": "Windose build.bat not found. Expected at System/ELF/programs/build.bat."
            }

        prog_dir = os.path.dirname(build_bat)
        src_dir = os.path.join(prog_dir, "src")
        bin_dir = os.path.join(prog_dir, "bin")
        os.makedirs(src_dir, exist_ok=True)
        os.makedirs(bin_dir, exist_ok=True)

        clean_name = "".join(c if c.isalnum() or c == "_" else "_" for c in project_name.lower().strip())
        if not clean_name:
            clean_name = "app"

        src_file = os.path.join(src_dir, f"{clean_name}.c")
        bin_file = os.path.join(bin_dir, f"{clean_name}.elf")

        try:
            with open(src_file, "w", encoding="utf-8") as f:
                f.write(c_code)

            cmd = ["cmd.exe", "/c", "build.bat", clean_name]
            proc = subprocess.run(cmd, cwd=prog_dir, capture_output=True, text=True, timeout=30)

            full_log = (proc.stdout or "") + ("\n" + proc.stderr if proc.stderr else "")

            if proc.returncode != 0 or not os.path.isfile(bin_file):
                return {
                    "success": False,
                    "error": f"build.bat failed with return code {proc.returncode}.",
                    "log": full_log,
                    "src_path": src_file
                }

            return {
                "success": True,
                "type": "Windose Native ELF (PIE)",
                "path": bin_file,
                "src_path": src_file,
                "size": os.path.getsize(bin_file),
                "log": full_log,
                "prog_name": clean_name
            }

        except Exception as ex:
            return {"success": False, "error": str(ex)}

    @classmethod
    def install_to_windose_kernel(cls, project_name: str) -> Dict[str, Any]:
        """
        Embeds the built ELF binary into Windose OS Kernel.cs and Windose.csproj.
        Ensures the application is mounted at /mnt/Programs/<name>.elf on OS boot.
        """
        clean_name = "".join(c if c.isalnum() or c == "_" else "_" for c in project_name.lower().strip())
        bat = cls.find_windose_build_bat()
        if not bat:
            return {"success": False, "error": "Could not find Windose build.bat"}
        root_dir = os.path.abspath(os.path.join(os.path.dirname(bat), "..", "..", ".."))
        kernel_cs = os.path.join(root_dir, "Kernel.cs")
        csproj = os.path.join(root_dir, "Windose.csproj")
        bin_elf = os.path.join(os.path.dirname(bat), "bin", f"{clean_name}.elf")

        if not os.path.isfile(bin_elf):
            return {"success": False, "error": f"Binary bin/{clean_name}.elf does not exist. Build it first."}

        # 1. Update Kernel.cs
        if os.path.isfile(kernel_cs):
            with open(kernel_cs, "r", encoding="utf-8") as f:
                content = f.read()
            entry = f'ResourceLoader.WriteStorage("/mnt/Programs/{clean_name}.elf", "Windose.System.ELF.programs.bin.{clean_name}.elf", true);'
            if entry not in content:
                anchor = 'ResourceLoader.WriteStorage("/mnt/Programs/'
                idx = content.rfind(anchor)
                if idx != -1:
                    line_end = content.find("\n", idx)
                    content = content[:line_end + 1] + f"        {entry}\n" + content[line_end + 1:]
                    with open(kernel_cs, "w", encoding="utf-8") as f:
                        f.write(content)

        # 2. Update Windose.csproj
        if os.path.isfile(csproj):
            with open(csproj, "r", encoding="utf-8") as f:
                content = f.read()
            embed_tag = f'<EmbeddedResource Include=".\\System\\ELF\\programs\\bin\\{clean_name}.elf" />'
            remove_tag = f'<None Remove="System\\ELF\\programs\\bin\\{clean_name}.elf" />'
            changed = False
            if embed_tag not in content:
                idx = content.find('<EmbeddedResource Include=".\\System\\ELF\\programs\\bin\\')
                if idx != -1:
                    close_idx = content.find('</ItemGroup>', idx)
                    content = content[:close_idx] + f"    {embed_tag}\n  " + content[close_idx:]
                    changed = True
            if remove_tag not in content:
                idx = content.find('<None Remove="System\\ELF\\programs\\bin\\')
                if idx != -1:
                    close_idx = content.find('</ItemGroup>', idx)
                    content = content[:close_idx] + f"    {remove_tag}\n  " + content[close_idx:]
                    changed = True
            if changed:
                with open(csproj, "w", encoding="utf-8") as f:
                    f.write(content)

        return {"success": True, "path": bin_elf, "prog_name": clean_name}

    @classmethod
    def compile_elf_object(cls, c_code: str, output_obj_path: str) -> Dict[str, Any]:
        """Compiles C code into a 64-bit ELF relocatable object (.o)."""
        tools = cls.find_llvm_tools()
        if not tools["clang"]:
            return {"success": False, "error": "Clang compiler not found"}

        temp_dir = tempfile.mkdtemp(prefix="elf_obj_")
        c_file = os.path.join(temp_dir, "screen.c")

        try:
            with open(c_file, "w", encoding="utf-8") as f:
                f.write(c_code)

            cmd = [
                tools["clang"],
                "-target", "x86_64-unknown-linux-gnu",
                "-O2",
                "-c", c_file,
                "-o", output_obj_path
            ]
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
            if proc.returncode != 0:
                return {"success": False, "error": proc.stderr or proc.stdout}

            return {
                "success": True,
                "path": output_obj_path,
                "size": os.path.getsize(output_obj_path)
            }
        except Exception as ex:
            return {"success": False, "error": str(ex)}
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)
