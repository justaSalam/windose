"""
Dedicated ELF Builder & Binary Inspector Panel for ELF Studio.
Allows building real 64-bit ELF binaries directly on Windows and inspecting their
headers, sections, and symbols.
"""

import os
import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk
from typing import Optional, Callable
from core.models import Project
from core.elf_builder import ElfBuilder
from core.elf_analyzer import ElfAnalyzer
from codegen import generate_framebuffer_c, generate_windose_c


class ElfPanel(ctk.CTkFrame):
    """Panel for ELF Compilation, Linker orchestration, and Binary Header Inspection."""

    def __init__(self, master, project: Project, **kwargs):
        super().__init__(master, fg_color="#0a0c10", corner_radius=0, **kwargs)
        self.project = project
        self.last_built_elf: Optional[str] = None
        self.windose_bat_path = ElfBuilder.find_windose_build_bat()

        self._build_ui()
        self._check_toolchain()

    def _build_ui(self) -> None:
        # Top banner
        header = ctk.CTkFrame(self, fg_color="#111622", height=44, corner_radius=0)
        header.pack(fill="x", side="top")

        ctk.CTkLabel(
            header, text="🛡️ ELF BINARY SUITE (COMPILER & INSPECTOR)",
            font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"),
            text_color="#38bdf8"
        ).pack(side="left", padx=16)

        # Toolchain status indicator
        self.lbl_toolchain = ctk.CTkLabel(
            header, text="Detecting Clang & LLD...",
            font=ctk.CTkFont(family="Segoe UI", size=11),
            text_color="#94a3b8"
        )
        self.lbl_toolchain.pack(side="right", padx=16)

        # Main splitter (Left: Build & Actions, Right: ELF Inspector Report)
        split = ctk.CTkFrame(self, fg_color="transparent")
        split.pack(fill="both", expand=True, padx=12, pady=12)

        left_col = ctk.CTkFrame(split, fg_color="#101520", width=340, corner_radius=8)
        left_col.pack(side="left", fill="y", padx=(0, 8))
        left_col.pack_propagate(False)

        right_col = ctk.CTkFrame(split, fg_color="#101520", corner_radius=8)
        right_col.pack(side="left", fill="both", expand=True)

        # --- Section 1: Windose OS Native build.bat ---
        ctk.CTkLabel(
            left_col, text="WINDOSE OS ELF COMPILER",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#10b981"
        ).pack(anchor="w", padx=14, pady=(14, 2))

        bat_display = "System/ELF/programs/build.bat" if self.windose_bat_path else "build.bat not detected"
        self.lbl_bat_status = ctk.CTkLabel(
            left_col,
            text=f"Using: {bat_display}",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#34d399" if self.windose_bat_path else "#f59e0b",
            wraplength=310,
            justify="left"
        )
        self.lbl_bat_status.pack(anchor="w", padx=14, pady=(0, 6))

        b_build_windose = ctk.CTkButton(
            left_col,
            text="⚡ Build with Windose build.bat",
            fg_color="#059669",
            hover_color="#047857",
            height=38,
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            command=self._build_windose_elf
        )
        b_build_windose.pack(fill="x", padx=14, pady=2)

        b_install = ctk.CTkButton(
            left_col,
            text="📥 Install to Windose Kernel",
            fg_color="#0284c7",
            hover_color="#0369a1",
            height=30,
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            command=self._install_windose_elf
        )
        b_install.pack(fill="x", padx=14, pady=2)

        b_locate_bat = ctk.CTkButton(
            left_col,
            text="🔍 Custom build.bat location...",
            fg_color="#182234",
            hover_color="#2563eb",
            height=26,
            font=ctk.CTkFont(family="Segoe UI", size=10),
            command=self._browse_build_bat
        )
        b_locate_bat.pack(fill="x", padx=14, pady=(2, 10))

        sep0 = ctk.CTkFrame(left_col, height=1, fg_color="#1f293d")
        sep0.pack(fill="x", padx=14, pady=6)

        # --- Section 2: Generic / Standalone ELF ---
        ctk.CTkLabel(
            left_col, text="STANDALONE ELF (DIRECT LLVM)",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#38bdf8"
        ).pack(anchor="w", padx=14, pady=(6, 4))

        b_build_exec = ctk.CTkButton(
            left_col,
            text="🚀 Freestanding ELF Executable",
            fg_color="#1e293b",
            hover_color="#334155",
            height=32,
            font=ctk.CTkFont(family="Segoe UI", size=11),
            command=self._build_standalone_elf
        )
        b_build_exec.pack(fill="x", padx=14, pady=2)

        b_build_obj = ctk.CTkButton(
            left_col,
            text="📦 Relocatable Object (.o)",
            fg_color="#1e293b",
            hover_color="#334155",
            height=32,
            font=ctk.CTkFont(family="Segoe UI", size=11),
            command=self._build_elf_object
        )
        b_build_obj.pack(fill="x", padx=14, pady=2)

        sep1 = ctk.CTkFrame(left_col, height=1, fg_color="#1f293d")
        sep1.pack(fill="x", padx=14, pady=10)

        # --- Section 3: Inspect External ELF ---
        ctk.CTkLabel(
            left_col, text="INSPECT EXTERNAL ELF",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#38bdf8"
        ).pack(anchor="w", padx=14, pady=(2, 4))

        b_inspect_file = ctk.CTkButton(
            left_col,
            text="📂 Open .ELF / .o File to Inspect...",
            fg_color="#2563eb",
            hover_color="#1d4ed8",
            height=32,
            font=ctk.CTkFont(family="Segoe UI", size=11),
            command=self._browse_and_inspect_elf
        )
        b_inspect_file.pack(fill="x", padx=14, pady=2)

        # Build log area at bottom of left column
        ctk.CTkLabel(
            left_col, text="BUILD LOGS:",
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold"),
            text_color="#64748b"
        ).pack(anchor="w", padx=14, pady=(12, 2))

        self.log_text = tk.Text(
            left_col, bg="#080a0f", fg="#94a3b8", font=("Consolas", 9),
            height=8, bd=0, wrap="word", padx=8, pady=6
        )
        self.log_text.pack(fill="both", expand=True, padx=14, pady=(0, 14))

        # --- Right Column: ELF Binary Analysis Report ---
        r_header = ctk.CTkFrame(right_col, fg_color="transparent")
        r_header.pack(fill="x", padx=14, pady=(12, 6))

        ctk.CTkLabel(
            r_header, text="BINARY STRUCTURE & SECTION HEADERS",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            text_color="#38bdf8"
        ).pack(side="left")

        # Quick stats badges row
        self.stat_bar = ctk.CTkFrame(right_col, fg_color="#0a0e17", height=32, corner_radius=6)
        self.stat_bar.pack(fill="x", padx=14, pady=(0, 8))

        self.lbl_stats = ctk.CTkLabel(
            self.stat_bar,
            text="No binary analyzed yet. Build an ELF above or open an existing Linux binary.",
            font=ctk.CTkFont(family="Segoe UI", size=11),
            text_color="#64748b"
        )
        self.lbl_stats.pack(side="left", padx=12, pady=4)

        # Detailed Report Text View
        self.report_text = tk.Text(
            right_col, bg="#06080d", fg="#e2e8f0", font=("Consolas", 10),
            bd=0, padx=12, pady=10
        )
        r_scroll = tk.Scrollbar(right_col, orient="vertical", command=self.report_text.yview)
        self.report_text.configure(yscrollcommand=r_scroll.set)
        r_scroll.pack(side="right", fill="y", padx=(0, 10), pady=(0, 10))
        self.report_text.pack(fill="both", expand=True, padx=(14, 0), pady=(0, 10))

    def _check_toolchain(self) -> None:
        tools = ElfBuilder.find_llvm_tools()
        if tools["available"]:
            c_name = os.path.basename(tools["clang"])
            l_info = " + ld.lld" if tools["lld"] else ""
            self.lbl_toolchain.configure(
                text=f"✔ Active Toolchain: {c_name}{l_info}",
                text_color="#10b981"
            )
            self._log(f"Found LLVM compiler: {tools['clang']}")
            if tools["lld"]:
                self._log(f"Found ELF linker: {tools['lld']}")
        else:
            self.lbl_toolchain.configure(
                text="⚠ Clang not found in PATH or Cosmos Tools",
                text_color="#f59e0b"
            )
            self._log("Notice: Clang / LLD not found. You can still export C code or inspect external ELF files.")

        if self.windose_bat_path:
            self._log(f"Found Windose build script: {self.windose_bat_path}")
        else:
            self._log("Notice: Windose build.bat not detected automatically. Use 'Custom build.bat location' to select it.")

    def _log(self, msg: str) -> None:
        self.log_text.insert("end", msg + "\n")
        self.log_text.see("end")

    def _build_windose_elf(self) -> None:
        if not self.windose_bat_path or not os.path.isfile(self.windose_bat_path):
            self._browse_build_bat()
            if not self.windose_bat_path:
                return

        self._log(f"Compiling '{self.project.name}' via Windose build.bat...")
        c_code = generate_windose_c(self.project)
        res = ElfBuilder.compile_windose_elf(self.project.name, c_code, self.windose_bat_path)

        if res.get("log"):
            for line in res["log"].splitlines():
                if line.strip():
                    self._log(f"  [build.bat] {line}")

        if res.get("success"):
            elf_path = res["path"]
            self.last_built_elf = elf_path
            size = res.get("size", 0)
            self._log(f"✔ Build successful: {os.path.basename(elf_path)} ({size} bytes)")
            self._log(f"  Source file: {res.get('src_path')}")
            self._log(f"  Binary file: {elf_path}")
            messagebox.showinfo(
                "Windose Build Succeeded",
                f"Successfully compiled native Windose ELF binary!\n\n"
                f"Binary: {elf_path}\n"
                f"Source: {res.get('src_path')}\n"
                f"Size: {size} bytes\n\n"
                f"Ready to execute inside Windose OS!"
            )
            self._inspect_file_path(elf_path)
        else:
            err = res.get("error", "Unknown build error")
            self._log(f"✖ Windose build failed: {err}")
            messagebox.showerror("Windose Build Error", f"build.bat failed:\n{err}\n\nSee build log for details.")

    def _install_windose_elf(self) -> None:
        self._log(f"Installing '{self.project.name}' into Windose Kernel...")
        res = ElfBuilder.install_to_windose_kernel(self.project.name)
        if res.get("success"):
            prog = res["prog_name"]
            self._log(f"✔ Successfully configured Windose Kernel and Windose.csproj!")
            self._log(f"  Target path inside Windose: /mnt/Programs/{prog}.elf")
            messagebox.showinfo(
                "Kernel Integration Succeeded",
                f"Successfully embedded '{prog}.elf' into Windose OS!\n\n"
                f"• Added storage mapping: /mnt/Programs/{prog}.elf\n"
                f"• Embedded in Windose.csproj\n\n"
                f"Run 'dotnet build' to produce your final bootable Windose ISO."
            )
        else:
            err = res.get("error", "Unknown error")
            self._log(f"✖ Kernel install failed: {err}")
            messagebox.showerror("Installation Error", err)

    def _browse_build_bat(self) -> None:
        filepath = filedialog.askopenfilename(
            title="Select Windose build.bat",
            filetypes=[("Batch Scripts", "build.bat"), ("All Files", "*.*")]
        )
        if filepath:
            self.windose_bat_path = filepath
            rel = os.path.relpath(filepath)
            self.lbl_bat_status.configure(
                text=f"Using: {rel}",
                text_color="#34d399"
            )
            self._log(f"Set Windose build script: {filepath}")

    def _build_standalone_elf(self) -> None:
        out_file = filedialog.asksaveasfilename(
            defaultextension=".elf",
            initialfile=f"{self.project.name.lower()}.elf",
            filetypes=[("ELF Executable", "*.elf"), ("All Files", "*.*")]
        )
        if not out_file:
            return

        self._log(f"Starting standalone ELF build for '{self.project.name}'...")
        c_code = generate_framebuffer_c(self.project)
        res = ElfBuilder.compile_standalone_elf(c_code, out_file)

        if res.get("success"):
            self.last_built_elf = out_file
            size_kb = res.get("size", 0) / 1024.0
            self._log(f"✔ Build successful: {os.path.basename(out_file)} ({size_kb:.1f} KB)")
            messagebox.showinfo("ELF Build Success", f"Successfully built 64-bit ELF binary:\n{out_file}\nSize: {res.get('size')} bytes")
            self._inspect_file_path(out_file)
        else:
            err = res.get("error", "Unknown compilation error")
            self._log(f"✖ Build failed: {err}")
            messagebox.showerror("ELF Build Error", f"Compilation failed:\n{err}")

    def _build_elf_object(self) -> None:
        out_file = filedialog.asksaveasfilename(
            defaultextension=".o",
            initialfile=f"{self.project.name.lower()}.o",
            filetypes=[("ELF Object", "*.o"), ("All Files", "*.*")]
        )
        if not out_file:
            return

        self._log(f"Compiling ELF object '{self.project.name}.o'...")
        c_code = generate_framebuffer_c(self.project)
        res = ElfBuilder.compile_elf_object(c_code, out_file)

        if res.get("success"):
            self._log(f"✔ Object compiled: {os.path.basename(out_file)} ({res.get('size')} bytes)")
            self._inspect_file_path(out_file)
        else:
            self._log(f"✖ Object compilation failed: {res.get('error')}")

    def _browse_and_inspect_elf(self) -> None:
        filepath = filedialog.askopenfilename(
            filetypes=[("ELF Binaries", "*.elf;*.o;*.so;*"), ("All Files", "*.*")]
        )
        if filepath:
            self._inspect_file_path(filepath)

    def _inspect_file_path(self, filepath: str) -> None:
        self._log(f"Analyzing ELF binary: {os.path.basename(filepath)}...")
        try:
            info = ElfAnalyzer.parse_file(filepath)
            if not info.get("valid"):
                self.lbl_stats.configure(text=f"Error: {info.get('error')}", text_color="#ef4444")
                self.report_text.delete("1.0", "end")
                self.report_text.insert("1.0", f"File: {filepath}\nError: {info.get('error')}")
                return

            # Update badge stats
            badge = f"Format: {info['class']} | Arch: {info['machine']} | Type: {info['type']} | Size: {info['file_size']} B | Sections: {len(info.get('sections', []))}"
            self.lbl_stats.configure(text=badge, text_color="#38bdf8")

            # Formatted analysis report
            report = ElfAnalyzer.format_summary(info)
            self.report_text.delete("1.0", "end")
            self.report_text.insert("1.0", report)
            self._log(f"✔ Completed analysis for {os.path.basename(filepath)}")

        except Exception as ex:
            self._log(f"✖ Analysis exception: {str(ex)}")
            messagebox.showerror("Analysis Error", str(ex))
