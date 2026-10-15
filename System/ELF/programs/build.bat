@echo off
rem Builds libc and every program in src\ into bin\ (PIE ELF). Usage: build.bat            (all)
rem                                                                    build.bat hello      (only src\hello.c)
rem                                                                    build.bat clean      (remove obj + bin)
rem Needs clang + ld.lld (NOT MinGW/MSVC - those make .exe, not ELF). Winget:  winget install LLVM.LLVM
rem The Cosmos llvm-tools folder (clang + ld.lld, no archiver needed) also works.
setlocal enabledelayedexpansion
cd /d "%~dp0"

if /I "%~1"=="clean" (
  if exist obj rmdir /s /q obj
  if exist bin rmdir /s /q bin
  echo cleaned.
  exit /b 0
)

if not exist bin mkdir bin
if not exist obj mkdir obj

rem --- locate clang / ld.lld: PATH first, then known install spots ---
where clang >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\LLVM\bin\clang.exe" (
    set "PATH=%ProgramFiles%\LLVM\bin;!PATH!"
  ) else if exist "%LocalAppData%\Cosmos\Tools\llvm-tools\bin\clang.exe" (
    set "PATH=%LocalAppData%\Cosmos\Tools\llvm-tools\bin;!PATH!"
  )
)
where clang >nul 2>&1
if errorlevel 1 (
  echo ERROR: clang not found. Install LLVM ^(winget install LLVM.LLVM^) or make sure
  echo Cosmos llvm-tools is present, then re-run from a fresh terminal.
  exit /b 1
)
where ld.lld >nul 2>&1
if errorlevel 1 (
  echo ERROR: ld.lld not found next to clang. Install LLVM ^(winget install LLVM.LLVM^).
  exit /b 1
)
for /f "delims=" %%v in ('clang --version 2^>nul ^| findstr /c:"clang version"') do echo Using %%v
for /f "delims=" %%r in ('clang -print-resource-dir 2^>nul') do set "RESDIR=%%r"
if not defined RESDIR (
  echo ERROR: could not query clang resource dir.
  exit /b 1
)

set CFLAGS=--target=x86_64-unknown-none-elf -c -O2 -Wall -Wno-unused-function -ffreestanding -fno-builtin -fPIE -fvisibility=hidden -fno-stack-protector -fcf-protection=none -ffunction-sections -fdata-sections -mno-red-zone -fno-asynchronous-unwind-tables -nostdinc -isystem include -isystem "%RESDIR%\include"
set LDFLAGS=-pie --no-dynamic-linker -e _start -z norelro -z noseparate-code -z max-page-size=0x1000 -z common-page-size=0x1000 --build-id=none --gc-sections

if exist obj\*.o del /q obj\*.o

echo Compiling libc...
for %%f in (libc\*.c) do (
  clang %CFLAGS% "%%f" -o "obj\%%~nf.o" || ( echo FAILED: %%f & exit /b 1 )
)

rem No archiver needed: link the objects directly (--gc-sections drops what isn't used).
set LIBOBJS=
for %%f in (obj\*.o) do if /I not "%%~nxf"=="crt0.o" set LIBOBJS=!LIBOBJS! "obj\%%~nxf"

if not "%~1"=="" (
  call :build "%~1" || exit /b 1
) else (
  for %%f in (src\*.c) do call :build "%%~nf" || exit /b 1
)
echo All built.
exit /b 0

:build
if not exist "src\%~1.c" ( echo No such program: src\%~1.c & exit /b 1 )
echo Building %~1...
clang %CFLAGS% "src\%~1.c" -o "obj\prog_%~1.o" || exit /b 1
ld.lld %LDFLAGS% -o "bin\%~1.elf" "obj\crt0.o" "obj\prog_%~1.o" !LIBOBJS! || exit /b 1
echo built bin\%~1.elf
exit /b 0
