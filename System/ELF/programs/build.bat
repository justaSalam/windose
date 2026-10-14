@echo off
rem Builds libc.a and every program in src\ into bin\ (PIE ELF). Usage: build.bat            (all)
rem                                                                    build.bat hello      (only src\hello.c)
rem Needs LLVM:  winget install LLVM.LLVM   (clang, ld.lld, llvm-ar). NOT MinGW/MSVC - those make .exe, not ELF.
rem Untested on Windows - if it fails, use WSL + build.sh instead.
setlocal enabledelayedexpansion
cd /d "%~dp0"
if not exist bin mkdir bin
if not exist obj mkdir obj
set CFLAGS=--target=x86_64-unknown-none-elf -c -O2 -Wall -ffreestanding -fno-builtin -fPIE -fvisibility=hidden -fno-stack-protector -fcf-protection=none -ffunction-sections -fdata-sections -mno-red-zone -fno-asynchronous-unwind-tables -nostdlibinc -isystem include
set LDFLAGS=-pie --no-dynamic-linker -e _start -z norelro -z noseparate-code -z max-page-size=0x1000 -z common-page-size=0x1000 --gc-sections
del /q obj\*.o libc.a 2>nul
for %%f in (libc\*.c) do (clang %CFLAGS% %%f -o obj\%%~nf.o || exit /b 1)
set LIBOBJS=
for %%f in (obj\*.o) do if /I not "%%~nxf"=="crt0.o" set LIBOBJS=!LIBOBJS! %%f
llvm-ar rcs libc.a !LIBOBJS! || exit /b 1
if not "%1"=="" (call :build %1) else (for %%f in (src\*.c) do call :build %%~nf)
exit /b 0
:build
clang %CFLAGS% src\%1.c -o obj\prog_%1.o || exit /b 1
ld.lld %LDFLAGS% -o bin\%1.elf obj\crt0.o obj\prog_%1.o libc.a || exit /b 1
echo built bin\%1.elf
exit /b 0
